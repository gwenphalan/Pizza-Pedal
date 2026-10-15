using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;

/// <summary>
/// Defines the rotation behavior for the map.
/// </summary>
public enum MapRenderBehavior
{
    [Tooltip("Map remains stationary with North always facing up.")]
    Static,
    [Tooltip("Map rotates so that the target's forward direction is always 'up'.")]
    Rotating
}

/// <summary>
/// Struct for UI style data (outline, player icon colors, etc.)
/// </summary>
public struct MapUIStyles {
    public Color outlineColor;
    public Color playerIconColor;
}

/// <summary>
/// A centralized service for rendering the map (streets, paths, etc.) to a RenderTexture.
/// This service is completely decoupled from any UI and is controlled by other scripts.
/// </summary>
public class MapRendererService : MonoBehaviour
{
    public static MapRendererService Instance { get; private set; }

    /// <summary>
    /// The final texture containing the rendered map. UI components can subscribe to this.
    /// </summary>
    public RenderTexture MapTexture => _mapTexture;

    [Header("Styling")]
    [Tooltip("The color of the street lines.")]
    public Color streetColor = Color.white;
    [Tooltip("The width of the street lines in world units.")]
    public float streetWidth = 0.5f;
    [Tooltip("The color of the calculated path.")]
    public Color pathColor = Color.cyan;
    [Tooltip("The width of the calculated path line in world units.")]
    public float pathWidth = 1f;
    [Tooltip("The background color of the minimap camera.")]
    public Color mapBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
    [Header("UI Styling")]
    [Tooltip("The color of the minimap outline.")]
    public Color outlineColor = Color.black;
    [Tooltip("The color of the player icon.")]
    public Color playerIconColor = Color.red;

    [Header("Path Rendering")]
    [Tooltip("A small Y-offset to render the path just above the street geometry to prevent z-fighting.")]
    public float pathYOffset = 0.1f;

    [Header("Technical Settings")]
    [Tooltip("The layer(s) that the minimap camera will render.")]
    [SerializeField] private LayerMask minimapLayer;

    // Events
    public event Action<RenderTexture> OnMapUpdated;
    public event Action<MapUIStyles> OnStyleUpdated;

    // Service state
    private Camera _mapCamera;
    private RenderTexture _mapTexture;
    private List<LineRenderer> _pathRenderers = new List<LineRenderer>();
    private Transform _currentTarget;
    private MapRenderBehavior _currentBehavior = MapRenderBehavior.Rotating;
    private float _currentZoom = 50f;

    // Scene Dependencies
    private GeoJSONProcessor _geoJsonProcessor;
    
