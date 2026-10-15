using UnityEngine;

/// <summary>
/// Represents a tile prefab with its associated placement offsets.
/// </summary>
[System.Serializable]
public class TilePrefab
{
    [Tooltip("The prefab GameObject for this tile.")]
    public GameObject prefab;
    [Tooltip("The position offset to apply when instantiating this tile.")]
    public Vector3 positionOffset = Vector3.zero;
    [Tooltip("The rotation offset (in Euler angles) to apply when instantiating this tile.")]
    public Vector3 rotationOffset = Vector3.zero;
}

/// <summary>
/// A centralized repository for all road tile meshes, making them easily accessible for procedural generation.
/// </summary>
public class TileManager : MonoBehaviour
{
    /// <summary>
    /// Singleton instance of the TileManager for easy global access.
    /// </summary>
    public static TileManager Instance { get; private set; }

    [Header("Gizmo Settings")]
    [Tooltip("If true, the road builder will display a grid in the scene view.")]
    public bool showGridGizmo = true;

    [Header("Bike Lanes")]
    public TilePrefab bikeLane;
    public TilePrefab bikeLaneArrow;
    public TilePrefab bikeLaneSymbol;
    public TilePrefab bikeLaneTwoWay;
    public TilePrefab bikeLaneTwoWayArrow;
    public TilePrefab bikeLaneTwoWaySymbol;

    [Header("Pedestrian")]
    public TilePrefab crosswalk;
    public TilePrefab sidewalk;
    
    [Header("Curbs & Slopes")]
    public TilePrefab curb;
    public TilePrefab curbCornerInner;
    public TilePrefab curbCornerOuter;
    public TilePrefab curbSlope;
    public TilePrefab curbSlopeSingle;
    public TilePrefab curbSlopeTransition;

    [Header("Road Base")]
    public TilePrefab road;

    [Header("Road Lines")]
    public TilePrefab roadLine;
    public TilePrefab roadLineCorner;
    public TilePrefab roadLineNoPassing;
    public TilePrefab roadLineNoPassingCorner;
    public TilePrefab roadLinePassing;
    public TilePrefab roadLinePassingCorner;
    public TilePrefab roadLinePassRight;
    public TilePrefab roadLinePassRightCornerInner;
    public TilePrefab roadLinePassRightCornerOuter;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // When a value is changed in the inspector, repaint all scene views
        // to ensure gizmos from other objects (like RoadBuilder) are updated instantly.
        UnityEditor.SceneView.RepaintAll();
    }
#endif

    private void Awake()
    {
        // Basic singleton pattern implementation
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }
}
