using UnityEngine;

/// <summary>
/// Drives this transform to match the tracked face — position, rotation, and scale.
/// Place any accessory (hat, glasses, beard, etc.) as a child of this GameObject.
/// Children need only a local-space position/rotation offset; distance compensation
/// and head rotation are handled automatically by the anchor.
///
/// Scene setup:
///   1. Create an empty GameObject named "FaceAnchor" at scene root.
///   2. Attach this component (no other components needed on this object).
///   3. Keep FaceAnchor at scene root so localScale == worldScale.
///   4. Drag accessory GameObjects as children of FaceAnchor.
///   5. Tune each child's localPosition for its slot:
///        Hat     ~  (0,  0.15,  0.0)   above face center
///        Glasses ~  (0,  0.02,  0.05)  eye level, slightly forward
///        Beard   ~  (0, -0.12,  0.0)   below face center
///   6. Tune each child's localScale to match the model's native size.
///      FaceAnchor.sizeScale handles uniform sizing; the perspective camera
///      handles distance compensation automatically.
///
/// Calibration:
///   Keep referenceHeadWidth and baseDistance identical to FaceMeshOccluder
///   so that accessories sit flush with the occluder in depth.
/// </summary>
[DisallowMultipleComponent]
public class FaceAnchor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found via FindFirstObjectByType if left null.")]
    public MediaPipeDataProvider dataProvider;
    [Tooltip("Auto-found via Camera.main if left null.")]
    public Camera cam;

    [Header("Calibration — keep in sync with FaceMeshOccluder")]
    [Tooltip("Reference cheek-to-cheek landmark width used only for depth estimation. Match FaceMeshOccluder.")]
    public float referenceHeadWidth = 0.12f;
    [Tooltip("Base Z distance from camera when face width matches reference. Match FaceMeshOccluder.")]
    public float baseDistance = 2.15f;
    [Tooltip("Shifts all children toward the camera. Use a small negative value (-0.05 to -0.15) so accessories render in front of the FaceMeshOccluder depth writes.")]
    public float zBias = -0.08f;
    [Tooltip("Uniform scale applied to FaceAnchor and all children. Tune once at any distance — perspective camera handles the rest automatically.")]
    public float sizeScale = 1f;

    [Header("Smoothing")]
    [Tooltip("Lerp speed for position, rotation, and scale.")]
    public float smoothing = 15f;

    [Header("Mirror")]
    [Tooltip("Mirror X axis — must match FaceMeshOccluder.mirrorX.")]
    public bool mirrorX = true;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (dataProvider == null)
            dataProvider = FindFirstObjectByType<MediaPipeDataProvider>();

        if (cam == null)
            cam = Camera.main;

        if (dataProvider == null || cam == null)
            return;

        float[][] face = dataProvider.face_data;
        if (face == null || face.Length < 468)
            return;

        // Landmarks needed: 1 nose-tip, 10 forehead, 33 left-eye, 152 chin, 234 left-cheek,
        //                   263 right-eye, 454 right-cheek
        if (!HasLandmark(face, 1)   || !HasLandmark(face, 10)  || !HasLandmark(face, 33)  || !HasLandmark(face, 152) ||
            !HasLandmark(face, 234) || !HasLandmark(face, 263) || !HasLandmark(face, 454))
            return;

        // ── Depth + scale from cheek-to-cheek width ───────────────────────────
        float x234 = MX(face[234][0]);
        float x454 = MX(face[454][0]);
        float y234 = 1f - face[234][1];
        float y454 = 1f - face[454][1];

        // Compute a rotation-invariant cheek-to-cheek distance using all three axes.
        //
        // Problem: using only 2D viewport distance means any head rotation that brings
        // one cheek "behind" the face shrinks the apparent distance even though the real
        // physical distance is unchanged:
        //   • Yaw  → cheek-to-cheek X shrinks by cos(angle), cap becomes too small.
        //   • Roll → cheek vector rotates from horizontal to vertical; without aspect-
        //             ratio correction Y is inflated by ~1.78× on a 16:9 display.
        //
        // MediaPipe documents that the face-landmark Z axis uses roughly the same scale
        // as X. When the face yaws, the Z delta between the two cheeks grows by exactly
        // as much as the X delta shrinks, so sqrt(dx²+dy²+dz²) stays constant under
        // any head rotation. Aspect-correcting dy makes it compatible with dx/dz units.
        float dx = x454 - x234;
        float dy = (y454 - y234) / cam.aspect;
        float dz = face[454][2] - face[234][2]; // Z: same scale as X per MediaPipe docs
        float faceWidth = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        if (faceWidth < 0.001f)
            return;

        float scaleFactor = faceWidth / Mathf.Max(referenceHeadWidth, 0.0001f);
        float zDist       = baseDistance / Mathf.Max(scaleFactor, 0.0001f) + zBias;

        // ── Anchor position: nose tip projected into world space ──────────────
        // Landmark 1 (nose tip) is on the face symmetry axis — it does not drift
        // under head yaw unlike the cheek midpoint. Cheeks 234/454 are used only
        // for scale/depth estimation above.
        // Remap X for UV crop: portrait X ↔ landscape Y (the cropped axis).
        // Depth estimation above uses raw coords intentionally — the crop doesn't
        // change the physical distance, only the on-screen position.
        float xNose = MX(WebcamFillCover.RemapGuiY(face[1][0]));
        float yNose = 1f - face[1][1];
        Vector3 targetPos = cam.ViewportToWorldPoint(new Vector3(xNose, yNose, zDist));

        // ── Rotation from 3D face landmarks ───────────────────────────────────
        // Landmarks are converted to a consistent face-space (x mirrored, y flipped, z negated)
        // and used only to derive directional vectors — the cross-product gives face orientation.
        Vector3 rightEye3 = FaceVec(face, 263);
        Vector3 leftEye3  = FaceVec(face, 33);
        Vector3 forehead3 = FaceVec(face, 10);
        Vector3 chin3     = FaceVec(face, 152);

        Vector3 right   = (rightEye3 - leftEye3).normalized;
        Vector3 up      = (forehead3 - chin3).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;

        if (right.sqrMagnitude < 0.0001f || up.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
            return;

        // Ensure forward points toward camera (positive Z in view space).
        if (forward.z < 0f) { forward = -forward; right = -right; }
        up = Vector3.Cross(forward, right).normalized;

        // FaceVec +Z points toward the camera, opposite to Unity's LookRotation convention.
        // Both pitch (X) and yaw (Y) come out inverted; roll (Z) is unaffected.
        // Require mirrorX = true (match FaceMeshOccluder) for correct position AND rotation.
        Quaternion raw   = Quaternion.LookRotation(forward, up);
        Vector3    euler = raw.eulerAngles;
        Quaternion targetRot = Quaternion.Euler(
            -NormalizeAngle(euler.x),
            -NormalizeAngle(euler.y),
             NormalizeAngle(euler.z));

        // ── Apply with smoothing ───────────────────────────────────────────────
        float t = Mathf.Clamp01(smoothing * Time.deltaTime);
        transform.position   = Vector3.Lerp(transform.position,   targetPos,  t);
        transform.rotation   = Quaternion.Slerp(transform.rotation, targetRot, t);
        transform.localScale = Vector3.one * sizeScale;  // constant; perspective handles distance scaling
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    // Mirror the X value if required.
    private float MX(float x) => mirrorX ? 1f - x : x;

    // Returns a landmark as a direction-space vector (used only for rotation math).
    private Vector3 FaceVec(float[][] face, int idx)
        => new Vector3(MX(face[idx][0]), 1f - face[idx][1], -face[idx][2]);

    private static bool HasLandmark(float[][] face, int idx)
        => idx >= 0 && idx < face.Length && face[idx] != null && face[idx].Length >= 3;

    // Maps Unity's [0, 360] eulerAngles to [-180, 180] for correct sign when negating.
    private static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;
}
