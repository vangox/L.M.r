using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MoleController : MonoBehaviour
{
    public enum MoleState { Hidden, Rising, Visible, Hit, Hiding }

    [Header("References")]
    public RectTransform          MoleRect;
    public Image                  MoleImage;
    public HandGestureDwellButton DwellButton;

    [Header("Animation")]
    public float MoleHeight        = 260f;
    public float RiseSpeed         = 6f;
    public float HideSpeed         = 5f;
    public float HitPunchScale     = 1.3f;
    public float HitPunchDuration  = 0.12f;
    public float HitShrinkDuration = 0.15f;

    [Header("Colors")]
    public Color[] MoleColors = new Color[]
    {
        new Color(1.00f, 0.40f, 0.40f),
        new Color(1.00f, 0.80f, 0.20f),
        new Color(0.40f, 0.85f, 0.40f),
        new Color(0.40f, 0.70f, 1.00f),
        new Color(0.90f, 0.50f, 1.00f),
    };

    public MoleState State { get; private set; } = MoleState.Hidden;

    // Callback registered by WhacAMoleGame
    public System.Action<MoleController> OnHit;

    private float _visibleTimer;
    private float _hitTimer;
    private int   _hitPhase;   // 0 = punch up, 1 = shrink to zero

    private void Awake()
    {
        DwellButton.OnDwellComplete.AddListener(HandleDwellComplete);
        DwellButton.enabled = false;
        SetMoleY(-MoleHeight);
        MoleRect.localScale = Vector3.one;
    }

    private void Update()
    {
        switch (State)
        {
            case MoleState.Rising:
            {
                float newY = Mathf.Lerp(MoleRect.anchoredPosition.y, 0f, Time.deltaTime * RiseSpeed);
                SetMoleY(newY);
                if (Mathf.Abs(newY) < 2f)
                {
                    SetMoleY(0f);
                    EnterState(MoleState.Visible);
                }
                break;
            }

            case MoleState.Visible:
            {
                _visibleTimer -= Time.deltaTime;
                if (_visibleTimer <= 0f)
                    EnterState(MoleState.Hiding);
                break;
            }

            case MoleState.Hit:
            {
                _hitTimer += Time.deltaTime;
                if (_hitPhase == 0)
                {
                    float t = Mathf.Clamp01(_hitTimer / HitPunchDuration);
                    MoleRect.localScale = Vector3.one * Mathf.Lerp(1f, HitPunchScale, t);
                    if (t >= 1f)
                    {
                        _hitPhase = 1;
                        _hitTimer = 0f;
                    }
                }
                else
                {
                    float t = Mathf.Clamp01(_hitTimer / HitShrinkDuration);
                    MoleRect.localScale = Vector3.one * Mathf.Lerp(HitPunchScale, 0f, t);
                    if (t >= 1f)
                    {
                        MoleRect.localScale = Vector3.one;
                        EnterState(MoleState.Hiding);
                    }
                }
                break;
            }

            case MoleState.Hiding:
            {
                float newY = Mathf.Lerp(MoleRect.anchoredPosition.y, -MoleHeight, Time.deltaTime * HideSpeed);
                SetMoleY(newY);
                if (Mathf.Abs(newY + MoleHeight) < 2f)
                {
                    SetMoleY(-MoleHeight);
                    EnterState(MoleState.Hidden);
                }
                break;
            }
        }
    }

    public void Show(float duration)
    {
        if (State != MoleState.Hidden) return;
        _visibleTimer = duration;
        RandomizeColor();
        MoleRect.localScale = Vector3.one;
        EnterState(MoleState.Rising);
    }

    public void ForceHide()
    {
        EnterState(MoleState.Hidden);
        SetMoleY(-MoleHeight);
        MoleRect.localScale = Vector3.one;
    }

    private void EnterState(MoleState newState)
    {
        State = newState;
        DwellButton.enabled = (newState == MoleState.Visible);

        if (newState == MoleState.Hit)
        {
            _hitTimer = 0f;
            _hitPhase = 0;
        }
    }

    private void HandleDwellComplete()
    {
        if (State != MoleState.Visible) return;
        EnterState(MoleState.Hit);
        OnHit?.Invoke(this);
    }

    private void SetMoleY(float y)
    {
        MoleRect.anchoredPosition = new Vector2(0f, y);
    }

    private void RandomizeColor()
    {
        if (MoleColors == null || MoleColors.Length == 0) return;
        MoleImage.color = MoleColors[Random.Range(0, MoleColors.Length)];
    }
}
