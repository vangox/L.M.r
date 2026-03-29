using UnityEngine;

/// <summary>
/// Drives this transform to match the tracked torso — position, rotation, and scale.
/// Designed for garment try-on: place a humanoid-rigged jacket/shirt as a child.
/// Body.cs and Hand.cs on the child read worldLandmarks for bone rotation.
///
/// Follows the FaceAnchor pattern (constant scale, perspective handles distance)
/// with BodyAnchor's world-landmark projection for Body.cs/Hand.cs compatibility.
///
/// Scene setup:
///   1. Create an empty GameObject named "TorsoAnchor" at scene root.
///   2. Attach this component.
///   3. Drag garment model (humanoid-rigged FBX with Animator) as a child.
///   4. Add Body.cs to garment → assign torsoAnchor in Inspector.
///   5. Add Hand.cs to garment → assign torsoAnchor in Inspector.
///   6. Tune sizeScale, anchorBlend, and child localPosition/localRotation.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1)]
public class TorsoAnchor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found via FindFirstObjectByType if left null.")]
    public MediaPipeDataProvider dataProvider;
    [Tooltip("Auto-found via Camera.main if left null.")]
    public Camera cam;

    [Header("Calibration")]
    [Tooltip("Reference shoulder-to-shoulder landmark width (normalized) at the reference distance.")]
    public float referenceShoulderWidth = 0.30f;
    [Tooltip("Base Z distance from camera when shoulder width matches the reference.")]
    public float baseDistance = 2.15f;
    [Tooltip("Shifts all children toward the camera. Small negative value keeps garment in front of background.")]
    public float zBias = -0.05f;
    [Tooltip("Uniform scale multiplier. Perspective camera handles distance compensation automatically.")]
    public float sizeScale = 1f;
    [Tooltip("World-space offset added after computing the anchor position.")]
    public Vector3 positionOffset = Vector3.zero;

    [Header("Anchor Position")]
    [Tooltip("0 = shoulder midpoint, 1 = torso center (midpoint of shoulders and hips). Blend for garment centering.")]
    [Range(0f, 1f)]
    public float anchorBlend = 0f;

    [Header("Rotation")]
    [Tooltip("Enable torso rotation tracking (yaw/pitch/roll from shoulder+hip landmarks).")]
    public bool trackRotation = true;
    [Tooltip("Use hips (23+24) for the spine up-vector. When false, uses ears (7+8) as a fallback.")]
    public bool useHipsForUpVector = true;

    [Header("Smoothing")]
    [Tooltip("Lerp speed for anchor position.")]
    public float positionSmoothing = 15f;
    [Tooltip("Slerp speed for anchor rotation.")]
    public float rotationSmoothing = 12f;
    [Tooltip("Lerp speed for individual world-space landmarks.")]
    public float landmarkSmoothing = 12f;

    [Header("Mirror")]
    [Tooltip("Mirror X axis — should match webcam horizontal flip setting.")]
    public bool mirrorX = true;

    [Header("Body Part Transforms (optional — assign cubes or other objects)")]
    [Tooltip("Torso segment: shoulder midpoint → hip midpoint.")]
    public Transform partTorso;
    [Tooltip("Left shoulder segment: shoulder center → left shoulder (landmark 11).")]
    public Transform partLeftShoulder;
    [Tooltip("Right shoulder segment: shoulder center → right shoulder (landmark 12).")]
    public Transform partRightShoulder;
    [Tooltip("Left upper arm: left shoulder (11) → left elbow (13).")]
    public Transform partLeftUpperArm;
    [Tooltip("Right upper arm: right shoulder (12) → right elbow (14).")]
    public Transform partRightUpperArm;
    [Tooltip("Left forearm: left elbow (13) → left wrist (15).")]
    public Transform partLeftForearm;
    [Tooltip("Right forearm: right elbow (14) → right wrist (16).")]
    public Transform partRightForearm;
    [Tooltip("Left hand: at left wrist (15), oriented along forearm direction.")]
    public Transform partLeftHand;
    [Tooltip("Right hand: at right wrist (16), oriented along forearm direction.")]
    public Transform partRightHand;

    [Header("Body Part Settings")]
    [Tooltip("Auto-scale each part's Y to bone length and X/Z to partThickness.")]
    public bool autoScaleParts = true;
    [Tooltip("X/Z thickness when auto-scaling body parts.")]
    public float partThickness = 0.05f;
    [Tooltip("Length of hand cubes when auto-scaling (no end landmark from pose data).")]
    public float handLength = 0.08f;

    [Header("Segment Length Multipliers")]
    [Tooltip("Scale the torso segment length.")]
    public float torsoLength = 1f;
    [Tooltip("Scale the shoulder segment length.")]
    public float shoulderLength = 1f;
    [Tooltip("Scale the upper arm segment length.")]
    public float upperArmLength = 1f;
    [Tooltip("Scale the forearm segment length.")]
    public float forearmLength = 1f;

    /// <summary>
    /// All 33 pose landmarks projected into world space. Body.cs reads these.
    /// </summary>
    [HideInInspector]
    public Vector3[] worldLandmarks;

    /// <summary>
    /// 21 left-hand landmarks projected into world space. Hand.cs reads these.
    /// Null when no left hand is detected this frame.
    /// </summary>
    [HideInInspector]
    public Vector3[] leftHandWorldLandmarks;

    /// <summary>
    /// 21 right-hand landmarks projected into world space. Hand.cs reads these.
    /// Null when no right hand is detected this frame.
    /// </summary>
    [HideInInspector]
    public Vector3[] rightHandWorldLandmarks;

    private bool landmarksInitialized = false;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (dataProvider == null)
            dataProvider = FindFirstObjectByType<MediaPipeDataProvider>();

        if (cam == null)
            cam = Camera.main;

        if (dataProvider == null || cam == null)
            return;

        float[][] pose = dataProvider.pose_data;
        if (pose == null || pose.Length < 25)
            return;

        if (!HasLandmark(pose, 11) || !HasLandmark(pose, 12) ||
            !HasLandmark(pose, 23) || !HasLandmark(pose, 24))
            return;

        // ── Depth from shoulder-to-shoulder width ────────────────────────────
        float dx = MX(pose[12][0]) - MX(pose[11][0]);
        float dy = (pose[12][1] - pose[11][1]) / cam.aspect;
        float dz = pose[12][2] - pose[11][2];
        float shoulderWidth = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        if (shoulderWidth < 0.001f)
            return;

        float scaleFactor = shoulderWidth / Mathf.Max(referenceShoulderWidth, 0.0001f);
        float zDist       = baseDistance / Mathf.Max(scaleFactor, 0.0001f) + zBias;

        // ── Project all 33 landmarks to world space (smoothed) ───────────────
        if (worldLandmarks == null || worldLandmarks.Length != pose.Length)
            worldLandmarks = new Vector3[pose.Length];

        float tLm = Mathf.Clamp01(landmarkSmoothing * Time.deltaTime);

        for (int i = 0; i < pose.Length; i++)
        {
            if (pose[i] != null && pose[i].Length >= 3)
            {
                float vx = MX(pose[i][0]);
                float vy = 1f - pose[i][1];
                Vector3 raw = cam.ViewportToWorldPoint(new Vector3(vx, vy, zDist));
                worldLandmarks[i] = landmarksInitialized
                    ? Vector3.Lerp(worldLandmarks[i], raw, tLm)
                    : raw;
            }
        }
        landmarksInitialized = true;

        // ── Project hand landmarks to world space ────────────────────────────
        leftHandWorldLandmarks  = ProjectHandLandmarks(dataProvider.left_hand_data,  zDist, tLm, leftHandWorldLandmarks);
        rightHandWorldLandmarks = ProjectHandLandmarks(dataProvider.right_hand_data, zDist, tLm, rightHandWorldLandmarks);

        // ── Anchor position ──────────────────────────────────────────────────
        Vector3 shoulderMidpoint = (worldLandmarks[11] + worldLandmarks[12]) * 0.5f;
        Vector3 hipMidpoint      = (worldLandmarks[23] + worldLandmarks[24]) * 0.5f;
        Vector3 torsoCenter      = (shoulderMidpoint + hipMidpoint) * 0.5f;
        Vector3 targetPos        = Vector3.Lerp(shoulderMidpoint, torsoCenter, anchorBlend);
        targetPos += positionOffset;

        // ── Rotation (optional) ──────────────────────────────────────────────
        Quaternion targetRot = Quaternion.identity;
        if (trackRotation)
        {
            Vector3 rightShoulder = PoseVec(pose, 12);
            Vector3 leftShoulder  = PoseVec(pose, 11);

            Vector3 right = (rightShoulder - leftShoulder).normalized;

            Vector3 up;
            if (useHipsForUpVector)
            {
                Vector3 sMid = (leftShoulder + rightShoulder) * 0.5f;
                Vector3 hMid = (PoseVec(pose, 23) + PoseVec(pose, 24)) * 0.5f;
                up = (sMid - hMid).normalized;
            }
            else
            {
                Vector3 leftEar  = PoseVec(pose, 7);
                Vector3 rightEar = PoseVec(pose, 8);
                Vector3 earMid   = (leftEar + rightEar) * 0.5f;
                Vector3 sMid     = (leftShoulder + rightShoulder) * 0.5f;
                up = (earMid - sMid).normalized;
            }

            Vector3 forward = Vector3.Cross(right, up).normalized;

            if (right.sqrMagnitude < 0.0001f || up.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
                return;

            if (forward.z < 0f) { forward = -forward; right = -right; }
            up = Vector3.Cross(forward, right).normalized;

            Quaternion raw   = Quaternion.LookRotation(forward, up);
            Vector3    euler = raw.eulerAngles;
            targetRot = Quaternion.Euler(
                -NormalizeAngle(euler.x),
                -NormalizeAngle(euler.y),
                 NormalizeAngle(euler.z));
        }

        // ── Apply with smoothing ─────────────────────────────────────────────
        float tPos = Mathf.Clamp01(positionSmoothing * Time.deltaTime);
        float tRot = Mathf.Clamp01(rotationSmoothing * Time.deltaTime);
        transform.position   = Vector3.Lerp(transform.position,   targetPos,  tPos);
        transform.rotation   = Quaternion.Slerp(transform.rotation, targetRot, tRot);
        transform.localScale = Vector3.one * sizeScale;

        // ── Drive body-part transforms ───────────────────────────────────────
        UpdateBodyParts();
    }

    // ── Body Parts ────────────────────────────────────────────────────────────

    private void UpdateBodyParts()
    {
        if (worldLandmarks == null || worldLandmarks.Length < 25)
            return;

        Vector3 shoulderMid = (worldLandmarks[11] + worldLandmarks[12]) * 0.5f;
        Vector3 hipMid      = (worldLandmarks[23] + worldLandmarks[24]) * 0.5f;

        UpdateSegment(partTorso,          shoulderMid,        hipMid,             torsoLength);
        UpdateSegment(partLeftShoulder,   shoulderMid,        worldLandmarks[11], shoulderLength);
        UpdateSegment(partRightShoulder,  shoulderMid,        worldLandmarks[12], shoulderLength);
        UpdateSegment(partLeftUpperArm,   worldLandmarks[11], worldLandmarks[13], upperArmLength);
        UpdateSegment(partRightUpperArm,  worldLandmarks[12], worldLandmarks[14], upperArmLength);
        UpdateSegment(partLeftForearm,    worldLandmarks[13], worldLandmarks[15], forearmLength);
        UpdateSegment(partRightForearm,   worldLandmarks[14], worldLandmarks[16], forearmLength);

        // Hands: extend past wrist along the forearm direction
        if (partLeftHand != null)
        {
            Vector3 dir = (worldLandmarks[15] - worldLandmarks[13]).normalized;
            UpdateSegment(partLeftHand, worldLandmarks[15], worldLandmarks[15] + dir * handLength);
        }
        if (partRightHand != null)
        {
            Vector3 dir = (worldLandmarks[16] - worldLandmarks[14]).normalized;
            UpdateSegment(partRightHand, worldLandmarks[16], worldLandmarks[16] + dir * handLength);
        }
    }

    /// <summary>
    /// Positions a transform at the midpoint of two landmarks, orients its local Y
    /// along the bone direction, and optionally auto-scales to match bone length.
    /// lengthMul scales the visual length around the midpoint (1 = exact landmark distance).
    /// </summary>
    private void UpdateSegment(Transform part, Vector3 from, Vector3 to, float lengthMul = 1f)
    {
        if (part == null) return;

        Vector3 dir = to - from;
        float length = dir.magnitude;
        if (length < 0.001f) return;

        Vector3 mid = (from + to) * 0.5f;
        part.position = mid;
        part.rotation = Quaternion.FromToRotation(Vector3.up, dir);

        if (autoScaleParts)
            part.localScale = new Vector3(partThickness, length * lengthMul, partThickness);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private float MX(float x) => mirrorX ? 1f - x : x;

    private Vector3 PoseVec(float[][] pose, int idx)
        => new Vector3(MX(pose[idx][0]), 1f - pose[idx][1], -pose[idx][2]);

    private static bool HasLandmark(float[][] pose, int idx)
        => idx >= 0 && idx < pose.Length && pose[idx] != null && pose[idx].Length >= 3;

    private static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;

    private Vector3[] ProjectHandLandmarks(float[][] handData, float zDist, float tLm, Vector3[] prev)
    {
        if (handData == null || handData.Length < 21)
            return null;

        bool hasPrev = prev != null && prev.Length == 21;
        Vector3[] result = hasPrev ? prev : new Vector3[21];

        for (int i = 0; i < 21; i++)
        {
            if (handData[i] == null || handData[i].Length < 3)
                continue;

            float vx = MX(handData[i][0]);
            float vy = 1f - handData[i][1];
            Vector3 raw = cam.ViewportToWorldPoint(new Vector3(vx, vy, zDist));
            result[i] = hasPrev ? Vector3.Lerp(result[i], raw, tLm) : raw;
        }

        return result;
    }
}
