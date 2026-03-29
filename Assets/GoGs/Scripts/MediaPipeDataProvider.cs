// MediaPipeDataProvider.cs
// Replaces DataManager.cs — runs MediaPipe entirely inside Unity using
// homuler/MediaPipeUnityPlugin, removing the Python + UDP dependency.
//
// ── INSTALLATION ──────────────────────────────────────────────────────────────
//  1. Download MediaPipeUnityPlugin-all.zip from the releases page:
//     https://github.com/homuler/MediaPipeUnityPlugin/releases
//  2. Unzip it and import the .unitypackage into this project:
//     Assets > Import Package > Custom Package
//
// ── MODEL FILES ───────────────────────────────────────────────────────────────
//  Download the following model files and place them in Assets/StreamingAssets/:
//  • pose_landmarker_full.task
//      https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task
//  • hand_landmarker.task
//      https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task
//
// ── SCENE SETUP ───────────────────────────────────────────────────────────────
//  1. Disable (or remove) the DataManager component on your manager GameObject.
//  2. Add this MediaPipeDataProvider component to the same GameObject.
//  3. Assign the Body and Hand fields in the Inspector.
//  4. In Edit > Project Settings > Player, make sure the WebCam permission
//     is enabled for your target platform.
//
// ── COMPILE NOTES ─────────────────────────────────────────────────────────────
//  If you see "The type or namespace 'Mediapipe' could not be found", the plugin
//  package has not been imported yet (see step 1 above).
//
//  If specific property names don't compile (e.g. delegateCase, modelAssetPath),
//  check the plugin version — v0.14+ uses the patterns written here.  The
//  equivalent constructor/property names are noted in comments beside each usage.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
using Unity.Collections;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.FaceLandmarker;

public enum InputSource { Webcam, Video, Image }

public enum SourceOrientation
{
    Landscape,       // No rotation needed (default)
    Portrait90CW,    // Content rotated 90° CW → apply 90° CCW correction
    Portrait90CCW,   // Content rotated 90° CCW → apply 90° CW correction
    Landscape180     // Content rotated 180° → apply 180° correction
}

/// <summary>
/// Drives body and hand pose estimation using MediaPipeUnityPlugin directly
/// inside Unity, replacing the external Python process and UDP socket.
///
/// Data flow:
///   WebCamTexture → ImageFrame → PoseLandmarker / HandLandmarker (LIVE_STREAM)
///       → callbacks → Body.pose_data / Hand.left_hand_data / Hand.right_hand_data
///
/// Body.cs and Hand.cs are completely unchanged — they still read float[][]
/// data from the same public fields as before.
/// </summary>
public class MediaPipeDataProvider : MonoBehaviour
{
    // ── Inspector fields ──────────────────────────────────────────────────────
    [Header("Input Source")]
    public InputSource inputSource = InputSource.Webcam;
    [Tooltip("Physical orientation of the input source. Use Portrait90CW if the camera/video is rotated clockwise.")]
    public SourceOrientation sourceOrientation = SourceOrientation.Landscape;

    [Header("Webcam  (inputSource = Webcam)")]
    [Tooltip("Index into WebCamTexture.devices. 0 = default/first camera.")]
    public int webcamIndex = 0;
    public int requestedWidth  = 1280;
    public int requestedHeight = 720;
    public int requestedFPS    = 30;

    [Header("Detection Resolution")]
    [Tooltip("Width for MediaPipe detection. 0 = use full source resolution.")]
    public int detectionWidth  = 640;
    [Tooltip("Height for MediaPipe detection. 0 = use full source resolution.")]
    public int detectionHeight = 360;

    [Header("Video Input  (inputSource = Video)")]
    [Tooltip("Drag a VideoClip asset here, or leave null and set videoFilePath.")]
    public VideoClip videoClip;
    [Tooltip("Absolute or StreamingAssets-relative path to a video file.")]
    public string videoFilePath = "";
    public bool loopVideo = true;

    [Header("Image Input  (inputSource = Image)")]
    [Tooltip("Drag a Texture2D asset here to use as a static input frame. Must have Read/Write enabled in import settings.")]
    public Texture2D staticImageTexture;

