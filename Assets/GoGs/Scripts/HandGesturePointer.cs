using UnityEngine;

[DisallowMultipleComponent]
public class HandGesturePointer : MonoBehaviour
{
    public static HandGesturePointer Instance { get; private set; }

    [Header("References")]
    [Tooltip("Auto-discovered via FindFirstObjectByType if null.")]
    public HandGestureDetector detector;

    /// <summary>Current pointer position in screen pixels. Returns Vector2.zero when detector is absent.</summary>
    public Vector2 ScreenPosition => detector != null ? detector.PointerScreenPosition : Vector2.zero;

    /// <summary>True when the hand is currently tracked. Does NOT depend on this GO being active.</summary>
    public bool IsHandVisible => detector != null && detector.IsPointerVisible;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[HandGesturePointer] Duplicate instance on '" + gameObject.name + "'. Destroying this component.");
            Destroy(this);
            return;
        }
        Instance = this;

        if (detector == null)
            detector = FindFirstObjectByType<HandGestureDetector>();

        if (detector == null)
            Debug.LogWarning("[HandGesturePointer] No HandGestureDetector found in scene.");
        else
            Debug.Log("[HandGesturePointer] Found detector on: " + detector.gameObject.name);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
