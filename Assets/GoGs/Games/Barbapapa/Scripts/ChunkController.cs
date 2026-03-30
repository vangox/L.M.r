using UnityEngine;

/// <summary>
/// Attached to each level chunk prefab.
/// A chunk is a section of ground (and decorations) that scrolls toward the camera.
/// Obstacle slots are child GameObjects pre-placed at the 3 lane X positions;
/// LevelScrollManager enables/disables them on each activation cycle.
/// </summary>
public class ChunkController : MonoBehaviour
{
    [Header("Chunk Geometry")]
    [Tooltip("Length of this chunk along the Z axis (world units).")]
    public float Length = 30f;

    [Header("Obstacle Slots")]
    [Tooltip("One element per obstacle row. Each row has up to 3 lane slots " +
             "(index 0=left, 1=center, 2=right). Leave element null if that lane " +
             "has no obstacle in this chunk variant.")]
    public ObstacleRow[] Rows;

    [System.Serializable]
    public class ObstacleRow
    {
        [Tooltip("Obstacle GameObject for each lane (0=left, 1=center, 2=right). " +
                 "Null = no obstacle available for that lane.")]
        public GameObject[] LaneObstacles = new GameObject[3];
    }

    /// <summary>Z position of the near (camera-side) edge of this chunk.</summary>
    public float BackZ  => transform.position.z;
    /// <summary>Z position of the far edge of this chunk.</summary>
    public float FrontZ => transform.position.z + Length;

    /// <summary>
    /// Called by LevelScrollManager when this chunk is pulled from the pool.
    /// Pass a per-row bool[3] indicating which lanes should be blocked.
    /// Rows with null entries in that slot are silently skipped.
    /// </summary>
    public void Activate(bool[][] lanePatterns)
    {
        if (Rows == null) return;
        int rowCount = Mathf.Min(Rows.Length, lanePatterns != null ? lanePatterns.Length : 0);

        for (int r = 0; r < Rows.Length; r++)
        {
            var row = Rows[r];
            if (row.LaneObstacles == null) continue;

            bool[] pattern = (lanePatterns != null && r < lanePatterns.Length)
                ? lanePatterns[r]
                : new bool[3]; // all clear if no pattern provided

            for (int lane = 0; lane < row.LaneObstacles.Length; lane++)
            {
                if (row.LaneObstacles[lane] == null) continue;
                bool blocked = lane < pattern.Length && pattern[lane];
                row.LaneObstacles[lane].SetActive(blocked);
            }
        }
    }

    /// <summary>Disable all obstacle slots and return the chunk to a clean state.</summary>
    public void Recycle()
    {
        if (Rows == null) return;
        foreach (var row in Rows)
        {
            if (row.LaneObstacles == null) continue;
            foreach (var obs in row.LaneObstacles)
                if (obs != null) obs.SetActive(false);
        }
    }
}
