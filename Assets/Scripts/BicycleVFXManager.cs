using UnityEngine;
using System; // For Serializable attribute

[RequireComponent(typeof(BicycleController))]
public class BicycleVFXManager : MonoBehaviour
{
    [Header("Component References")]
    [Tooltip("Transform representing the front fork/steering assembly.")]
    [SerializeField] private Transform frontAssembly;
    [Tooltip("Transform representing the front wheel.")]
    [SerializeField] private Transform frontWheel;
    [Tooltip("Transform representing the rear wheel.")]
    [SerializeField] private Transform rearWheel;
    [Tooltip("Transform representing the pedal assembly (crank arms).")]
    [SerializeField] private Transform pedalAssembly;
    [Tooltip("Transform representing the pedal gear (front chainring).")]
    [SerializeField] private Transform pedalGear;
    [Tooltip("Transform representing the left pedal.")]
    [SerializeField] private Transform leftPedal;
    [Tooltip("Transform representing the right pedal.")]
    [SerializeField] private Transform rightPedal;
    
    [Header("Gear Settings")]
    [Tooltip("Radius of the pedal gear (front chainring) in meters.")]
    [SerializeField] private float pedalGearRadius = 0.15f;
    [Tooltip("Radius of the rear gear (rear sprocket) in meters.")]
    [SerializeField] private float rearGearRadius = 0.05f;
    [Tooltip("Show gizmos for pedal and rear gears in the Scene view.")]
    [SerializeField] private bool showGearGizmos = true;
    
    [Header("Animation Settings")]
    [Tooltip("Radius of the wheels in meters. Used for calculating rotation speed.")]
    [SerializeField] public float wheelRadiusInMeters = 0.35f;
    [Tooltip("Maximum steering angle in degrees.")]
    [SerializeField] private float maxSteeringAngle = 35f;
    [Tooltip("Show gizmos for wheels in the Scene view.")]
    [SerializeField] private bool showWheelGizmos = true;
    [Tooltip("Show gizmo for steering angle in the Scene view.")]
    [SerializeField] private bool showSteeringGizmo = true;
    
    [Header("Rotation Axis Settings")]
    [Tooltip("Axis used for actual wheel rotation (not gizmo). X is typical for most models.")]
    [SerializeField] private RotationAxis wheelRotationAxis = RotationAxis.X;
    [Tooltip("Axis used for actual steering/front assembly rotation. Y is typical for most models.")]
    [SerializeField] private RotationAxis steeringRotationAxis = RotationAxis.Y;
    [Tooltip("Axis used for pedal assembly (crank arms) rotation. X is typical for most models.")]
    [SerializeField] private RotationAxis pedalAssemblyRotationAxis = RotationAxis.X;
    [Tooltip("Axis used for individual pedal rotation. X is typical for most models.")]
    [SerializeField] private RotationAxis pedalRotationAxis = RotationAxis.X;
    
    // References
    private BicycleController bicycleController;
    
    // State tracking
    private float pedalRotation = 0f;
    private float wheelRotation = 0f;
    private float steeringAngle = 0f;
    
    // Manual control variables
    private bool leftPedalManualControl = false;
    private float leftPedalScriptedRotation = 0f;
    private bool rightPedalManualControl = false;
    private float rightPedalScriptedRotation = 0f;
    
    // Enum for rotation axis selection
    public enum RotationAxis
    {
        X,
        Y,
        Z
    }
    
    private void Awake()
    {
        bicycleController = GetComponent<BicycleController>();
        
        if (bicycleController == null)
        {
            Debug.LogError("BicycleVFXManager requires a BicycleController component on the same GameObject.");
            enabled = false;
            return;
        }
        
        // Validate component references
        ValidateComponents();
    }
    
