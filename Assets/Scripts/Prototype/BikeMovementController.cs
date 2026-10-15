using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Prototype.Bicycle
{
    /// <summary>
    /// Handles physics, collision, and movement for bikes/trikes/vehicles.
    /// Outputs state data for the visual controller to interpret.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class BikeMovementController : MonoBehaviour
    {
        #region Movement Settings
        
        [Header("Movement Settings")]
        [Tooltip("Maximum forward speed of the vehicle.")]
        public float maxSpeed = 12f;
        
        [Tooltip("Minimum speed required to crash into obstacles (below this, just stops)")]
        public float minCrashSpeed = 2f;
        
        [Tooltip("Maximum reverse speed of the vehicle. This value must be negative.")]
        public float maxReverseSpeed = -2f;
        
        [Tooltip("Base acceleration rate.")]
        public float acceleration = 8f;
        
        [Tooltip("How quickly the vehicle slows down from rolling resistance when coasting.")]
        public float coastingDeceleration = 1f;
        
        [Tooltip("How quickly the vehicle slows down when braking.")]
        public float brakeForce = 25f;
        
        [Tooltip("Base turn speed in degrees per second.")]
        public float turnSpeed = 100f;
        
        [Header("Movement Curves")]
        [Tooltip("How speed affects turning radius (x: normalized speed, y: turn multiplier)")]
        public AnimationCurve speedToTurnFactor;
        
        [Tooltip("Acceleration modifier based on current speed")]
        public AnimationCurve accelerationCurve;
        
        #endregion
        
        #region Physics Settings
        
        [Header("Physics Configuration")]
        [Tooltip("Center of mass offset from GameObject pivot")]
        public Vector3 centerOfMassOffset = new Vector3(0, -0.1f, 0);
        
        [Tooltip("How much to resist sliding sideways")]
        public float lateralGripStrength = 500f;
        
        [Tooltip("Air drag coefficient")]
        public float airDrag = 0.5f;
        
        [Tooltip("How quickly to align to ground normal")]
        public float groundAlignmentSpeed = 10f;
        
        [Tooltip("Use arcade-style air physics (no rotation)")]
        public bool arcadeAirPhysics = true;
        
        #endregion
        
        #region Wheel Configuration
        
        [Header("Wheel Detection")]
        [Tooltip("Wheel transforms for ground detection (same as visual controller)")]
        public Transform[] wheelTransforms;
        
        [Tooltip("Suspension neutral height for each wheel")]
        public float suspensionHeight = 0.35f;
        
        [Tooltip("Extra distance for ground detection")]
        public float groundCheckDistance = 0.1f;
        
        [Tooltip("Layers that count as ground")]
        public LayerMask groundLayers = 128;
        
        #endregion
        
        #region Air Control
        
        [Header("Air Control")]
        [Tooltip("Enable pitch/yaw control in air")]
        public bool enableAirControl = true;
        
        [Tooltip("Maximum pitch angle in air (degrees)")]
        public float maxAirPitch = 30f;
        
        [Tooltip("Pitch rotation speed in air (deg/sec)")]
        public float airPitchRate = 45f;
        
        [Tooltip("Yaw rotation speed in air (deg/sec)")]
        public float airYawRate = 90f;
        
        [Tooltip("How much control player has in air (0-1)")]
        public float airControlStrength = 0.3f;
        
        #endregion
        
        #region Bunny Hop Settings
        
        [Header("Bunny Hop")]
        [Tooltip("Base vertical impulse force")]
        public float baseHopForce = 5f;
        
        [Tooltip("Maximum charge time for hop")]
        public float maxHopChargeTime = 1f;
        
        [Tooltip("Power increase over charge time")]
        public AnimationCurve hopPowerCurve;
        
        [Tooltip("Forward boost on hop (0-1)")]
        public float hopForwardBoost = 0.1f;
        
        [Tooltip("Time window to boost off ramps")]
        public float rampBoostWindow = 0.2f;
        
        [Tooltip("Extra velocity multiplier for ramp boosts")]
        public float rampBoostMultiplier = 1.5f;
        
        [Tooltip("Coyote time in seconds")]
        public float coyoteTime = 0.1f;
        
        #endregion
        
        #region Terrain Handling
        
        [Header("Terrain Handling")]
        [Tooltip("Minimum height difference to register as a bump")]
        public float bumpGraceHeight = 0.05f;
        
        [Tooltip("Maximum climbable height without bunny hop")]
        public float maxClimbableHeight = 0.1f;
        
        [Tooltip("Recovery speed when bike is on its side")]
        public float sideRecoveryTorque = 500f;
        
        [Tooltip("Impact force threshold for crash")]
        public float crashImpactThreshold = 15f;
        
        #endregion
        
        #region Drift Settings
        
        [Header("Drift (Not Implemented)")]
        [Tooltip("Grip reduction while drifting (0-1)")]
        public float driftGripReduction = 0.5f;
        
        [Tooltip("How drift intensity changes with speed")]
        public AnimationCurve driftIntensityOverSpeed;
        
        #endregion
        
        #region State Machine
        
        public enum MovementState
        {
            Grounded,
            Airborne,
            Landing,
            Crashed
        }
        
        [Header("State Info (Read Only)")]
        [SerializeField] private MovementState currentState = MovementState.Grounded;
        [SerializeField] private float currentSpeed;
        [SerializeField] private bool isGrounded;
        [SerializeField] private Vector3 groundNormal = Vector3.up;
        [SerializeField] private int wheelsGrounded;
        
        #endregion
        
        #region Public State Properties
        
        // Properties for the visual controller to read
        [Debuggable("State")]
        public MovementState CurrentState => currentState;
        [Debuggable("Speed (m/s)")]
        public float CurrentSpeed => currentSpeed;
        [Debuggable("Grounded")]
        public bool IsGrounded => isGrounded;
        [Debuggable("Wheels Grounded")]
        public int WheelsGrounded => wheelsGrounded;
        public Vector3 GroundNormal => groundNormal;
        public Vector3 Velocity => rb.linearVelocity;
        public Vector3 LocalVelocity => transform.InverseTransformDirection(rb.linearVelocity);
        [Debuggable("Lateral Slip")]
        public float LateralSlip => LocalVelocity.x;
        [Debuggable("Normalized Speed")]
        public float NormalizedSpeed => Mathf.Clamp01(Mathf.Abs(currentSpeed) / maxSpeed);
        
        // Input state for visual controller
        [Debuggable("Accel Input")]
        public float AccelerationInput => accelerationInput;
        [Debuggable("Turn Input")]
        public float TurnInput => turnInput;
        [Debuggable("Brake Input")]
        public bool BrakeInput => brakeInput;
        [Debuggable("Drift Input")]
        public bool DriftInput => driftInput;
        [Debuggable("Hop Charge")]
        public float HopChargeAmount => hopChargeTime / maxHopChargeTime;
        
        // Wheel ground info for visual controller
        public WheelGroundInfo[] WheelGroundStates => wheelGroundStates;
        
        #endregion
        
        #region Private Variables
        
        // Components
        private Rigidbody rb;
        private PlayerInput playerInput;
        
        // Input state
        private float accelerationInput;
        private float turnInput;
        private bool brakeInput;
        private bool driftInput;
        private bool isChargingHop;
        private float hopChargeTime;
        private bool _mustReleaseBrakeBeforeReversing;
        
        // Physics state
        private WheelGroundInfo[] wheelGroundStates;
        private float lastGroundedTime;
        private float lastRampExitTime;
        private Vector3 lastRampNormal;
        private Vector3 launchVelocity;
        
        // Air control state
        private float currentAirPitch;
        private float currentAirYaw;
        private Quaternion lockedOrientation;
        
        // Terrain handling state
        private float lastGroundHeight;
        private bool isRecoveringFromSide;
        
        // Crash state
        private float crashTime;
        
        // Collision events for visual controller
        private List<CollisionEvent> recentCollisions = new List<CollisionEvent>();
        
        [System.Serializable]
        public class WheelGroundInfo
        {
            public Transform transform;
            public bool isGrounded;
            public RaycastHit hit;
            public float distanceToGround;
            public Vector3 contactPoint;
            public Vector3 contactNormal;
        }
        
        [System.Serializable]
        public class CollisionEvent
        {
            public Vector3 point;
            public Vector3 normal;
            public float force;
            public float timestamp;
            
            public CollisionEvent(Vector3 p, Vector3 n, float f)
            {
                point = p;
                normal = n;
                force = f;
                timestamp = Time.time;
            }
        }
        
        #endregion
        
        #region Events
        
        public System.Action<MovementState, MovementState> OnStateChanged;
        public System.Action<float> OnSpeedChanged;
        public System.Action<CollisionEvent> OnCollisionOccurred;
        
        #endregion
        
        #region Unity Lifecycle
        
#if UNITY_EDITOR
        private void Reset()
        {
            InitializeDefaultCurves();
        }
#endif
        
        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            
            // Configure rigidbody
            rb.mass = 100f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 2f;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            
            // Set center of mass
            rb.centerOfMass = centerOfMassOffset;
            
            // Adjust inertia for better stability
            rb.inertiaTensor = new Vector3(rb.inertiaTensor.x * 2.5f, rb.inertiaTensor.y, rb.inertiaTensor.z * 0.5f);
            
            playerInput = GetComponent<PlayerInput>();
            
            // Initialize wheel ground states
            if (wheelTransforms != null && wheelTransforms.Length > 0)
            {
                wheelGroundStates = new WheelGroundInfo[wheelTransforms.Length];
                for (int i = 0; i < wheelTransforms.Length; i++)
                {
                    wheelGroundStates[i] = new WheelGroundInfo
                    {
                        transform = wheelTransforms[i]
                    };
                }
            }
            else
            {
                Debug.LogError("BikeMovementController: No wheel transforms assigned!");
            }
            
            // Initialize default curves
            InitializeDefaultCurves();
        }
        
        private void Start()
        {
            DebugManager.LogInspectorSettings(this);
        }
        
        private void OnEnable()
        {
            var actionMap = playerInput.currentActionMap;
            actionMap["Accelerate"].performed += OnAccelerate;
            actionMap["Accelerate"].canceled += OnAccelerate;
            actionMap["Brake"].performed += OnBrake;
            actionMap["Brake"].canceled += OnBrake;
            actionMap["Turn"].performed += OnTurn;
            actionMap["Turn"].canceled += OnTurn;
            actionMap["Hop"].performed += OnHopPressed;
            actionMap["Hop"].canceled += OnHopReleased;
            
            // Drift action (if it exists)
            var driftAction = actionMap.FindAction("Drift");
            if (driftAction != null)
            {
                driftAction.performed += OnDrift;
                driftAction.canceled += OnDrift;
            }
        }
        
        private void OnDisable()
        {
            if (playerInput == null || playerInput.currentActionMap == null) return;

            var actionMap = playerInput.currentActionMap;
            actionMap["Accelerate"].performed -= OnAccelerate;
            actionMap["Accelerate"].canceled -= OnAccelerate;
            actionMap["Brake"].performed -= OnBrake;
            actionMap["Brake"].canceled -= OnBrake;
            actionMap["Turn"].performed -= OnTurn;
            actionMap["Turn"].canceled -= OnTurn;
            actionMap["Hop"].performed -= OnHopPressed;
            actionMap["Hop"].canceled -= OnHopReleased;
            
            var driftAction = actionMap.FindAction("Drift");
            if (driftAction != null)
            {
                driftAction.performed -= OnDrift;
                driftAction.canceled -= OnDrift;
            }
        }
        
        private void FixedUpdate()
        {
            // Update wheel-based ground detection
            UpdateWheelGroundDetection();
            
            // Calculate ground plane from wheels
            UpdateGroundPlane();
            
            // Update state machine
            UpdateStateMachine();
            
            // Handle movement based on state
            switch (currentState)
            {
                case MovementState.Grounded:
                    HandleGroundedMovement();
                    break;
                case MovementState.Airborne:
                    HandleAirborneMovement();
                    break;
                case MovementState.Landing:
                    HandleLanding();
                    break;
                case MovementState.Crashed:
                    HandleCrashed();
                    break;
            }
            
            // Update hop charging
            if (isChargingHop && CanHop())
            {
                hopChargeTime = Mathf.Min(hopChargeTime + Time.fixedDeltaTime, maxHopChargeTime);
            }
            
            // Clean old collision events
            recentCollisions.RemoveAll(c => Time.time - c.timestamp > 1f);

            // Removed string allocation in FixedUpdate
            // Debug logging handled more efficiently elsewhere

            DebugManager.ReportDebuggableMembers(this);
        }
        
        #endregion
        
        #region Ground Detection
        
        private void UpdateWheelGroundDetection()
        {
            wheelsGrounded = 0;
            
            for (int i = 0; i < wheelGroundStates.Length; i++)
            {
                var wheel = wheelGroundStates[i];
                if (wheel.transform == null) continue;
                
                Vector3 rayOrigin = wheel.transform.position;
                float baseRayLength = suspensionHeight + groundCheckDistance;
                
                // Try multiple ray directions to ensure ground detection
                bool wheelHit = false;
                RaycastHit bestHit = new RaycastHit();
                float shortestDistance = float.MaxValue;
                
                // Calculate how pitched the bike is
                float pitchDot = Mathf.Abs(Vector3.Dot(transform.forward, Vector3.up));
                
                // Ray directions to try (in order of preference)
                Vector3[] rayDirections;
                
                if (pitchDot < 0.3f) // Not very pitched - use simple detection
                {
                    rayDirections = new Vector3[] { -transform.up };
                }
                else // Significantly pitched - try multiple rays
                {
                    rayDirections = new Vector3[]
                    {
                        -transform.up,                              // Bike's local down
                        Vector3.down,                               // World down
                        Vector3.Slerp(-transform.up, Vector3.down, 0.5f).normalized, // Halfway between
                        Vector3.Slerp(-transform.up, Vector3.down, 0.75f).normalized  // 3/4 toward world down
                    };
                }
                
                // Try each ray direction
                foreach (var direction in rayDirections)
                {
                    // Adjust ray length based on angle
                    float angleFactor = Mathf.Max(0.5f, Vector3.Dot(direction, -transform.up));
                    float adjustedLength = baseRayLength / angleFactor;
                    
                    if (Physics.Raycast(rayOrigin, direction, out RaycastHit hit, adjustedLength, groundLayers))
                    {
                        if (hit.distance < shortestDistance)
                        {
                            wheelHit = true;
                            bestHit = hit;
                            shortestDistance = hit.distance;
                        }
                    }
                }
                
                if (wheelHit)
                {
                    wheel.hit = bestHit;
                    wheel.distanceToGround = bestHit.distance;
                    wheel.isGrounded = wheel.distanceToGround <= suspensionHeight + 0.05f;
                    wheel.contactPoint = bestHit.point;
                    wheel.contactNormal = bestHit.normal;
                    
                    if (wheel.isGrounded)
                    {
                        wheelsGrounded++;
                    }
                }
                else
                {
                    wheel.isGrounded = false;
                    wheel.distanceToGround = float.MaxValue;
                }
            }
            
            // Vehicle is grounded if any wheel is grounded
            isGrounded = wheelsGrounded > 0;
        }
        
        private void UpdateGroundPlane()
        {
            if (!isGrounded)
            {
                // Check if bike is on its side and needs recovery
                float rollAngle = Vector3.Angle(transform.up, Vector3.up);
                if (rollAngle > 45f)
                {
                    isRecoveringFromSide = true;
                    // Do a sphere cast to find ground when on side
                    if (Physics.SphereCast(transform.position, 0.5f, Vector3.down, out RaycastHit hit, 2f, groundLayers))
                    {
                        groundNormal = hit.normal;
                        isGrounded = true;
                        wheelsGrounded = 1; // Force grounded state
                    }
                }
                return;
            }
            
            // Reset recovery flag if we're upright
            if (isRecoveringFromSide && Vector3.Angle(transform.up, Vector3.up) < 10f)
            {
                isRecoveringFromSide = false;
            }
            
            // Get all grounded wheel positions
            var groundedWheels = wheelGroundStates.Where(w => w.isGrounded).ToList();
            
            if (groundedWheels.Count == 0)
            {
                return;
            }
            
            // Calculate average ground height for bump detection
            float avgGroundHeight = 0f;
            foreach (var wheel in groundedWheels)
            {
                avgGroundHeight += wheel.contactPoint.y;
            }
            avgGroundHeight /= groundedWheels.Count;
            
            // Check for bumps/obstacles
            if (lastGroundHeight != 0f)
            {
                float heightDiff = avgGroundHeight - lastGroundHeight;
                if (heightDiff > bumpGraceHeight && heightDiff < maxClimbableHeight)
                {
                    // Hit an obstacle
                    if (Mathf.Abs(currentSpeed) < minCrashSpeed)
                    {
                        // Too slow - just stop
                        currentSpeed = 0;
                    }
                    else
                    {
                        // Fast enough - crash
                        // currentState = MovementState.Crashed;
                        // OnStateEnter(MovementState.Crashed);
                    }
                }
                else if (heightDiff > maxClimbableHeight)
                {
                    // Hit something too big - always crash if moving
                    if (Mathf.Abs(currentSpeed) > 0.1f)
                    {
                        // currentState = MovementState.Crashed;
                        // OnStateEnter(MovementState.Crashed);
                    }
                }
            }
            lastGroundHeight = avgGroundHeight;
            
            // Calculate ground normal
            if (groundedWheels.Count == 1)
            {
                // Single wheel - use its normal
                groundNormal = groundedWheels[0].contactNormal;
            }
            else
            {
                // Multiple wheels - average normals
                Vector3 avgNormal = Vector3.zero;
                foreach (var wheel in groundedWheels)
                {
                    avgNormal += wheel.contactNormal;
                }
                groundNormal = avgNormal.normalized;
            }
            
            // Detect ramp exits
            if (groundNormal.y < 0.9f && Vector3.Dot(groundNormal, transform.forward) > 0.3f)
            {
                // We're on a ramp facing upward
                if (!isGrounded && wheelsGrounded == 0)
                {
                    // Just left the ramp
                    lastRampExitTime = Time.time;
                    lastRampNormal = groundNormal;
                }
            }
        }
        
        #endregion
        
        #region State Machine
        
        private float landingConfirmationTime = 0f;
        private const float LANDING_CONFIRM_DURATION = 0.1f;
        
        private float lastGroundedStateChange = 0f;

        // Modified UpdateStateMachine method:
        private void UpdateStateMachine()
        {
            MovementState previousState = currentState;
            
            switch (currentState)
            {
                case MovementState.Grounded:
                    if (!isGrounded)
                    {
                        // Add small delay before going airborne to prevent oscillation
                        if (Time.time - lastGroundedStateChange > 0.1f)
                        {
                            currentState = MovementState.Airborne;
                            OnStateExit(MovementState.Grounded);
                            OnStateEnter(MovementState.Airborne);
                            lastGroundedStateChange = Time.time;
                        }
                    }
                    break;
                    
                case MovementState.Airborne:
                    if (isGrounded)
                    {
                        currentState = MovementState.Landing;
                        OnStateExit(MovementState.Airborne);
                        OnStateEnter(MovementState.Landing);
                        lastGroundedStateChange = Time.time;
                    }
                    break;
                    
                case MovementState.Landing:
                    if (isGrounded)
                    {
                        landingConfirmationTime += Time.fixedDeltaTime;
                        if (landingConfirmationTime >= LANDING_CONFIRM_DURATION)
                        {
                            currentState = MovementState.Grounded;
                            OnStateExit(MovementState.Landing);
                            OnStateEnter(MovementState.Grounded);
                            landingConfirmationTime = 0f;
                        }
                    }
                    else
                    {
                        // Lost ground contact during landing - back to airborne
                        currentState = MovementState.Airborne;
                        landingConfirmationTime = 0f;
                    }
                    break;
                    
                case MovementState.Crashed:
                    // Check if bike has settled
                    if (rb.linearVelocity.magnitude < 0.5f && rb.angularVelocity.magnitude < 0.5f)
                    {
                        currentState = MovementState.Grounded;
                        OnStateExit(MovementState.Crashed);
                        OnStateEnter(MovementState.Grounded);
                    }
                    break;
            }

            if (previousState != currentState)
            {
                DebugManager.Log($"State changed from {previousState} to {currentState}. Position: {transform.position:F2}, Rotation: {transform.eulerAngles:F2}, Velocity: {rb.linearVelocity:F2}", "State Machine");
                OnStateChanged?.Invoke(previousState, currentState);
            }
        }
        
        private void OnStateEnter(MovementState state)
        {
            switch (state)
            {
                case MovementState.Airborne:
                    if (arcadeAirPhysics)
                    {
                        // Lock orientation for arcade feel
                        lockedOrientation = transform.rotation;
                        currentAirPitch = 0;
                        currentAirYaw = 0;
                        
                        // Store launch velocity
                        launchVelocity = rb.linearVelocity;
                        
                        // Kill angular velocity for stable flight
                        rb.angularVelocity = Vector3.zero;
                    }
                    break;
                    
                case MovementState.Landing:
                    // Stabilize landing
                    if (arcadeAirPhysics)
                    {
                        rb.angularVelocity *= 0.5f;
                    }

                    // Sync currentSpeed with the actual velocity upon landing. This prevents a "jerk"
                    // where the grounded movement code tries to instantly apply a speed that doesn't
                    // match the momentum from being airborne.
                    Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
                    if (horizontalVel.magnitude > 0.1f)
                    {
                        // Calculate speed based on magnitude, but maintain the sign (forward/reverse)
                        currentSpeed = horizontalVel.magnitude * Mathf.Sign(Vector3.Dot(rb.linearVelocity, transform.forward));
                    }
                    break;
                    
                case MovementState.Crashed:
                    crashTime = Time.time;
                    currentSpeed = 0;
                    break;
            }
        }
        
        private void OnStateExit(MovementState state)
        {
            switch (state)
            {
                case MovementState.Grounded:
                    lastGroundedTime = Time.time;
                    break;
            }
        }
        
        #endregion
        
        #region Movement
        
        private void HandleGroundedMovement()
        {
            // Handle side recovery
            if (isRecoveringFromSide)
            {
                // Apply torque to right the bike
                Vector3 uprightDirection = Vector3.Cross(transform.right, Vector3.up);
                rb.AddTorque(uprightDirection * sideRecoveryTorque * Time.fixedDeltaTime);
                
                float rollAngle = Vector3.Angle(transform.up, Vector3.up);
                if (rollAngle < 10f)
                {
                    isRecoveringFromSide = false;
                }
                return; // Skip normal movement while recovering
            }
            
            // Update speed based on input
            float normalizedSpeed = NormalizedSpeed;
            float accelerationFactor = accelerationCurve.Evaluate(normalizedSpeed);
            
            // 1. Handle Forward Acceleration
            if (accelerationInput > 0.1f)
            {
                currentSpeed += accelerationInput * acceleration * accelerationFactor * Time.fixedDeltaTime;
                currentSpeed = Mathf.Min(currentSpeed, maxSpeed);
            }
            // 2. Handle Braking and Reversing
            else if (brakeInput)
            {
                // If we are almost stationary (or already reversing) and the player is not locked out of reversing, then reverse.
                if ((Mathf.Abs(currentSpeed) < 0.1f || currentSpeed < 0) && !_mustReleaseBrakeBeforeReversing)
                {
                    // Allow reversing
                    currentSpeed -= acceleration * Time.fixedDeltaTime;
                    currentSpeed = Mathf.Max(currentSpeed, maxReverseSpeed);
                }
                else
                {
                    // Normal braking
                    currentSpeed = Mathf.MoveTowards(currentSpeed, 0, brakeForce * Time.fixedDeltaTime);
                }
            }
            // 3. Handle Coasting
            else
            {
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0, coastingDeceleration * Time.fixedDeltaTime);
            }
            
            // Bike-style steering
            if (Mathf.Abs(currentSpeed) > 0.01f)
            {
                float turnFactor = speedToTurnFactor.Evaluate(normalizedSpeed);
                float turnAngle = turnInput * turnSpeed * turnFactor * Time.fixedDeltaTime;
                
                // When reversing, invert the turn angle for intuitive steering
                if (currentSpeed < 0f)
                {
                    turnAngle *= -1f;
                }

                // Apply drift reduction if drifting
                if (driftInput)
                {
                    float driftIntensity = driftIntensityOverSpeed.Evaluate(normalizedSpeed);
                    turnAngle *= (1f + driftIntensity);
                }
                
                transform.Rotate(0, turnAngle, 0);
            }
            
            // Move forward along current direction
            Vector3 moveDirection = Vector3.ProjectOnPlane(transform.forward, groundNormal).normalized;
            Vector3 targetVelocity = moveDirection * currentSpeed;
            
            // Set velocity directly for predictable movement
            Vector3 newVelocity = targetVelocity;
            newVelocity.y = rb.linearVelocity.y;
            rb.linearVelocity = newVelocity;
            
            // Apply lateral grip (reduced if drifting)
            float gripMultiplier = driftInput ? (1f - driftGripReduction) : 1f;
            float lateralVelocity = LocalVelocity.x;
            rb.AddRelativeForce(-lateralVelocity * lateralGripStrength * gripMultiplier * Time.fixedDeltaTime, 0, 0, ForceMode.Acceleration);
            
            // Maintain height above ground using suspension
            ApplySuspensionForces();
            
            // Align to ground plane
            AlignToGroundPlane();

            // If we are almost stopped and not accelerating, kill all horizontal velocity to prevent jitter.
            if (accelerationInput < 0.1f && !brakeInput && Mathf.Abs(turnInput) < 0.1f && rb.linearVelocity.magnitude < 0.1f)
            {
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
                rb.angularVelocity = Vector3.zero;
            }
        }
        
        private void HandleAirborneMovement()
        {
            if (arcadeAirPhysics)
            {
                // Maintain launch orientation with limited control
                if (enableAirControl)
                {
                    // Pitch control (accel/brake)
                    float pitchInput = (accelerationInput - (brakeInput ? 1f : 0f)) * airControlStrength;
                    currentAirPitch += pitchInput * airPitchRate * Time.fixedDeltaTime;
                    currentAirPitch = Mathf.Clamp(currentAirPitch, -maxAirPitch, maxAirPitch);
                    
                    // Yaw control (turning)
                    currentAirYaw += turnInput * airYawRate * airControlStrength * Time.fixedDeltaTime;
                }
                
                // Apply controlled rotation
                Quaternion targetRotation = lockedOrientation * Quaternion.Euler(currentAirPitch, currentAirYaw, 0);
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, Time.fixedDeltaTime * 10f));
                
                // Maintain forward momentum
                Vector3 horizontalVelocity = new Vector3(launchVelocity.x, 0, launchVelocity.z);
                horizontalVelocity *= (1f - airDrag * Time.fixedDeltaTime);
                
                Vector3 newVelocity = horizontalVelocity;
                newVelocity.y = rb.linearVelocity.y; // Let gravity handle vertical
                rb.linearVelocity = newVelocity;
            }
            else
            {
                // Realistic physics mode
                if (Mathf.Abs(turnInput) > 0.1f)
                {
                    rb.AddTorque(Vector3.up * turnInput * 100f * airControlStrength, ForceMode.Force);
                }
                
                // Apply air drag
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0, airDrag * Time.fixedDeltaTime);
            }
            
            // Sync speed with velocity
            Vector3 horizontalVel = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            if (horizontalVel.magnitude > 0.1f)
            {
                currentSpeed = horizontalVel.magnitude * Mathf.Sign(Vector3.Dot(horizontalVel, transform.forward));
            }
        }
        
        private void HandleLanding()
        {
            // Landing is handled by state transitions
            // Visual controller handles the visual impact
            HandleGroundedMovement();
        }
        
        private void HandleCrashed()
        {
            // Let physics handle the crash
            currentSpeed = 0;
            
            // Could add visual effects or other crash handling here
            // Recovery is handled in the state machine
        }
        
        private void ApplySuspensionForces()
        {
            foreach (var wheel in wheelGroundStates)
            {
                if (!wheel.isGrounded || wheel.transform == null) continue;
                
                // Calculate suspension compression
                float compression = Mathf.Max(0, suspensionHeight - wheel.distanceToGround);
                float compressionRatio = compression / suspensionHeight;
                
                // Spring force
                float springForce = compressionRatio * 5000f;
                
                // Damping
                float velocityAtWheel = Vector3.Dot(rb.GetPointVelocity(wheel.transform.position), transform.up);
                float damperForce = velocityAtWheel * 500f;
                
                // Apply force at wheel position
                Vector3 force = transform.up * (springForce - damperForce);
                rb.AddForceAtPosition(force, wheel.transform.position);
            }
        }
        
        private void AlignToGroundPlane()
        {
            if (!isGrounded) return;
            
            // Calculate target up vector based on ground normal
            Vector3 targetUp = Vector3.Slerp(transform.up, groundNormal, groundAlignmentSpeed * Time.fixedDeltaTime);
            
            // Create rotation that maintains forward direction while aligning up
            Quaternion targetRotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(transform.forward, targetUp).normalized, 
                targetUp
            );
            
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, groundAlignmentSpeed * Time.fixedDeltaTime));
        }
        
        #endregion
        
        #region Bunny Hop
        
        private void PerformHop()
        {
            if (!CanHop()) return;
            
            // Calculate hop power from charge time
            float hopPower = hopPowerCurve.Evaluate(hopChargeTime / maxHopChargeTime);
            float hopForce = baseHopForce + (baseHopForce * hopPower);
            
            // Check for ramp boost
            bool isRampBoost = Time.time - lastRampExitTime < rampBoostWindow;
            
            if (isRampBoost)
            {
                DebugManager.Log("Ramp boost activated!", "Player Action");
                hopForce *= rampBoostMultiplier;

                // 1. Find the "right" vector of the ramp's slope by taking the cross product of the normal and world up.
                Vector3 rampRight = Vector3.Cross(lastRampNormal, Vector3.up);
                // 2. Find the vector pointing up the slope by taking the cross product of the ramp's right vector and its normal.
                Vector3 rampUpDirection = Vector3.Cross(rampRight, lastRampNormal);

                // Add extra velocity in the correct ramp direction
                Vector3 rampBoost = rampUpDirection.normalized * hopForce * 0.5f;
                rb.linearVelocity += rampBoost;
            }
            
            DebugManager.Log($"Hop performed. Charge: {hopPower:F2}, Force: {hopForce:F1}", "Player Action");
            
            // Apply vertical impulse
            Vector3 hopVelocity = rb.linearVelocity;
            hopVelocity.y = hopForce;
            
            // Add forward boost if accelerating
            if (accelerationInput > 0.1f)
            {
                hopVelocity += transform.forward * hopForwardBoost * maxSpeed;
            }
            
            rb.linearVelocity = hopVelocity;
            
            // Force airborne state
            isGrounded = false;
            
            // Reset charge
            hopChargeTime = 0f;
            isChargingHop = false;
        }
        
        private bool CanHop()
        {
            return isGrounded || (Time.time - lastGroundedTime < coyoteTime);
        }
        
        #endregion
        
        #region Input Handlers
        
        private void OnAccelerate(InputAction.CallbackContext context)
        {
            accelerationInput = context.ReadValue<float>();
            // Hitting the gas cancels any reverse lock.
            if (accelerationInput > 0.1f)
            {
                _mustReleaseBrakeBeforeReversing = false;
            }
            DebugManager.Log($"Accelerate input: {accelerationInput}", "Input", DebugManager.LogLevel.Info);
        }
        
        private void OnBrake(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                brakeInput = true;
                // If we press brake while moving forward, we lock reverse until the brake is released.
                if (currentSpeed > 0.1f)
                {
                    _mustReleaseBrakeBeforeReversing = true;
                }
            }
            else if (context.canceled)
            {
                brakeInput = false;
                // Releasing the brake always resets the lock.
                _mustReleaseBrakeBeforeReversing = false;
            }
            DebugManager.Log($"Brake input: {brakeInput}", "Input", DebugManager.LogLevel.Info);
        }
        
        private void OnTurn(InputAction.CallbackContext context)
        {
            turnInput = context.ReadValue<float>();
            DebugManager.Log($"Turn input: {turnInput}", "Input", DebugManager.LogLevel.Info);
        }
        
        private void OnHopPressed(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                isChargingHop = true;
                hopChargeTime = 0f;
                DebugManager.Log("Hop charge started", "Input", DebugManager.LogLevel.Info);
            }
        }
        
        private void OnHopReleased(InputAction.CallbackContext context)
        {
            if (isChargingHop)
            {
                DebugManager.Log($"Hop released. Charge time: {hopChargeTime:F2}", "Input", DebugManager.LogLevel.Info);
                PerformHop();
            }
        }
        
        private void OnDrift(InputAction.CallbackContext context)
        {
            driftInput = context.performed;
            DebugManager.Log($"Drift input: {driftInput}", "Input", DebugManager.LogLevel.Info);
        }
        
        #endregion
        
        #region Collision Handling
        
        private void OnCollisionEnter(Collision collision)
        {
            ContactPoint contact = collision.GetContact(0);
            float impactForce = collision.relativeVelocity.magnitude;
            
            recentCollisions.Add(new CollisionEvent(contact.point, contact.normal, impactForce));
            
            OnCollisionOccurred?.Invoke(new CollisionEvent(contact.point, contact.normal, impactForce));
            
            DebugManager.Log($"Collision with {collision.gameObject.name}, impact: {impactForce:F2}", "Collision");

            // Ignore crash check for ground layer collisions
            if (groundLayers == (groundLayers | (1 << collision.gameObject.layer)))
            {
                return;
            }

            // Check for crash conditions
            if (impactForce > crashImpactThreshold && Vector3.Dot(contact.normal, -transform.forward) > 0.7f)
            {
                // A crash-worthy collision occurred. Stop the bike's forward momentum.
                currentSpeed = 0f;

                // The crashed state itself is disabled, so we don't transition.
                // currentState = MovementState.Crashed;
                // OnStateEnter(MovementState.Crashed);
                // DebugManager.Log("Crash detected!", "State Machine", DebugManager.LogLevel.Warning);
            }
        }
        
        public List<CollisionEvent> GetRecentCollisions()
        {
            return new List<CollisionEvent>(recentCollisions);
        }
        
        #endregion
        
        #region Setup
        
        private void InitializeDefaultCurves()
        {
            // Speed to turn factor
            if (speedToTurnFactor == null || speedToTurnFactor.keys.Length == 0)
            {
                speedToTurnFactor = new AnimationCurve(
                    new Keyframe(0f, 0f, 5f, 5f),
                    new Keyframe(0.2f, 1f, 2.1875f, 2.1875f),
                    new Keyframe(1f, 0.5f, -0.625f, -0.625f)
                );
            }
            
            // Acceleration curve
            if (accelerationCurve == null || accelerationCurve.keys.Length == 0)
            {
                accelerationCurve = new AnimationCurve(
                    new Keyframe(-0.002f, 0.317f, 0f, 0f),
                    new Keyframe(1f, 1f, 2f, 2f)
                );
            }
            
            // Hop power curve
            if (hopPowerCurve == null || hopPowerCurve.keys.Length == 0)
            {
                hopPowerCurve = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 2f),
                    new Keyframe(0.5f, 0.7f, 1.5f, 1.5f),
                    new Keyframe(1f, 1f, 0.5f, 0f)
                );
            }
            
            // Drift intensity curve
            if (driftIntensityOverSpeed == null || driftIntensityOverSpeed.keys.Length == 0)
            {
                driftIntensityOverSpeed = new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.5f, 0.5f),
                    new Keyframe(1f, 1f)
                );
            }
        }
        
        #endregion
        
        #region Debug
        
        [DebuggableProvider]
        private Dictionary<string, object> GetWheelDebugData()
        {
            var data = new Dictionary<string, object>();
            if (wheelGroundStates == null) return data;

            for (int i = 0; i < wheelGroundStates.Length; i++)
            {
                var wheel = wheelGroundStates[i];
                data[$"Wheel {i} Grounded"] = wheel.isGrounded;
                data[$"Wheel {i} Distance"] = wheel.distanceToGround.ToString("F2");
            }
            return data;
        }
        
        [Debuggable("Air Pitch")]
        public float DebugAirPitch => currentAirPitch;
        
        [Debuggable("Air Yaw")] 
        public float DebugAirYaw => currentAirYaw;
        
        /*
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || wheelGroundStates == null) return;
            
            // Draw wheel ground detection
            foreach (var wheel in wheelGroundStates)
            {
                if (wheel.transform == null) continue;
                
                Vector3 rayStart = wheel.transform.position;
                Vector3 rayEnd = rayStart - transform.up * (suspensionHeight + groundCheckDistance);
                
                Gizmos.color = wheel.isGrounded ? Color.green : Color.red;
                Gizmos.DrawLine(rayStart, rayEnd);
                
                if (wheel.isGrounded)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(wheel.contactPoint, 0.1f);
                    Gizmos.DrawRay(wheel.contactPoint, wheel.contactNormal * 0.5f);
                }
            }
            
            // Draw ground normal
            if (isGrounded)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(transform.position, groundNormal * 2f);
            }
            
            // Draw velocity
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, rb.linearVelocity.normalized * 2f);
        }
        */
        
        #endregion
    }
}