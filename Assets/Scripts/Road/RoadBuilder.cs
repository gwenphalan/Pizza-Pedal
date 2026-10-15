using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

/// <summary>
/// Responsible for procedurally generating the road network based on map data.
/// It utilizes the TileManager to get the necessary road piece prefabs.
/// </summary>
[RequireComponent(typeof(GeoJSONProcessor))]
public class RoadBuilder : MonoBehaviour
{
    private enum IntersectionType { Complex, Straight, Corner, TJunction, DeadEnd }

    [System.Serializable]
    public class GridSettings
    {
        [Tooltip("The size of each grid cell. This determines the snapping resolution.")]
        public float gridSize = 1f;
    }

    private TileManager tileManager;
    private GeoJSONProcessor geoJsonProcessor;

    [Header("Road Settings")]
    [Tooltip("A container for the generated road objects. If empty, a new GameObject will be created.")]
    public Transform roadContainer;
    [Tooltip("The number of tiles used for a single lane. Must be an odd number to ensure grid alignment.")]
    public int laneWidth = 1;
    [Tooltip("Settings that define the grid for snapping and visualization.")]
    public GridSettings gridSettings = new GridSettings();

    [Header("Terrain Integration")]
    [Tooltip("Assign the Terrain object to project roads onto. If null, this feature is disabled.")]
    public Terrain terrain;
    [Tooltip("The vertical distance to raise the road surface above the terrain.")]
    public float roadSurfaceOffset = 0.05f;

    private const string ROAD_CONTAINER_NAME = "Generated Roads";
    private const string SIDEWALK_CONTAINER_NAME = "Generated Sidewalks";
    private Transform tileContainer;
    private Transform sidewalkTileContainer;
    private Transform sidewalkContainer;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Ensure laneWidth is always an odd number to maintain grid alignment.
        if (laneWidth % 2 == 0)
        {
            laneWidth++;
        }
        laneWidth = Mathf.Max(1, laneWidth);
    }
