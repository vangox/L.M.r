// Assets/Scripts/HandGestureDetector.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class HandGestureDetector : MonoBehaviour
{
    public enum HandMode { LeftOnly, RightOnly, Both }

    [Header("References")]
    [Tooltip("Auto-discovered if null.")]
    public MediaPipeDataProvider dataProvider;

    [Header("Events")]
    public UnityEvent OnSwipeLeft;
    public UnityEvent OnSwipeRight;

    [Header("Detection Parameters")]
    [Range(0.05f, 0.8f)]
    [Tooltip("Min horizontal wrist displacement (0\u20131 image space) to register as a swipe.")]
    public float swipeDistanceThreshold = 0.20f;

    [Range(0.2f, 2.0f)]
    [Tooltip("Max seconds for the swipe arc to complete.")]
    public float maxDuration = 0.6f;

    [Range(0.1f, 1.0f)]
    [Tooltip("|deltaY| must be less than verticalRatio \u00d7 |deltaX|. Rejects diagonals.")]
    public float verticalRatio = 0.5f;

    [Range(0.05f, 2.0f)]
    [Tooltip("Minimum peak wrist X velocity (normalized units/sec) within the window.")]
    public float minSpeed = 0.3f;

    [Range(0.1f, 3.0f)]
    [Tooltip("Seconds to block further gestures after one fires. Shared by both hands.")]
    public float cooldown = 0.8f;

    [Header("Hand Selection")]
    public HandMode trackHands = HandMode.Both;

    [Header("Mirror")]
    [Tooltip("Flip X to match a mirrored webcam display. Must match FaceAnchor/FaceMeshOccluder settings.")]
    public bool mirrorX = true;

    [Header("Hand Pointer")]
    [Tooltip("Enable hand-to-screen pointer mapping. Independent from swipe detection.")]
    public bool enablePointer = false;

    [Tooltip("UI RectTransform to move as the visual cursor (Screen Space - Overlay canvas). Leave null to skip visual update.")]
    public RectTransform pointerElement;

    [Tooltip("Which hand drives the pointer. Can differ from swipe trackHands.")]
    public HandMode pointerHand = HandMode.RightOnly;

    [Range(0, 20)]
    [Tooltip("MediaPipe hand landmark to track (0=wrist, 8=index fingertip, 12=middle tip).")]
    public int pointerLandmark = 8;

    [Range(1f, 30f)]
    [Tooltip("Lerp smoothing speed. Higher = more responsive, lower = smoother trail.")]
    public float smoothing = 10f;

    [Tooltip("Hide pointerElement when the tracked hand is not visible.")]
    public bool hideWhenNoHand = true;

    [Tooltip("Also warp the OS mouse cursor to match. Requires New Input System (com.unity.inputsystem).")]
    public bool emulateMouse = false;

    [Header("Hand Pointer – Workspace Mapping")]
    [Tooltip("Hand workspace min in MediaPipe normalized space (post-mirrorX for X). " +
             "X: leftmost visible hand pos, Y: topmost visible hand pos (MediaPipe Y=0 is top). " +
             "Decrease inputMin.y if the pointer can't reach the top of screen.")]
    public Vector2 inputMin = Vector2.zero;

    [Tooltip("Hand workspace max in MediaPipe normalized space (post-mirrorX for X). " +
             "X: rightmost visible hand pos, Y: bottommost visible hand pos before hand exits camera. " +
             "Decrease inputMax.y (e.g. 0.6–0.7) so the pointer reaches the bottom of screen.")]
    public Vector2 inputMax = Vector2.one;

    private struct TimedSample { public float x, y, time; }

    private readonly Queue<TimedSample> _leftBuffer  = new Queue<TimedSample>();
    private readonly Queue<TimedSample> _rightBuffer = new Queue<TimedSample>();
    private float _cooldownRemaining;
    private Vector2 _pointerScreenPos;

    private void OnDisable()
    {
        _leftBuffer.Clear();
        _rightBuffer.Clear();
        _cooldownRemaining = 0f;
        _pointerScreenPos = Vector2.zero;
    }

    private void Update()
    {
        if (dataProvider == null)
            dataProvider = FindFirstObjectByType<MediaPipeDataProvider>();
        if (dataProvider == null) return;

        if (_cooldownRemaining > 0f) _cooldownRemaining -= Time.deltaTime;

        float now = Time.time;
        if (trackHands != HandMode.RightOnly) UpdateBuffer(_leftBuffer,  dataProvider.left_hand_data,  now);
        if (trackHands != HandMode.LeftOnly)  UpdateBuffer(_rightBuffer, dataProvider.right_hand_data, now);

        if (_cooldownRemaining > 0f) return;

        if (trackHands != HandMode.RightOnly && EvaluateSwipe(_leftBuffer,  out int ld)) { FireSwipe(ld); return; }
        if (trackHands != HandMode.LeftOnly  && EvaluateSwipe(_rightBuffer, out int rd)) { FireSwipe(rd); }

        if (enablePointer) UpdatePointer();
    }

    private void UpdateBuffer(Queue<TimedSample> buf, float[][] data, float now)
    {
        bool present = data != null && data.Length >= 1 && data[0] != null && data[0].Length >= 2;
        if (!present) { buf.Clear(); return; }

        float x = mirrorX ? 1f - data[0][0] : data[0][0];
        float y = data[0][1];
        buf.Enqueue(new TimedSample { x = x, y = y, time = now });

        while (buf.Count > 1 && (now - buf.Peek().time > maxDuration))
            buf.Dequeue();
    }

    private bool EvaluateSwipe(Queue<TimedSample> buf, out int direction)
    {
        direction = 0;
        if (buf.Count < 2) return false;

        var samples = buf.ToArray();
        int n = samples.Length;
        float elapsed = samples[n-1].time - samples[0].time;
        if (elapsed <= 0f || elapsed > maxDuration) return false;

        float dx = samples[n-1].x - samples[0].x;
        float dy = samples[n-1].y - samples[0].y;
        if (Mathf.Abs(dx) < swipeDistanceThreshold) return false;
        if (Mathf.Abs(dy) > verticalRatio * Mathf.Abs(dx)) return false;

        float peak = 0f;
        for (int i = 1; i < n; i++)
        {
            float dt = samples[i].time - samples[i-1].time;
            if (dt <= 0f) continue;
            float s = Mathf.Abs(samples[i].x - samples[i-1].x) / dt;
            if (s > peak) peak = s;
        }
        if (peak < minSpeed) return false;

        direction = dx > 0f ? 1 : -1;   // +1 = SwipeLeft, -1 = SwipeRight
        buf.Clear();
        return true;
    }

    private void FireSwipe(int direction)
    {
        _cooldownRemaining = cooldown;
        if (direction > 0) { Debug.Log("[HandGestureDetector] SwipeLeft");  OnSwipeLeft?.Invoke(); }
        else               { Debug.Log("[HandGestureDetector] SwipeRight"); OnSwipeRight?.Invoke(); }
    }

    private void UpdatePointer()
    {
        // Select hand data: prefer the configured hand, fall back to the other if Both
        float[][] data = null;
        if (pointerHand == HandMode.RightOnly || pointerHand == HandMode.Both)
            data = dataProvider.right_hand_data;
        if (data == null && (pointerHand == HandMode.LeftOnly || pointerHand == HandMode.Both))
            data = dataProvider.left_hand_data;

        int pL = pointerLandmark;
        bool present = data != null && data.Length > pL && data[pL] != null && data[pL].Length >= 2;

        if (!present)
        {
            if (hideWhenNoHand && pointerElement != null)
                pointerElement.gameObject.SetActive(false);
            return;
        }

        if (pointerElement != null && hideWhenNoHand)
            pointerElement.gameObject.SetActive(true);

        float x = mirrorX ? 1f - data[pL][0] : data[pL][0];
        float y = data[pL][1];
        // Remap from hand workspace to [0,1]; clamp so pointer stays on screen
        float xNorm = Mathf.Clamp01(Mathf.InverseLerp(inputMin.x, inputMax.x, x));
        float yNorm = Mathf.Clamp01(Mathf.InverseLerp(inputMin.y, inputMax.y, y));
        // MediaPipe Y=0 is top; screen Y=0 is bottom → flip Y
        var target = new Vector2(xNorm * Screen.width, (1f - yNorm) * Screen.height);
        _pointerScreenPos = Vector2.Lerp(_pointerScreenPos, target, Time.deltaTime * smoothing);

        if (pointerElement != null)
            pointerElement.position = new Vector3(_pointerScreenPos.x, _pointerScreenPos.y, 0f);

#if ENABLE_INPUT_SYSTEM
        if (emulateMouse)
            UnityEngine.InputSystem.Mouse.current?.WarpCursorPosition(_pointerScreenPos);
#endif
    }
}
