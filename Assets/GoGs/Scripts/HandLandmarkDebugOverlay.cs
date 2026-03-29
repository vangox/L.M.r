using UnityEngine;

namespace Novena.MaliSalon.Mediapipe
{
    /// <summary>
    /// Toggleable debug overlay that renders all 21 MediaPipe hand landmarks as
    /// colored dots and optional skeleton connection lines on top of the game view,
    /// for both left (yellow) and right (orange) hands independently.
    ///
    /// Drawing is done entirely in OnGUI() (IMGUI phase), which is URP-safe and
    /// requires no Camera.onPostRender or custom RenderPass setup.
    ///
    /// Scene setup:
    ///   1. Add this component to any GameObject in the scene (e.g. DataManager).
    ///   2. Leave dataProvider null — it will be found automatically — or drag-assign.
    ///   3. Enter Play mode and toggle Show Hand Landmarks in the Inspector.
    ///   4. Ensure mirrorX matches MediaPipeDataProvider / Hand mirror setting.
    ///
    /// Coordinate notes:
    ///   Dots  (GUI.DrawTexture) : OnGUI Y=0 is top  → same as MediaPipe → sy = ny * sh
    ///   Lines (GL.LoadPixelMatrix inside OnGUI): also Y=0 at top in this context → gy = ny * sh
    /// </summary>
    public class HandLandmarkDebugOverlay : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Auto-found if left null.")]
        public MediaPipeDataProvider dataProvider;

        [Header("Display")]
        public bool showHandLandmarks = false;
        public bool showConnections   = true;

        [Range(1f, 15f)] public float pointRadius = 4f;
        public Color leftHandColor        = Color.yellow;
        public Color leftConnectionColor  = new Color(
            1f, 1f, 0f, 0.5f 
        );
        public Color rightHandColor       = new Color(
            1f, 0.4f, 0f, 1f 
        );
        public Color rightConnectionColor = new Color(
            1f, 0.4f, 0f, 0.5f 
        );

        [Header("Smoothing")]
        [Range(1f, 30f)]
        [Tooltip("Lerp speed for landmark screen positions. Lower = smoother but laggier.")]
        public float smoothing = 10f;

        [Header("Mirror")]
        [Tooltip("Mirror X axis — must match MediaPipeDataProvider mirrorX.")]
        public bool mirrorX = true;

        // ── Private state ─────────────────────────────────────────────────────────

        private Texture2D _dotTexture;
        private Material  _lineMaterial;

        private Vector2[] _smoothedLeft;
        private Vector2[] _smoothedRight;
        private bool _smoothedLeftInit  = false;
        private bool _smoothedRightInit = false;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void Start()
        {
            if (dataProvider == null)
                dataProvider = FindFirstObjectByType<MediaPipeDataProvider>();

            _dotTexture = CreateCircleTexture(16);
        }

        private void OnDestroy()
        {
            if (_dotTexture   != null) Destroy(_dotTexture);
            if (_lineMaterial != null) Destroy(_lineMaterial);
        }

        // ── Rendering ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!showHandLandmarks || dataProvider == null)
                return;

            float sw = UnityEngine.Screen.width;
            float sh = UnityEngine.Screen.height;
            float t  = Mathf.Clamp01(smoothing * Time.deltaTime);

            float[][] leftData  = dataProvider.left_hand_data;
            float[][] rightData = dataProvider.right_hand_data;

            // Reset smoothed state when hand is lost so next detection starts fresh.
            if (leftData == null)
                _smoothedLeftInit = false;

            if (rightData == null)
                _smoothedRightInit = false;

            // ── Left hand ───────────────────────────────────────────────────────
            if (leftData != null && leftData.Length == 21)
            {
                UpdateSmoothed(leftData, ref _smoothedLeft, ref _smoothedLeftInit, t);

                if (showConnections && Event.current.type == EventType.Repaint)
                    DrawConnectionsGUI(_smoothedLeft, leftConnectionColor, sw, sh);

                DrawDots(_smoothedLeft, leftHandColor, sw, sh);
            }

            // ── Right hand ──────────────────────────────────────────────────────
            if (rightData != null && rightData.Length == 21)
            {
                UpdateSmoothed(rightData, ref _smoothedRight, ref _smoothedRightInit, t);

                if (showConnections && Event.current.type == EventType.Repaint)
                    DrawConnectionsGUI(_smoothedRight, rightConnectionColor, sw, sh);

                DrawDots(_smoothedRight, rightHandColor, sw, sh);
            }

