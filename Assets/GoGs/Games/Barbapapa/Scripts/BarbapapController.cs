using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the Barbapapa character.
/// Listens for swipe gestures and moves Barbapapa between the three lanes.
/// Detects obstacle collisions and notifies BarbapapGameManager.
/// The character's Z position never changes — the level moves toward it.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BarbapapController : MonoBehaviour
{
    [Header("Lane Settings")]
    [Tooltip("Half-distance between lanes (world units). " +
             "Lane positions: -LaneWidth, 0, +LaneWidth.")]
    public float LaneWidth    = 1.5f;
    [Tooltip("Smoothing speed for lateral lane transitions.")]
    public float LaneSmoothing = 8f;

    [Header("Hit Feedback")]
    [Tooltip("Duration of the red-flash visual on hit.")]
    public float HitFlashDuration = 0.4f;
    [Tooltip("Renderers to flash red on hit. If empty, no flash occurs.")]
    public Renderer[] FlashRenderers;

    [Header("References")]
    [Tooltip("Auto-discovered if null.")]
    public BarbapapGameManager GameManager;

    private HandGestureDetector _gestureDetector;
    private int   _currentLane = 1; // 0=left, 1=center, 2=right
    private float _targetX;
    private bool  _isFlashing;

    private static readonly Color HitColor = new Color(1f, 0.3f, 0.3f);

    private void Awake()
    {
        _gestureDetector = FindFirstObjectByType<HandGestureDetector>();

        if (GameManager == null)
            GameManager = FindFirstObjectByType<BarbapapGameManager>();

        // Trigger collider required for obstacle detection
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnEnable()
    {
        if (_gestureDetector == null) return;
        _gestureDetector.OnSwipeLeft.AddListener(MoveLeft);
        _gestureDetector.OnSwipeRight.AddListener(MoveRight);
    }

    private void OnDisable()
    {
        if (_gestureDetector == null) return;
        _gestureDetector.OnSwipeLeft.RemoveListener(MoveLeft);
        _gestureDetector.OnSwipeRight.RemoveListener(MoveRight);
    }

    private void Update()
    {
        if (GameManager != null && !GameManager.IsPlaying) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.wasPressedThisFrame) MoveLeft();
            if (kb.dKey.wasPressedThisFrame) MoveRight();
        }

        float x = Mathf.Lerp(transform.position.x, _targetX, Time.deltaTime * LaneSmoothing);
        transform.position = new Vector3(x, transform.position.y, transform.position.z);
    }

    public void ResetPlayer()
    {
        _currentLane = 1;
        _targetX     = 0f;
        transform.position = new Vector3(0f, transform.position.y, transform.position.z);
        StopAllCoroutines();
        _isFlashing = false;
        RestoreRendererColors();
    }

    /// <summary>Briefly flashes the character red to signal a life lost.</summary>
    public void PlayHitFeedback()
    {
        if (!_isFlashing)
            StartCoroutine(FlashRoutine());
    }

    // ── Lane Movement ─────────────────────────────────────────────────────────

    public void SetLane(int lane)
    {
        if (GameManager != null && !GameManager.IsPlaying) return;
        _currentLane = Mathf.Clamp(lane, 0, 2);
        _targetX     = LaneX(_currentLane);
    }

    private void MoveLeft()
    {
        if (_currentLane > 0) SetLane(_currentLane - 1);
    }

    private void MoveRight()
    {
        if (_currentLane < 2) SetLane(_currentLane + 1);
    }

    private float LaneX(int lane) => (lane - 1) * LaneWidth;

    // ── Collision ─────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent<ObstacleController>(out _))
            GameManager?.OnPlayerHit();
    }

    // ── Visual Feedback ───────────────────────────────────────────────────────

    private IEnumerator FlashRoutine()
    {
        _isFlashing = true;

        var origColors = new Color[FlashRenderers.Length];
        for (int i = 0; i < FlashRenderers.Length; i++)
        {
            if (FlashRenderers[i] == null) continue;
            origColors[i] = FlashRenderers[i].material.color;
            FlashRenderers[i].material.color = HitColor;
        }

        yield return new WaitForSeconds(HitFlashDuration);
        RestoreRendererColors(origColors);
        _isFlashing = false;
    }

    private void RestoreRendererColors(Color[] colors = null)
    {
        for (int i = 0; i < FlashRenderers.Length; i++)
        {
            if (FlashRenderers[i] == null) continue;
            FlashRenderers[i].material.color = colors != null ? colors[i] : Color.white;
        }
    }
}
