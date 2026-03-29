using UnityEngine;

public class MalletPointer : MonoBehaviour
{
    [Header("Audio")]
    public AudioClip HitSound;

    [Header("Swing Animation")]
    [Tooltip("Resting rotation Z (mallet up).")]
    public float IdleRotationZ  = -319f;

    [Tooltip("Strike rotation Z (mallet down).")]
    public float HitRotationZ   = -255f;

    [Tooltip("Degrees per second for the down-swing (fast strike).")]
    public float SwingDownSpeed = 600f;

    [Tooltip("Degrees per second for the return swing (slow lift).")]
    public float SwingUpSpeed   = 200f;

    private enum SwingState { Idle, SwingingDown, SwingingUp }
    private SwingState  _swing    = SwingState.Idle;
    private float       _currentZ;
    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _currentZ = IdleRotationZ;
        ApplyRotation();
    }

    private void Start()
    {
        // Subscribe to every mole in the scene — no manual wiring needed
        foreach (var mole in FindObjectsByType<MoleController>(FindObjectsSortMode.None))
            mole.OnHit += _ => TriggerSwing();
    }

    public void TriggerSwing()
    {
        // Re-triggers even mid-swing so rapid hits feel responsive
        _swing = SwingState.SwingingDown;

        if (HitSound != null)
            _audioSource.PlayOneShot(HitSound);
    }

    private void Update()
    {
        switch (_swing)
        {
            case SwingState.SwingingDown:
                _currentZ = Mathf.MoveTowards(_currentZ, HitRotationZ, SwingDownSpeed * Time.deltaTime);
                ApplyRotation();
                if (Mathf.Abs(_currentZ - HitRotationZ) < 0.1f)
                {
                    _currentZ = HitRotationZ;
                    _swing    = SwingState.SwingingUp;
                }
                break;

            case SwingState.SwingingUp:
                _currentZ = Mathf.MoveTowards(_currentZ, IdleRotationZ, SwingUpSpeed * Time.deltaTime);
                ApplyRotation();
                if (Mathf.Abs(_currentZ - IdleRotationZ) < 0.1f)
                {
                    _currentZ = IdleRotationZ;
                    _swing    = SwingState.Idle;
                }
                break;
        }
    }

    // Only ever WRITE to localEulerAngles — never read back from it,
    // because Unity normalises the getter to [0, 360) which would break
    // the MoveTowards math for negative angles like -319 and -255.
    private void ApplyRotation()
    {
        Vector3 e = transform.localEulerAngles;
        e.z = _currentZ;
        transform.localEulerAngles = e;
    }
}
