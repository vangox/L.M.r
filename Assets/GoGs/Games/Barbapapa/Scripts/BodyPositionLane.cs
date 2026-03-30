using UnityEngine;

/// <summary>
/// Reads the player's body X position from MediaPipe pose landmarks and maps it
/// to one of three lanes (left / center / right), calling BarbapapController.SetLane()
/// each frame. Works alongside keyboard and swipe controls.
///
/// Coordinate transform mirrors BodyLandmarkDebugOverlay: applies WebcamFillCover.RemapGuiY()
/// to account for camera UV crop, then optionally flips for a mirrored webcam.
/// </summary>
public class BodyPositionLane : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-discovered if null.")]
    public MediaPipeDataProvider DataProvider;
    [Tooltip("Auto-discovered if null.")]
    public BarbapapController Player;

    [Header("Body Tracking")]
    public bool EnableBodyTracking = true;

    [Tooltip("Flip X axis — must match the mirrorX setting on HandGestureDetector / BodyLandmarkDebugOverlay.")]
    public bool MirrorX = true;

    [Header("Zone Thresholds  (0 = left edge, 1 = right edge)")]
    [Range(0.1f, 0.49f)]
    [Tooltip("Body X below this value → left lane.")]
    public float LeftThreshold = 0.40f;

    [Range(0.51f, 0.9f)]
    [Tooltip("Body X above this value → right lane.")]
    public float RightThreshold = 0.60f;

    [Range(0f, 0.1f)]
    [Tooltip("Dead-band around each threshold to prevent jitter when standing near a boundary.")]
    public float HysteresisBand = 0.03f;

    private int _currentBodyLane = 1; // last confirmed lane

    private void Awake()
    {
        if (DataProvider == null)
            DataProvider = FindFirstObjectByType<MediaPipeDataProvider>();
        if (Player == null)
            Player = FindFirstObjectByType<BarbapapController>();
    }

    private void Update()
    {
        if (!EnableBodyTracking) return;
        if (DataProvider == null || Player == null) return;

        float[][] pose = DataProvider.pose_data;
        if (pose == null || pose.Length < 13) return;

        // ── Compute body-centre X ────────────────────────────────────────────
        // Prefer mid-point of shoulders (11, 12) for stability.
        // Fall back to nose (0) if either shoulder is missing.
        float raw;
        bool leftShoulder  = pose[11] != null && pose[11].Length >= 2;
        bool rightShoulder = pose[12] != null && pose[12].Length >= 2;

        if (leftShoulder && rightShoulder)
            raw = (pose[11][0] + pose[12][0]) * 0.5f;
        else if (pose[0] != null && pose[0].Length >= 2)
            raw = pose[0][0];
        else
            return; // no usable data this frame

        // ── Coordinate transform (matches BodyLandmarkDebugOverlay) ──────────
        float remapped = WebcamFillCover.RemapGuiY(raw);
        float bodyX    = MirrorX ? 1f - remapped : remapped;

        // ── Zone → lane with hysteresis ──────────────────────────────────────
        int targetLane;

        if (bodyX < LeftThreshold - (_currentBodyLane == 0 ? 0f : HysteresisBand))
            targetLane = 0;
        else if (bodyX > RightThreshold + (_currentBodyLane == 2 ? 0f : HysteresisBand))
            targetLane = 2;
        else if (bodyX >= LeftThreshold  + HysteresisBand &&
                 bodyX <= RightThreshold - HysteresisBand)
            targetLane = 1;
        else
            targetLane = _currentBodyLane; // stay put — inside dead-band

        if (targetLane != _currentBodyLane)
        {
            _currentBodyLane = targetLane;
            Player.SetLane(targetLane);
        }
    }

    /// <summary>Reset tracked lane to center (call alongside BarbapapController.ResetPlayer).</summary>
    public void ResetLane()
    {
        _currentBodyLane = 1;
    }
}