    [Header("Model Files  (Assets/StreamingAssets/)")]
    [Tooltip("Filename of the pose landmarker .task model in StreamingAssets.")]
    public string poseLandmarkerModel = "pose_landmarker_full.task";
    [Tooltip("Filename of the hand landmarker .task model in StreamingAssets.")]
    public string handLandmarkerModel = "hand_landmarker.task";
    [Tooltip("Filename of the face landmarker model in StreamingAssets. Falls back to package resource if not found.")]
    public string faceLandmarkerModel = "face_landmarker_v2_with_blendshapes.bytes";

    [Header("Pose Confidence")]
    [Range(0f, 1f)] public float poseDetectionConfidence = 0.5f;
    [Range(0f, 1f)] public float posePresenceConfidence  = 0.5f;
    [Range(0f, 1f)] public float poseTrackingConfidence  = 0.5f;

    [Header("Hand Confidence")]
    [Range(0f, 1f)] public float handDetectionConfidence = 0.5f;
    [Range(0f, 1f)] public float handPresenceConfidence  = 0.5f;
    [Range(0f, 1f)] public float handTrackingConfidence  = 0.5f;
    [Range(1, 2)]   public int   maxHands                = 2;

    [Header("Face Confidence")]
    [Range(0f, 1f)] public float faceDetectionConfidence = 0.5f;
    [Range(0f, 1f)] public float facePresenceConfidence  = 0.5f;
    [Range(0f, 1f)] public float faceTrackingConfidence  = 0.5f;

    [Header("Pose Output")]
    [Tooltip("Most recent pose landmarks (33 points), normalized [x, y, z]. Null when no person detected.")]
    public float[][] pose_data;

    [Header("Face Output")]
    [Tooltip("Most recent face landmarks (first detected face), normalized as [x, y, z].")]
    public float[][] face_data;

    [Header("Hand Output")]
    [Tooltip("Most recent left hand landmarks (21 points), normalized [x, y, z]. Null when no left hand detected.")]
    public float[][] left_hand_data;

    [Tooltip("Most recent right hand landmarks (21 points), normalized [x, y, z]. Null when no right hand detected.")]
    public float[][] right_hand_data;

    [Header("Camera Preview")]
    [Tooltip("Assign a UI RawImage to display the webcam feed in Play mode. Optional.")]
    [SerializeField] private UnityEngine.UI.RawImage _previewImage;
    [Tooltip("Flip the preview image horizontally.")]
    [SerializeField] private bool previewFlipX = false;
    [Tooltip("Flip the preview image vertically. For webcam, combines with the driver's videoVerticallyMirrored flag.")]
    [SerializeField] private bool previewFlipY = false;
    [Tooltip("Rotate the preview image (degrees, Z-axis). Use 90 or -90 for portrait sources.")]
    [SerializeField] private float previewRotation = 0f;

    // ── Private state ─────────────────────────────────────────────────────────

    private WebCamTexture   _webcam;
    public WebCamTexture Webcam => _webcam;

    private VideoPlayer   _videoPlayer;
    private RenderTexture _videoRenderTexture;
    private Texture2D     _videoReadbackTexture;
    private long          _lastVideoFrame = -1;

    private PoseLandmarker  _poseLandmarker;
    private HandLandmarker  _handLandmarker;
    private FaceLandmarker  _faceLandmarker;

    // Monotonically-increasing millisecond timestamp required by the Task API.
    // MediaPipe rejects frames whose timestamp is not strictly greater than the
    // previous one, so we accumulate instead of sampling Time.time directly.
    private long _timestamp;

    // Pre-allocated pixel buffer reused every frame — avoids per-frame GC alloc.
    // Contains RGBA bytes in top-left-origin order (MediaPipe convention).
    private byte[] _pixelBuffer;
    private byte[] _rotatedPixelBuffer;
    private int    _detW, _detH;  // resolved detection dimensions

    // GPU-side downscale: Blit the full-res webcam into this small RT, then readback
    // only _detW×_detH pixels instead of the full webcam resolution.
    // This avoids a 3.6 MB synchronous GPU→CPU readback that stalls the render pipeline.
    private RenderTexture _detectionRT;
    private Texture2D     _detectionReadback;

