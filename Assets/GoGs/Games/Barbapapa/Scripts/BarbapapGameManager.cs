using System;
using System.Collections;
using UnityEngine;
using TMPro;

public class BarbapapGameManager : MonoBehaviour
{
    private enum GameState { Start, Countdown, Playing, GameOver }

    [Header("Game Settings")]
    public float BaseSpeed         = 8f;
    public float MaxSpeed          = 22f;
    public float SpeedRampDuration = 90f;   // seconds to reach MaxSpeed
    public int   StartLives        = 3;

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
    public TMP_Text LivesText;
    public TMP_Text FinalScoreText;

    [Header("UI – Buttons")]
    public HandGestureDwellButton PlayButton;
    public HandGestureDwellButton PlayAgainButton;

    [Header("References")]
    public LevelScrollManager LevelManager;
    public BarbapapController Player;

    // Exposed to LevelScrollManager for speed-based difficulty
    public float CurrentSpeed { get; private set; }
    public bool  IsPlaying    => _state == GameState.Playing;

    public event Action OnGameStart;
    public event Action OnGameOver;

    private GameState _state = GameState.Start;
    private int   _lives;
    private int   _score;
    private float _playTime;

    private void Awake()
    {
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

        _playTime += Time.deltaTime;

        float t = Mathf.Clamp01(_playTime / SpeedRampDuration);
        CurrentSpeed = Mathf.Lerp(BaseSpeed, MaxSpeed, t);

        // Score = rough distance (speed × time × scale factor)
        _score = Mathf.RoundToInt(_playTime * Mathf.Lerp(BaseSpeed, CurrentSpeed, 0.5f) * 0.1f);
        UpdateScoreUI();
    }

    // Called by BarbapapController when the player touches an obstacle
    public void OnPlayerHit()
    {
        if (_state != GameState.Playing) return;

        _lives--;
        UpdateLivesUI();

        if (_lives <= 0)
            EnterState(GameState.GameOver);
        else
            Player?.PlayHitFeedback();
    }

    private void StartCountdown() => EnterState(GameState.Countdown);

    private void EnterState(GameState newState)
    {
        _state = newState;

        StartScreen?.SetActive(newState    == GameState.Start);
        CountdownScreen?.SetActive(newState == GameState.Countdown);
        PlayingScreen?.SetActive(newState   == GameState.Playing);
        GameOverScreen?.SetActive(newState  == GameState.GameOver);

        switch (newState)
        {
            case GameState.Start:
                CurrentSpeed = 0f;
                break;

            case GameState.Countdown:
                CurrentSpeed = 0f;
                _score       = 0;
                _lives       = StartLives;
                _playTime    = 0f;
                UpdateScoreUI();
                UpdateLivesUI();
                LevelManager?.ResetLevel();
                Player?.ResetPlayer();
                StartCoroutine(CountdownSequence());
                break;

            case GameState.Playing:
                CurrentSpeed = BaseSpeed;
                OnGameStart?.Invoke();
                break;

            case GameState.GameOver:
                CurrentSpeed = 0f;
                if (FinalScoreText != null)
                    FinalScoreText.text = $"Score: {_score}";
                OnGameOver?.Invoke();
                break;
        }
    }

    private IEnumerator CountdownSequence()
    {
        string[] steps = { "3", "2", "1", "GO!" };
        foreach (string step in steps)
        {
            if (CountdownText != null) CountdownText.text = step;
            yield return new WaitForSeconds(CountdownStepDuration);
        }
        EnterState(GameState.Playing);
    }

    private void UpdateScoreUI()
    {
        if (ScoreText != null) ScoreText.text = $"Score: {_score}";
    }

    private void UpdateLivesUI()
    {
        if (LivesText != null) LivesText.text = new string('♥', Mathf.Max(0, _lives));
    }
}