#endif

    private void Awake()
    {
        // Automatically find the TileManager instance in the scene upon initialization.
        tileManager = FindFirstObjectByType<TileManager>();
        geoJsonProcessor = GetComponent<GeoJSONProcessor>();

        if (tileManager == null)
        {
            // Log an error and disable the component if the TileManager is not found.
            // This prevents null reference exceptions during road construction.
            Debug.LogError("RoadBuilder could not find a TileManager in the scene. Please ensure a GameObject with the TileManager component exists.");
            enabled = false;
        }
    }

    /// <summary>
    /// Clears all previously generated road objects.
    /// </summary>
    public void ClearRoads()
    {
#if UNITY_EDITOR
        if (roadContainer == null)
        {
            roadContainer = transform.Find(ROAD_CONTAINER_NAME);
        }

        if (roadContainer != null)
        {
            while (roadContainer.childCount > 0)
            {
                DestroyImmediate(roadContainer.GetChild(0).gameObject);
            }
        }

        // For backwards compatibility, also check for a root-level optimized world from previous runs
        GameObject oldOptimizedWorld = GameObject.Find("Optimized World");
        if (oldOptimizedWorld != null && oldOptimizedWorld.transform.parent == null)
        {
            DestroyImmediate(oldOptimizedWorld);
        }
#endif
    }

    /// <summary>
    /// Clears all previously generated sidewalk objects.
    /// </summary>
    public void ClearSidewalks()
    {
#if UNITY_EDITOR
        if (sidewalkContainer == null)
        {
            sidewalkContainer = transform.Find(SIDEWALK_CONTAINER_NAME);
        }

        if (sidewalkContainer != null)
        {
            while (sidewalkContainer.childCount > 0)
            {
                DestroyImmediate(sidewalkContainer.GetChild(0).gameObject);
            }
        }
#endif
    }

    /// <summary>
    /// Generates the road network in the editor based on the GeoJSON data.
    /// </summary>
    public void BuildRoads()
    {
#if UNITY_EDITOR
        try
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("Roads can only be built in Editor mode.");
                return;
            }

            EditorUtility.DisplayProgressBar("Building Roads", "Initializing...", 0f);

            // Ensure components are found, as Awake() isn't called reliably in editor mode.
            if (tileManager == null)
            {
                tileManager = FindFirstObjectByType<TileManager>();
            }
            if (geoJsonProcessor == null)
            {
                geoJsonProcessor = GetComponent<GeoJSONProcessor>();
            }
            // Force process the data if it hasn't been processed yet by OnValidate.
            if (geoJsonProcessor != null && geoJsonProcessor.mapData == null && geoJsonProcessor.geoJsonFile != null)
            {
                 geoJsonProcessor.ProcessGeoJsonV2(geoJsonProcessor.geoJsonFile.text);
            }

            EditorUtility.DisplayProgressBar("Building Roads", "Clearing old roads...", 0.05f);
            ClearRoads();

            if (tileManager == null || geoJsonProcessor == null || geoJsonProcessor.mapData == null)
            {
                Debug.LogError("Required components (TileManager, GeoJSONProcessor) are not assigned or have no data.", this);
                return;
            }

            if (roadContainer == null)
            {
                GameObject containerGO = new GameObject(ROAD_CONTAINER_NAME);
                roadContainer = containerGO.transform;
                roadContainer.SetParent(this.transform, false);
            }

            // Create a sub-container for the individual tiles
            GameObject tileContainerGO = new GameObject("Road Tiles");
            tileContainer = tileContainerGO.transform;
            tileContainer.SetParent(roadContainer, false);

            // --- Pre-computation Pass ---
            EditorUtility.DisplayProgressBar("Building Roads", "Analyzing connections...", 0.1f);
            Dictionary<int, List<MapData.Street>> nodeConnections = new Dictionary<int, List<MapData.Street>>();
            Dictionary<int, IntersectionType> nodeTypes = new Dictionary<int, IntersectionType>();

            var allNodes = geoJsonProcessor.mapData.RoutingNodes.Values.ToList();
            for (int i = 0; i < allNodes.Count; i++)
            {
                var node = allNodes[i];
                var connections = GetNodeConnections(node);
                nodeConnections[node.Id] = connections;
                nodeTypes[node.Id] = ClassifyIntersection(node, connections);
                float progress = 0.1f + ((float)i / allNodes.Count) * 0.1f;
                EditorUtility.DisplayProgressBar("Building Roads", $"Analyzing node {i + 1}/{allNodes.Count}", progress);
            }

            // --- Intersection Build Pass ---
            EditorUtility.DisplayProgressBar("Building Roads", "Building complex intersections...", 0.2f);
            foreach (var node in geoJsonProcessor.mapData.RoutingNodes.Values)
            {
                var connections = nodeConnections[node.Id];
                var nodeType = nodeTypes[node.Id];

                if (nodeType == IntersectionType.Complex || nodeType == IntersectionType.TJunction)
                {
                    BuildComplexIntersection(node, connections);
                }
            }

            // --- Street Segment Pass ---
            var allStreets = geoJsonProcessor.mapData.Streets;
            for (int s = 0; s < allStreets.Count; s++)
            {
                var street = allStreets[s];
                float progress = 0.3f + ((float)s / allStreets.Count) * 0.5f;
                EditorUtility.DisplayProgressBar("Building Roads", $"Building street {s + 1}/{allStreets.Count}: {street.Name}", progress);

                if (tileManager.road == null || tileManager.road.prefab == null) continue;

                Vector3 p1 = street.Points[0];
                Vector3 p2 = street.Points[1];
                Vector3 direction = (p2 - p1).normalized;

                if (Mathf.Abs(direction.x) > 0.01f && Mathf.Abs(direction.z) > 0.01f)
                {
                    Debug.LogWarning($"Skipping diagonal road '{street.Name}'. Only axis-aligned roads are currently supported.");
                    continue;
                }

                int streetWidthInTiles = GetStreetWidthInTiles(street);
                Vector3 perpendicular = new Vector3(-direction.z, 0, direction.x);
                float sideOffsetStart = (streetWidthInTiles - 1) / 2.0f * gridSettings.gridSize;

                int startNodeId = geoJsonProcessor.mapData.RoutingNodes.First(kvp => kvp.Value.Position == street.Points[0]).Key;
                int endNodeId = geoJsonProcessor.mapData.RoutingNodes.First(kvp => kvp.Value.Position == street.Points[1]).Key;

                IntersectionType startNodeType = nodeTypes[startNodeId];
                IntersectionType endNodeType = nodeTypes[endNodeId];

                float startOffset = 0;
                if (startNodeType == IntersectionType.Complex || startNodeType == IntersectionType.TJunction)
                {
                    var dims = CalculateIntersectionDimensions(nodeConnections[startNodeId]);
                    // If the street runs along the X axis, its length is reduced by the intersection's width (dims.x)
                    if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
                    {
                        startOffset = dims.x / 2.0f * gridSettings.gridSize;
                    }
                    // If the street runs along the Z axis, its length is reduced by the intersection's height (dims.y)
                    else
                    {
                        startOffset = dims.y / 2.0f * gridSettings.gridSize;
                    }
                    if(street.HasSidewalk) startOffset += gridSettings.gridSize;
                }
                else if (startNodeType == IntersectionType.Corner) startOffset = (GetNodeMaxWidthInTiles(nodeConnections[startNodeId]) / 2.0f + 0.5f) * gridSettings.gridSize;

                float endOffset = 0;
                if (endNodeType == IntersectionType.Complex || endNodeType == IntersectionType.TJunction)
                {
                     var dims = CalculateIntersectionDimensions(nodeConnections[endNodeId]);
                     if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
                     {
                         endOffset = dims.x / 2.0f * gridSettings.gridSize;
                     }
                     else
                     {
                         endOffset = dims.y / 2.0f * gridSettings.gridSize;
                     }
                     if(street.HasSidewalk) endOffset += gridSettings.gridSize;
                }
                else if (endNodeType == IntersectionType.Corner) endOffset = (GetNodeMaxWidthInTiles(nodeConnections[endNodeId]) / 2.0f + 0.5f) * gridSettings.gridSize;
                
                float totalDistance = Vector3.Distance(p1, p2);
                int steps = Mathf.RoundToInt(totalDistance / gridSettings.gridSize) + 1;

                for (int i = 0; i < steps; i++)
                {
                    float currentDist = i * gridSettings.gridSize;

                    // Skip tiles that fall within the intersection area
                    if (currentDist < startOffset - 0.01f || currentDist > totalDistance - endOffset + 0.01f)
                    {
                        continue;
                    }
                    
                    Vector3 segmentCenterPos = p1 + direction * currentDist;

                    for (int j = 0; j < streetWidthInTiles; j++)
                    {
                        Vector3 tilePos = segmentCenterPos - perpendicular * sideOffsetStart + perpendicular * j * gridSettings.gridSize;
                        TilePrefab tileToPlace = GetTilePrefabForIndex(street, j, streetWidthInTiles);
                        PlaceTile(tileToPlace, tilePos, Quaternion.LookRotation(direction));
                    }
                }
            }
            
            // --- Continuation/Corner Center Tile Pass ---
            EditorUtility.DisplayProgressBar("Building Roads", "Finalizing intersections...", 0.8f);
            for (int i = 0; i < allNodes.Count; i++)
            {
                var node = allNodes[i];
                float progress = 0.8f + ((float)i / allNodes.Count) * 0.2f;
                EditorUtility.DisplayProgressBar("Building Roads", $"Finalizing node {i + 1}/{allNodes.Count}", progress);

                var nodeType = nodeTypes[node.Id];
                if(nodeType == IntersectionType.Straight)
                {
                    var connections = nodeConnections[node.Id];
                    if (connections.Count < 2) continue;
                    // For a straight connection, we just need to build a cross-section of the road at the node point
                    var street = connections[0];
                    int streetWidthInTiles = GetStreetWidthInTiles(street);
                    Vector3 direction = (GetNodeConnections(node)[1].Points[1] - node.Position).normalized;
                    Vector3 perpendicular = new Vector3(-direction.z, 0, direction.x);
                    float sideOffsetStart = (streetWidthInTiles - 1) / 2.0f * gridSettings.gridSize;
                    Vector3 segmentCenterPos = node.Position;

                    for (int j = 0; j < streetWidthInTiles; j++)
                    {
                        Vector3 tilePos = segmentCenterPos - perpendicular * sideOffsetStart + perpendicular * j * gridSettings.gridSize;
                        TilePrefab tileToPlace = GetTilePrefabForIndex(street, j, streetWidthInTiles);
                        PlaceTile(tileToPlace, tilePos, Quaternion.LookRotation(direction));
                    }
                }
                else if (nodeType == IntersectionType.Corner)
                {
                    BuildCorner(node, nodeConnections[node.Id]);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
#endif
    }

    /// <summary>
    /// Generates the sidewalk network in the editor based on the GeoJSON data.
    /// </summary>
    public void BuildSidewalks()
    {
#if UNITY_EDITOR
        try
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("Sidewalks can only be built in Editor mode.");
                return;
            }

            EditorUtility.DisplayProgressBar("Building Sidewalks", "Initializing...", 0f);

            // Ensure components are found
            if (tileManager == null) tileManager = FindFirstObjectByType<TileManager>();
            if (geoJsonProcessor == null) geoJsonProcessor = GetComponent<GeoJSONProcessor>();
            if (geoJsonProcessor != null && geoJsonProcessor.mapData == null && geoJsonProcessor.geoJsonFile != null)
            {
                geoJsonProcessor.ProcessGeoJsonV2(geoJsonProcessor.geoJsonFile.text);
            }

            EditorUtility.DisplayProgressBar("Building Sidewalks", "Clearing old sidewalks...", 0.05f);
            ClearSidewalks();

            if (tileManager == null || geoJsonProcessor == null || geoJsonProcessor.mapData == null)
            {
                Debug.LogError("Required components (TileManager, GeoJSONProcessor) are not assigned or have no data.", this);
                return;
            }
             if (tileManager.sidewalk == null || tileManager.sidewalk.prefab == null)
            {
                Debug.LogError("TileManager is missing a prefab for the 'Sidewalk' tile. Please assign it.", tileManager);
                return;
            }

            // Ensure the main road container exists to parent the sidewalk container to it.
            if (roadContainer == null)
            {
                GameObject containerGO = new GameObject(ROAD_CONTAINER_NAME);
                roadContainer = containerGO.transform;
                roadContainer.SetParent(this.transform, false);
            }

            if (sidewalkContainer == null)
            {
                GameObject containerGO = new GameObject(SIDEWALK_CONTAINER_NAME);
                sidewalkContainer = containerGO.transform;
                sidewalkContainer.SetParent(roadContainer, false);
            }
            
            GameObject sidewalkTileContainerGO = new GameObject("Sidewalk Tiles");
            sidewalkTileContainer = sidewalkTileContainerGO.transform;
            sidewalkTileContainer.SetParent(sidewalkContainer, false);


            // --- Pre-computation Pass ---
            EditorUtility.DisplayProgressBar("Building Sidewalks", "Analyzing connections...", 0.1f);
            Dictionary<int, List<MapData.Street>> nodeConnections = new Dictionary<int, List<MapData.Street>>();
            Dictionary<int, IntersectionType> nodeTypes = new Dictionary<int, IntersectionType>();

            var allNodes = geoJsonProcessor.mapData.RoutingNodes.Values.ToList();
            for (int i = 0; i < allNodes.Count; i++)
            {
                var node = allNodes[i];
                var connections = GetNodeConnections(node);
                nodeConnections[node.Id] = connections;
                nodeTypes[node.Id] = ClassifyIntersection(node, connections);
                float progress = 0.1f + ((float)i / allNodes.Count) * 0.2f;
                EditorUtility.DisplayProgressBar("Building Sidewalks", $"Analyzing node {i + 1}/{allNodes.Count}", progress);
            }

            // --- Sidewalk Segment Pass ---
            var allStreets = geoJsonProcessor.mapData.Streets;
            for (int s = 0; s < allStreets.Count; s++)
            {
                var street = allStreets[s];
                float progress = 0.3f + ((float)s / allStreets.Count) * 0.7f;
                EditorUtility.DisplayProgressBar("Building Sidewalks", $"Building sidewalk for street {s + 1}/{allStreets.Count}: {street.Name}", progress);

                if (!street.HasSidewalk) continue;

                Vector3 p1 = street.Points[0];
                Vector3 p2 = street.Points[1];
                Vector3 direction = (p2 - p1).normalized;

                if (Mathf.Abs(direction.x) > 0.01f && Mathf.Abs(direction.z) > 0.01f)
                {
                    // Diagonal streets are not supported for sidewalks yet
                    continue;
                }

                int streetWidthInTiles = GetStreetWidthInTiles(street);
                Vector3 perpendicular = new Vector3(-direction.z, 0, direction.x);
                // Offset from the street centerline to the center of the sidewalk tile
                float sidewalkOffset = (streetWidthInTiles / 2.0f + 0.5f) * gridSettings.gridSize;


                int startNodeId = geoJsonProcessor.mapData.RoutingNodes.First(kvp => kvp.Value.Position == street.Points[0]).Key;
                int endNodeId = geoJsonProcessor.mapData.RoutingNodes.First(kvp => kvp.Value.Position == street.Points[1]).Key;

                IntersectionType startNodeType = nodeTypes[startNodeId];
                IntersectionType endNodeType = nodeTypes[endNodeId];

                // --- Calculate Start/End Offsets to avoid drawing into intersections ---
                float startOffset = 0;
                if (startNodeType == IntersectionType.Complex || startNodeType == IntersectionType.TJunction)
                {
                    var dims = CalculateIntersectionDimensions(nodeConnections[startNodeId]);
                    if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z)) startOffset = dims.x / 2.0f * gridSettings.gridSize;
                    else startOffset = dims.y / 2.0f * gridSettings.gridSize;
                    startOffset += gridSettings.gridSize; // Add padding for crosswalk
                }
                else if (startNodeType == IntersectionType.Corner)
                {
                     startOffset = (GetNodeMaxWidthInTiles(nodeConnections[startNodeId]) / 2.0f + 0.5f) * gridSettings.gridSize;
                }

                float endOffset = 0;
                if (endNodeType == IntersectionType.Complex || endNodeType == IntersectionType.TJunction)
                {
                     var dims = CalculateIntersectionDimensions(nodeConnections[endNodeId]);
                     if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z)) endOffset = dims.x / 2.0f * gridSettings.gridSize;
                     else endOffset = dims.y / 2.0f * gridSettings.gridSize;
                     endOffset += gridSettings.gridSize; // Add padding for crosswalk
                }
                 else if (endNodeType == IntersectionType.Corner)
                {
                    endOffset = (GetNodeMaxWidthInTiles(nodeConnections[endNodeId]) / 2.0f + 0.5f) * gridSettings.gridSize;
                }
                
                float totalDistance = Vector3.Distance(p1, p2);
                int steps = Mathf.RoundToInt(totalDistance / gridSettings.gridSize) + 1;

                for (int i = 0; i < steps; i++)
                {
                    float currentDist = i * gridSettings.gridSize;

                    // Skip tiles that fall within the intersection area
                    if (currentDist < startOffset - 0.01f || currentDist > totalDistance - endOffset + 0.01f)
                    {
                        continue;
                    }
                    
                    Vector3 segmentCenterPos = p1 + direction * currentDist;

                    // Place left and right sidewalk tiles
                    Vector3 rightTilePos = segmentCenterPos + perpendicular * sidewalkOffset;
                    Vector3 leftTilePos = segmentCenterPos - perpendicular * sidewalkOffset;

                    PlaceTile(tileManager.sidewalk, rightTilePos, Quaternion.LookRotation(direction), true);
                    PlaceTile(tileManager.sidewalk, leftTilePos, Quaternion.LookRotation(direction), true);
                }
            }

            // TODO: Intersection corner pass for sidewalks
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
#endif
    }

    /// <summary>
    /// Iterates through all generated road tiles and adjusts their vertical position to match the terrain height.
    /// </summary>
    public void ProjectToTerrain()
    {
#if UNITY_EDITOR
        try
        {
            if (terrain == null)
            {
                Debug.LogWarning("No Terrain assigned to RoadBuilder. Cannot project.", this);
                return;
            }

            EditorUtility.DisplayProgressBar("Projecting to Terrain", "Preparing...", 0f);

            Transform tilesToProject = tileContainer;
            if (tilesToProject == null && roadContainer != null)
            {
                tilesToProject = roadContainer.Find("Road Tiles");
            }

            if (tilesToProject == null || tilesToProject.childCount == 0)
            {
                Debug.LogWarning("Could not find a 'Road Tiles' container with tiles inside. Please build roads before projecting to terrain.", this);
                return;
            }

            Debug.Log("Projecting road tiles to terrain...");
            int childCount = tilesToProject.childCount;
            for (int i = 0; i < childCount; i++)
            {
                float progress = (float)i / childCount;
                EditorUtility.DisplayProgressBar("Projecting to Terrain", $"Processing tile {i + 1}/{childCount}", progress);

                Transform tile = tilesToProject.GetChild(i);
                Vector3 tileWorldPos = tile.position;

                // Sample the terrain height at the tile's world position
                float terrainHeight = terrain.SampleHeight(tileWorldPos);

                // Set the tile's new world position, accounting for terrain's own position and the surface offset
                tile.position = new Vector3(
                    tileWorldPos.x,
                    terrain.transform.position.y + terrainHeight + roadSurfaceOffset,
                    tileWorldPos.z
                );
            }
            Debug.Log($"Projection complete. Processed {childCount} tiles.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
#endif
    }

    /// <summary>
    /// Builds a complex (4-way) or T-junction intersection using a minimal bounding box.
    /// </summary>
    private void BuildComplexIntersection(MapData.RoutingNode node, List<MapData.Street> connections)
    {
        Vector2Int dimensions = CalculateIntersectionDimensions(connections);
        int widthInTiles = dimensions.x; // Span along X-axis
        int heightInTiles = dimensions.y; // Span along Z-axis

        Vector3 center = node.Position;
        float halfWidth = (widthInTiles - 1) / 2.0f * gridSettings.gridSize;
        float halfHeight = (heightInTiles - 1) / 2.0f * gridSettings.gridSize;

        Vector3 bottomLeft = center - new Vector3(halfWidth, 0, halfHeight);

        // Fill the core intersection area
        for (int x = 0; x < widthInTiles; x++)
        {
            for (int z = 0; z < heightInTiles; z++)
            {
                Vector3 tilePos = bottomLeft + new Vector3(x * gridSettings.gridSize, 0, z * gridSettings.gridSize);
                PlaceTile(tileManager.road, tilePos, Quaternion.identity);
            }
        }
        
        // Extrude crosswalks where needed
        foreach (var street in connections)
        {
            if (street.HasSidewalk)
            {
                Vector3 streetDir = (street.Points.First(p => p != node.Position) - node.Position).normalized;
                
                float crosswalkOffset;
                // Horizontal street, offset is based on the intersection's width
                if (Mathf.Abs(streetDir.x) > Mathf.Abs(streetDir.z)) 
                {
                    crosswalkOffset = halfWidth;
                }
                // Vertical street, offset is based on the intersection's height
                else 
                {
                    crosswalkOffset = halfHeight;
                }
                
                Vector3 crosswalkStartPos = center + streetDir * (crosswalkOffset + gridSettings.gridSize);
                
                int streetWidth = GetStreetWidthInTiles(street);
                Vector3 perpendicular = new Vector3(-streetDir.z, 0, streetDir.x);
                float sideOffsetStart = (streetWidth - 1) / 2.0f * gridSettings.gridSize;

                for(int i = 0; i < streetWidth; i++)
                {
                    Vector3 tilePos = crosswalkStartPos - perpendicular * sideOffsetStart + perpendicular * i * gridSettings.gridSize;
                    // The crosswalk should be oriented along the perpendicular vector, not the street direction.
                    PlaceTile(tileManager.crosswalk, tilePos, Quaternion.LookRotation(perpendicular));
                }
            }
        }
    }

    /// <summary>
    /// Builds a corner intersection by projecting the incoming lanes and filling the area.
    /// </summary>
    private void BuildCorner(MapData.RoutingNode node, List<MapData.Street> connections)
    {
        var street1 = connections[0];
        var street2 = connections[1];

        // Determine orientation of the two connecting streets
        Vector3 dir1 = (street1.Points.First(p => p != node.Position) - node.Position).normalized;
        Vector3 dir2 = (street2.Points.First(p => p != node.Position) - node.Position).normalized;
        
        Vector3 up = (Mathf.Abs(dir1.z) > Mathf.Abs(dir2.z)) ? dir1 : dir2;
        Vector3 right = (up == dir1) ? dir2 : dir1;
        var hStreet = (up == dir1) ? street2 : street1;
        var vStreet = (up == dir1) ? street1 : street2;

        int width = GetStreetWidthInTiles(street1);
        float halfWidthOffset = (width - 1) / 2.0f * gridSettings.gridSize;
        Vector3 cornerCenter = node.Position;
        Vector3 bottomLeft = cornerCenter - (right * halfWidthOffset) - (up * halfWidthOffset);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < width; z++)
            {
                Vector3 tilePos = bottomLeft + right * x * gridSettings.gridSize + up * z * gridSettings.gridSize;

                // Case 1: Handle the diagonal meeting points
                if (x == z)
                {
                    var hTile = GetTilePrefabForIndex(hStreet, z, width);
                    var vTile = GetTilePrefabForIndex(vStreet, x, width);
                    
                    // If two blank road tiles meet (lane-meets-lane or curb-meets-curb), fill the corner.
                    if (hTile == tileManager.road && vTile == tileManager.road)
                    {
                        PlaceTile(tileManager.road, tilePos, Quaternion.identity);
                    }
                    // If two divider tiles meet, place the appropriate corner tile.
                    else if (hTile != tileManager.road && vTile != tileManager.road)
                    {
                        TilePrefab cornerTile = GetCornerTileFor(hTile);
                        float rotationAngle = GetCornerRotation(dir1, dir2);
                        PlaceTile(cornerTile, tilePos, Quaternion.Euler(0, rotationAngle, 0));
                    }
                }
                // Case 2: Fill the part of the corner belonging to the vertical street.
                else if (x < z)
                {
                    var tileToPlace = GetTilePrefabForIndex(vStreet, x, width);
                    PlaceTile(tileToPlace, tilePos, Quaternion.LookRotation(up));
                }
                // Case 3: Fill the part of the corner belonging to the horizontal street.
                else // z < x
                {
                    var tileToPlace = GetTilePrefabForIndex(hStreet, z, width);
                    PlaceTile(tileToPlace, tilePos, Quaternion.LookRotation(right));
                }
            }
        }
    }

    /// <summary>
    /// Determines which tile prefab to use for a specific position in a street's cross-section.
    /// </summary>
    private TilePrefab GetTilePrefabForIndex(MapData.Street street, int tileIndex, int totalWidth)
    {
        int curbWidth = 1; // Always assume a 1-tile curb area on each side

        // Check for curb tiles on the edges
        if (tileIndex == 0 || tileIndex == totalWidth - 1)
        {
            return tileManager.road; // Placeholder for future curb/shoulder tile
        }

        int roadIndex = tileIndex - curbWidth;
        int roadWidth = totalWidth - (curbWidth * 2);

        // A "block" consists of a number of lane tiles followed by one divider tile.
        int laneAndDividerBlockSize = laneWidth + 1;
        int positionInBlock = roadIndex % laneAndDividerBlockSize;
        
        // The last tile in a block is the divider.
        bool isDivider = positionInBlock == laneWidth;

        if (!isDivider)
        {
            // This is a standard lane tile.
            return tileManager.road;
        }
        else // It is a divider
        {
            if (street.IsOneWay)
            {
                return tileManager.roadLine;
            }
            else // Two-way street, dividers can be passing or solid.
            {
                // The center-most divider is the main passing line.
                int centerIndex = (roadWidth - 1) / 2;
                if (roadIndex == centerIndex)
                {
                    return tileManager.roadLinePassing; // Center line for two-way traffic
                }
                else
                {
                    return tileManager.roadLine;
                }
            }
        }
    }

    /// <summary>
    /// Calculates the total width of a street in tiles, including lanes, dividers, and curb areas.
    /// </summary>
    private int GetStreetWidthInTiles(MapData.Street street)
    {
        if (street.LaneCount == 0) return 0;
        int totalLanes = street.IsOneWay ? street.LaneCount : street.LaneCount * 2;
        // Each lane is now composed of multiple tiles.
        int totalLaneTiles = totalLanes * laneWidth;
        int dividerCount = totalLanes > 1 ? totalLanes - 1 : 0;
        int curbCount = 2; // Always add 2 tiles for shoulders/curb area
        return totalLaneTiles + dividerCount + curbCount;
    }

    /// <summary>
    /// Finds the maximum width in tiles required for an intersection by checking all connecting streets.
    /// </summary>
    private int GetNodeMaxWidthInTiles(List<MapData.Street> connectedStreets)
    {
        int maxWidth = 0;
        foreach (var street in connectedStreets)
        {
            int streetWidth = GetStreetWidthInTiles(street);
            if (streetWidth > maxWidth)
            {
                maxWidth = streetWidth;
            }
        }
        return (maxWidth % 2 == 0) ? maxWidth + 1 : maxWidth;
    }

    /// <summary>
    /// Gets all streets from map data that connect to a given node.
    /// </summary>
    private List<MapData.Street> GetNodeConnections(MapData.RoutingNode node)
    {
        List<MapData.Street> connections = new List<MapData.Street>();
        foreach (var street in geoJsonProcessor.mapData.Streets)
        {
            if (street.Points.Contains(node.Position))
            {
                connections.Add(street);
            }
        }
        return connections;
    }

    /// <summary>
    /// Classifies an intersection based on its connecting streets.
    /// </summary>
    private IntersectionType ClassifyIntersection(MapData.RoutingNode node, List<MapData.Street> connections)
    {
        if (connections.Count <= 1)
        {
            return IntersectionType.DeadEnd;
        }

        if (connections.Count == 2)
        {
            var street1 = connections[0];
            var street2 = connections[1];
            Vector3 dir1 = (street1.Points.First(p => p != node.Position) - node.Position).normalized;
            Vector3 dir2 = (street2.Points.First(p => p != node.Position) - node.Position).normalized;

            // If vectors are nearly perpendicular, it's a corner.
            if (Mathf.Abs(Vector3.Dot(dir1, dir2)) < 0.1f)
            {
                // Only a true corner if streets have similar properties. Otherwise, treat as complex.
                if (street1.IsOneWay == street2.IsOneWay && street1.LaneCount == street2.LaneCount)
                {
                    return IntersectionType.Corner;
                }
            }
            // If vectors are nearly parallel, it's a straight continuation.
            else
            {
                return IntersectionType.Straight;
            }
        }
        
        if (connections.Count == 3)
        {
             // Check if the three connections form a T-shape (two are co-linear)
            Vector3 dir1 = (connections[0].Points.First(p => p != node.Position) - node.Position).normalized;
            Vector3 dir2 = (connections[1].Points.First(p => p != node.Position) - node.Position).normalized;
            Vector3 dir3 = (connections[2].Points.First(p => p != node.Position) - node.Position).normalized;

            // If any two vectors are nearly parallel, they form the top of the 'T'.
            if (Mathf.Abs(Vector3.Dot(dir1, dir2)) > 0.9f ||
                Mathf.Abs(Vector3.Dot(dir1, dir3)) > 0.9f ||
                Mathf.Abs(Vector3.Dot(dir2, dir3)) > 0.9f)
            {
                return IntersectionType.TJunction;
            }
        }

        // Default to complex for 4-way intersections or irregular 3-ways that aren't clean T-junctions.
        return IntersectionType.Complex;
    }

    /// <summary>
    /// Calculates the minimal bounding box (in tiles) for an intersection based on connecting road widths.
    /// </summary>
    private Vector2Int CalculateIntersectionDimensions(List<MapData.Street> connections)
    {
        int widthInTiles = 0;  // Represents span along X-axis, determined by vertical streets.
        int heightInTiles = 0; // Represents span along Z-axis, determined by horizontal streets.

        foreach (var street in connections)
        {
            var p1 = street.Points[0];
            var p2 = street.Points[1];
            Vector3 dir = (p2 - p1).normalized;

            // Horizontal street contributes to the intersection's height (Z-span)
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z)) 
            {
                heightInTiles = Mathf.Max(heightInTiles, GetStreetWidthInTiles(street));
            }
            // Vertical street contributes to the intersection's width (X-span)
            else 
            {
                widthInTiles = Mathf.Max(widthInTiles, GetStreetWidthInTiles(street));
            }
        }
        
        // Ensure odd dimensions for a clear center tile, which simplifies calculations.
        if (widthInTiles > 0 && widthInTiles % 2 == 0) widthInTiles++;
        if (heightInTiles > 0 && heightInTiles % 2 == 0) heightInTiles++;

        return new Vector2Int(widthInTiles, heightInTiles);
    }

    private void PlaceTile(TilePrefab tileDef, Vector3 basePosition, Quaternion baseRotation, bool isSidewalk = false)
    {
        if (tileDef == null || tileDef.prefab == null) return;

        Transform parentContainer = isSidewalk ? sidewalkTileContainer : tileContainer;
        if (parentContainer == null)
        {
            Debug.LogError($"Cannot place tile, the required container (isSidewalk: {isSidewalk}) is null.");
            return;
        }

        GameObject tileInstance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(tileDef.prefab, parentContainer);
        tileInstance.transform.localPosition = basePosition + tileDef.positionOffset;
        tileInstance.transform.localRotation = baseRotation * Quaternion.Euler(tileDef.rotationOffset);

        // Add and configure the tile info component for the combiner.
        RoadTileInfo tileInfo = tileInstance.AddComponent<RoadTileInfo>();
        if (tileDef == tileManager.road) tileInfo.tileType = RoadTileInfo.TileType.Road;
        else if (tileDef == tileManager.crosswalk) tileInfo.tileType = RoadTileInfo.TileType.Crosswalk;
        else if (tileDef == tileManager.roadLine) tileInfo.tileType = RoadTileInfo.TileType.RoadLine;
        else if (tileDef == tileManager.roadLinePassing) tileInfo.tileType = RoadTileInfo.TileType.RoadLinePassing;
        else if (tileDef == tileManager.roadLineNoPassing) tileInfo.tileType = RoadTileInfo.TileType.RoadLineNoPassing;
        else if (tileDef == tileManager.roadLineCorner) tileInfo.tileType = RoadTileInfo.TileType.RoadLine; // Treat corners as regular lines for now
        else if (tileDef == tileManager.roadLinePassingCorner) tileInfo.tileType = RoadTileInfo.TileType.RoadLinePassing;
        else if (tileDef == tileManager.roadLineNoPassingCorner) tileInfo.tileType = RoadTileInfo.TileType.RoadLineNoPassing;
        // NOTE: Curb/Sidewalk types will be assigned here once they are added to TileManager.
        else if (tileDef == tileManager.sidewalk) tileInfo.tileType = RoadTileInfo.TileType.Sidewalk;
    }

    /// <summary>
    /// Returns the appropriate corner tile prefab for a given straight line prefab.
    /// </summary>
    private TilePrefab GetCornerTileFor(TilePrefab straightTile)
    {
        if (straightTile == tileManager.roadLine) return tileManager.roadLineCorner;
        if (straightTile == tileManager.roadLinePassing) return tileManager.roadLinePassingCorner;
        if (straightTile == tileManager.roadLineNoPassing) return tileManager.roadLineNoPassingCorner;
        // Add other mappings here as needed
        return null; // Return null if no corner equivalent is found
    }

    /// <summary>
    /// Calculates the required Y-axis rotation for a corner tile based on the incoming street vectors.
    /// </summary>
    private float GetCornerRotation(Vector3 dir1, Vector3 dir2)
    {
        // Vectors point AWAY from the node. We want the quadrant they form.
        // dir1 and dir2 are normalized and axis-aligned.
        float sumX = dir1.x + dir2.x;
        float sumZ = dir1.z + dir2.z;

        // Top-Right Quadrant (X>0, Z>0)
        if (sumX > 0 && sumZ > 0) return 0;
        // Top-Left Quadrant (X<0, Z>0)
        if (sumX < 0 && sumZ > 0) return 270;
        // Bottom-Left Quadrant (X<0, Z<0)
        if (sumX < 0 && sumZ < 0) return 180;
        // Bottom-Right Quadrant (X>0, Z<0)
        if (sumX > 0 && sumZ < 0) return 90;

        return 0; // Default case
    }