    private void ValidateComponents()
    {
        if (frontAssembly == null) Debug.LogWarning("Front Assembly reference is missing.");
        if (frontWheel == null) Debug.LogWarning("Front Wheel reference is missing.");
        if (rearWheel == null) Debug.LogWarning("Rear Wheel reference is missing.");
        if (pedalAssembly == null) Debug.LogWarning("Pedal Assembly reference is missing.");
        if (pedalGear == null) Debug.LogWarning("Pedal Gear reference is missing.");
        if (leftPedal == null) Debug.LogWarning("Left Pedal reference is missing.");
        if (rightPedal == null) Debug.LogWarning("Right Pedal reference is missing.");
    }
    
    private void Update()
    {
        // Get current speed and steering input from the controller
        float currentSpeed = bicycleController.CurrentSpeed;
        float turnInput = bicycleController.TurnInput;
        
        // Update visual components
        UpdateWheelRotation(currentSpeed);
        UpdatePedalRotation(currentSpeed);
        UpdateSteeringRotation(turnInput);
    }
    
    private void UpdateWheelRotation(float speed)
    {
        if (wheelRadiusInMeters <= 0) return;
        
        // Calculate wheel rotation based on speed and wheel radius
        // Speed is in m/s, radius in meters, so rotation is in radians/s
        float rotationSpeed = speed / wheelRadiusInMeters;
        
        // Convert to degrees/s and accumulate rotation
        wheelRotation += rotationSpeed * Mathf.Rad2Deg * Time.deltaTime;
        
        // Apply rotation to wheels based on selected axis
        if (frontWheel != null)
        {
            frontWheel.localRotation = GetAxisRotation(wheelRotationAxis, wheelRotation);
        }
        
        if (rearWheel != null)
        {
            rearWheel.localRotation = GetAxisRotation(wheelRotationAxis, wheelRotation);
        }
    }
    
    private void UpdatePedalRotation(float speed)
    {
        if (pedalGearRadius <= 0 || rearGearRadius <= 0 || pedalAssembly == null) return;
        
        // Calculate gear ratio (how many pedal rotations per wheel rotation)
        float gearRatio = rearGearRadius / pedalGearRadius;
        
        // Calculate pedal rotation based on speed, wheel radius, and gear ratio
        float pedalRotationSpeed = (speed / wheelRadiusInMeters) * gearRatio;
        
        // Convert to degrees/s and accumulate rotation
        pedalRotation += pedalRotationSpeed * Mathf.Rad2Deg * Time.deltaTime;
        
        // Apply rotation to the entire pedal assembly based on selected axis
        pedalAssembly.localRotation = GetAxisRotation(pedalAssemblyRotationAxis, pedalRotation);
        
        // Apply counter-rotation to pedals to compensate for assembly rotation
        // plus any scripted rotation, using the selected axis
        if (leftPedal != null && !leftPedalManualControl)
        {
            leftPedal.localRotation = GetAxisRotation(pedalRotationAxis, -pedalRotation + leftPedalScriptedRotation);
        }
        
        if (rightPedal != null && !rightPedalManualControl)
        {
            rightPedal.localRotation = GetAxisRotation(pedalRotationAxis, -pedalRotation + rightPedalScriptedRotation);
        }
    }
    
    private void UpdateSteeringRotation(float turnInput)
    {
        if (frontAssembly == null) return;
        
        // Calculate steering angle based on turn input
        steeringAngle = Mathf.Lerp(steeringAngle, turnInput * maxSteeringAngle, Time.deltaTime * 5f);
        
        // Apply rotation to front assembly based on selected axis
        frontAssembly.localRotation = GetAxisRotation(steeringRotationAxis, steeringAngle);
    }
    
    // Helper method to create rotation quaternion based on selected axis
    private Quaternion GetAxisRotation(RotationAxis axis, float angle)
    {
        switch (axis)
        {
            case RotationAxis.X:
                return Quaternion.Euler(angle, 0, 0);
            case RotationAxis.Y:
                return Quaternion.Euler(0, angle, 0);
            case RotationAxis.Z:
                return Quaternion.Euler(0, 0, angle);
            default:
                return Quaternion.Euler(0, 0, 0);
        }
    }
    
