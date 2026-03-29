// WebcamFillCover.cs
// Sets the UV rect on a RawImage so the webcam texture fills the parent rect
// using "cover" behaviour: scale to fill, crop what overflows. No stretching.
//
// Also exposes CropRect (the current uvRect) plus static remap helpers so that
// debug overlays and 3D anchors can convert webcam-normalized landmark coordinates
// into the cropped display space.
//
// Attach to the same GameObject as the RawImage (webcam preview or PersonSegmenter output).

using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class WebcamFillCover : MonoBehaviour
{
    /// <summary>
    /// The current UV crop rect applied to the RawImage.
    /// Defaults to identity (no crop) until Update() runs.
    /// X/Y/Width/Height are in Unity UV space (Y=0=bottom of texture).
    /// </summary>
    public static Rect CropRect = new Rect(0f, 0f, 1f, 1f);

    // ── Coordinate remap helpers ──────────────────────────────────────────────

    /// <summary>
    /// Remaps a webcam-normalized X coordinate (0=left, 1=right) to the
    /// equivalent position in the cropped display space (0=left edge of crop,
    /// 1=right edge of crop).
    /// The crop is always centered, so this is commutative with mirror:
    ///   RemapX(1 - x) == 1 - RemapX(x)
    /// </summary>
    public static float RemapX(float webcamNx)
        => (webcamNx - CropRect.x) / CropRect.width;

    /// <summary>
    /// Remaps a webcam-normalized Y in Unity Viewport space (0=bottom, 1=top)
    /// to the equivalent position in the cropped display viewport space.
    /// Use this before calling Camera.ViewportToWorldPoint.
    /// </summary>
    public static float RemapViewportY(float viewportY)
        => (viewportY - CropRect.y) / CropRect.height;

    /// <summary>
    /// Remaps a webcam-normalized Y in MediaPipe / OnGUI space (0=top, 1=bottom)
    /// to the equivalent position in the cropped display (0=top of crop, 1=bottom).
    /// Use this in OnGUI() when mapping landmarks to screen pixels.
    /// </summary>
    public static float RemapGuiY(float mediapipeY)
    {
        float mediapipeTop = 1f - (CropRect.y + CropRect.height);
        return (mediapipeY - mediapipeTop) / CropRect.height;
    }

    // ── MonoBehaviour ─────────────────────────────────────────────────────────

    private RawImage _rawImage;

    private void Awake()
    {
        _rawImage = GetComponent<RawImage>();
    }

    private void Update()
    {
        var tex = _rawImage.texture;
        if (tex == null) return;

        // Container size (the rect this RawImage occupies on screen).
        var rect = _rawImage.rectTransform.rect;
        float containerW = rect.width;
        float containerH = rect.height;
        if (containerW <= 0 || containerH <= 0) return;

        float texW = tex.width;
        float texH = tex.height;

        float containerAspect = containerW / containerH;
        float texAspect       = texW / texH;

        float uvW, uvH, uvX, uvY;

        if (texAspect > containerAspect)
        {
            // Texture is wider than the container → fill by height, crop width.
            uvH = 1f;
            uvW = containerAspect / texAspect;
            uvX = (1f - uvW) * 0.5f;
            uvY = 0f;
        }
        else
        {
            // Texture is taller than the container → fill by width, crop height.
            uvW = 1f;
            uvH = texAspect / containerAspect;
            uvY = (1f - uvH) * 0.5f;
            uvX = 0f;
        }

        var newRect = new Rect(uvX, uvY, uvW, uvH);
        if (_rawImage.uvRect != newRect)
            _rawImage.uvRect = newRect;
        CropRect = newRect;
    }
}
