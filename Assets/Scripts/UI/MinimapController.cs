using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A UI component that controls the display of the minimap.
/// It acts as a client to the MapRendererService, telling it what to render.
/// THIS SCRIPT SHOULD BE ON THE PARENT MINIMAP OBJECT (the one with the mask/frame).
/// </summary>
public class MinimapController : MonoBehaviour
{
    [Header("Core Components")]
    [Tooltip("The RawImage component that will display the actual map content. MUST BE A CHILD of this object.")]
    [SerializeField] private RawImage mapContentImage;
    [Tooltip("The transform for the map to follow (typically the player).")]
    [SerializeField] private Transform playerTransform;
    [Tooltip("The UI icon representing the player on the map. MUST BE A CHILD of this object.")]
    [SerializeField] private RectTransform playerIcon;
    [Tooltip("Reference to the MapRendererService.")]
    [SerializeField] private MapRendererService mapService;
    [Header("UI References")]
    [Tooltip("The outline Image component (child of MinimapRoot, maskable off).")]
    [SerializeField] private Image outlineImage;
    [Tooltip("The player icon Image component (child of MinimapRoot or PlayerIcon GameObject).")]
    [SerializeField] private Image playerIconImage;

    [Header("Behavior")]
    [Tooltip("Controls the rotation behavior of the map.")]
    [SerializeField] private MapRenderBehavior mapBehavior = MapRenderBehavior.Rotating;
    [Tooltip("The view radius of the map in world units. Smaller numbers zoom in.")]
    [SerializeField] private float zoomLevel = 50f;

    // Component References
    private RectTransform _rectTransform;
    private bool _subscribed = false;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        // The content image MUST be white so it doesn't tint the RenderTexture.
        if(mapContentImage != null) mapContentImage.color = Color.white;

        SubscribeToService();
        if (mapService != null && playerTransform != null)
        {
            mapService.SetTarget(playerTransform);
            mapService.SetZoom(zoomLevel);
            mapService.SetRotationBehavior(mapBehavior);
            mapService.ResizeTexture((int)_rectTransform.rect.width, (int)_rectTransform.rect.height);
            mapService.UpdateAndRenderMap();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromService();
        if (mapService != null)
        {
            mapService.SetTarget(null);
        }
    }

    private void LateUpdate()
    {
        if (playerIcon == null || playerTransform == null) return;
        switch (mapBehavior)
        {
            case MapRenderBehavior.Rotating:
                playerIcon.transform.localEulerAngles = Vector3.zero;
                break;
            case MapRenderBehavior.Static:
                float playerYAngle = playerTransform.eulerAngles.y;
                playerIcon.transform.localEulerAngles = new Vector3(0, 0, -playerYAngle);
                break;
        }
    }
    
    private void HandleMapUpdate(RenderTexture mapTexture)
    {
        if (mapContentImage != null)
            mapContentImage.texture = mapTexture;
    }

    private void HandleStyleUpdate(MapUIStyles styles)
    {
        if (outlineImage != null)
            outlineImage.color = styles.outlineColor;
        if (playerIconImage != null)
            playerIconImage.color = styles.playerIconColor;
    }

    private void SubscribeToService()
    {
        if (_subscribed) return;
        if (mapService == null) return;
        mapService.OnMapUpdated -= HandleMapUpdate;
        mapService.OnStyleUpdated -= HandleStyleUpdate;
        mapService.OnMapUpdated += HandleMapUpdate;
        mapService.OnStyleUpdated += HandleStyleUpdate;
        _subscribed = true;
    }

    private void UnsubscribeFromService()
    {
        if (!_subscribed) return;
        if (mapService == null) return;
        mapService.OnMapUpdated -= HandleMapUpdate;
        mapService.OnStyleUpdated -= HandleStyleUpdate;
        _subscribed = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Always (re)subscribe in edit mode
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
            SubscribeToService();
            // Force a UI refresh with the latest values
            if (mapService != null)
            {
                mapService.SetZoom(zoomLevel);
                mapService.SetRotationBehavior(mapBehavior);
                mapService.ResizeTexture((int)(_rectTransform != null ? _rectTransform.rect.width : 128), (int)(_rectTransform != null ? _rectTransform.rect.height : 128));
                mapService.UpdateAndRenderMap();
            }
        }
    }
#endif
} 