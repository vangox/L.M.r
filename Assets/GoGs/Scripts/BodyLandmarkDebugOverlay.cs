using UnityEngine;

/// <summary>
/// Toggleable debug overlay that renders all 33 MediaPipe pose landmarks as
/// cyan dots and optional skeleton connection lines on top of the game view.
///
/// Drawing is done entirely in OnGUI() (IMGUI phase), which is URP-safe and
/// requires no Camera.onPostRender or custom RenderPass setup.
///
/// Scene setup:
///   1. Add this component to any GameObject in the scene (e.g. DataManager).
///   2. Leave dataProvider null — it will be found automatically — or drag-assign.
///   3. Enter Play mode and toggle Show Body Landmarks in the Inspector.
///   4. Ensure mirrorX matches MediaPipeDataProvider / Body mirror setting.
///
/// Coordinate notes:
///   Dots  (GUI.DrawTexture) : OnGUI Y=0 is top  → same as MediaPipe → sy = ny * sh
///   Lines (GL.LoadPixelMatrix inside OnGUI): also Y=0 at top in this context → gy = ny * sh
/// </summary>
public class BodyLandmarkDebugOverlay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found if left null.")]
    public MediaPipeDataProvider dataProvider;

    [Header("Display")]
    public bool showBodyLandmarks = false;
    public bool showConnections   = true;

    [Range(1f, 15f)] public float pointRadius = 4f;
    public Color landmarkColor   = Color.cyan;
    public Color connectionColor = new Color(0f, 1f, 1f, 0.5f);

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

    // Smoothed normalized landmark positions (x, y per landmark).
    private Vector2[] _smoothedPositions;
    private bool _smoothedInitialized = false;

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
        if (!showBodyLandmarks)
            return;

        if (dataProvider == null || dataProvider.pose_data == null)
            return;

        float[][] pose = dataProvider.pose_data;
        if (pose.Length == 0)
            return;

        // ── Smooth landmark positions ────────────────────────────────────────
        if (_smoothedPositions == null || _smoothedPositions.Length != pose.Length)
        {
            _smoothedPositions = new Vector2[pose.Length];
            _smoothedInitialized = false;
        }

        float t = Mathf.Clamp01(smoothing * Time.deltaTime);

        for (int i = 0; i < pose.Length; i++)
        {
            if (pose[i] == null || pose[i].Length < 2)
                continue;

            // Remap X for UV crop: portrait X ↔ landscape Y (the cropped axis).
            float rx = WebcamFillCover.RemapGuiY(pose[i][0]);
            float nx = mirrorX ? 1f - rx : rx;
            float ny = pose[i][1];
            Vector2 raw = new Vector2(nx, ny);

            _smoothedPositions[i] = _smoothedInitialized
                ? Vector2.Lerp(_smoothedPositions[i], raw, t)
                : raw;
        }
        _smoothedInitialized = true;

        float sw = Screen.width;
        float sh = Screen.height;

        // Draw connection lines first so dots render on top.
        if (showConnections && Event.current.type == EventType.Repaint)
            DrawConnectionsGUI(sw, sh);

        // Draw landmark dots.
        GUI.color = landmarkColor;
        float half = pointRadius;
        float size = pointRadius * 2f;

        for (int i = 0; i < _smoothedPositions.Length; i++)
        {
            float sx = _smoothedPositions[i].x * sw;
            float sy = _smoothedPositions[i].y * sh;

            GUI.DrawTexture(new Rect(sx - half, sy - half, size, size), _dotTexture);
        }

        GUI.color = Color.white; // restore
    }

    /// <summary>
    /// Draws pose skeleton connection lines using GL inside OnGUI.
    /// Must be called only during EventType.Repaint.
    /// When called inside OnGUI(), GL.LoadPixelMatrix() uses Y=0-at-top (same as IMGUI),
    /// so no Y flip is needed — matches the dot formula exactly.
    /// </summary>
    private void DrawConnectionsGUI(float sw, float sh)
    {
        if (_lineMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogWarning("[BodyLandmarkDebugOverlay] 'Hidden/Internal-Colored' shader not found. " +
                                 "Add it to Always Included Shaders for build support.");
                return;
            }
            _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _lineMaterial.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_Cull",      (int)UnityEngine.Rendering.CullMode.Off);
            _lineMaterial.SetInt("_ZWrite",    0);
        }

        _lineMaterial.SetPass(0);

        GL.PushMatrix();
        GL.LoadPixelMatrix();

        GL.Begin(GL.LINES);
        GL.Color(connectionColor);

        foreach (int[] pair in PoseConnections)
        {
            int a = pair[0], b = pair[1];
            if (a >= _smoothedPositions.Length || b >= _smoothedPositions.Length)
                continue;

            GL.Vertex3(_smoothedPositions[a].x * sw, _smoothedPositions[a].y * sh, 0f);
            GL.Vertex3(_smoothedPositions[b].x * sw, _smoothedPositions[b].y * sh, 0f);
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
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
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
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();
        return tex;
    }

    // ── Pose connection index pairs (35 edges) ────────────────────────────────
    // Sourced from PoseLandmarkListAnnotation.cs in homuler/MediaPipeUnityPlugin.
    // MediaPipe full-body pose: 33 landmarks (0–32).

    private static readonly int[][] PoseConnections = new int[][]
    {
        // Face
        new[]{0,1},  new[]{1,2},  new[]{2,3},  new[]{3,7},
        new[]{0,4},  new[]{4,5},  new[]{5,6},  new[]{6,8},

        // Mouth
        new[]{9,10},

        // Shoulders
        new[]{11,12},

        // Left arm
        new[]{11,13}, new[]{13,15}, new[]{15,17}, new[]{15,19}, new[]{15,21}, new[]{17,19},

        // Right arm
        new[]{12,14}, new[]{14,16}, new[]{16,18}, new[]{16,20}, new[]{16,22}, new[]{18,20},

        // Torso
        new[]{11,23}, new[]{12,24}, new[]{23,24},

        // Left leg
        new[]{23,25}, new[]{25,27}, new[]{27,29}, new[]{29,31}, new[]{27,31},

        // Right leg
        new[]{24,26}, new[]{26,28}, new[]{28,30}, new[]{30,32}, new[]{28,32},
    };
}
