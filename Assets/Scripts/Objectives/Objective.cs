using UnityEngine;

/// <summary>
/// A data container for a single runtime instance of an objective.
/// </summary>
[System.Serializable]
public class Objective
{
    public ObjectiveMarkerData MarkerType { get; private set; }
    public Vector3 Position { get; private set; }
    public ObjectiveStatus Status { get; set; }

    /// <summary>
    /// The callback to invoke when this objective is completed.
    /// </summary>
    public System.Action<Objective> OnComplete;

    /// <summary>
    /// A reference to the spawned prefab instance in the scene.
    /// </summary>
    public GameObject MarkerInstance { get; set; }

    /// <summary>
    /// A cached reference to the view script on the marker instance.
    /// </summary>
    public ObjectiveMarkerView MarkerView { get; set; }

    public enum ObjectiveStatus
    {
        Active,
        Completed
    }

    /// <summary>
    /// Creates a new runtime objective instance.
    /// </summary>
    /// <param name="markerType">The ScriptableObject defining the marker's appearance.</param>
    /// <param name="position">The world-space position of the objective.</param>
    public Objective(ObjectiveMarkerData markerType, Vector3 position)
    {
        MarkerType = markerType;
        Position = position;
        Status = ObjectiveStatus.Active;
        MarkerInstance = null;
        MarkerView = null;
        OnComplete = null;
    }
}
