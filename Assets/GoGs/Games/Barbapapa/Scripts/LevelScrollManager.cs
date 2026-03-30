using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the infinite scrolling level.
/// All chunk GameObjects are children of this transform.
/// Every frame, active chunks are moved in -Z (toward the camera).
/// New chunks are spawned at the front; old ones are recycled back to the pool.
/// </summary>
public class LevelScrollManager : MonoBehaviour
{
    [Header("References")]
    public BarbapapGameManager GameManager;

    [Header("Chunk Prefabs")]
    [Tooltip("Pool of chunk prefab variants. Variants are chosen randomly during spawning.")]
    public ChunkController[] ChunkPrefabs;

    [Header("Layout Settings")]
    [Tooltip("How far ahead (Z) chunks are maintained. Keep at least 2× the longest chunk length.")]
    public float SpawnDistance = 80f;
    [Tooltip("Chunks whose BackZ drops below this value are recycled (-Z = behind camera).")]
    public float DespawnZ = -20f;
    [Tooltip("Pre-allocated pool size. Increase if you see chunks disappearing.")]
    public int PoolSize = 8;

    // Obstacle difficulty parameters
    [Header("Obstacle Tuning")]
    [Range(0f, 1f)]
    [Tooltip("Chance of spawning any obstacle on a given chunk when difficulty = 0.")]
    public float MinObstacleChance = 0f;
    [Range(0f, 1f)]
    [Tooltip("Chance of spawning any obstacle on a given chunk at full difficulty.")]
    public float MaxObstacleChance = 0.85f;
    [Range(0f, 1f)]
    [Tooltip("Fraction of max speed at which two-lane blocking becomes possible.")]
    public float DoubleLaneThreshold = 0.5f;

    private readonly List<ChunkController>  _active = new List<ChunkController>();
    private readonly Queue<ChunkController> _pool   = new Queue<ChunkController>();

    // Tracks the FrontZ of the furthest spawned chunk
    private float _frontZ;

    private void Awake()
    {
        if (GameManager == null)
            GameManager = FindFirstObjectByType<BarbapapGameManager>();

        if (ChunkPrefabs == null || ChunkPrefabs.Length == 0)
        {
            Debug.LogError("[LevelScrollManager] No ChunkPrefabs assigned!");
            return;
        }

        for (int i = 0; i < PoolSize; i++)
            CreateAndPoolChunk();
    }

    private void Start()
    {
        ResetLevel();
    }

    private void Update()
    {
        if (GameManager == null || !GameManager.IsPlaying) return;

        float delta = GameManager.CurrentSpeed * Time.deltaTime;

        // Scroll all active chunks toward camera
        foreach (var chunk in _active)
            chunk.transform.position += Vector3.back * delta;

        _frontZ -= delta;

        // Maintain level length ahead
        while (_frontZ < SpawnDistance)
            SpawnNextChunk();

        // Recycle chunks that have fully passed the camera
        while (_active.Count > 0 && _active[0].BackZ < DespawnZ)
            RecycleChunk(_active[0]);
    }

    /// <summary>Recycle all active chunks and rebuild the level from scratch.</summary>
    public void ResetLevel()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
            RecycleChunk(_active[i]);

        _frontZ = 0f;
        while (_frontZ < SpawnDistance)
            SpawnNextChunk();
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private void SpawnNextChunk()
    {
        ChunkController chunk = GetFromPool();
        chunk.transform.position = new Vector3(0f, 0f, _frontZ);
        chunk.gameObject.SetActive(true);

        bool[][] patterns = GenerateObstaclePatterns(chunk);
        chunk.Activate(patterns);

        _active.Add(chunk);
        _frontZ += chunk.Length;
    }

    private void RecycleChunk(ChunkController chunk)
    {
        chunk.Recycle();
        chunk.gameObject.SetActive(false);
        _active.Remove(chunk);
        _pool.Enqueue(chunk);
    }

    private ChunkController GetFromPool()
    {
        if (_pool.Count > 0)
            return _pool.Dequeue();

        // Pool exhausted — grow it
        Debug.LogWarning("[LevelScrollManager] Pool exhausted, growing.");
        return CreateAndPoolChunk(returnToPool: false);
    }

    private ChunkController CreateAndPoolChunk(bool returnToPool = true)
    {
        ChunkController prefab = ChunkPrefabs[Random.Range(0, ChunkPrefabs.Length)];
        ChunkController chunk  = Instantiate(prefab, transform);
        chunk.gameObject.SetActive(false);
        if (returnToPool) _pool.Enqueue(chunk);
        return chunk;
    }

    private bool[][] GenerateObstaclePatterns(ChunkController chunk)
    {
        if (chunk.Rows == null || chunk.Rows.Length == 0)
            return null;

        float speedFraction = GameManager != null
            ? Mathf.Clamp01(GameManager.CurrentSpeed / GameManager.MaxSpeed)
            : 0f;

        float obstacleChance = Mathf.Lerp(MinObstacleChance, MaxObstacleChance, speedFraction);
        bool allowDouble = speedFraction >= DoubleLaneThreshold;

        var patterns = new bool[chunk.Rows.Length][];
        for (int r = 0; r < chunk.Rows.Length; r++)
        {
            patterns[r] = new bool[3]; // default: all lanes clear

            if (Random.value > obstacleChance) continue;

            int laneCount = (allowDouble && Random.value > 0.6f) ? 2 : 1;
            var available = new List<int> { 0, 1, 2 };

            for (int b = 0; b < laneCount && available.Count > 1; b++)
            {
                // Always leave at least one lane open
                if (available.Count <= 1) break;
                int pick = available[Random.Range(0, available.Count)];
                patterns[r][pick] = true;
                available.Remove(pick);
            }
        }

        return patterns;
    }
}
