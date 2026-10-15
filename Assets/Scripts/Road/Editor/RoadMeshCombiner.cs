using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A static utility class that handles the logic for combining road meshes.
/// </summary>
public static class RoadMeshCombiner
{
    private const string OPTIMIZED_CONTAINER_NAME = "Optimized World";
    private const string ROAD_CHUNK_CONTAINER_NAME = "Road Chunks";
    private const string TILE_CONTAINER_NAME = "Road Tiles";

    /// <summary>
    /// Main entry point for the combination process.
    /// </summary>
    public static void Combine(GameObject roadContainer, float chunkSize)
    {
        if (roadContainer == null)
        {
            Debug.LogError("Road container is null. Cannot combine.");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Combining Meshes", "Preparing...", 0f);

            // Find the container with the individual tiles to be processed.
            var tileContainer = roadContainer.transform.Find(TILE_CONTAINER_NAME);
            if (tileContainer == null)
            {
                Debug.LogError($"Could not find the '{TILE_CONTAINER_NAME}' container. Please build roads first.", roadContainer);
                return;
            }

            var allTiles = tileContainer.GetComponentsInChildren<RoadTileInfo>();
            if (allTiles.Length == 0)
            {
                Debug.LogWarning("No RoadTileInfo components found in the tile container. Nothing to combine.");
                return;
            }

            // --- Create Parent Structure ---
            // Find or create the main container for all optimized objects, as a child of the main road container.
            var optimizedWorld = roadContainer.transform.Find(OPTIMIZED_CONTAINER_NAME);
            if (optimizedWorld != null)
            {
                Object.DestroyImmediate(optimizedWorld.gameObject);
            }
            optimizedWorld = new GameObject(OPTIMIZED_CONTAINER_NAME).transform;
            optimizedWorld.SetParent(roadContainer.transform, false);
            
            // --- Define Tile Groups ---
            // This structure makes it easy to add more types (like sidewalks) later.
            var roadTypes = new List<RoadTileInfo.TileType>
            {
                RoadTileInfo.TileType.Road,
                RoadTileInfo.TileType.RoadLine,
                RoadTileInfo.TileType.RoadLinePassing,
                RoadTileInfo.TileType.RoadLineNoPassing,
                RoadTileInfo.TileType.Crosswalk
            };
            
            // --- Run Combination ---
            Debug.Log("Starting road mesh combination...");
            CombineTileTypes(allTiles, roadTypes, optimizedWorld.transform, ROAD_CHUNK_CONTAINER_NAME, chunkSize);
            
            // --- Cleanup ---
            // Destroy the original tile container after combination is complete.
            EditorUtility.DisplayProgressBar("Combining Meshes", "Cleaning up...", 0.99f);
            Debug.Log($"Destroying original '{TILE_CONTAINER_NAME}' container.");
            Object.DestroyImmediate(tileContainer.gameObject);

            Debug.Log("Mesh combination complete!");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>
    /// The core reusable combining logic. It finds all tiles of the specified types,
    /// groups them into chunks, and combines them.
    /// </summary>
    private static void CombineTileTypes(IEnumerable<RoadTileInfo> allTiles, List<RoadTileInfo.TileType> typesToCombine, Transform parent, string chunkContainerName, float chunkSize)
    {
        var filteredTiles = allTiles.Where(t => typesToCombine.Contains(t.tileType)).ToList();
        if (filteredTiles.Count == 0) return;

        // Create a container for the new chunks
        var chunkContainer = parent.Find(chunkContainerName);
        if (chunkContainer != null) Object.DestroyImmediate(chunkContainer.gameObject);
        chunkContainer = new GameObject(chunkContainerName).transform;
        chunkContainer.parent = parent;
        
        // Partition tiles into chunks
        EditorUtility.DisplayProgressBar("Combining Meshes", "Partitioning tiles into chunks...", 0.1f);
        var chunks = new Dictionary<Vector2Int, List<RoadTileInfo>>();
        foreach (var tile in filteredTiles)
        {
            Vector2Int chunkCoord = new Vector2Int(
                Mathf.FloorToInt(tile.transform.position.x / chunkSize),
                Mathf.FloorToInt(tile.transform.position.z / chunkSize)
            );
            if (!chunks.ContainsKey(chunkCoord))
            {
                chunks[chunkCoord] = new List<RoadTileInfo>();
            }
            chunks[chunkCoord].Add(tile);
        }

        // Process each chunk
        var chunkPairs = chunks.ToList();
        for (int c = 0; c < chunkPairs.Count; c++)
        {
            var chunkPair = chunkPairs[c];
            float progress = 0.2f + ((float)c / chunkPairs.Count) * 0.8f;
            EditorUtility.DisplayProgressBar("Combining Meshes", $"Processing chunk {c + 1}/{chunkPairs.Count}", progress);

            var chunkTiles = chunkPair.Value;
            if (chunkTiles.Count == 0) continue;

            // --- Rebuild LOD Group ---
            var originalLODGroup = chunkTiles[0].GetComponent<LODGroup>();
            if (originalLODGroup == null) continue;
            
            var lods = originalLODGroup.GetLODs();
            var newLODs = new LOD[lods.Length];
            
            // Create a parent for the combined LOD meshes
            GameObject chunkParentGO = new GameObject($"{chunkContainerName}_Chunk_{chunkPair.Key.x}_{chunkPair.Key.y}");
            chunkParentGO.transform.parent = chunkContainer;

            // Combine each LOD level
            for (int i = 0; i < lods.Length; i++)
            {
                var originalRenderers = new List<Renderer>();
                foreach (var tile in chunkTiles)
                {
                    var tileLODGroup = tile.GetComponent<LODGroup>();
                    if(tileLODGroup != null && tileLODGroup.GetLODs().Length > i)
                    {
                        originalRenderers.AddRange(tileLODGroup.GetLODs()[i].renderers.Where(r => r != null));
                    }
                }
                if (originalRenderers.Count == 0) continue;

                var meshFilters = originalRenderers.Select(r => r.GetComponent<MeshFilter>()).ToList();
                var combineInstances = new CombineInstance[meshFilters.Count];
                for (int j = 0; j < meshFilters.Count; j++)
                {
                    combineInstances[j].mesh = meshFilters[j].sharedMesh;
                    combineInstances[j].transform = meshFilters[j].transform.localToWorldMatrix;
                }

                var combinedMesh = new Mesh();
                combinedMesh.CombineMeshes(combineInstances);

                // Create the new GameObject for this combined LOD mesh
                GameObject lodGO = new GameObject($"LOD_{i}");
                lodGO.transform.parent = chunkParentGO.transform;
                var mf = lodGO.AddComponent<MeshFilter>();
                mf.sharedMesh = combinedMesh;
                var mr = lodGO.AddComponent<MeshRenderer>();
                mr.sharedMaterial = originalRenderers[0].sharedMaterial; // Assume all use the same material
                
                newLODs[i] = new LOD(lods[i].screenRelativeTransitionHeight, new Renderer[] { mr });
            }
            
            var newLODGroup = chunkParentGO.AddComponent<LODGroup>();
            newLODGroup.SetLODs(newLODs);
            newLODGroup.RecalculateBounds();
        }
    }
} 