using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class WhacAMoleGame : MonoBehaviour
{
    private enum GameState { Start, Countdown, Playing, GameOver }

    [Header("Moles")]
    public MoleController[] Moles;

    [Header("Game Settings")]
    public float GameDuration          = 60f;
    public float MoleVisibleDuration   = 4f;
    public float SpawnIntervalStart    = 2.5f;
    public float SpawnIntervalMin      = 0.8f;
    public int   MaxActiveMolesStart   = 1;
    public int   MaxActiveMolesMax     = 3;
    public int   PointsPerHit          = 10;

    [Header("Countdown")]
    public float CountdownStepDuration = 1f;

    [Header("UI – Screens")]
    public GameObject StartScreen;
    public GameObject CountdownScreen;
    public GameObject PlayingScreen;
    public GameObject GameOverScreen;

    [Header("UI – Text")]
    public TMP_Text CountdownText;
    public TMP_Text ScoreText;
    public TMP_Text TimerText;
    public TMP_Text FinalScoreText;

    [Header("UI – Buttons")]
    public HandGestureDwellButton PlayButton;
    public HandGestureDwellButton PlayAgainButton;

    private GameState _state = GameState.Start;
    private int       _score;
    private float     _timeRemaining;
    private Coroutine _spawnCoroutine;

    private void Awake()
    {
        if (Moles == null || Moles.Length == 0)
        {
            Debug.LogError("[WhacAMoleGame] No moles assigned!");
            return;
        }

        foreach (var mole in Moles)
            mole.OnHit += HandleMoleHit;

        PlayButton?.OnDwellComplete.AddListener(StartCountdown);
        PlayAgainButton?.OnDwellComplete.AddListener(StartCountdown);
    }

    private void Start()
    {
        EnterState(GameState.Playing);
    }

    private void Update()
    {
        if (_state != GameState.Playing) return;

        _timeRemaining -= Time.deltaTime;
        UpdateTimerUI();

        if (_timeRemaining <= 0f)
            EnterState(GameState.GameOver);
    }

    private void EnterState(GameState newState)
    {
        _state = newState;

        PlayingScreen?.SetActive(true);

        switch (newState)
        {
            case GameState.Start:
                HideAllMoles();
                break;

            case GameState.Countdown:
                _score = 0;
                UpdateScoreUI();
                HideAllMoles();
                StopSpawnCoroutine();
                StartCoroutine(CountdownSequence());
                break;

            case GameState.Playing:
                _timeRemaining = GameDuration;
                UpdateTimerUI();
                _spawnCoroutine = StartCoroutine(SpawnLoop());
                break;

            case GameState.GameOver:
                StopSpawnCoroutine();
                HideAllMoles();
                if (FinalScoreText != null)
                    FinalScoreText.text = $"Score: {_score}";
                StartCoroutine(RestartAfterDelay(2f));
                break;
        }
    }

    private void StartCountdown()
    {
        EnterState(GameState.Countdown);
    }

    private void HandleMoleHit(MoleController mole)
    {
        if (_state != GameState.Playing) return;
        _score += PointsPerHit;
        UpdateScoreUI();
    }

    private void UpdateScoreUI()
    {
        if (ScoreText != null)
            ScoreText.text = $"Score: {_score}";
    }

    private void UpdateTimerUI()
    {
        if (TimerText != null)
            TimerText.text = Mathf.CeilToInt(Mathf.Max(0f, _timeRemaining)).ToString();
    }

    private void HideAllMoles()
    {
        if (Moles == null) return;
        foreach (var mole in Moles)
            mole.ForceHide();
    }

    private void StopSpawnCoroutine()
    {
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }
    }

    private IEnumerator CountdownSequence()
    {
        string[] steps = { "3", "2", "1", "GO!" };
        foreach (string step in steps)
        {
            if (CountdownText != null)
                CountdownText.text = step;
            yield return new WaitForSeconds(CountdownStepDuration);
        }
        EnterState(GameState.Playing);
    }

    private IEnumerator RestartAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        _score = 0;
        UpdateScoreUI();
        EnterState(GameState.Playing);
    }

    private IEnumerator SpawnLoop()
    {
        var hiddenMoles = new List<MoleController>();

        while (true)
        {
            float elapsed = GameDuration - _timeRemaining;
            float t       = Mathf.Clamp01(elapsed / GameDuration);
            float interval = Mathf.Lerp(SpawnIntervalStart, SpawnIntervalMin, t);
            int   maxActive = Mathf.RoundToInt(Mathf.Lerp(MaxActiveMolesStart, MaxActiveMolesMax, t));

            int activeCount = 0;
            hiddenMoles.Clear();
            foreach (var mole in Moles)
            {
                if (mole.State == MoleController.MoleState.Hidden)
                    hiddenMoles.Add(mole);
                else
                    activeCount++;
            }

            if (activeCount < maxActive && hiddenMoles.Count > 0)
            {
                MoleController chosen = hiddenMoles[Random.Range(0, hiddenMoles.Count)];
                chosen.Show(MoleVisibleDuration);
            }

            yield return new WaitForSeconds(interval);
        }
    }
}
