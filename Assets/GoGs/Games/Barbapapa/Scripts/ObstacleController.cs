using UnityEngine;

/// <summary>
/// Marker component placed on obstacle GameObjects inside chunk prefabs.
/// The obstacle needs a Collider set to isTrigger = true.
/// BarbapapController's OnTriggerEnter detects this component and notifies the GameManager.
/// No movement logic here — the parent chunk handles all scrolling.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ObstacleController : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }
}
