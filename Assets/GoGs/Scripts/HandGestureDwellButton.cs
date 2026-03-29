using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class HandGestureDwellButton : MonoBehaviour
{
    [Header("Dwell Settings")]
    [Range(0.01f, 10f)]
    [Tooltip("Seconds the pointer must stay over this element to trigger.")]
    public float dwellDuration = 3f;

    [Range(0f, 10f)]
    [Tooltip("Seconds before this button can trigger again after a successful dwell.")]
    public float cooldown = 2f;

    [Range(0f, 1f)]
    [Tooltip("Seconds of hand-tracking loss to tolerate before resetting the dwell timer.")]
    public float handAbsenceGrace = 0.4f;

    [Header("Visual Feedback")]
    [Tooltip("Optional Image (Type: Filled) whose fillAmount tracks dwell progress 0\u21921.")]
    public Image fillIndicator;

    [Header("Events")]
    [Tooltip("Fired when dwell completes. A UnityEngine.UI.Button on this GameObject is also clicked automatically.")]
    public UnityEvent OnDwellComplete;

    [Header("Debug")]
    [Tooltip("Print state to Console every ~2 seconds and on hover enter/exit/complete.")]
    public bool debugLog = false;

    private RectTransform _rectTransform;
    private Canvas        _canvas;
    private Button        _button;
    private float         _dwellTimer;
    private float         _cooldownRemaining;
    private bool          _isHovering;
    private float         _handAbsentTimer;
    private float         _debugTimer;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _canvas        = GetComponentInParent<Canvas>();
        _button        = GetComponent<Button>();

        if (_canvas == null)
            Debug.LogWarning("[HandGestureDwellButton] No Canvas found in parent hierarchy of '" + gameObject.name + "'.");
    }

    private void Update()
    {
        // ── Cooldown ──────────────────────────────────────────────────────────
        if (_cooldownRemaining > 0f)
        {
            _cooldownRemaining -= Time.deltaTime;
            SetFill(0f);
            return;
        }

        // ── Pointer availability ──────────────────────────────────────────────
        var pointer = HandGesturePointer.Instance;

        if (pointer == null)
        {
            // Log every 2s so it's not spam
            if (debugLog) { _debugTimer += Time.deltaTime; if (_debugTimer >= 2f) { _debugTimer = 0f; Debug.LogWarning("[HandGestureDwellButton] HandGesturePointer.Instance is NULL. Make sure HandGesturePointer component is on the HandPointer GO and the scene has been saved/reloaded."); } }
            ResetDwell();
            return;
        }

        bool handVisible = pointer.IsHandVisible;

        if (!handVisible)
        {
            _handAbsentTimer += Time.deltaTime;
            if (debugLog) { _debugTimer += Time.deltaTime; if (_debugTimer >= 2f) { _debugTimer = 0f; Debug.Log($"[HandGestureDwellButton] Hand not visible. Absent {_handAbsentTimer:F1}s / grace {handAbsenceGrace}s. detector={(pointer.detector != null ? pointer.detector.name : "NULL")} enablePointer={pointer.detector?.enablePointer}"); } }
            if (_handAbsentTimer >= handAbsenceGrace)
                ResetDwell();
            return;
        }

        _handAbsentTimer = 0f;

        // ── Hit test ──────────────────────────────────────────────────────────
        Vector2 screenPos = pointer.ScreenPosition;
        Camera  uiCamera  = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? _canvas.worldCamera : null;
        bool    hovering  = RectTransformUtility.RectangleContainsScreenPoint(_rectTransform, screenPos, uiCamera);

        // Periodic diagnostic log (every ~2s)
        if (debugLog)
        {
            _debugTimer += Time.deltaTime;
            if (_debugTimer >= 2f)
            {
                _debugTimer = 0f;
                var corners = new Vector3[4];
                _rectTransform.GetWorldCorners(corners);
                Debug.Log($"[HandGestureDwellButton] screenPos={screenPos}  hovering={hovering}  " +
                          $"button BL={corners[0]:F0} TR={corners[2]:F0}  canvas={(_canvas != null ? _canvas.renderMode.ToString() : "NULL")}  uiCam={uiCamera}");
            }
        }

        // ── State transitions ─────────────────────────────────────────────────
        if (hovering && !_isHovering)
        {
            if (debugLog) Debug.Log("[HandGestureDwellButton] Hover ENTER on " + gameObject.name);
        }
        else if (!hovering && _isHovering)
        {
            if (debugLog) Debug.Log("[HandGestureDwellButton] Hover EXIT on " + gameObject.name);
            ResetDwell();
        }

        _isHovering = hovering;

        // ── Dwell accumulation ────────────────────────────────────────────────
        if (_isHovering)
        {
            _dwellTimer += Time.deltaTime;
            SetFill(_dwellTimer / dwellDuration);

            if (_dwellTimer >= dwellDuration)
            {
                if (debugLog) Debug.Log("[HandGestureDwellButton] Dwell COMPLETE on " + gameObject.name);
                _cooldownRemaining = cooldown;
                ResetDwell();
                OnDwellComplete?.Invoke();
                _button?.onClick.Invoke();
            }
        }
    }

    private void OnDisable()
    {
        ResetDwell();
        _cooldownRemaining = 0f;
        _handAbsentTimer   = 0f;
        _debugTimer        = 0f;
    }

    private void ResetDwell()
    {
        _dwellTimer = 0f;
        _isHovering = false;
        SetFill(0f);
    }

    private void SetFill(float value)
    {
        if (fillIndicator != null)
            fillIndicator.fillAmount = Mathf.Clamp01(value);
    }
}