    // Double-buffer for display: WebCamTexture can update mid-render on Windows,
    // causing tearing / flickering on the RawImage.  Blitting to a stable RT each
    // frame and displaying that instead eliminates the artifact.
    private RenderTexture _displayRT;

    // Pending landmark data written by MediaPipe callbacks (worker thread) and
    // consumed on the Unity main thread in Update().
    // Using local-variable capture in FlushPendingResults() avoids races between
    // the null-check and the read without requiring a heavyweight lock.
    private volatile float[][] _pendingPose;
    private volatile float[][] _pendingLeft;
    private volatile float[][] _pendingRight;
    private volatile float[][] _pendingFace;
    private volatile bool      _handDataReady;
    private volatile bool      _faceDataReady;

    private bool _initialized;
    private bool _previewAutoFlipY;

    // Webcam frame rate diagnostic — logs actual camera delivery rate every few seconds.
    private int   _webcamFrameCount;
    private float _webcamFrameTimer;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start() => StartCoroutine(Initialize());

    private IEnumerator Initialize()
    {
        int w, h;

        switch (inputSource)
        {
            // ── Webcam ────────────────────────────────────────────────────────
            case InputSource.Webcam:
            {
                var devices = WebCamTexture.devices;
                if (devices.Length == 0)
                {
                    Debug.LogError("[MediaPipe] No webcam device found. " +
                                   "Make sure a camera is connected and webcam permission is granted.");
                    yield break;
                }

                int deviceIdx = Mathf.Clamp(webcamIndex, 0, devices.Length - 1);
                _webcam = new WebCamTexture(
                    devices[deviceIdx].name, requestedWidth, requestedHeight, requestedFPS 
                );
                _webcam.Play();

                // Unity reports 16×16 until the camera is actually ready.
                yield return new WaitUntil(
                    () => _webcam.width > 16 
                );

                _previewAutoFlipY = _webcam.videoVerticallyMirrored;

                // Double-buffer: blit the webcam into a stable RT each frame so the
                // RawImage never reads a half-updated WebCamTexture mid-render.
                _displayRT = new RenderTexture(
                    _webcam.width, _webcam.height, 0, RenderTextureFormat.ARGB32 
                );
                _displayRT.name = "WebcamDisplayRT";

                if (_previewImage != null)
                {
                    _previewImage.texture = _displayRT;
                }

                // Expose the stable display RT to the depth-occluder shader so it can
                // sample the background at the occluder pixels.
                Shader.SetGlobalTexture("_WebcamTex", _displayRT);
                Shader.SetGlobalFloat("_OccluderFlipY", _webcam.videoVerticallyMirrored ? -1f : 1f);

                w = _webcam.width;
                h = _webcam.height;
                Debug.Log($"[MediaPipe] Input: Webcam — {w}×{h} @ {devices[deviceIdx].name}");
                break;
            }

            // ── Video File ────────────────────────────────────────────────────
            case InputSource.Video:
            {
                _videoPlayer = gameObject.AddComponent<VideoPlayer>();
                _videoPlayer.playOnAwake   = false;
                _videoPlayer.renderMode    = VideoRenderMode.RenderTexture;
                _videoPlayer.isLooping     = loopVideo;
                _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

                if (videoClip != null)
                {
                    _videoPlayer.clip = videoClip;
                }
                else
                {
                    string vpath = Path.IsPathRooted(videoFilePath)
                        ? videoFilePath
                        : Path.Combine(Application.streamingAssetsPath, videoFilePath);
                    _videoPlayer.url = new Uri(
                        Path.GetFullPath(vpath) 
                    ).AbsoluteUri;
                }

                _videoPlayer.Prepare();
                yield return new WaitUntil(
                    () => _videoPlayer.isPrepared 
                );

                w = (int)_videoPlayer.width;
                h = (int)_videoPlayer.height;
                _videoRenderTexture   = new RenderTexture(
                    w, h, 0, RenderTextureFormat.ARGB32 
                );
                _videoReadbackTexture = new Texture2D(
                    w, h, TextureFormat.RGBA32, false 
                );
                _videoPlayer.targetTexture = _videoRenderTexture;

                if (_previewImage != null) _previewImage.texture = _videoRenderTexture;
                Shader.SetGlobalTexture("_WebcamTex", _videoRenderTexture);
                Shader.SetGlobalFloat("_OccluderFlipY", 1f);

                _videoPlayer.Play();
                Debug.Log($"[MediaPipe] Input: Video — {w}×{h}");
                break;
            }

            // ── Static Image ──────────────────────────────────────────────────
            case InputSource.Image:
            {
                if (staticImageTexture == null)
                {
                    Debug.LogError("[MediaPipe] No image assigned to staticImageTexture.");
                    yield break;
                }

                w = staticImageTexture.width;
                h = staticImageTexture.height;

                if (_previewImage != null) _previewImage.texture = staticImageTexture;
                Shader.SetGlobalTexture("_WebcamTex", staticImageTexture);
                Shader.SetGlobalFloat("_OccluderFlipY", 1f);
                Debug.Log($"[MediaPipe] Input: Image — {w}×{h}");
                break;
            }

            default:
                yield break;
        }

        // Tell the depth-occluder shader how to rotate its UV lookup to match the
        // corrected-upright coordinate space that MediaPipe landmarks are in.
        float occluderTexRotation = sourceOrientation switch
        {
            SourceOrientation.Portrait90CW  =>  90f,
            SourceOrientation.Portrait90CCW => -90f,
            SourceOrientation.Landscape180  => 180f,
            _                               =>   0f
        };
        Shader.SetGlobalFloat("_OccluderTexRotation", occluderTexRotation);

        // Resolve detection resolution (smaller than source for cheaper MediaPipe inference).
        _detW = (detectionWidth > 0 && detectionWidth < w) ? detectionWidth : w;
        _detH = (detectionHeight > 0 && detectionHeight < h) ? detectionHeight : h;

        // Pre-allocate the pixel buffer at detection resolution: _detW * _detH pixels × 4 bytes (RGBA).
        _pixelBuffer = new byte[_detW * _detH * 4];
        if (sourceOrientation != SourceOrientation.Landscape)
            _rotatedPixelBuffer = new byte[_detW * _detH * 4];  // same size — rotation preserves pixel count

        // GPU-side downscale resources: Blit full-res source into a small RT, then
        // ReadPixels only the detection-sized texture.  Avoids the expensive full-res
        // GetPixels32() call that stalls the rendering pipeline.
        _detectionRT       = new RenderTexture(
            _detW, _detH, 0, RenderTextureFormat.ARGB32 
        );
        _detectionReadback = new Texture2D(
            _detW, _detH, TextureFormat.RGBA32, false 
        );

        // ── Pose Landmarker ───────────────────────────────────────────────────
        string posePath = System.IO.Path.Combine(Application.streamingAssetsPath,"mediapipe", poseLandmarkerModel);
        _poseLandmarker = PoseLandmarker.CreateFromOptions(
            new PoseLandmarkerOptions(
                baseOptions: new BaseOptions(
                    delegateCase:   BaseOptions.Delegate.CPU,  // GPU not available on Windows
                    modelAssetPath: posePath),
                runningMode:               RunningMode.LIVE_STREAM,
                numPoses:                  1,
                minPoseDetectionConfidence: poseDetectionConfidence,
                minPosePresenceConfidence:  posePresenceConfidence,
                minTrackingConfidence:      poseTrackingConfidence,
                outputSegmentationMasks:    false,
                resultCallback:            OnPoseResult));

        // ── Hand Landmarker ───────────────────────────────────────────────────
        string handPath = System.IO.Path.Combine(Application.streamingAssetsPath,"mediapipe", handLandmarkerModel);
        _handLandmarker = HandLandmarker.CreateFromOptions(
            new HandLandmarkerOptions(
                baseOptions: new BaseOptions(
                    delegateCase: BaseOptions.Delegate.CPU,
                    modelAssetPath: handPath 
                ),
                runningMode: RunningMode.LIVE_STREAM,
                numHands: maxHands,
                minHandDetectionConfidence: handDetectionConfidence,
                minHandPresenceConfidence: handPresenceConfidence,
                minTrackingConfidence: handTrackingConfidence,
                resultCallback: OnHandResult 
            ));

        // Face model path: prefer StreamingAssets, fallback to package resources.
        string facePath = ResolveFaceModelPath(faceLandmarkerModel);
        if (!string.IsNullOrEmpty(facePath) && File.Exists(facePath))
        {
            _faceLandmarker = FaceLandmarker.CreateFromOptions(
                new FaceLandmarkerOptions(
                    baseOptions: new BaseOptions(
                        delegateCase: BaseOptions.Delegate.CPU,
                        modelAssetPath: facePath 
                    ),
                    runningMode: RunningMode.LIVE_STREAM,
                    numFaces: 1,
                    minFaceDetectionConfidence: faceDetectionConfidence,
                    minFacePresenceConfidence: facePresenceConfidence,
                    minTrackingConfidence: faceTrackingConfidence,
                    outputFaceBlendshapes: false,
                    outputFaceTransformationMatrixes: false,
                    resultCallback: OnFaceResult 
                ));
        }
        else
        {
            Debug.LogWarning("[MediaPipe] Face model not found. Face tracking disabled.");
        }

        _initialized = true;
        Debug.Log($"[MediaPipe] Initialized — source {w}×{h}, detection {_detW}×{_detH}");
    }

