using UnityEngine;

/// <summary>
/// A simple data component attached to each generated tile to identify its purpose.
/// This is used by the RoadMeshCombiner to selectively group meshes.
/// </summary>
public class RoadTileInfo : MonoBehaviour
{
    /// <summary>
    /// Defines the specific type of a road tile.
    /// </summary>
    public enum TileType
    {
        Road,
        RoadLine,
        RoadLinePassing,
        RoadLineNoPassing, // In case you add this later
        Crosswalk,
        Curb,
        Sidewalk
    }

    /// <summary>
    /// The type of this specific tile.
    /// </summary>
    public TileType tileType;
} 