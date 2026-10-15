using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;
using Prototype.Bicycle;

/// <summary>
/// Manages pathfinding requests and provides the final calculated path.
/// This is the central coordinator for the navigation system.
/// </summary>
public class NavigationManager : MonoBehaviour
{
    public static NavigationManager Instance { get; private set; }

    [Tooltip("The processor that contains the map data.")]
    [SerializeField] private GeoJSONProcessor mapProcessor;
    
    [Tooltip("The distance in meters the player must be from the path before a recalculation is triggered.")]
    [SerializeField] public float recalculationThreshold = 20f;
    
    [Header("Path Rendering")]
    [Tooltip("If true, the rendered path will include a segment from the player's current position back to the road network.")]
    [SerializeField] private bool allowOffRoadPathConnection = false;
    
    /// <summary>
    /// Fired when a new path has been calculated. The list of Vector3 represents the points on the path.
    /// </summary>
    public event Action<List<Vector3>> OnPathUpdated;

    private Pathfinder _pathfinder;
    private List<Vector3> _currentPath;
    private bool _isCalculating;

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            _pathfinder = new Pathfinder();
        }
    }

    private void Update()
    {
        if (_isCalculating || _currentPath == null || _currentPath.Count < 2)
        {
            return;
        }

        var playerPosition = FindFirstObjectByType<BikeMovementController>()?.transform.position ?? Vector3.zero;

        // Find the player's progress along the calculated path
        var (segmentIndex, closestPointOnPath) = FindClosestSegmentOnPolyline(_currentPath, playerPosition);

        // First, check if we need to recalculate the whole path because we've deviated too much.
        if (Vector3.Distance(playerPosition, closestPointOnPath) > recalculationThreshold)
        {
            var finalDestination = new List<Vector3> { _currentPath.Last() };
            RequestPath(finalDestination);
            return; // Exit after requesting a new path
        }
        
        // If we are on the path, create a new list for rendering that starts from our current position.
        // This makes the path "disappear" behind the player.
        var pathToRender = new List<Vector3>();

        if (allowOffRoadPathConnection)
        {
            // Start the visible path from the player's X/Z position, but use the road's Y-coordinate
            // to ensure the y-offset is applied correctly and prevent clipping.
            var startPoint = new Vector3(playerPosition.x, closestPointOnPath.y, playerPosition.z);
            pathToRender.Add(startPoint);
        }
        
        // The rendered path will always connect to the closest point ON THE ROAD.
        pathToRender.Add(closestPointOnPath);

        // Add the rest of the points from the original path
        for (int i = segmentIndex + 1; i < _currentPath.Count; i++)
        {
            pathToRender.Add(_currentPath[i]);
        }

        // Send this dynamic path to the renderer every frame.
        OnPathUpdated?.Invoke(pathToRender);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Requests a path to be calculated through a series of waypoints.
    /// </summary>
    /// <param name="waypoints">A list of world positions to route through.</param>
    /// <param name="pathColor">An optional color to apply to the rendered path.</param>
    public void RequestPath(List<Vector3> waypoints, Color? pathColor = null)
    {
        if (_isCalculating || waypoints == null || waypoints.Count == 0)
        {
            return;
        }

        if (pathColor.HasValue && MapRendererService.Instance != null)
        {
            MapRendererService.Instance.SetPathColor(pathColor.Value);
        }

        StartCoroutine(CalculatePathCoroutine(waypoints));
    }

    /// <summary>
    /// Calculates the total distance of a potential route through a series of waypoints.
    /// This is a synchronous, blocking call intended for UI planning purposes.
    /// </summary>
    /// <param name="waypoints">A list of world positions to route through.</param>
    /// <returns>The total distance of the path in meters.</returns>
    public float CalculateRouteDistance(List<Vector3> waypoints)
    {
        if (waypoints == null || waypoints.Count < 2)
        {
            return 0f;
        }

        float totalDistance = 0f;
        List<Vector3> fullPath = new List<Vector3>();
        
        // Assume player position is the start
        var currentPos = FindFirstObjectByType<BikeMovementController>()?.transform.position ?? Vector3.zero;

        for (int i = 0; i < waypoints.Count; i++)
        {
            var startPointInfo = FindClosestPointOnNetwork(currentPos);
            var endPointInfo = FindClosestPointOnNetwork(waypoints[i]);
            
            if (startPointInfo.startNodeId == -1 || endPointInfo.startNodeId == -1) continue;

            var pathNodes = _pathfinder.FindPath(mapProcessor.mapData, startPointInfo.endNodeId, endPointInfo.startNodeId);
            if (pathNodes != null)
            {
                fullPath.Add(currentPos);
                fullPath.Add(startPointInfo.point);
                fullPath.AddRange(pathNodes.Select(node => node.Position));
                fullPath.Add(endPointInfo.point);
                fullPath.Add(waypoints[i]);
            }
            
            currentPos = waypoints[i];
        }

        for (int i = 0; i < fullPath.Count - 1; i++)
        {
            totalDistance += Vector3.Distance(fullPath[i], fullPath[i+1]);
        }
        
        return totalDistance;
    }

    #endregion

    #region Coroutines

    private System.Collections.IEnumerator CalculatePathCoroutine(List<Vector3> waypoints)
    {
        _isCalculating = true;

        if (waypoints.Count == 0)
        {
            _isCalculating = false;
            yield break;
        }
        var destination = waypoints[0];
        var playerPosition = FindFirstObjectByType<BikeMovementController>()?.transform.position ?? Vector3.zero;

        var startNetworkInfo = FindClosestPointOnNetwork(playerPosition);
        var endNetworkInfo = FindClosestPointOnNetwork(destination);

        if (startNetworkInfo.startNodeId == -1 || endNetworkInfo.startNodeId == -1)
        {
            Debug.LogError("[NavigationManager] Could not find a valid start or end point on the network.");
            _isCalculating = false;
            yield break;
        }

        // --- Intelligent Path Selection ---
        // We now test all 4 possible node connections between the start and end segments
        // to find the absolute shortest path. This prevents "stupid routing" at intersections.

        var potentialStarts = new[] { startNetworkInfo.startNodeId, startNetworkInfo.endNodeId };
        var potentialEnds = new[] { endNetworkInfo.startNodeId, endNetworkInfo.endNodeId };
        
        List<Vector3> bestPath = null;
        float shortestDistance = float.MaxValue;

        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                var pathNodes = _pathfinder.FindPath(mapProcessor.mapData, potentialStarts[i], potentialEnds[j]);
                if (pathNodes == null) continue;

                // Calculate the total length of this potential path
                var worldPath = pathNodes.Select(node => mapProcessor.mapBoundsTransform.TransformPoint(node.Position)).ToList();
                float currentDistance = GetPathDistance(worldPath);
                
                // Add the distance from the snap points to the actual start/end
                currentDistance += Vector3.Distance(startNetworkInfo.point, worldPath.First());
                currentDistance += Vector3.Distance(endNetworkInfo.point, worldPath.Last());

                if (currentDistance < shortestDistance)
                {
                    shortestDistance = currentDistance;
                    bestPath = worldPath;
                }
            }
        }
        
        // --- Path Construction ---
        List<Vector3> fullPath = new List<Vector3>();
        if (bestPath != null)
        {
            // Path starts at the point on the road network closest to the player
            fullPath.Add(startNetworkInfo.point);
            // Add the nodes of the best path
            fullPath.AddRange(bestPath);
            // Path ends at the point on the road network closest to the destination
            fullPath.Add(endNetworkInfo.point);
        }
        else
        {
            Debug.LogError("[NavigationManager] Failed to find any valid path between start and end points.");
        }

        _currentPath = fullPath;
        OnPathUpdated?.Invoke(_currentPath);
        _isCalculating = false;
        yield return null;
    }

    #endregion

    #region Private Helpers
    
    /// <summary>
    /// Finds the closest point on any street segment in the map data to a given world position.
    /// </summary>
    /// <param name="position">The position to find the closest point to.</param>
    /// <returns>A tuple containing the point on the network, and the IDs of the start and end nodes of the segment it's on.</returns>
    private (Vector3 point, int startNodeId, int endNodeId) FindClosestPointOnNetwork(Vector3 position)
    {
        float minDistance = float.MaxValue;
        (Vector3 point, int startNodeId, int endNodeId) result = (Vector3.zero, -1, -1);

        if (mapProcessor == null || mapProcessor.mapData == null || mapProcessor.mapBoundsTransform == null)
        {
            Debug.LogWarning("Map data or mapBoundsTransform is not available.", this);
            return result;
        }

        Vector3 localPosition = mapProcessor.mapBoundsTransform.InverseTransformPoint(position);

        foreach (var street in mapProcessor.mapData.Streets)
        {
            for (int i = 0; i < street.Points.Count - 1; i++)
            {
                Vector3 p1 = street.Points[i];
                Vector3 p2 = street.Points[i + 1];

                Vector3 closestPoint = FindClosestPointOnLineSegment(p1, p2, localPosition);
                float distance = Vector3.Distance(localPosition, closestPoint);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    // This is now correct because the nodes are granular.
                    int startNodeId = FindNodeIdByPosition(p1);
                    int endNodeId = FindNodeIdByPosition(p2);
                    result = (mapProcessor.mapBoundsTransform.TransformPoint(closestPoint), startNodeId, endNodeId);
                }
            }
        }
        return result;
    }
    
    private Vector3 FindClosestPointOnLineSegment(Vector3 p1, Vector3 p2, Vector3 point)
    {
        Vector3 lineDirection = (p2 - p1).normalized;
        float lineLength = Vector3.Distance(p1, p2);
        Vector3 pointDirection = point - p1;
        
        float dot = Vector3.Dot(pointDirection, lineDirection);
        dot = Mathf.Clamp(dot, 0f, lineLength);
        
        return p1 + lineDirection * dot;
    }
    
    private int FindNodeIdByPosition(Vector3 position)
    {
        foreach (var node in mapProcessor.mapData.RoutingNodes.Values)
        {
            if (Vector3.Distance(node.Position, position) < 0.01f)
            {
                return node.Id;
            }
        }
        return -1; // Not found
    }

    private float GetPathDistance(List<Vector3> path)
    {
        if (path == null || path.Count < 2) return 0;
        float distance = 0;
        for (int i = 0; i < path.Count - 1; i++)
        {
            distance += Vector3.Distance(path[i], path[i+1]);
        }
        return distance;
    }

    private (int index, Vector3 point) FindClosestSegmentOnPolyline(List<Vector3> path, Vector3 point)
    {
        float minDistance = float.MaxValue;
        Vector3 closestPoint = Vector3.zero;
        int closestSegmentIndex = 0;

        if (path == null || path.Count < 2) return (0, point);

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 p1 = path[i];
            Vector3 p2 = path[i + 1];
            Vector3 pointOnSegment = FindClosestPointOnLineSegment(p1, p2, point);
            float distance = Vector3.Distance(point, pointOnSegment);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestPoint = pointOnSegment;
                closestSegmentIndex = i;
            }
        }
        return (closestSegmentIndex, closestPoint);
    }

    #endregion
} 