    private void Update()
    {
        if (!_initialized) return;
        ApplyPreviewTransform(_previewAutoFlipY);

        // Fill _pixelBuffer from the current input source.
        // All Unity texture types use bottom-left origin; FillPixelBufferFromPixels
        // flips rows vertically to match MediaPipe's top-left convention.
        int w, h;

        switch (inputSource)
        {
            case InputSource.Webcam:
                if (_webcam == null || !_webcam.didUpdateThisFrame) return;

                // Diagnostic: log actual camera frame delivery rate.
                _webcamFrameCount++;
                _webcamFrameTimer += Time.deltaTime;
                if (_webcamFrameTimer >= 3f)
                {
                    Debug.Log($"[MediaPipe] Webcam actual FPS: {_webcamFrameCount / _webcamFrameTimer:F1}");
                    _webcamFrameCount = 0;
                    _webcamFrameTimer = 0f;
                }

                // Snapshot the webcam into the stable display RT so the RawImage
                // never reads a half-updated texture mid-render.
                if (_displayRT != null)
                    Graphics.Blit(_webcam, _displayRT);

                // GPU-side downscale: Blit the full-res webcam into a small RT, then
                // readback only _detW×_detH pixels.  Much cheaper than GetPixels32()
                // on the full 1280×720 texture which stalls the rendering pipeline.
                Graphics.Blit(_webcam, _detectionRT);
                FillPixelBufferFromRenderTexture(_detectionRT, _detectionReadback, _pixelBuffer, _detW, _detH);
                w = _detW; h = _detH;
                break;

            case InputSource.Video:
                if (_videoPlayer == null || !_videoPlayer.isPlaying) return;
                long vframe = _videoPlayer.frame;
                if (vframe == _lastVideoFrame || vframe < 0) return;
                _lastVideoFrame = vframe;
                FillPixelBufferFromRenderTexture(_videoRenderTexture, _videoReadbackTexture, _pixelBuffer, _detW, _detH);
                w = _detW; h = _detH;
                break;

            case InputSource.Image:
                // Re-submit the same image every frame so downstream components
                // stay active (MediaPipe LIVE_STREAM only requires increasing timestamps).
                DownscalePixelsIntoBuffer(staticImageTexture.GetPixels32(),
                    staticImageTexture.width, staticImageTexture.height,
                    _pixelBuffer, _detW, _detH);
                w = _detW; h = _detH;
                break;

            default: return;
        }

        // Apply orientation correction if needed, then build Image instances.
        // DetectAsync takes ownership of the Image and disposes it after submission,
        // so each detector needs its own Image instance built from the same pixel data.
        byte[] activeBuffer = _pixelBuffer;
        int mpW = w, mpH = h;
        if (sourceOrientation != SourceOrientation.Landscape)
        {
            ApplyOrientationRotation(_pixelBuffer, w, h, _rotatedPixelBuffer, out mpW, out mpH);
            activeBuffer = _rotatedPixelBuffer;
        }

        var buf1 = new NativeArray<byte>(activeBuffer, Allocator.Temp);
        var poseImage = new Image(
            ImageFormat.Types.Format.Srgba, mpW, mpH, mpW * 4, buf1 
        );
        buf1.Dispose();

        var buf2 = new NativeArray<byte>(activeBuffer, Allocator.Temp);
        var handImage = new Image(
            ImageFormat.Types.Format.Srgba, mpW, mpH, mpW * 4, buf2 
        );
        buf2.Dispose();

        Image faceImage = null;
        if (_faceLandmarker != null)
        {
            var buf3 = new NativeArray<byte>(activeBuffer, Allocator.Temp);
            faceImage = new Image(
                ImageFormat.Types.Format.Srgba, mpW, mpH, mpW * 4, buf3 
            );
            buf3.Dispose();
        }

        // Accumulate timestamp in milliseconds — must be strictly increasing.
        _timestamp += (long)(Time.deltaTime * 1000f);

        // Submit frames to both detectors.  Results arrive asynchronously via
        // OnPoseResult / OnHandResult callbacks (potentially on a worker thread).
        _poseLandmarker.DetectAsync(poseImage, _timestamp);
        _handLandmarker.DetectAsync(handImage, _timestamp);
        _faceLandmarker?.DetectAsync(faceImage, _timestamp);

        // Transfer the most recent detected landmarks to Body and Hand.
        FlushPendingResults();
    }