#if UNITY_EDITOR
    [Header("Gizmo Settings")]
    [SerializeField] private Color gridColor = Color.gray;

    private void OnDrawGizmos()
    {
        // Get TileManager reference for gizmo toggle
        if (tileManager == null)
        {
            tileManager = FindFirstObjectByType<TileManager>();
        }

        // Respect the toggle in TileManager
        if (tileManager == null || !tileManager.showGridGizmo)
        {
            return;
        }

        // Ensure we have the processor, especially in edit mode.
        if (geoJsonProcessor == null)
        {
            geoJsonProcessor = GetComponent<GeoJSONProcessor>();
        }

        if (geoJsonProcessor == null || geoJsonProcessor.mapData == null || geoJsonProcessor.MapBounds.size == Vector3.zero)
        {
            return;
        }

        Bounds bounds = geoJsonProcessor.MapBounds;
        Gizmos.color = gridColor;

        // Apply the same transform as the GeoJSONProcessor's gizmos to ensure alignment.
        if (geoJsonProcessor.mapBoundsTransform != null)
        {
            Gizmos.matrix = geoJsonProcessor.mapBoundsTransform.localToWorldMatrix;
        }
        
        float size = gridSettings.gridSize;
        if (size <= 0) return;

        // Draw vertical grid lines to outline tiles
        float startX = Mathf.Ceil(bounds.min.x / size - 0.5f);
        float endX = Mathf.Floor(bounds.max.x / size + 0.5f);
        for (float i = startX; i <= endX; i++)
        {
            float x = (i - 0.5f) * size;
            Gizmos.DrawLine(new Vector3(x, 0, bounds.min.z), new Vector3(x, 0, bounds.max.z));
        }

        // Draw horizontal grid lines to outline tiles
        float startZ = Mathf.Ceil(bounds.min.z / size - 0.5f);
        float endZ = Mathf.Floor(bounds.max.z / size + 0.5f);
        for (float i = startZ; i <= endZ; i++)
        {
            float z = (i - 0.5f) * size;
            Gizmos.DrawLine(new Vector3(bounds.min.x, 0, z), new Vector3(bounds.max.x, 0, z));
        }
    }
#endif
}
