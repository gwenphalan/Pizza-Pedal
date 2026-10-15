using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// A singleton manager for creating, tracking, and completing objectives.
/// This object must exist in the scene for the objective system to work.
/// </summary>
public class ObjectiveManager : MonoBehaviour
{
    #region Singleton
    public static ObjectiveManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }
    #endregion

    [Header("System References")]
    [Tooltip("The player's transform. Required for proximity checks to complete objectives.")]
    public Transform playerTransform;

    private readonly List<Objective> _activeObjectives = new List<Objective>();

    private void Update()
    {
        if (playerTransform == null) return;

        // Loop backwards since we might modify the list by completing objectives
        for (int i = _activeObjectives.Count - 1; i >= 0; i--)
        {
            Objective objective = _activeObjectives[i];
            if (objective.Status != Objective.ObjectiveStatus.Active) continue;

            // Check if player is within the collection zone
            float horizontalDistance = Vector2.Distance(
                new Vector2(playerTransform.position.x, playerTransform.position.z),
                new Vector2(objective.Position.x, objective.Position.z)
            );
            float verticalDistance = Mathf.Abs(playerTransform.position.y - objective.Position.y);

            if (horizontalDistance <= objective.MarkerView.collectionRadius &&
                verticalDistance <= objective.MarkerView.collectionHeight / 2f)
            {
                CompleteObjective(objective);
            }
        }
    }

    /// <summary>
    /// Creates a new objective and spawns its visual marker in the world.
    /// </summary>
    /// <param name="position">The world-space position to create the objective at.</param>
    /// <param name="markerData">The ScriptableObject defining the marker's appearance and effects.</param>
    /// <param name="onComplete">An optional callback to invoke when the objective is completed.</param>
    /// <returns>The created Objective instance.</returns>
    public Objective CreateObjective(Vector3 position, ObjectiveMarkerData markerData, System.Action<Objective> onComplete = null)
    {
        if (markerData == null || markerData.markerPrefab == null)
        {
            Debug.LogError("Cannot create objective: MarkerData or its prefab is null.");
            return null;
        }

        // Create the runtime objective data object
        Objective newObjective = new Objective(markerData, position)
        {
            OnComplete = onComplete
        };

        // Spawn the marker prefab in the scene
        GameObject markerInstance = Instantiate(markerData.markerPrefab, position, Quaternion.identity);
        markerInstance.name = $"ObjectiveMarker_{markerData.name}";

        newObjective.MarkerInstance = markerInstance;
        newObjective.MarkerView = markerInstance.GetComponent<ObjectiveMarkerView>();

        if (newObjective.MarkerView == null)
        {
            Debug.LogError($"The marker prefab '{markerData.markerPrefab.name}' is missing the ObjectiveMarkerView component.", markerInstance);
            Destroy(markerInstance);
            return null;
        }

        _activeObjectives.Add(newObjective);
        return newObjective;
    }

    /// <summary>
    /// Marks an objective as complete, destroys its marker, and plays a collection effect.
    /// </summary>
    /// <param name="objectiveToComplete">The objective to complete.</param>
    private void CompleteObjective(Objective objectiveToComplete)
    {
        if (objectiveToComplete == null) return;

        objectiveToComplete.Status = Objective.ObjectiveStatus.Completed;

        // Invoke the completion callback
        objectiveToComplete.OnComplete?.Invoke(objectiveToComplete);

        // Spawn collection effect if it exists
        if (objectiveToComplete.MarkerType.collectionEffectPrefab != null)
        {
            GameObject effectInstance = Instantiate(objectiveToComplete.MarkerType.collectionEffectPrefab, objectiveToComplete.Position, Quaternion.identity);

            // Add a component to automatically destroy the effect instance after it has finished playing.
            // This check prevents adding the component if the prefab already has it.
            if (effectInstance.GetComponent<AutoDestroyParticleSystem>() == null)
            {
                effectInstance.AddComponent<AutoDestroyParticleSystem>();
            }
        }

        // Destroy the marker instance
        if (objectiveToComplete.MarkerInstance != null)
        {
            Destroy(objectiveToComplete.MarkerInstance);
        }

        // We can remove completed objectives from the active list to keep it clean
        _activeObjectives.Remove(objectiveToComplete);
    }
    
    private void OnDrawGizmos()
    {
        if (_activeObjectives.Count == 0) return;

        Gizmos.color = Color.yellow;
        foreach (var objective in _activeObjectives)
        {
            if (objective.Status == Objective.ObjectiveStatus.Active)
            {
                Gizmos.DrawLine(transform.position, objective.Position);
            }
        }
    }
}
