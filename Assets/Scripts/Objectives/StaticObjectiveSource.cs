using UnityEngine;

/// <summary>
/// A component to define an objective that exists in the scene from the start.
/// It registers itself with the ObjectiveManager when the game begins.
/// </summary>
public class StaticObjectiveSource : MonoBehaviour
{
    [Tooltip("The ScriptableObject that defines the type of objective to create at this location.")]
    public ObjectiveMarkerData markerData;

    void Start()
    {
        if (markerData == null)
        {
            Debug.LogWarning($"StaticObjectiveSource on {gameObject.name} has no Marker Data assigned and will be ignored.", this);
            return;
        }

        if (ObjectiveManager.Instance != null)
        {
            ObjectiveManager.Instance.CreateObjective(transform.position, markerData);
        }
        else
        {
            Debug.LogError($"Could not find an instance of ObjectiveManager in the scene. The objective on {gameObject.name} will not be created.", this);
        }

        // Disable this component after it has done its job to save a tiny bit of performance.
        this.enabled = false;
    }
}