    private void OnDestroy()
    {
        _webcam?.Stop();
        if (_videoPlayer != null) _videoPlayer.Stop();
        if (_videoRenderTexture != null) { _videoRenderTexture.Release(); Destroy(_videoRenderTexture); }
        if (_videoReadbackTexture != null) Destroy(_videoReadbackTexture);
        if (_displayRT != null) { _displayRT.Release(); Destroy(_displayRT); }
        if (_detectionRT != null) { _detectionRT.Release(); Destroy(_detectionRT); }
        if (_detectionReadback != null) Destroy(_detectionReadback);
        _poseLandmarker?.Close();
        _handLandmarker?.Close();
        _faceLandmarker?.Close();
    }

    // ── MediaPipe callbacks ───────────────────────────────────────────────────
    // These may be called from a worker thread in LIVE_STREAM mode.
    // Only write to volatile fields here — never touch Unity APIs.

    /// <summary>Receives pose landmark results from the PoseLandmarker task.</summary>
    private void OnPoseResult(PoseLandmarkerResult result, Image image, long timestamp)
    {
        // No pose detected this frame — clear pending data.
        if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
        {
            _pendingPose = null;
            return;
        }

        // poseLandmarks[0] = first (and only, since numPoses=1) detected person.
        // .landmarks = IReadOnlyList<NormalizedLandmark> with 33 elements.
        // Each NormalizedLandmark has .x, .y, .z in [0, 1] normalized range.
        var lms  = result.poseLandmarks[0].landmarks;
        var data = new float[lms.Count][];
        for (int i = 0; i < lms.Count; i++)
            data[i] = new[] { lms[i].x, lms[i].y, lms[i].z };

        _pendingPose = data;
    }