    // Public methods for manual control of pedals (for animation)
    public void SetLeftPedalControl(bool manualControl, float rotation = 0f)
    {
        leftPedalManualControl = manualControl;
        leftPedalScriptedRotation = rotation;
        
        if (manualControl && leftPedal != null)
        {
            leftPedal.localRotation = GetAxisRotation(pedalRotationAxis, -pedalRotation + rotation);
        }
    }
    
    public void SetRightPedalControl(bool manualControl, float rotation = 0f)
    {
        rightPedalManualControl = manualControl;
        rightPedalScriptedRotation = rotation;
        
        if (manualControl && rightPedal != null)
        {
            rightPedal.localRotation = GetAxisRotation(pedalRotationAxis, -pedalRotation + rotation);
        }
    }
    
    // Draw gizmos to visualize gear sizes, wheels, and steering angle
    private void OnDrawGizmos()
    {
        // Draw gear gizmos
        if (showGearGizmos)
        {
            // Draw pedal gear using the selected pedal assembly rotation axis
            if (pedalGear != null && pedalAssembly != null)
            {
                Vector3 pedalGearNormal = GetAxisVector(pedalAssembly, pedalAssemblyRotationAxis);
                Gizmos.color = Color.green;
                DrawCircle(pedalGear.position, pedalGearNormal, pedalGearRadius, 32);
                DrawAxisGizmo(pedalGear.position, pedalGearNormal, pedalGearRadius * 1.2f, Color.green);
                DrawRotationDirectionGizmo(pedalGear.position, pedalGearNormal, pedalGearRadius, Color.green);
            }
            // Draw rear gear using the selected wheel rotation axis
            if (rearWheel != null)
            {
                Vector3 rearGearNormal = GetAxisVector(rearWheel, wheelRotationAxis);
                Gizmos.color = Color.yellow;
                DrawCircle(rearWheel.position, rearGearNormal, rearGearRadius, 32);
                DrawAxisGizmo(rearWheel.position, rearGearNormal, rearGearRadius * 1.2f, Color.yellow);
                DrawRotationDirectionGizmo(rearWheel.position, rearGearNormal, rearGearRadius, Color.yellow);
            }
        }
        
        // Draw wheel gizmos
        if (showWheelGizmos)
        {
            if (frontWheel != null)
            {
                Vector3 wheelNormal = GetAxisVector(frontWheel, wheelRotationAxis);
                Gizmos.color = Color.cyan;
                DrawCircle(frontWheel.position, wheelNormal, wheelRadiusInMeters, 32);
                DrawAxisGizmo(frontWheel.position, wheelNormal, wheelRadiusInMeters * 1.2f, Color.cyan);
                DrawRotationDirectionGizmo(frontWheel.position, wheelNormal, wheelRadiusInMeters, Color.cyan);
            }
            
            if (rearWheel != null)
            {
                Vector3 wheelNormal = GetAxisVector(rearWheel, wheelRotationAxis);
                Gizmos.color = Color.cyan;
                DrawCircle(rearWheel.position, wheelNormal, wheelRadiusInMeters, 32);
                DrawAxisGizmo(rearWheel.position, wheelNormal, wheelRadiusInMeters * 1.2f, Color.cyan);
                DrawRotationDirectionGizmo(rearWheel.position, wheelNormal, wheelRadiusInMeters, Color.cyan);
            }
        }
        
        // Draw steering angle gizmo
        if (showSteeringGizmo && frontAssembly != null)
        {
            Vector3 position = frontAssembly.position;
            // Get the global axis of rotation for the steering
            Vector3 upDirection = GetAxisVector(frontAssembly, steeringRotationAxis);
            // Choose a base forward direction perpendicular to the axis
            Vector3 forwardDirection = Vector3.ProjectOnPlane(frontAssembly.forward, upDirection).normalized;
            if (forwardDirection.magnitude < 0.001f)
            {
                forwardDirection = Vector3.ProjectOnPlane(frontAssembly.up, upDirection).normalized;
            }
            // Draw current steering angle
            Gizmos.color = Color.red;
            Vector3 currentDirection = Quaternion.AngleAxis(steeringAngle, upDirection) * forwardDirection;
            Gizmos.DrawRay(position, currentDirection * 0.5f);
            // Draw max left steering angle
            Gizmos.color = Color.gray;
            Vector3 maxLeftDirection = Quaternion.AngleAxis(-maxSteeringAngle, upDirection) * forwardDirection;
            Gizmos.DrawRay(position, maxLeftDirection * 0.4f);
            // Draw max right steering angle
            Vector3 maxRightDirection = Quaternion.AngleAxis(maxSteeringAngle, upDirection) * forwardDirection;
            Gizmos.DrawRay(position, maxRightDirection * 0.4f);
            // Draw arc connecting the max angles
            DrawArc(position, upDirection, maxLeftDirection, maxRightDirection, 0.3f, 20);
            // Draw axis of rotation for steering
            DrawAxisGizmo(position, upDirection, 0.6f, Color.magenta);
        }
    }
    