            GUI.color = Color.white; // restore
        }

        private void UpdateSmoothed(float[][] data, ref Vector2[] smoothed, ref bool initialized, float t)
        {
            if (smoothed == null || smoothed.Length != data.Length)
            {
                smoothed     = new Vector2[data.Length];
                initialized  = false;
            }

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == null || data[i].Length < 2)
                    continue;

                // Remap X for UV crop: portrait X ↔ landscape Y (the cropped axis).
                float rx  = WebcamFillCover.RemapGuiY(data[i][0]);
                float nx  = mirrorX ? 1f - rx : rx;
                float ny  = data[i][1];
                Vector2 raw = new Vector2(
                    nx, ny 
                );

                smoothed[i] = initialized ? Vector2.Lerp(smoothed[i], raw, t) : raw;
            }

            initialized = true;
        }

        private void DrawDots(Vector2[] positions, Color color, float sw, float sh)
        {
            GUI.color = color;
            float half = pointRadius;
            float size = pointRadius * 2f;

            for (int i = 0; i < positions.Length; i++)
            {
                float sx = positions[i].x * sw;
                float sy = positions[i].y * sh;
                GUI.DrawTexture(new Rect(
                    sx - half, sy - half, size, size
                ), _dotTexture);
            }
        }

        /// <summary>
        /// Draws hand skeleton connection lines using GL inside OnGUI.
        /// Must be called only during EventType.Repaint.
        /// When called inside OnGUI(), GL.LoadPixelMatrix() uses Y=0-at-top (same as IMGUI),
        /// so no Y flip is needed — matches the dot formula exactly.
        /// </summary>
        private void DrawConnectionsGUI(Vector2[] positions, Color color, float sw, float sh)
        {
            if (_lineMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    Debug.LogWarning("[HandLandmarkDebugOverlay] 'Hidden/Internal-Colored' shader not found. " +
                                     "Add it to Always Included Shaders for build support.");
                    return;
                }
                _lineMaterial = new Material(
                    shader 
                ) { hideFlags = HideFlags.HideAndDontSave };
                _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _lineMaterial.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
                _lineMaterial.SetInt("_ZWrite",   0);
            }

            _lineMaterial.SetPass(0);

            GL.PushMatrix();
            GL.LoadPixelMatrix();

            GL.Begin(GL.LINES);
            GL.Color(color);

            foreach (int[] pair in HandConnections)
            {
                int a = pair[0], b = pair[1];
                if (a >= positions.Length || b >= positions.Length)
                    continue;

                GL.Vertex3(positions[a].x * sw, positions[a].y * sh, 0f);
                GL.Vertex3(positions[b].x * sw, positions[b].y * sh, 0f);
            }

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// Creates an RGBA32 circle texture with anti-aliased edges.
        /// Alpha fades from 1 at the center to 0 at the rim over the last pixel.
        /// </summary>
        private static Texture2D CreateCircleTexture(int size)
        {
            var tex = new Texture2D(
                size, size, TextureFormat.RGBA32, false 
            );
            tex.filterMode = FilterMode.Bilinear;

            float radius = size * 0.5f;
            float cx     = radius - 0.5f;
            float cy     = radius - 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist  = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float alpha = Mathf.Clamp01(radius - dist); // 1 inside, fades over 1 px rim
                    tex.SetPixel(x, y, new Color(
                        1f, 1f, 1f, alpha 
                    ));
                }
            }

            tex.Apply();
            return tex;
        }

        // ── Hand connection index pairs (21 edges) ────────────────────────────────
        // Matches the standard MediaPipe HAND_CONNECTIONS topology (used in Hand.cs).
        // Landmarks 0–20; 0 = wrist.

        private static readonly int[][] HandConnections = new int[][]
        {
            // Palm
            new[]{0,1},   new[]{0,5},   new[]{0,17},
            new[]{5,9},   new[]{9,13},  new[]{13,17},

            // Thumb
            new[]{1,2},   new[]{2,3},   new[]{3,4},

            // Index
            new[]{5,6},   new[]{6,7},   new[]{7,8},

            // Middle
            new[]{9,10},  new[]{10,11}, new[]{11,12},

            // Ring
            new[]{13,14}, new[]{14,15}, new[]{15,16},

            // Pinky
            new[]{17,18}, new[]{18,19}, new[]{19,20},
        };
    }
}
