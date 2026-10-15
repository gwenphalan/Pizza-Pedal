using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor for the RoadBuilder component.
/// Adds "Build Roads" and "Clear Roads" buttons to the inspector for easy use.
/// </summary>
[CustomEditor(typeof(RoadBuilder))]
[CanEditMultipleObjects]
public class RoadBuilderEditor : Editor
{
    private float chunkSize = 100f;

    public override void OnInspectorGUI()
    {
        // Draw the default inspector fields (e.g., Road Container, Grid Color)
        DrawDefaultInspector();

        // Get a reference to the RoadBuilder script instance
        RoadBuilder roadBuilder = (RoadBuilder)target;

        // Add a horizontal layout group for the buttons
        EditorGUILayout.BeginHorizontal();

        // Add a "Build Roads" button
        if (GUILayout.Button("Build Roads"))
        {
            // Call the BuildRoads method when the button is clicked
            roadBuilder.BuildRoads();
        }

        // Add a "Clear Roads" button
        if (GUILayout.Button("Clear Roads"))
        {
            // Call the ClearRoads method when the button is clicked
            roadBuilder.ClearRoads();
        }

        EditorGUILayout.EndHorizontal();

        // --- Terrain Projection Section ---
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Terrain Projection", EditorStyles.boldLabel);

        if (GUILayout.Button("Project Roads to Terrain"))
        {
            if (roadBuilder.terrain != null)
            {
                roadBuilder.ProjectToTerrain();
            }
            else
            {
                Debug.LogError("RoadBuilder's 'Terrain' is not assigned. Cannot project roads.");
            }
        }

        // --- Combine Meshes Section ---
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Mesh Combining", EditorStyles.boldLabel);
        
        chunkSize = EditorGUILayout.FloatField("Chunk Size", chunkSize);

        if (GUILayout.Button("Combine Road Meshes"))
        {
            if (roadBuilder.roadContainer != null)
            {
                RoadMeshCombiner.Combine(roadBuilder.roadContainer.gameObject, chunkSize);
            }
            else
            {
                Debug.LogError("RoadBuilder's 'Road Container' is not assigned. Cannot combine meshes.");
            }
        }

        // --- Sidewalk Section ---
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Sidewalk Generation", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Build Sidewalks"))
        {
            roadBuilder.BuildSidewalks();
        }

        if (GUILayout.Button("Clear Sidewalks"))
        {
            roadBuilder.ClearSidewalks();
        }

        EditorGUILayout.EndHorizontal();
    }
} 