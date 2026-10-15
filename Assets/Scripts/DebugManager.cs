using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Text;
using System;
using UnityEngine.InputSystem;
using System.Reflection;

/// <summary>
/// A centralized singleton for all debugging operations.
/// Provides on-screen UI, console logging, and debug drawing utilities.
/// </summary>
public class DebugManager : MonoBehaviour
{
    #region Singleton
    
    private static DebugManager _instance;
    public static DebugManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<DebugManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("DebugManager");
                    _instance = go.AddComponent<DebugManager>();
                }
            }
            return _instance;
        }
    }
    
    #endregion

    #region Log Levels & Channels
    
    /// <summary>
    /// Granularity of log messages.
    /// </summary>
    public enum LogLevel
    {
        Verbose,
        Info,
        Warning,
        Error,
        Fatal
    }
    
    #endregion

    #region Properties
    
    [Header("Input Settings")]
    [Tooltip("The Input Action Asset for the debug controls. Should be 'InputSystem_Actions'.")]
    public InputActionAsset inputActions;

    [Header("UI Settings")]
    [Tooltip("The TextMeshProUGUI component for displaying debug information.")]
    public TextMeshProUGUI debugTextUI;
    
    [Tooltip("If true, the debug UI starts visible. If false, it's hidden until the toggle key is pressed.")]
    public bool displayUiOnStart = true;
    
    [Header("Logging Settings")]
    [Tooltip("The minimum log level to display in the console.")]
    public LogLevel consoleLogLevel = LogLevel.Info;
    
    [Tooltip("A list of log channels to enable. If empty, all channels are enabled.")]
    public List<string> enabledChannels = new List<string>();
    
    // Private data structures for the systems
    private InputAction toggleDebugAction;
    private Dictionary<string, string> uiDebugValues;
    private List<Action> gizmoDrawActions;
    private List<LineRenderer> lineRendererPool;
    private List<LineRenderer> activeLineRenderers;
    private int lineRendererPoolSize = 20;
    private bool isUiVisible;
    private StringBuilder uiStringBuilder = new StringBuilder();
    
    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        
        // Set initial UI visibility from the inspector
        isUiVisible = displayUiOnStart;
        if (debugTextUI != null)
        {
            debugTextUI.gameObject.SetActive(isUiVisible);
        }
        
        // Initialize collections
        uiDebugValues = new Dictionary<string, string>();
        gizmoDrawActions = new List<Action>();
        lineRendererPool = new List<LineRenderer>();
        activeLineRenderers = new List<LineRenderer>();

        // Find and set up debug actions
        if (inputActions != null)
        {
            toggleDebugAction = inputActions.FindAction("Debug/ToggleDebugUI");
            if (toggleDebugAction == null)
            {
                Debug.LogError("DebugManager: 'ToggleDebugUI' action not found in the provided InputActionAsset. Please check the action map 'Debug' and action name 'ToggleDebugUI'.");
            }
        }
        else
        {
            Debug.LogError("DebugManager: InputActionAsset is not assigned in the inspector.");
        }

        // Create LineRenderer pool for UI drawing
        int layer = LayerMask.NameToLayer("DebugUILayer");
        if (layer == -1)
        {
            Debug.LogWarning("DebugManager: 'DebugUILayer' not found. Please add it to Project Settings -> Tags and Layers.");
            return;
        }

        for (int i = 0; i < lineRendererPoolSize; i++)
        {
            GameObject go = new GameObject($"LineRenderer_{i}");
            go.transform.SetParent(transform);
            go.layer = layer;
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.startWidth = 0.02f;
            lr.endWidth = 0.02f;
            lr.material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
            lr.positionCount = 2;
            lr.enabled = false;
            lineRendererPool.Add(lr);
        }
    }

    private void OnEnable()
    {
        toggleDebugAction?.Enable();
    }

    private void OnDisable()
    {
        toggleDebugAction?.Disable();
    }

    private void Update()
    {
        // Toggle UI visibility
        if (toggleDebugAction != null && toggleDebugAction.WasPressedThisFrame())
        {
            isUiVisible = !isUiVisible;
            if (debugTextUI != null)
            {
                debugTextUI.gameObject.SetActive(isUiVisible);
            }
        }
        
        // Update UI text if visible
        if (isUiVisible && debugTextUI != null)
        {
            UpdateUIText();
        }
    }

    private void LateUpdate()
    {
        // Reset and return active LineRenderers to the pool
        foreach (var lr in activeLineRenderers)
        {
            lr.enabled = false;
            lineRendererPool.Add(lr);
        }
        activeLineRenderers.Clear();
    }

    private void OnDrawGizmos()
    {
        if (gizmoDrawActions == null || gizmoDrawActions.Count == 0) return;

        foreach (var action in gizmoDrawActions)
        {
            action.Invoke();
        }
        gizmoDrawActions.Clear();
    }

    #endregion

    #region Logging System

    /// <summary>
    /// Logs a message to the console with specified level and channel.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="channel">The category of the log message.</param>
    /// <param name="level">The severity of the message.</param>
    public static void Log(string message, string channel = "Default", LogLevel level = LogLevel.Info)
    {
        if (Instance == null) return;
        
        // Filter by log level
        if (level < Instance.consoleLogLevel) return;

        // Filter by channel
        if (Instance.enabledChannels.Count > 0 && !Instance.enabledChannels.Contains(channel)) return;
        
        // Format and log
        string formattedMessage = $"[{channel.ToUpper()}][{Time.fixedTime:F2}] {message}";
        
        switch (level)
        {
            case LogLevel.Warning:
                Debug.LogWarning(formattedMessage);
                break;
            case LogLevel.Error:
            case LogLevel.Fatal:
                Debug.LogError(formattedMessage);
                break;
            default:
                Debug.Log(formattedMessage);
                break;
        }
    }

    #endregion

    #region Gizmo System

    /// <summary>
    /// Queues a line to be drawn in the Scene View on the next Gizmo update.
    /// </summary>
    public static void DrawGizmoLine(Vector3 start, Vector3 end, Color color)
    {
        if (Instance == null) return;
        Instance.gizmoDrawActions.Add(() =>
        {
            Gizmos.color = color;
            Gizmos.DrawLine(start, end);
        });
    }

    /// <summary>
    /// Queues a sphere to be drawn in the Scene View on the next Gizmo update.
    /// </summary>
    public static void DrawGizmoSphere(Vector3 center, float radius, Color color)
    {
        if (Instance == null) return;
        Instance.gizmoDrawActions.Add(() =>
        {
            Gizmos.color = color;
            Gizmos.DrawSphere(center, radius);
        });
    }

    #endregion

    #region UI Drawing System

    /// <summary>
    /// Draws a line in the Game View using a LineRenderer.
    /// This is drawn by a separate camera and overlaid on the UI.
    /// </summary>
    public static void DrawUILine(Vector3 start, Vector3 end, Color color)
    {
        if (Instance == null || Instance.lineRendererPool.Count == 0) return;

        // Get a LineRenderer from the pool
        LineRenderer lr = Instance.lineRendererPool[Instance.lineRendererPool.Count - 1];
        Instance.lineRendererPool.RemoveAt(Instance.lineRendererPool.Count - 1);
        
        // Configure and enable it
        lr.enabled = true;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        
        // Add to active list for cleanup
        Instance.activeLineRenderers.Add(lr);
    }

    #endregion

    #region Inspector Logging

    /// <summary>
    /// Logs all public fields of a given component to the console using reflection.
    /// </summary>
    /// <param name="component">The component whose settings to log.</param>
    public static void LogInspectorSettings(MonoBehaviour component)
    {
        if (Instance == null) return;

        var type = component.GetType();
        var sb = new StringBuilder();
        sb.AppendLine($"--- {type.Name} Settings ---");

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (var field in fields)
        {
            // This will skip fields that are not serialized in the inspector, like those from base classes unless they are public.
            if (field.IsDefined(typeof(NonSerializedAttribute), true) || field.IsDefined(typeof(HideInInspector), true)) continue;
            
            object value = field.GetValue(component);
            sb.AppendLine($"  {field.Name} = {value}");
        }

        Log(sb.ToString(), "Settings");
    }

    #endregion

    #region Runtime Reporting

    /// <summary>
    /// Reports all members of a component marked with [Debuggable] or [DebuggableProvider] to the UI.
    /// </summary>
    public static void ReportDebuggableMembers(MonoBehaviour component)
    {
        if (Instance == null) return;

        var type = component.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Process fields and properties
        foreach (var member in type.GetMembers(flags))
        {
            if (member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property) continue;

            var attr = member.GetCustomAttribute<DebuggableAttribute>();
            if (attr == null) continue;

            string displayName = attr.DisplayName ?? member.Name;
            object value = member.MemberType == MemberTypes.Field
                ? ((FieldInfo)member).GetValue(component)
                : ((PropertyInfo)member).GetValue(component);
            
            Set(displayName, value);
        }

        // Process provider methods
        foreach (var method in type.GetMethods(flags))
        {
            var attr = method.GetCustomAttribute<DebuggableProviderAttribute>();
            if (attr == null) continue;

            if (method.GetParameters().Length > 0 || method.ReturnType != typeof(Dictionary<string, object>))
            {
                Log($"DebuggableProvider method '{method.Name}' in '{type.Name}' has an invalid signature. It must have no parameters and return Dictionary<string, object>.", "DebugManager", LogLevel.Warning);
                continue;
            }

            var dict = (Dictionary<string, object>)method.Invoke(component, null);
            if (dict == null) continue;
            
            foreach (var kvp in dict)
            {
                Set(kvp.Key, kvp.Value);
            }
        }
    }

    #endregion

    #region UI Text System

    /// <summary>
    /// Sets a key-value pair to be displayed on the debug UI.
    /// </summary>
    public static void Set(string key, object value)
    {
        if (Instance == null) return;
        Instance.uiDebugValues[key] = value.ToString();
    }

    private void UpdateUIText()
    {
        uiStringBuilder.Clear();
        
        uiStringBuilder.AppendLine("<b>-- Debug Info --</b>");
        foreach (var kvp in uiDebugValues)
        {
            uiStringBuilder.AppendLine($"<b>{kvp.Key}:</b> {kvp.Value}");
        }
        
        debugTextUI.text = uiStringBuilder.ToString();
    }

    #endregion
}
