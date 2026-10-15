using UnityEngine;

/// <summary>
/// Defines the static data for a type of objective marker.
/// Create instances of this as ScriptableObject assets in the project.
/// </summary>
[CreateAssetMenu(fileName = "ObjectiveMarkerData", menuName = "Pizza Pedal/Objective Marker Data", order = 1)]
public class ObjectiveMarkerData : ScriptableObject
{
    [Header("Assets")]
    [Tooltip("The visual prefab for the marker itself, which will be placed in the world.")]
    public GameObject markerPrefab;

    [Tooltip("The particle effect prefab to spawn when the objective is collected.")]
    public GameObject collectionEffectPrefab;

    [Tooltip("(Optional) An icon for use on a minimap or other UI elements.")]
    public Sprite mapIcon;
}