    private void DrawCircle(Vector3 center, Vector3 normal, float radius, int segments)
    {
        Vector3 from = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized * radius;
        if (from.magnitude < 0.001f)
        {
            from = Vector3.ProjectOnPlane(Vector3.up, normal).normalized * radius;
        }
        
        Quaternion rotation = Quaternion.AngleAxis(360f / segments, normal);
        Vector3 to = rotation * from;
        
        for (int i = 0; i < segments; i++)
        {
            Gizmos.DrawLine(center + from, center + to);
            from = to;
            to = rotation * from;
        }
    }
    
    private void DrawArc(Vector3 center, Vector3 normal, Vector3 from, Vector3 to, float radius, int segments)
    {
        // Calculate angle between vectors
        float angle = Vector3.SignedAngle(from, to, normal);
        float angleStep = angle / segments;
        
        Vector3 current = from.normalized * radius;
        for (int i = 0; i < segments; i++)
        {
            Vector3 next = Quaternion.AngleAxis(angleStep, normal) * current;
            Gizmos.DrawLine(center + current, center + next);
            current = next;
        }
    }
    
    // Draws a line showing the axis of rotation
    private void DrawAxisGizmo(Vector3 center, Vector3 normal, float length, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawLine(center - normal.normalized * length * 0.5f, center + normal.normalized * length * 0.5f);
    }

    // Draws an arrow tangent to the circle to show direction of rotation
    private void DrawRotationDirectionGizmo(Vector3 center, Vector3 normal, float radius, Color color)
    {
        // Pick a point on the circle
        Vector3 from = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized * radius;
        if (from.magnitude < 0.001f)
        {
            from = Vector3.ProjectOnPlane(Vector3.up, normal).normalized * radius;
        }
        Vector3 pointOnCircle = center + from;
        // Tangent direction (right-hand rule)
        Vector3 tangent = Vector3.Cross(normal.normalized, from.normalized).normalized;
        float arrowLength = radius * 0.4f;
        Vector3 arrowEnd = pointOnCircle + tangent * arrowLength;
        Gizmos.color = color;
        Gizmos.DrawLine(pointOnCircle, arrowEnd);
        // Draw arrowhead
        Vector3 arrowHeadDir1 = Quaternion.AngleAxis(20, normal) * -tangent;
        Vector3 arrowHeadDir2 = Quaternion.AngleAxis(-20, normal) * -tangent;
        float headLength = arrowLength * 0.3f;
        Gizmos.DrawLine(arrowEnd, arrowEnd + arrowHeadDir1 * headLength);
        Gizmos.DrawLine(arrowEnd, arrowEnd + arrowHeadDir2 * headLength);
    }

    // Helper to get the global axis vector for a given transform and axis enum
    private Vector3 GetAxisVector(Transform t, RotationAxis axis)
    {
        switch (axis)
        {
            case RotationAxis.X:
                return t.TransformDirection(Vector3.right);
            case RotationAxis.Y:
                return t.TransformDirection(Vector3.up);
            case RotationAxis.Z:
                return t.TransformDirection(Vector3.forward);
            default:
                return t.TransformDirection(Vector3.forward);
        }
    }
}