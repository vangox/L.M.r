using UnityEngine;

/// <summary>
/// Toggleable debug overlay that renders all 468 MediaPipe face landmarks as
/// green dots and optional face-mesh connection lines on top of the game view.
///
/// Drawing is done entirely in OnGUI() (IMGUI phase), which is URP-safe and
/// requires no Camera.onPostRender or custom RenderPass setup.
///
/// Scene setup:
///   1. Add this component to any GameObject in the scene (e.g. DataManager).
///   2. Leave dataProvider null — it will be found automatically — or drag-assign.
///   3. Enter Play mode and toggle Show Face Landmarks in the Inspector.
///   4. Ensure mirrorX matches HeadAccessory.mirrorX (both default true).
///
/// Coordinate notes:
///   Dots  (GUI.DrawTexture) : OnGUI Y=0 is top  → same as MediaPipe → sy = ny * sh
///   Lines (GL.LoadPixelMatrix inside OnGUI): also Y=0 at top in this context → gy = ny * sh
/// </summary>
public class FaceLandmarkDebugOverlay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found if left null.")]
    public MediaPipeDataProvider dataProvider;

    [Header("Display")]
    public bool showFaceLandmarks = false;
    public bool showConnections   = true;

    [Range(1f, 15f)] public float pointRadius = 4f;
    public Color landmarkColor   = Color.green;
    public Color connectionColor = new Color(0f, 1f, 0f, 0.4f);

    [Header("Mirror")]
    [Tooltip("Mirror X axis — must match HeadAccessory.mirrorX.")]
    public bool mirrorX = true;

    // ── Private state ─────────────────────────────────────────────────────────

    private Texture2D _dotTexture;
    private Material  _lineMaterial;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        if (dataProvider == null)
            dataProvider = FindFirstObjectByType<MediaPipeDataProvider>();

        _dotTexture = CreateCircleTexture(16);
    }

    private void OnDestroy()
    {
        if (_dotTexture  != null) Destroy(_dotTexture);
        if (_lineMaterial != null) Destroy(_lineMaterial);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!showFaceLandmarks)
            return;

        if (dataProvider == null || dataProvider.face_data == null)
            return;

        float[][] face = dataProvider.face_data;
        if (face.Length == 0)
            return;

        float sw = Screen.width;
        float sh = Screen.height;

        // Draw connection lines first so dots render on top.
        if (showConnections && Event.current.type == EventType.Repaint)
            DrawConnectionsGUI(face, sw, sh);

        // Draw landmark dots.
        GUI.color = landmarkColor;
        float half = pointRadius;
        float size = pointRadius * 2f;

        for (int i = 0; i < face.Length; i++)
        {
            if (face[i] == null || face[i].Length < 2)
                continue;

            // Remap X for UV crop: portrait X ↔ landscape Y (the cropped axis).
            float nx = WebcamFillCover.RemapGuiY(face[i][0]);
            float ny = face[i][1];

            // OnGUI Y=0 is top — matches MediaPipe Y=0=top — no flip needed.
            float sx = mirrorX ? (1f - nx) * sw : nx * sw;
            float sy = ny * sh;

            GUI.DrawTexture(new Rect(sx - half, sy - half, size, size), _dotTexture);
        }

        GUI.color = Color.white; // restore
    }

    /// <summary>
    /// Draws face-mesh connection lines using GL inside OnGUI.
    /// Must be called only during EventType.Repaint.
    /// When called inside OnGUI(), GL.LoadPixelMatrix() uses Y=0-at-top (same as IMGUI),
    /// so no Y flip is needed — matches the dot formula exactly.
    /// </summary>
    private void DrawConnectionsGUI(float[][] face, float sw, float sh)
    {
        if (_lineMaterial == null)
        {
            // Hidden/Internal-Colored is always available in the Editor.
            // For builds: add it to Project Settings > Graphics > Always Included Shaders.
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogWarning("[FaceLandmarkDebugOverlay] 'Hidden/Internal-Colored' shader not found. " +
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
        GL.LoadPixelMatrix(); // GL pixel coords: Y=0 at bottom

        GL.Begin(GL.LINES);
        GL.Color(connectionColor);

        foreach (int[] pair in FaceConnections)
        {
            int a = pair[0], b = pair[1];
            if (a >= face.Length || b >= face.Length)
                continue;
            if (face[a] == null || face[a].Length < 2 || face[b] == null || face[b].Length < 2)
                continue;

            float rax = WebcamFillCover.RemapGuiY(face[a][0]);
            float ax = mirrorX ? (1f - rax) * sw : rax * sw;
            float ay = face[a][1] * sh;

            float rbx = WebcamFillCover.RemapGuiY(face[b][0]);
            float bx = mirrorX ? (1f - rbx) * sw : rbx * sw;
            float by = face[b][1] * sh;

            GL.Vertex3(ax, ay, 0f);
            GL.Vertex3(bx, by, 0f);
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
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
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

    // ── Face connection index pairs (124 total) ───────────────────────────────
    // Sourced from the MediaPipe face_mesh_connections reference and
    // FaceLandmarkListAnnotation.cs in homuler/MediaPipeUnityPlugin.

    private static readonly int[][] FaceConnections = new int[][]
    {
        // Face Oval (36)
        new[]{10,338}, new[]{338,297}, new[]{297,332}, new[]{332,284},
        new[]{284,251}, new[]{251,389}, new[]{389,356}, new[]{356,454},
        new[]{454,323}, new[]{323,361}, new[]{361,288}, new[]{288,397},
        new[]{397,365}, new[]{365,379}, new[]{379,378}, new[]{378,400},
        new[]{400,377}, new[]{377,152}, new[]{152,148}, new[]{148,176},
        new[]{176,149}, new[]{149,150}, new[]{150,136}, new[]{136,172},
        new[]{172,58},  new[]{58,132},  new[]{132,93},  new[]{93,234},
        new[]{234,127}, new[]{127,162}, new[]{162,21},  new[]{21,54},
        new[]{54,103},  new[]{103,67},  new[]{67,109},  new[]{109,10},

        // Left Eye (16)
        new[]{33,7},   new[]{7,163},   new[]{163,144}, new[]{144,145},
        new[]{145,153},new[]{153,154}, new[]{154,155}, new[]{155,133},
        new[]{33,246}, new[]{246,161}, new[]{161,160}, new[]{160,159},
        new[]{159,158},new[]{158,157}, new[]{157,173}, new[]{173,133},

        // Left Eyebrow (8)
        new[]{46,53},  new[]{53,52},   new[]{52,65},   new[]{65,55},
        new[]{70,63},  new[]{63,105},  new[]{105,66},  new[]{66,107},

        // Right Eye (16)
        new[]{263,249},new[]{249,390}, new[]{390,373}, new[]{373,374},
        new[]{374,380},new[]{380,381}, new[]{381,382}, new[]{382,362},
        new[]{263,466},new[]{466,388}, new[]{388,387}, new[]{387,386},
        new[]{386,385},new[]{385,384}, new[]{384,398}, new[]{398,362},

        // Right Eyebrow (8)
        new[]{276,283},new[]{283,282}, new[]{282,295}, new[]{295,285},
        new[]{300,293},new[]{293,334}, new[]{334,296}, new[]{296,336},

        // Lips Inner (20)
        new[]{78,95},  new[]{95,88},   new[]{88,178},  new[]{178,87},
        new[]{87,14},  new[]{14,317},  new[]{317,402}, new[]{402,318},
        new[]{318,324},new[]{324,308},
        new[]{78,191}, new[]{191,80},  new[]{80,81},   new[]{81,82},
        new[]{82,13},  new[]{13,312},  new[]{312,311}, new[]{311,310},
        new[]{310,415},new[]{415,308},

        // Lips Outer (20)
        new[]{61,146}, new[]{146,91},  new[]{91,181},  new[]{181,84},
        new[]{84,17},  new[]{17,314},  new[]{314,405}, new[]{405,321},
        new[]{321,375},new[]{375,291},
        new[]{61,185}, new[]{185,40},  new[]{40,39},   new[]{39,37},
        new[]{37,0},   new[]{0,267},   new[]{267,269}, new[]{269,270},
        new[]{270,409},new[]{409,291},
    };
}