    /// <summary>Receives hand landmark results from the HandLandmarker task.</summary>
    private void OnHandResult(HandLandmarkerResult result, Image image, long timestamp)
    {
        float[][] left  = null;
        float[][] right = null;

        if (result.handLandmarks != null)
        {
            for (int h = 0; h < result.handLandmarks.Count; h++)
            {
                // handedness[h].categories[0].categoryName is "Left" or "Right".
                // Note: MediaPipe labels from the camera's perspective, which is
                // mirrored relative to the subject — the same behaviour as the
                // Python Holistic solution this replaces.
                string side = result.handedness?[h].categories[0].categoryName ?? string.Empty;

                var lms  = result.handLandmarks[h].landmarks;
                var data = new float[lms.Count][];
                for (int i = 0; i < lms.Count; i++)
                    data[i] = new[] { lms[i].x, lms[i].y, lms[i].z };

                if (side == "Left")  left  = data;
                else                 right = data;
            }
        }

        _pendingLeft  = left;
        _pendingRight = right;
        _handDataReady = true;
    }

    /// <summary>Receives face landmark results from the FaceLandmarker task.</summary>
    private void OnFaceResult(FaceLandmarkerResult result, Image image, long timestamp)
    {
        if (result.faceLandmarks == null || result.faceLandmarks.Count == 0)
        {
            _pendingFace = null;
            _faceDataReady = true;
            return;
        }

        var lms = result.faceLandmarks[0].landmarks;
        var data = new float[lms.Count][];
        for (int i = 0; i < lms.Count; i++)
            data[i] = new[] { lms[i].x, lms[i].y, lms[i].z };

        _pendingFace = data;
        _faceDataReady = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplyPreviewTransform(bool autoFlipY = false)
    {
        if (_previewImage == null) return;
        float sx = previewFlipX ? -1f : 1f;
        float sy = (autoFlipY ^ previewFlipY) ? -1f : 1f;
        _previewImage.rectTransform.localScale       = new Vector3(
            sx, sy, 1f 
        );
        _previewImage.rectTransform.localEulerAngles = new Vector3(
            0f, 0f, previewRotation 
        );
    }

    /// <summary>
    /// Copies a Color32 pixel array into <paramref name="dst"/> as flat RGBA bytes,
    /// flipping rows vertically (Unity bottom-left → MediaPipe top-left origin).
    /// </summary>
    private static void FillPixelBufferFromPixels(Color32[] pixels, int w, int h, byte[] dst)
    {
        for (int row = 0; row < h; row++)
        {
            // Source row index (bottom-to-top) → destination row index (top-to-bottom)
            int srcRow = (h - 1 - row);

            for (int col = 0; col < w; col++)
            {
                var  p   = pixels[srcRow * w + col];
                int  idx = (row * w + col) * 4;
                dst[idx]     = p.r;
                dst[idx + 1] = p.g;
                dst[idx + 2] = p.b;
                dst[idx + 3] = p.a;
            }
        }
    }

    /// <summary>
    /// Reads the current contents of <paramref name="rt"/> into <paramref name="readback"/>,
    /// then fills <paramref name="dst"/> at the requested detection resolution.
    /// </summary>
    private static void FillPixelBufferFromRenderTexture(
        RenderTexture rt, Texture2D readback, byte[] dst, int dstW, int dstH)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        readback.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = prev;
        DownscalePixelsIntoBuffer(readback.GetPixels32(), rt.width, rt.height, dst, dstW, dstH);
    }

