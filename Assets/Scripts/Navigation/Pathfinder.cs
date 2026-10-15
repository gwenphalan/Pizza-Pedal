using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Contains the A* pathfinding algorithm to find a route between two nodes on the map.
/// This is a pure C# class and does not derive from MonoBehaviour.
/// </summary>
public class Pathfinder
{
    /// <summary>
    /// Finds a path between a start and end node using the A* algorithm.
    /// </summary>
    /// <param name="mapData">The map data containing the routing graph.</param>
    /// <param name="startNodeId">The ID of the starting node.</param>
    /// <param name="endNodeId">The ID of the ending node.</param>
    /// <returns>A list of RouteNodes representing the path from start to end, or null if no path is found.</returns>
    public List<MapData.RoutingNode> FindPath(MapData mapData, int startNodeId, int endNodeId)
    {
        var openSet = new PriorityQueue<int>();
        openSet.Enqueue(startNodeId, 0);

        var cameFrom = new Dictionary<int, int>();

        var gScore = new Dictionary<int, float>();
        gScore[startNodeId] = 0;

        var fScore = new Dictionary<int, float>();
        fScore[startNodeId] = Heuristic(mapData.RoutingNodes[startNodeId], mapData.RoutingNodes[endNodeId]);

        while (openSet.Count > 0)
        {
            int currentId = openSet.Dequeue();
            if (currentId == endNodeId)
            {
                return ReconstructPath(cameFrom, currentId, mapData);
            }

            var currentNode = mapData.RoutingNodes[currentId];
            foreach (var connection in currentNode.Connections)
            {
                int neighborId = connection.Key;
                float distance = connection.Value;
                
                var neighborNode = mapData.RoutingNodes[neighborId];
                float tentativeGScore = gScore.GetValueOrDefault(currentId, float.MaxValue) + distance;

                if (tentativeGScore < gScore.GetValueOrDefault(neighborId, float.MaxValue))
                {
                    cameFrom[neighborId] = currentId;
                    gScore[neighborId] = tentativeGScore;
                    fScore[neighborId] = tentativeGScore + Heuristic(neighborNode, mapData.RoutingNodes[endNodeId]);
                    
                    if (!openSet.Contains(neighborId))
                    {
                        openSet.Enqueue(neighborId, fScore[neighborId]);
                    }
                }
            }
        }
        
        // No path found
        return null;
    }

    /// <summary>
    /// Reconstructs the path from the end node back to the start node.
    /// </summary>
    /// <param name="cameFrom">A dictionary mapping each node to its predecessor on the path.</param>
    /// <param name="current">The current node (should be the end node).</param>
    /// <param name="mapData">The map data to retrieve node objects.</param>
    /// <returns>A list of nodes representing the path.</returns>
    private List<MapData.RoutingNode> ReconstructPath(Dictionary<int, int> cameFrom, int currentId, MapData mapData)
    {
        var totalPath = new List<MapData.RoutingNode> { mapData.RoutingNodes[currentId] };
        while (cameFrom.ContainsKey(currentId))
        {
            currentId = cameFrom[currentId];
            totalPath.Insert(0, mapData.RoutingNodes[currentId]);
        }
        return totalPath;
    }

    /// <summary>
    /// The heuristic function for A*. Estimates the cost to get from one node to another.
    /// </summary>
    private float Heuristic(MapData.RoutingNode a, MapData.RoutingNode b)
    {
        return Vector3.Distance(a.Position, b.Position);
    }
}

/// <summary>
/// A simple Priority Queue implementation for the A* algorithm.
/// </summary>
public class PriorityQueue<T>
{
    private List<(T item, float priority)> elements = new List<(T, float)>();

    public int Count => elements.Count;

    public void Enqueue(T item, float priority)
    {
        elements.Add((item, priority));
        elements.Sort((a, b) => a.priority.CompareTo(b.priority));
    }

    public T Dequeue()
    {
        var item = elements[0].item;
        elements.RemoveAt(0);
        return item;
    }

    public bool Contains(T item)
    {
        foreach (var element in elements)
        {
            if (EqualityComparer<T>.Default.Equals(element.item, item))
            {
                return true;
            }
        }
        return false;
    }
} 