    #region Unity Lifecycle

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            // In play mode, do full update (including camera render and events)
            UpdateAndRenderMap();
        }
        else
        {
            // In edit mode, only update style data and fire style event (no camera render)
            // Apply map style to any available renderers (for scene view preview, if possible)
            if (_pathRenderers != null)
            {
                foreach (var lr in _pathRenderers)
                {
                    if (lr != null)
                    {
                        lr.startColor = pathColor;
                        lr.endColor = pathColor;
                        lr.startWidth = pathWidth;
                        lr.endWidth = pathWidth;
                    }
                }
            }
            var streetParent = transform.Find("StreetRenderers");
            if (streetParent != null)
            {
                foreach (var lr in streetParent.GetComponentsInChildren<LineRenderer>())
                {
                    lr.startColor = streetColor;
                    lr.endColor = streetColor;
                    lr.startWidth = streetWidth;
                    lr.endWidth = streetWidth;
                }
            }
            // Fire style updated event for UI color previews
            MapUIStyles styles = new MapUIStyles {
                outlineColor = outlineColor,
                playerIconColor = playerIconColor
            };
            OnStyleUpdated?.Invoke(styles);
        }
    }
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _geoJsonProcessor = GetComponent<GeoJSONProcessor>();
        if (_geoJsonProcessor == null)
        {
            Debug.LogError("MapRendererService: GeoJSONProcessor not found on the same GameObject! Service cannot function.", this);
            enabled = false;
            return;
        }
        CreateMapCamera();
        CreateRenderTexture(512, 512);
        CreateRenderers();
        StartCoroutine(GenerateMapGeometryCoroutine());
    }
        
    private void Start()
    {
        if (NavigationManager.Instance != null)
        {
            NavigationManager.Instance.OnPathUpdated += OnPathUpdated;
        }
        else
        {
            Debug.LogWarning("[MapRendererService] NavigationManager.Instance not available in Start(). Path updates will not be received.");
        }
    }

    private void OnDestroy()
    {
        if (NavigationManager.Instance != null)
        {
            NavigationManager.Instance.OnPathUpdated -= OnPathUpdated;
        }
        if (_mapTexture != null)
        {
            _mapTexture.Release();
        }
        if (_pathRenderers != null)
        {
            foreach (var lr in _pathRenderers)
            {
                if (lr != null && lr.gameObject != null)
                    Destroy(lr.gameObject);
            }
            _pathRenderers.Clear();
        }
        _mapCamera.enabled = true; // The camera must be enabled to render
    }

    private void LateUpdate()
    {
        if (_mapCamera == null || _currentTarget == null) return;
        UpdateCameraTransform();
        UpdateAndRenderMap();
    }

    #endregion

    #region Public API

    /// <summary>
    /// Applies all configured styling to the map components and fires style events.
    /// </summary>
    public void UpdateAndRenderMap()
    {
        // Apply map styles
        if (_mapCamera != null)
        {
            _mapCamera.backgroundColor = mapBackgroundColor;
        }
        if (_pathRenderers != null)
        {
            foreach (var lr in _pathRenderers)
            {
                if (lr != null && lr.enabled)
                {
                    lr.startColor = pathColor;
                    lr.endColor = pathColor;
                    lr.startWidth = pathWidth;
                    lr.endWidth = pathWidth;
                }
            }
        }
        var streetParent = transform.Find("StreetRenderers");
        if (streetParent != null)
        {
            foreach (var lr in streetParent.GetComponentsInChildren<LineRenderer>())
            {
                lr.startColor = streetColor;
                lr.endColor = streetColor;
                lr.startWidth = streetWidth;
                lr.endWidth = streetWidth;
            }
        }
        // Render camera to update RenderTexture
        if (_mapCamera != null)
        {
            _mapCamera.Render();
        }
        // Fire map updated event
        OnMapUpdated?.Invoke(_mapTexture);
        // Fire style updated event
        MapUIStyles styles = new MapUIStyles {
            outlineColor = outlineColor,
            playerIconColor = playerIconColor
        };
        OnStyleUpdated?.Invoke(styles);
    }

    public void SetTarget(Transform target)
    {
        _currentTarget = target;
    }

    public void SetZoom(float zoomLevel)
    {
        _currentZoom = Mathf.Max(1f, zoomLevel);
    }

    public void SetRotationBehavior(MapRenderBehavior behavior)
    {
        _currentBehavior = behavior;
    }

    /// <summary>
    /// Sets the color of the rendered path dynamically.
    /// </summary>
    /// <param name="newColor">The new color for the path.</param>
    public void SetPathColor(Color newColor)
    {
        pathColor = newColor;
        // Also apply this color immediately to all existing path renderers.
        if (_pathRenderers != null)
        {
            foreach (var lr in _pathRenderers)
            {
                if (lr != null)
                {
                    lr.material.color = newColor;
                }
            }
        }
    }

    public void ResizeTexture(int width, int height)
    {
        if (_mapTexture != null && (_mapTexture.width != width || _mapTexture.height != height))
        {
            CreateRenderTexture(width, height);
            StartCoroutine(GenerateMapGeometryCoroutine());
        }
    }

    #endregion

    #region Internal Setup

    private void EnsureEditorSubsystemsExist()
    {
        var cameraTransform = transform.Find("MapRendererService_Camera");
        if (cameraTransform == null)
        {
            CreateMapCamera();
        }
        if (_mapCamera == null) _mapCamera = GetComponentInChildren<Camera>();
        if (transform.Find("StreetRenderers") == null)
        {
            CreateRenderers();
        }
        var streetParent = transform.Find("StreetRenderers");
        if (_geoJsonProcessor == null) _geoJsonProcessor = FindFirstObjectByType<GeoJSONProcessor>();
        if (_geoJsonProcessor != null && _geoJsonProcessor.mapData != null && streetParent != null && streetParent.childCount == 0 && _geoJsonProcessor.mapData.Streets.Count > 0)
        {
            GenerateStreetGeometry_Internal(streetParent);
        }
    }

    private void CreateMapCamera()
    {
        GameObject camGo = new GameObject("MapRendererService_Camera");
        camGo.transform.parent = this.transform;
        _mapCamera = camGo.AddComponent<Camera>();
        _mapCamera.orthographic = true;
        _mapCamera.cullingMask = minimapLayer;
        _mapCamera.clearFlags = CameraClearFlags.SolidColor;
        _mapCamera.backgroundColor = mapBackgroundColor;
        _mapCamera.enabled = true; // The camera must be enabled to render
    }

    private void CreateRenderTexture(int width, int height)
    {
        if (_mapTexture != null)
        {
            _mapTexture.Release();
        }
        _mapTexture = new RenderTexture(width, height, 16, RenderTextureFormat.Default);
        _mapCamera.targetTexture = _mapTexture;
    }

    private void CreateRenderers()
    {
        // Street Renderers Parent
        var existingStreets = transform.Find("StreetRenderers");
        if (existingStreets != null) DestroyImmediate(existingStreets.gameObject);
        GameObject streetsParent = new GameObject("StreetRenderers");
        streetsParent.transform.parent = this.transform;
    }
    
    private IEnumerator GenerateMapGeometryCoroutine()
    {
        yield return new WaitUntil(() => _geoJsonProcessor.mapData != null && _geoJsonProcessor.mapData.Streets.Count > 0);
        
        Transform streetParent = transform.Find("StreetRenderers");
        GenerateStreetGeometry_Internal(streetParent);
    }

    private void GenerateStreetGeometry_Internal(Transform streetParent)
    {
        if (streetParent == null) return;
        
        foreach (Transform child in streetParent)
        {
#if UNITY_EDITOR
            if(!Application.isPlaying) DestroyImmediate(child.gameObject);
            else Destroy(child.gameObject);
#else
            Destroy(child.gameObject);
#endif
        }

        // Get merged polylines instead of individual streets
        var mergedPolylines = MergeConnectedStreets(_geoJsonProcessor.mapData.Streets);

        var streetPrefab = new GameObject("StreetPrefab");
        var lrTemplate = streetPrefab.AddComponent<LineRenderer>();
        var streetMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        streetMat.SetColor("_BaseColor", streetColor);
        lrTemplate.material = streetMat;
        lrTemplate.startColor = Color.white;
        lrTemplate.endColor = Color.white;
        lrTemplate.startWidth = streetWidth;
        lrTemplate.endWidth = streetWidth;
        lrTemplate.useWorldSpace = true;
        lrTemplate.numCornerVertices = 8;

        // Create LineRenderers for merged polylines
        for (int i = 0; i < mergedPolylines.Count; i++)
        {
            var polyline = mergedPolylines[i];
            GameObject streetObject = Instantiate(streetPrefab, streetParent);
            streetObject.name = $"MergedStreet_{i}";
            streetObject.layer = LayerMaskToLayer(minimapLayer);

            LineRenderer lineRenderer = streetObject.GetComponent<LineRenderer>();
            Vector3[] worldPositions = polyline.Select(p => _geoJsonProcessor.mapBoundsTransform.TransformPoint(p)).ToArray();
            
            lineRenderer.positionCount = worldPositions.Length;
            lineRenderer.SetPositions(worldPositions);
        }
        
#if UNITY_EDITOR
        if(!Application.isPlaying) DestroyImmediate(streetPrefab);
        else Destroy(streetPrefab);
#else
        Destroy(streetPrefab);
#endif
    }
    
    private List<List<Vector3>> MergeConnectedStreets(List<MapData.Street> streets)
    {
        var tolerance = 0.01f;
        var nodeConnections = new Dictionary<Vector3, List<(MapData.Street street, int pointIndex, bool isStart)>>();
        
        // Build connection map
        foreach (var street in streets)
        {
            if (street.Points.Count < 2) continue;
            
            var start = street.Points[0];
            var end = street.Points[street.Points.Count - 1];
            
            // Round to tolerance
            start = new Vector3(
                Mathf.Round(start.x / tolerance) * tolerance,
                Mathf.Round(start.y / tolerance) * tolerance,
                Mathf.Round(start.z / tolerance) * tolerance
            );
            end = new Vector3(
                Mathf.Round(end.x / tolerance) * tolerance,
                Mathf.Round(end.y / tolerance) * tolerance,
                Mathf.Round(end.z / tolerance) * tolerance
            );
            
            if (!nodeConnections.ContainsKey(start))
                nodeConnections[start] = new List<(MapData.Street, int, bool)>();
            if (!nodeConnections.ContainsKey(end))
                nodeConnections[end] = new List<(MapData.Street, int, bool)>();
                
            nodeConnections[start].Add((street, 0, true));
            nodeConnections[end].Add((street, street.Points.Count - 1, false));
        }
        
        var processedStreets = new HashSet<MapData.Street>();
        var mergedPolylines = new List<List<Vector3>>();
        
        foreach (var street in streets)
        {
            if (processedStreets.Contains(street)) continue;
            
            var polyline = new List<Vector3>(street.Points);
            processedStreets.Add(street);
            
            // Try to extend from end
            bool extended = true;
            while (extended)
            {
                extended = false;
                var endPoint = polyline[polyline.Count - 1];
                endPoint = new Vector3(
                    Mathf.Round(endPoint.x / tolerance) * tolerance,
                    Mathf.Round(endPoint.y / tolerance) * tolerance,
                    Mathf.Round(endPoint.z / tolerance) * tolerance
                );
                
                if (nodeConnections.ContainsKey(endPoint) && nodeConnections[endPoint].Count == 2)
                {
                    foreach (var connection in nodeConnections[endPoint])
                    {
                        if (!processedStreets.Contains(connection.street))
                        {
                            processedStreets.Add(connection.street);
                            if (connection.isStart)
                            {
                                polyline.AddRange(connection.street.Points.Skip(1));
                            }
                            else
                            {
                                var reversed = new List<Vector3>(connection.street.Points);
                                reversed.Reverse();
                                polyline.AddRange(reversed.Skip(1));
                            }
                            extended = true;
                            break;
                        }
                    }
                }
            }
            
            // Try to extend from start
            extended = true;
            while (extended)
            {
                extended = false;
                var startPoint = polyline[0];
                startPoint = new Vector3(
                    Mathf.Round(startPoint.x / tolerance) * tolerance,
                    Mathf.Round(startPoint.y / tolerance) * tolerance,
                    Mathf.Round(startPoint.z / tolerance) * tolerance
                );
                
                if (nodeConnections.ContainsKey(startPoint) && nodeConnections[startPoint].Count == 2)
                {
                    foreach (var connection in nodeConnections[startPoint])
                    {
                        if (!processedStreets.Contains(connection.street))
                        {
                            processedStreets.Add(connection.street);
                            if (connection.isStart)
                            {
                                var reversed = new List<Vector3>(connection.street.Points);
                                reversed.Reverse();
                                polyline.InsertRange(0, reversed.Take(reversed.Count - 1).ToList());
                            }
                            else
                            {
                                polyline.InsertRange(0, connection.street.Points.Take(connection.street.Points.Count - 1).ToList());
                            }
                            extended = true;
                            break;
                        }
                    }
                }
            }
            
            mergedPolylines.Add(polyline);
        }
        
        return mergedPolylines;
    }
    
    #endregion

    #region Event Handlers & Updates

    private void OnPathUpdated(List<Vector3> newPath)
    {
        if (_pathRenderers == null) _pathRenderers = new List<LineRenderer>();
        
        if (newPath == null || newPath.Count < 2)
        {
            // Disable all path renderers
            foreach (var lr in _pathRenderers)
            {
                if (lr != null) lr.enabled = false;
            }
            return;
        }

        DebugManager.Log($"Path Updated with {newPath.Count} points.", "Navigation", DebugManager.LogLevel.Verbose);

        // Apply Y-offset to prevent z-fighting
        var offsetPath = newPath.Select(p => p + new Vector3(0, pathYOffset, 0)).ToArray();
        
        // Ensure we have enough LineRenderers for each segment
        int segmentsNeeded = offsetPath.Length - 1;
        while (_pathRenderers.Count < segmentsNeeded)
        {
            CreateNewPathRenderer();
        }
        
        // Set up each segment
        for (int i = 0; i < segmentsNeeded; i++)
        {
            var lr = _pathRenderers[i];
            lr.positionCount = 2;
            lr.SetPosition(0, offsetPath[i]);
            lr.SetPosition(1, offsetPath[i + 1]);
            lr.startWidth = pathWidth;  // Use the inspector value
            lr.endWidth = pathWidth;     // Use the inspector value
            lr.enabled = true;
        }
        
        // Disable unused renderers
        for (int i = segmentsNeeded; i < _pathRenderers.Count; i++)
        {
            _pathRenderers[i].enabled = false;
        }
    }

    private void CreateNewPathRenderer()
    {
        GameObject pathGo = new GameObject($"PathSegment_{_pathRenderers.Count}");
        pathGo.transform.parent = this.transform;
        pathGo.layer = LayerMaskToLayer(minimapLayer);
        
        LineRenderer lr = pathGo.AddComponent<LineRenderer>();
        // The material color will be controlled directly. Vertex colors should be white.
        var pathMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        pathMat.color = pathColor; // Set initial color
        lr.material = pathMat;
        
        // Vertex colors are kept white so they don't tint the material's base color.
        lr.startColor = Color.white;
        lr.endColor = Color.white;
        lr.startWidth = pathWidth;
        lr.endWidth = pathWidth;
        lr.useWorldSpace = true;
        lr.numCornerVertices = 0;  // Don't need corner vertices for straight segments
        lr.numCapVertices = 5;     // Round end caps
        
        _pathRenderers.Add(lr);
    }

    private void UpdateCameraTransform()
    {
        _mapCamera.orthographicSize = _currentZoom;
        
        // Update Position
        Vector3 targetPosition = _currentTarget.position;
        _mapCamera.transform.position = new Vector3(targetPosition.x, targetPosition.y + 20f, targetPosition.z); // Lowered height to avoid fog

        // Update Rotation
        float targetYAngle = _currentTarget.eulerAngles.y;
        switch (_currentBehavior)
        {
            case MapRenderBehavior.Rotating:
                _mapCamera.transform.rotation = Quaternion.Euler(90, targetYAngle, 0);
                break;
            
            case MapRenderBehavior.Static:
                _mapCamera.transform.rotation = Quaternion.Euler(90, 0, 0);
                break;
        }
    }

    private static int LayerMaskToLayer(LayerMask layerMask) {
        int layerNumber = 0;
        int layer = layerMask.value;
        while(layer > 1) {
            layer = layer >> 1;
            layerNumber++;
        }
        return layerNumber;
    }
    
    #endregion
} 