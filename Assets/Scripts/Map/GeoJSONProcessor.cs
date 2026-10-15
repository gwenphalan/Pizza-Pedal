using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;

/// <summary>
/// Processes a GeoJSON file into usable map data structures.
/// This version automatically detects intersections between line segments, creates nodes at those intersections,
/// and rebuilds the street network as a fully connected graph.
/// </summary>
[System.Serializable]
public class GeoJSONProcessor : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("The GeoJSON file containing the map data.")]
    public TextAsset geoJsonFile;

    [Header("Map Representation")]
    [Tooltip("A transform that defines the area where the map data will be rendered. Its scale affects the size of the rendered map.")]
    public Transform mapBoundsTransform;

    [HideInInspector]
    public MapData mapData;

    [Header("Gizmo Settings")]
    [SerializeField] private Color streetColor = Color.white;
    [SerializeField] private Color nodeColor = Color.yellow;
    [SerializeField] private float nodeSize = 1.0f;

    public Bounds MapBounds { get; private set; }

    private void Awake()
    {
        if (geoJsonFile != null)
        {
            ProcessGeoJsonV2(geoJsonFile.text);
        }
        else
        {
            Debug.LogError("GeoJSON file is not assigned.", this);
        }
    }

    public void ProcessGeoJsonV2(string json)
    {
        mapData = new MapData();
        var parsedJson = JObject.Parse(json);
        var features = parsedJson["features"];

        if (features == null)
        {
            Debug.LogError("GeoJSON file does not contain a 'features' array.", this);
            return;
        }

        // A temporary representation of streets before they are split by intersections
        var tempStreets = new List<MapData.Street>();
        var allSegments = new List<(Vector3 start, Vector3 end, MapData.Street street)>();

        // Pass 1: Read all features and their segments from GeoJSON
        foreach (var feature in features)
        {
            var street = new MapData.Street();
            var points = feature["geometry"]["coordinates"]
                .Select(p => new Vector3(p[0].Value<float>(), 0, p[1].Value<float>()))
                .ToList();
            
            street.Points.AddRange(points);
            street.Name = feature["properties"]["Street Name"]?.ToString() ?? "Unnamed Street";
            street.HasSidewalk = feature["properties"]["sidewalk"]?.Value<bool>() ?? false;
            street.IsOneWay = feature["properties"]["oneWay"]?.Value<bool>() ?? false;
            street.LaneCount = feature["properties"]["laneCount"]?.Value<int>() ?? 1;
            tempStreets.Add(street);

            for (int i = 0; i < points.Count - 1; i++)
            {
                allSegments.Add((points[i], points[i + 1], street));
            }
        }

        // Pass 2: Find all intersection points and collect all unique node positions
        var nodePositions = new HashSet<Vector3>();
        foreach (var p in tempStreets.SelectMany(s => s.Points))
        {
            nodePositions.Add(p);
        }

        for (int i = 0; i < allSegments.Count; i++)
        {
            for (int j = i + 1; j < allSegments.Count; j++)
            {
                if (LineSegmentsIntersection(allSegments[i].start, allSegments[i].end, allSegments[j].start, allSegments[j].end, out var intersection))
                {
                    nodePositions.Add(intersection);
                }
            }
        }
        
        // Pass 3: Create routing nodes from the unique positions
        var nodesByPosition = new Dictionary<Vector3, int>();
        int nodeIdCounter = 0;
        foreach (var pos in nodePositions)
        {
            var newNode = new MapData.RoutingNode(nodeIdCounter, pos);
            mapData.RoutingNodes.Add(nodeIdCounter, newNode);
            nodesByPosition[pos] = nodeIdCounter;
            nodeIdCounter++;
        }

        // Pass 4: Create the pathfinding connections and a clean list of two-point street segments
        // for the renderer to process. This ensures MergeConnectedStreets gets the simple, clean
        // data it needs to function correctly.
        var createdSegments = new HashSet<(int, int)>();
        foreach (var originalSegment in allSegments)
        {
            var nodesOnSegment = new List<MapData.RoutingNode>();
            foreach (var node in mapData.RoutingNodes.Values)
            {
                if (IsPointOnLineSegment(originalSegment.start, originalSegment.end, node.Position))
                {
                    nodesOnSegment.Add(node);
                }
            }
            nodesOnSegment = nodesOnSegment.OrderBy(n => Vector3.Distance(originalSegment.start, n.Position)).ToList();

            for (int i = 0; i < nodesOnSegment.Count - 1; i++)
            {
                var node1 = nodesOnSegment[i];
                var node2 = nodesOnSegment[i + 1];

                // Create a canonical, order-independent key for the segment
                int id1 = node1.Id;
                int id2 = node2.Id;
                var segmentKey = id1 < id2 ? (id1, id2) : (id2, id1);

                // Ensure each segment is only created once
                if (createdSegments.Contains(segmentKey)) continue;

                // Add connections for pathfinding
                float distance = Vector3.Distance(node1.Position, node2.Position);
                if (!node1.Connections.ContainsKey(node2.Id)) node1.Connections.Add(node2.Id, distance);
                if (!node2.Connections.ContainsKey(node1.Id)) node2.Connections.Add(node1.Id, distance);
                
                // Create final two-point street segment for rendering
                var newStreet = new MapData.Street
                {
                    Name = originalSegment.street.Name, // Inherit properties from original
                    HasSidewalk = originalSegment.street.HasSidewalk,
                    IsOneWay = originalSegment.street.IsOneWay,
                    LaneCount = originalSegment.street.LaneCount,
                    Points = new List<Vector3> { node1.Position, node2.Position }
                };
                mapData.Streets.Add(newStreet);
                createdSegments.Add(segmentKey);
            }
        }
        
        // Final pass to calculate map bounds
        CalculateMapBounds(features);
    }
    
    private void CalculateMapBounds(JToken features)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var feature in features)
        {
            var coordinates = feature["geometry"]["coordinates"];
            foreach (var point in coordinates)
            {
                float x = point[0].Value<float>();
                float z = point[1].Value<float>();
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }
        }
        if (minX != float.MaxValue)
        {
            Vector3 size = new Vector3(maxX - minX, 1f, maxZ - minZ);
            Vector3 center = new Vector3(minX + size.x / 2, 0, minZ + size.z / 2);
            MapBounds = new Bounds(center, size);
        }
        else
        {
            MapBounds = new Bounds(Vector3.zero, Vector3.zero);
        }
    }
    
    // Check if two line segments intersect.
    public static bool LineSegmentsIntersection(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, out Vector3 intersection)
    {
        intersection = Vector3.zero;

        float d = (p2.x - p1.x) * (p4.z - p3.z) - (p2.z - p1.z) * (p4.x - p3.x);
        if (Mathf.Abs(d) < 0.0001f) return false; // Parallel

        float t = ((p3.x - p1.x) * (p4.z - p3.z) - (p3.z - p1.z) * (p4.x - p3.x)) / d;
        float u = -((p2.x - p1.x) * (p3.z - p1.z) - (p2.z - p1.z) * (p3.x - p1.x)) / d;

        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
        {
            intersection = new Vector3(p1.x + t * (p2.x - p1.x), 0, p1.z + t * (p2.z - p1.z));
            return true;
        }

        return false;
    }
    
    // Check if a point lies on a line segment.
    public static bool IsPointOnLineSegment(Vector3 start, Vector3 end, Vector3 point)
    {
        float totalDist = Vector3.Distance(start, end);
        float distToPoint = Vector3.Distance(start, point) + Vector3.Distance(point, end);
        // Using a tolerance for floating point inaccuracies
        return Mathf.Abs(totalDist - distToPoint) < 0.01f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (geoJsonFile != null && mapBoundsTransform != null)
        {
            ProcessGeoJsonV2(geoJsonFile.text);
        }
        else
        {
            mapData = null;
        }
    }
    
    private void OnDrawGizmos()
    {
        // Ensure data is processed in the editor, as OnValidate isn't always called.
        if (mapData == null && geoJsonFile != null)
        {
            ProcessGeoJsonV2(geoJsonFile.text);
        }

        if (mapBoundsTransform == null) return;

        Gizmos.matrix = mapBoundsTransform.localToWorldMatrix;
        UnityEditor.Handles.matrix = mapBoundsTransform.localToWorldMatrix;

        /*
        if (MapBounds.size != Vector3.zero)
        {
            Gizmos.color = new Color(0, 1, 1, 0.2f);
            Gizmos.DrawCube(MapBounds.center, MapBounds.size);
        }
        */

        if (mapData != null)
        {
            // --- Style for Gizmo Labels ---
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = 12; // Set a reasonable font size

            Gizmos.color = streetColor;
            foreach (var street in mapData.Streets)
            {
                if(street.Points.Count < 2) continue;
                
                Vector3 p1 = street.Points[0];
                Vector3 p2 = street.Points[1];

                Gizmos.DrawLine(p1, p2);

                // --- Draw Street Info ---
                Vector3 midPoint = (p1 + p2) * 0.5f;
                string label = $"\"{street.Name}\"";
                label += $"\nLanes: {street.LaneCount}";
                if (street.IsOneWay)
                {
                    label += " (One-Way)";
                }
                label += $"\nSidewalk: {(street.HasSidewalk ? "Yes" : "No")}";
                UnityEditor.Handles.Label(midPoint + new Vector3(0, 1.5f, 0), label, style);
            }

            Gizmos.color = nodeColor;
            float nodeRenderSize = nodeSize / mapBoundsTransform.lossyScale.x;
            foreach (var node in mapData.RoutingNodes.Values)
            {
                Gizmos.DrawSphere(node.Position, nodeRenderSize);
            }
        }
    }
#endif
}