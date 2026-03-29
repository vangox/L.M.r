// PersonSegmenter.cs
// Toggleable component that renders only the person (transparent background) using
// MediaPipe ImageSegmenter + selfie_multiclass_256x256.tflite.
//
// The multiclass model outputs 6 confidence masks:
//   0=background, 1=hair, 2=body-skin, 3=face-skin, 4=clothes, 5=accessories
// We combine channels 1-5 (max per pixel) into a single person mask.
//
// ── SETUP ─────────────────────────────────────────────────────────────────────
//  1. Download selfie_multiclass_256x256.tflite and place in Assets/StreamingAssets/:
//       https://storage.googleapis.com/mediapipe-models/image_segmenter/selfie_multiclass_256x256/float32/latest/selfie_multiclass_256x256.tflite
//
//  2. Canvas hierarchy:
//       Canvas
//       ├── BackgroundImage  (RawImage — your custom background; set lower sibling order)
//       ├── WebcamPreview    (existing webcam RawImage)
//       └── PersonImage      (new RawImage — PersonSegmenter writes to this at runtime)
//
//  3. Attach this component to any GameObject. Assign Inspector fields:
//       _dataProvider  → MediaPipeDataProvider
//       _personImage   → PersonImage RawImage
//       _webcamPreview → WebcamPreview RawImage (optional; enables auto-hide)
//
//  4. Toggle the component checkbox to enable/disable the feature.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.ImageSegmenter;

public class PersonSegmenter : MonoBehaviour
{
    // ── Inspector fields ──────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private MediaPipeDataProvider _dataProvider;
    [SerializeField] private UnityEngine.UI.RawImage _personImage;    // receives composited output
    [SerializeField] private UnityEngine.UI.RawImage _webcamPreview;  // existing preview (optional)

    [Header("Segmentation Model")]
    [SerializeField] private string _modelFilename = "selfie_multiclass_256x256.tflite";

    [Header("Performance")]
    [SerializeField] private BaseOptions.Delegate _delegate = BaseOptions.Delegate.CPU;
    [SerializeField] private bool _skipFrames = false;
    [SerializeField, Range(1, 10)] private int _processEveryNFrames = 3;

    [Header("Compositing")]
    [SerializeField, Range(0f, 1f)]   private float _maskThreshold = 0.5f;
    [SerializeField, Range(0f, 0.5f)] private float _maskSoftness  = 0.1f;
    [SerializeField]                  private bool  _mirrorX       = false;

    // ── Private state ─────────────────────────────────────────────────────────

    private ImageSegmenter _segmenter;
    private Texture2D      _maskTex;
    private RenderTexture  _outputRT;
    private Material       _material;
    private byte[]         _pixelBuffer;
    private long           _timestamp;

    // Written by the MediaPipe callback (worker thread); read on the main thread.
    private volatile float[] _pendingMask;
    private float[]          _channelBuf;   // temp buffer for reading individual class masks
    private volatile bool    _maskReady;
    private bool             _initialized;
    private int              _frameCounter;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private IEnumerator Start()
    {
        // Wait until MediaPipeDataProvider has started and the webcam is live.
        yield return new WaitUntil(() => _dataProvider != null && _dataProvider.Webcam != null
                                         && _dataProvider.Webcam.width > 16);

        var webcam = _dataProvider.Webcam;

        string modelPath = Path.Combine(Application.streamingAssetsPath, _modelFilename);
        if (!File.Exists(modelPath))
        {
            Debug.LogError($"[PersonSegmenter] Model not found: {modelPath}\n" +
                           "Download selfie_multiclass_256x256.tflite " +
                           "and place in Assets/StreamingAssets/.");
            yield break;
        }

        int w = webcam.width, h = webcam.height;
        _pixelBuffer = new byte[w * h * 4];
        _pendingMask = new float[w * h];
        _channelBuf  = new float[w * h];

        // RFloat texture: one float per pixel, matches TryReadChannelNormalized output.
        _maskTex = new Texture2D(w, h, TextureFormat.RFloat, false);

        // ARGB32 RenderTexture: webcam colour + person alpha.
        _outputRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        _outputRT.Create();

        // Material driven by our blit shader.
        var shader = Shader.Find("Custom/PersonSegmentation");
        if (shader == null)
        {
            Debug.LogError("[PersonSegmenter] Shader 'Custom/PersonSegmentation' not found. " +
                           "Ensure Assets/Shaders/PersonSegmentation.shader is in the project.");
            yield break;
        }
        _material = new Material(shader);
        _material.SetTexture("_MaskTex", _maskTex);

        // Create ImageSegmenter in LIVE_STREAM mode.
        // GPU delegate requires OpenGL; on Windows/D3D it will fail — fall back to CPU.
        var chosenDelegate = _delegate;
        try
        {
            _segmenter = CreateSegmenter(chosenDelegate, modelPath);
        }
        catch (System.Exception e)
        {
            if (chosenDelegate != BaseOptions.Delegate.CPU)
            {
                Debug.LogWarning($"[PersonSegmenter] {chosenDelegate} delegate failed ({e.Message}), falling back to CPU.");
                chosenDelegate = BaseOptions.Delegate.CPU;
                _segmenter = CreateSegmenter(chosenDelegate, modelPath);
            }
            else
            {
                throw;
            }
        }
        Debug.Log($"[PersonSegmenter] Delegate: {chosenDelegate}");

        // Point the RawImage at our output RT, matching the webcam's vertical orientation.
        _personImage.texture = _outputRT;
        // Graphics.Blit to a RenderTexture on D3D (Windows) flips Y in the output,
        // so negate the scale to compensate. If the webcam is also vertically mirrored,
        // the two flips cancel and we need +1 instead of -1.
        float sy = webcam.videoVerticallyMirrored ? 1f : -1f;
        _personImage.rectTransform.localScale = new Vector3(1f, sy, 1f);

        _initialized = true;
        Debug.Log($"[PersonSegmenter] Initialized — {w}×{h}");
    }