    /// <summary>
    /// Downscales a Color32 pixel array from (srcW x srcH) to (dstW x dstH) into <paramref name="dst"/>,
    /// flipping rows vertically (Unity bottom-left origin → MediaPipe top-left origin).
    /// Uses nearest-neighbor sampling. When srcW==dstW and srcH==dstH, behaves identically
    /// to <see cref="FillPixelBufferFromPixels"/>.
    /// </summary>
    private static void DownscalePixelsIntoBuffer(
        Color32[] pixels, int srcW, int srcH,
        byte[] dst, int dstW, int dstH)
    {
        float xRatio = (float)srcW / dstW;
        float yRatio = (float)srcH / dstH;

        for (int dstRow = 0; dstRow < dstH; dstRow++)
        {
            // Vertical flip: dstRow 0 (top in MediaPipe) = top of Unity image (bottom-left origin)
            int srcRow = (int)((dstH - 1 - dstRow) * yRatio);

            for (int dstCol = 0; dstCol < dstW; dstCol++)
            {
                int srcCol = (int)(dstCol * xRatio);
                var  p   = pixels[srcRow * srcW + srcCol];
                int  idx = (dstRow * dstW + dstCol) * 4;
                dst[idx]     = p.r;
                dst[idx + 1] = p.g;
                dst[idx + 2] = p.b;
                dst[idx + 3] = p.a;
            }
        }
    }

