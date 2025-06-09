using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class BicycleController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Child transform containing the bike's visual model for leaning.")]
    public Transform visualsToLean;

    [Header("Movement Settings")]
    public float maxSpeed = 10f;
    public float acceleration = 8f;
    public float deceleration = 4f;
    public float turnSpeed = 120f;
    public float leanAngle = 25f;
    [Tooltip("If true, inverts the direction of the visual lean.")]
    public bool invertLeanDirection = false;

    [Header("Grounding Settings")]
    public float groundCheckDistance = 0.5f;
    public LayerMask groundLayer;
    public float airborneGravity = 20f;
    public float groundStickForce = 10f;
    [Header("Wheel Ground Check")]
    public Transform wheelPosition;
    public float wheelRadius = 0.35f;

    // Public API
    public float CurrentSpeed { get; private set; }
    public float TurnInput { get; private set; }

    // Private
    Rigidbody rb;
    bool isGrounded;
    float accelerateInput;
    float brakeInput;
    float targetSpeed;
    float currentLean;
    float verticalVelocity;
    Quaternion visualsInitialLocalRotation; // Store initial visuals rotation

    public enum LeanAxis { X, Y, Z }
    [Header("Leaning Settings")]
    public LeanAxis leanAxis = LeanAxis.Z;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        Debug.Log($"[BicycleController] Awake: Rigidbody initialized, interpolation set, constraints applied.");
        if (visualsToLean)
            visualsInitialLocalRotation = visualsToLean.localRotation;
    }

    // Input System public methods
    public void OnAccelerate(InputAction.CallbackContext ctx)
    {
        accelerateInput = ctx.ReadValueAsButton() ? 1f : 0f;
        Debug.Log($"[BicycleController] OnAccelerate: accelerateInput={accelerateInput}, ctx.phase={ctx.phase}");
    }
    public void OnBrake(InputAction.CallbackContext ctx)
    {
        brakeInput = ctx.ReadValueAsButton() ? 1f : 0f;
        Debug.Log($"[BicycleController] OnBrake: brakeInput={brakeInput}, ctx.phase={ctx.phase}");
    }
    public void OnTurn(InputAction.CallbackContext ctx)
    {
        TurnInput = ctx.ReadValue<float>();
        Debug.Log($"[BicycleController] OnTurn: TurnInput={TurnInput}, ctx.phase={ctx.phase}");
    }

    void Update()
    {
        // Visual Leaning (roll)
        float speedPercent = Mathf.Clamp01(Mathf.Abs(CurrentSpeed) / maxSpeed);
        float leanDir = invertLeanDirection ? 1f : -1f;
        float targetLean = leanDir * TurnInput * leanAngle * speedPercent;
        currentLean = Mathf.Lerp(currentLean, targetLean, Time.deltaTime * 6f);
        if (visualsToLean)
        {
            Quaternion leanRot = Quaternion.identity;
            switch (leanAxis)
            {
                case LeanAxis.X:
                    leanRot = Quaternion.Euler(currentLean, 0f, 0f);
                    break;
                case LeanAxis.Y:
                    leanRot = Quaternion.Euler(0f, currentLean, 0f);
                    break;
                case LeanAxis.Z:
                    leanRot = Quaternion.Euler(0f, 0f, currentLean);
                    break;
            }
            visualsToLean.localRotation = visualsInitialLocalRotation * leanRot;
        }
        Debug.Log($"[BicycleController] Update: speedPercent={speedPercent}, targetLean={targetLean}, currentLean={currentLean}");
    }

    void FixedUpdate()
    {
        // Ground check using wheel position and radius
        if (wheelPosition == null)
        {
            Debug.LogWarning("[BicycleController] wheelPosition is not assigned!");
            isGrounded = false;
        }
        else
        {
            isGrounded = Physics.CheckSphere(wheelPosition.position, wheelRadius, groundLayer, QueryTriggerInteraction.Ignore);
            Debug.Log($"[BicycleController] FixedUpdate: isGrounded={isGrounded}, wheelPos={wheelPosition.position}, wheelRadius={wheelRadius}");
        }

        // Get current vertical velocity
        verticalVelocity = rb.linearVelocity.y;
        Debug.Log($"[BicycleController] FixedUpdate: verticalVelocity={verticalVelocity}");

        // Acceleration/Braking/Natural Deceleration
        if (brakeInput > 0.1f)
        {
            targetSpeed = 0f;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, deceleration * 2f * Time.fixedDeltaTime);
            Debug.Log($"[BicycleController] FixedUpdate: Braking, CurrentSpeed={CurrentSpeed}");
        }
        else if (accelerateInput > 0.1f)
        {
            targetSpeed = maxSpeed;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, acceleration * Time.fixedDeltaTime);
            Debug.Log($"[BicycleController] FixedUpdate: Accelerating, CurrentSpeed={CurrentSpeed}");
        }
        else
        {
            targetSpeed = 0f;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, deceleration * Time.fixedDeltaTime);
            Debug.Log($"[BicycleController] FixedUpdate: Natural Deceleration, CurrentSpeed={CurrentSpeed}");
        }

        // Turning (only if moving forward)
        float turnAmount = 0f;
        if (CurrentSpeed > 0.1f && Mathf.Abs(TurnInput) > 0.01f)
        {
            turnAmount = TurnInput * turnSpeed * Time.fixedDeltaTime;
            Quaternion turnRot = Quaternion.Euler(0f, turnAmount, 0f);
            rb.MoveRotation(rb.rotation * turnRot);
            Debug.Log($"[BicycleController] FixedUpdate: Turning, turnAmount={turnAmount}, TurnInput={TurnInput}");
        }
        else
        {
            Debug.Log($"[BicycleController] FixedUpdate: Not turning, CurrentSpeed={CurrentSpeed}, TurnInput={TurnInput}");
        }

        // Set velocity (preserve vertical)
        Vector3 forwardVel = transform.forward * CurrentSpeed;
        rb.linearVelocity = new Vector3(forwardVel.x, verticalVelocity, forwardVel.z);
        Debug.Log($"[BicycleController] FixedUpdate: Set linearVelocity={rb.linearVelocity}");

        // Ground/Air forces
        if (isGrounded)
        {
            rb.AddForce(-Vector3.up * groundStickForce, ForceMode.Acceleration);
            Debug.Log($"[BicycleController] FixedUpdate: Applying groundStickForce={groundStickForce}");
        }
        else
        {
            rb.AddForce(-Vector3.up * airborneGravity, ForceMode.Acceleration);
            Debug.Log($"[BicycleController] FixedUpdate: Applying airborneGravity={airborneGravity}");
        }
    }

    // Optional: Debug draw for ground check
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        if (wheelPosition != null)
        {
            Gizmos.DrawWireSphere(wheelPosition.position, wheelRadius);
        }
    }
} 