    private void OnEnable()
    {
        if (_personImage)   _personImage.gameObject.SetActive(true);
        if (_webcamPreview) _webcamPreview.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (_personImage)   _personImage.gameObject.SetActive(false);
        if (_webcamPreview) _webcamPreview.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!_initialized) return;
        var webcam = _dataProvider.Webcam;
        if (webcam == null || !webcam.didUpdateThisFrame) return;

        // Skip frames: only send to segmenter every N frames to save CPU/GPU.
        _frameCounter++;
        bool runThisFrame = !_skipFrames || (_frameCounter % _processEveryNFrames == 0);

        if (runThisFrame)
        {
            FillPixelBuffer(webcam, _pixelBuffer);
            int w = webcam.width, h = webcam.height;
            _timestamp += (long)(Time.deltaTime * 1000f);

            var buf = new NativeArray<byte>(_pixelBuffer, Allocator.Temp);
            var img = new Image(ImageFormat.Types.Format.Srgba, w, h, w * 4, buf);
            buf.Dispose();
            _segmenter.SegmentAsync(img, _timestamp);
        }

        // If a new mask arrived, upload it and blit webcam + mask → output RT.
        // Always blit every frame so the webcam feed stays smooth (reuses last mask).
        if (_maskReady)
        {
            _maskReady = false;
            _maskTex.SetPixelData<float>(_pendingMask, 0);
            _maskTex.Apply(false);
        }
        _material.SetFloat("_Threshold", _maskThreshold);
        _material.SetFloat("_Softness",  _maskSoftness);
        Graphics.Blit(webcam, _outputRT, _material);
    }

    private void OnDestroy()
    {
        _segmenter?.Close();
        _outputRT?.Release();
        if (_maskTex)  Destroy(_maskTex);
        if (_material) Destroy(_material);
    }

    private ImageSegmenter CreateSegmenter(BaseOptions.Delegate del, string modelPath)
    {
        return ImageSegmenter.CreateFromOptions(
            new ImageSegmenterOptions(
                baseOptions:           new BaseOptions(delegateCase: del,
                                                       modelAssetPath: modelPath),
                runningMode:           Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                outputConfidenceMasks: true,
                resultCallback:        OnSegmentResult));
    }

    // ── MediaPipe callback (worker thread) ────────────────────────────────────

    private void OnSegmentResult(ImageSegmenterResult result, Image image, long timestamp)
    {
        if (result.confidenceMasks == null || result.confidenceMasks.Count == 0)
            goto cleanup;

        if (result.confidenceMasks.Count == 1)
        {
            // Binary model (selfie_segmenter): single person confidence mask.
            result.confidenceMasks[0].TryReadChannelNormalized(
                0, _pendingMask,
                isHorizontallyFlipped: _mirrorX,
                isVerticallyFlipped:   true);
        }
        else
        {
            // Multiclass model: 0=background, 1=hair, 2=body-skin,
            // 3=face-skin, 4=clothes, 5=accessories.
            // Combine channels 1+ into a single person mask (max per pixel).
            int count = _pendingMask.Length;

            result.confidenceMasks[1].TryReadChannelNormalized(
                0, _pendingMask,
                isHorizontallyFlipped: _mirrorX,
                isVerticallyFlipped:   true);

            for (int ch = 2; ch < result.confidenceMasks.Count; ch++)
            {
                result.confidenceMasks[ch].TryReadChannelNormalized(
                    0, _channelBuf,
                    isHorizontallyFlipped: _mirrorX,
                    isVerticallyFlipped:   true);

                for (int i = 0; i < count; i++)
                {
                    if (_channelBuf[i] > _pendingMask[i])
                        _pendingMask[i] = _channelBuf[i];
                }
            }
        }

    cleanup:
        // Dispose all returned mask Images to avoid native memory leaks.
        foreach (var m in result.confidenceMasks ?? Enumerable.Empty<Image>())
            m.Dispose();

        _maskReady = true;   // volatile write — signals Update() on the main thread
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies the webcam frame into <paramref name="dst"/> as a flat RGBA byte array,
    /// flipping rows vertically (WebCamTexture bottom-left → MediaPipe top-left).
    /// </summary>
    private static void FillPixelBuffer(WebCamTexture webcam, byte[] dst)
    {
        var pixels = webcam.GetPixels32();
        int w = webcam.width, h = webcam.height;

        for (int row = 0; row < h; row++)
        {
            int srcRow = h - 1 - row;
            for (int col = 0; col < w; col++)
            {
                var p   = pixels[srcRow * w + col];
                int idx = (row * w + col) * 4;
                dst[idx]     = p.r;
                dst[idx + 1] = p.g;
                dst[idx + 2] = p.b;
                dst[idx + 3] = p.a;
            }
        }
    }
}