    /// <summary>
    /// Transfers any pending MediaPipe results into Body.pose_data and
    /// Hand.left_hand_data / right_hand_data on the Unity main thread.
    ///
    /// Reads each volatile field into a local variable first so the null-check
    /// and the subsequent assignment are consistent even if a callback fires
    /// between them.
    /// </summary>
    private void FlushPendingResults()
    {
        // ── Pose ──────────────────────────────────────────────────────────────
        var pose = _pendingPose;
        pose_data = pose;

        // ── Hands ─────────────────────────────────────────────────────────────
        if (_handDataReady)
        {
            var left  = _pendingLeft;
            var right = _pendingRight;
            left_hand_data  = left;
            right_hand_data = right;
            _handDataReady = false;
        }

        // ── Face ──────────────────────────────────────────────────────────────
        // Face data is used by HeadAccessory for more stable head rotation.
        if (_faceDataReady)
        {
            var face = _pendingFace;
            face_data = face;
            _faceDataReady = false;
        }
    }

    /// <summary>
    /// Rotates <paramref name="src"/> (srcW×srcH, top-left origin) into <paramref name="dst"/>
    /// according to <see cref="sourceOrientation"/> and returns the corrected dimensions.
    /// </summary>
    private void ApplyOrientationRotation(byte[] src, int srcW, int srcH, byte[] dst,
        out int dstW, out int dstH)
    {
        switch (sourceOrientation)
        {
            case SourceOrientation.Portrait90CW:
                // Content rotated 90° CW → rotate image 90° CCW to correct
                // 90° CCW: dst(dstCol, dstRow) = src(srcW-1-dstRow, dstCol)
                dstW = srcH; dstH = srcW;
                for (int dstRow = 0; dstRow < dstH; dstRow++)
                for (int dstCol = 0; dstCol < dstW; dstCol++)
                {
                    int si = ((dstCol) * srcW + (srcW - 1 - dstRow)) * 4;
                    int di = (dstRow * dstW + dstCol) * 4;
                    dst[di] = src[si]; dst[di+1] = src[si+1];
                    dst[di+2] = src[si+2]; dst[di+3] = src[si+3];
                }
                break;

            case SourceOrientation.Portrait90CCW:
                // Content rotated 90° CCW → rotate image 90° CW to correct
                // 90° CW: dst(dstCol, dstRow) = src(dstRow, srcH-1-dstCol)
                dstW = srcH; dstH = srcW;
                for (int dstRow = 0; dstRow < dstH; dstRow++)
                for (int dstCol = 0; dstCol < dstW; dstCol++)
                {
                    int si = ((srcH - 1 - dstCol) * srcW + dstRow) * 4;
                    int di = (dstRow * dstW + dstCol) * 4;
                    dst[di] = src[si]; dst[di+1] = src[si+1];
                    dst[di+2] = src[si+2]; dst[di+3] = src[si+3];
                }
                break;

            case SourceOrientation.Landscape180:
                // Content rotated 180° → rotate image 180° to correct
                // 180°: dst(dstCol, dstRow) = src(srcW-1-dstCol, srcH-1-dstRow)
                dstW = srcW; dstH = srcH;
                for (int dstRow = 0; dstRow < dstH; dstRow++)
                for (int dstCol = 0; dstCol < dstW; dstCol++)
                {
                    int si = ((srcH - 1 - dstRow) * srcW + (srcW - 1 - dstCol)) * 4;
                    int di = (dstRow * dstW + dstCol) * 4;
                    dst[di] = src[si]; dst[di+1] = src[si+1];
                    dst[di+2] = src[si+2]; dst[di+3] = src[si+3];
                }
                break;

            default:
                dstW = srcW; dstH = srcH;
                break;
        }
    }

    private static string ResolveFaceModelPath(string modelFilename)
    {
        string streamingPath = Path.Combine(Application.streamingAssetsPath,"mediapipe", modelFilename);
        if (File.Exists(streamingPath))
            return streamingPath;

        // Editor-friendly fallback to plugin package resources.
        return Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "..",
            "Packages",
            "com.github.homuler.mediapipe",
            "PackageResources",
            "MediaPipe",
            "face_landmarker_v2_with_blendshapes.bytes"));
    }
}
