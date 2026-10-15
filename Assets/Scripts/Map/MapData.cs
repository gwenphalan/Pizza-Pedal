using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A container for the processed map data, including streets and the routing graph.
/// </summary>
[System.Serializable]
public class MapData
{
    public List<Street> Streets = new List<Street>();
    public Dictionary<int, RoutingNode> RoutingNodes = new Dictionary<int, RoutingNode>();
    
    /// <summary>
    /// Represents a single street, which is a collection of points.
    /// </summary>
    [System.Serializable]
    public class Street
    {
        public string Name;
        public List<Vector3> Points = new List<Vector3>();
        public bool HasSidewalk;
        public bool IsOneWay;
        public int LaneCount;
    }

    /// <summary>
    /// Represents a node in the routing graph (e.g., an intersection or endpoint).
    /// </summary>
    [System.Serializable]
    public class RoutingNode
    {
        public int Id;
        public Vector3 Position;
        // The key is the ID of the connected node, the value is the distance to it.
        public Dictionary<int, float> Connections = new Dictionary<int, float>();
        
        public RoutingNode(int id, Vector3 position)
        {
            Id = id;
            Position = position;
        }
    }
} 