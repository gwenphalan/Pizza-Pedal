using UnityEngine;
using System.Collections.Generic;

namespace Prototype.Bicycle
{
    /// <summary>
    /// Handles all visual representation of bike/vehicle movement.
    /// Reads state from BikeMovementController and creates accurate visual feedback.
    /// </summary>
    [RequireComponent(typeof(BikeMovementController))]
    public class BikeVisualController : MonoBehaviour
    {
        #region Visual Settings
        
        [Header("Lean Settings")]
        [Tooltip("Maximum lean angle in degrees")]
        public float maxLeanAngle = 30f;
        
        [Tooltip("How quickly the bike leans")]
        public float leanSpeed = 5f;
        
        [Tooltip("How quickly the bike returns to upright when stopping")]
        public float stoppingLeanRecoverySpeed = 1f;
        
        [Header("Handlebar Settings")]
        [Tooltip("Maximum handlebar rotation angle")]
        public float maxHandlebarAngle = 30f;
        
        [Tooltip("How quickly the handlebar rotates")]
        public float handlebarRotationSpeed = 4f;
        
        [Tooltip("Head tube angle (affects handlebar rotation axis)")]
        public float headTubeAngle = -27.448f;
        
        [Header("Animation Curves")]
        [Tooltip("How speed affects lean angle")]
        public AnimationCurve speedToLeanFactor;
        
        [Tooltip("How handlebar angle affects lean")]
        public AnimationCurve handlebarToLeanFactor;
        
        [Tooltip("Lean recovery when stopping")]
        public AnimationCurve stoppingLeanRecovery;
        
        [Tooltip("Suspension compression based on force")]
        public AnimationCurve suspensionCompressionCurve;
        
        [Tooltip("Wheel spin-up when accelerating in air")]
        public AnimationCurve airborneWheelSpinCurve;
        
        #endregion
        
        #region Wheel Configuration
        
        [Header("Wheel Settings")]
        [Tooltip("Wheel radius in meters")]
        public float wheelRadius = 0.233f;
        
        [Tooltip("Wheel configurations for this vehicle")]
        public WheelConfig[] wheels;
        
        [System.Serializable]
        public class WheelConfig
        {
            public string name = "Wheel";
            public Transform wheelTransform;
            public Transform suspensionPoint; // Where to raycast from
            public bool isPowered = false; // Does this wheel get power?
            public bool steers = false; // Does this wheel steer?
            public float suspensionTravel = 0.15f;
            public float suspensionStiffness = 1f;
            
            [HideInInspector] public float currentRotation;
            [HideInInspector] public float rotationVelocity;
            [HideInInspector] public bool isGrounded;
            [HideInInspector] public RaycastHit groundHit;
            [HideInInspector] public float suspensionCompression;
            [HideInInspector] public Vector3 initialLocalPosition;
        }
        
        #endregion
        
        #region Vehicle Configuration
        
        [Header("Vehicle Type")]
        public VehicleType vehicleType = VehicleType.Bicycle;
        
        public enum VehicleType
        {
            Bicycle,      // 2 wheels, leans into turns
            Tricycle,     // 3 wheels, rolls away from turns
            Quad,         // 4 wheels, rolls away from turns
            Custom        // Use manual configuration
        }
        
        [Header("Suspension Type")]
        public SuspensionType suspensionType = SuspensionType.FullSuspension;
        
        public enum SuspensionType
        {
            Rigid,
            FrontOnly,
            FullSuspension
        }
        
        #endregion
        
        #region Visual References
        
        [Header("Visual Components")]
        public Transform bikeBody;
        public Transform handlebar;
        public Transform pedals;
        public Transform rider; // Optional rider model
        
        [Header("Pedal Settings")]
        [Tooltip("Gear ratio for pedal rotation")]
        public float pedalGearRatio = 2.5f;
        
        [Header("Look-At Settings")]
        [Tooltip("A target that follows the handlebars' position and yaw, but not pitch or roll. Useful for cameras.")]
        public Transform lookAtTarget;
        
        #endregion
        
        #region Private Variables
        
        private BikeMovementController movementController;
        private Vector3 initialLookAtOffset;
        
        // Visual state
        private float currentLean;
        private float currentPitch;
        private float currentHandlebarAngle;
        private float targetHandlebarAngle;
        private float currentPedalRotation;
        
        // Stopping state (from SimpleBikeController)
        private bool isInStoppingState;
        private float stoppingLeanAmount;
        private float stoppingHandlebarAngle;
        private float stoppingProgress;
        
        // Cached values
        private float previousSpeed;
        private Vector3 previousPosition;
        private float frameTime;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            movementController = GetComponent<BikeMovementController>();
            
            if (lookAtTarget != null && handlebar != null)
            {
                // Calculate the initial offset of the look-at target relative to the handlebar's flat orientation.
                // This captures the user's intended placement from the editor without manual vector input.
                Quaternion handlebarFlatWorldRot = Quaternion.Euler(0, handlebar.eulerAngles.y, 0);
                initialLookAtOffset = Quaternion.Inverse(handlebarFlatWorldRot) * (lookAtTarget.position - handlebar.position);
            }
            
            if (wheels != null)
            {
                foreach (var wheel in wheels)
                {
                    if (wheel.wheelTransform != null)
                    {
                        wheel.initialLocalPosition = wheel.wheelTransform.localPosition;
                    }
                }
            }
            
            // Subscribe to movement controller events
            movementController.OnStateChanged += HandleStateChanged;
            
            // Initialize animation curves with defaults from your settings
            InitializeDefaultCurves();
            
            // Auto-configure wheels based on vehicle type if not manually set
            if (wheels == null || wheels.Length == 0)
            {
                AutoConfigureWheels();
            }
            
            // Ensure wheel count matches movement controller
            if (movementController.wheelTransforms != null && wheels != null)
            {
                if (movementController.wheelTransforms.Length != wheels.Length)
                {
                    Debug.LogWarning($"BikeVisualController: Wheel count mismatch! Movement has {movementController.wheelTransforms.Length}, Visual has {wheels.Length}");
                }
            }
        }
        
        private void Start()
        {
            previousPosition = transform.position;
            frameTime = Time.fixedDeltaTime;
        }
        
        private void OnDestroy()
        {
            if (movementController != null)
            {
                movementController.OnStateChanged -= HandleStateChanged;
            }
        }
        
        private void LateUpdate()
        {
            // Update all visuals after physics
            frameTime = Time.deltaTime;
            
            UpdateWheelGroundDetection();
            UpdateHandlebars();
            UpdateLean();
            UpdateSuspension();
            UpdateWheelRotation();
            UpdatePedals();
            UpdateRider();
            UpdateLookAtTarget();
            
            // Store values for next frame
            previousSpeed = movementController.CurrentSpeed;
            previousPosition = transform.position;
        }
        
        #endregion
        
        #region Visual Updates
        
        private bool IsAnyWheelGrounded()
        {
            if (wheels == null) return false;
            foreach (var wheel in wheels)
            {
                if (wheel.isGrounded) return true;
            }
            return false;
        }
        
        private void UpdateWheelGroundDetection()
        {
            // Get wheel ground states from movement controller
            var movementWheelStates = movementController.WheelGroundStates;
            
            for (int i = 0; i < wheels.Length && i < movementWheelStates.Length; i++)
            {
                var wheel = wheels[i];
                var movementWheel = movementWheelStates[i];
                
                if (wheel.wheelTransform == null) continue;
                
                // Directly use movement controller's detection
                wheel.isGrounded = movementWheel.isGrounded;
                wheel.groundHit = movementWheel.hit;
                wheel.suspensionCompression = 0f;
                
                if (wheel.isGrounded)
                {
                    Vector3 wheelPos = wheel.wheelTransform.position;

                    // Only move wheel for suspension if not rigid
                    if (suspensionType != SuspensionType.Rigid)
                    {
                        // Position wheel at ground contact
                        wheelPos.y = movementWheel.contactPoint.y + wheelRadius;
                        
                        // Clamp wheel position to not go above its initial position
                        Vector3 maxWorldPos = wheel.wheelTransform.parent.TransformPoint(wheel.initialLocalPosition);
                        wheelPos.y = Mathf.Min(wheelPos.y, maxWorldPos.y);

                        wheel.wheelTransform.position = wheelPos;
                    }
                    
                    // Calculate visual suspension compression
                    if (wheel.suspensionPoint != null)
                    {
                        float restHeight = wheel.suspensionPoint.position.y - wheel.suspensionTravel;
                        float currentHeight = wheelPos.y;
                        wheel.suspensionCompression = Mathf.Clamp01((restHeight - currentHeight) / wheel.suspensionTravel);
                    }
                }
            }
        }
        
        private void UpdateHandlebars()
        {
            if (handlebar == null) return;
            
            // Handle stopping state
            if (isInStoppingState)
            {
                float recoveryFactor = stoppingLeanRecovery.Evaluate(stoppingProgress);
                currentHandlebarAngle = stoppingHandlebarAngle * recoveryFactor;
            }
            else
            {
                // Normal handlebar control
                targetHandlebarAngle = movementController.TurnInput * maxHandlebarAngle;
                currentHandlebarAngle = Mathf.Lerp(currentHandlebarAngle, targetHandlebarAngle, 
                    handlebarRotationSpeed * frameTime);
            }
            
            // Apply rotation with head tube angle
            Quaternion headTubeRotation = Quaternion.Euler(headTubeAngle, 0, 0);
            Quaternion turnRotation = Quaternion.Euler(0, currentHandlebarAngle, 0);
            handlebar.localRotation = headTubeRotation * turnRotation;
        }
        
        private void UpdateLean()
        {
            // Detect stopping state (from SimpleBikeController logic)
            if ((previousSpeed > 0.5f && movementController.CurrentSpeed < 0.1f) && 
                Mathf.Abs(currentHandlebarAngle) > 5f)
            {
                EnterStoppingState();
            }
            
            if (isInStoppingState)
            {
                HandleStoppingState();
            }
            else
            {
                // Calculate lean based on vehicle type
                float targetLean = 0f;
                
                if (IsAnyWheelGrounded())
                {
                    switch (vehicleType)
                    {
                        case VehicleType.Bicycle:
                            targetLean = CalculateBikeLean();
                            break;
                            
                        case VehicleType.Tricycle:
                        case VehicleType.Quad:
                            targetLean = CalculateVehicleRoll();
                            break;
                    }
                }
                
                currentLean = Mathf.Lerp(currentLean, targetLean, leanSpeed * frameTime);
            }
            
            // Apply lean to bike body
            if (bikeBody != null)
            {
                bikeBody.localRotation = Quaternion.Euler(currentPitch, 0, currentLean);
            }
        }
        
        private float CalculateBikeLean()
        {
            float speed = movementController.CurrentSpeed;
            float normalizedSpeed = movementController.NormalizedSpeed;
            
            if (Mathf.Abs(speed) < 0.1f) return 0f;
            
            // Kinematic lean from turning
            float normalizedHandlebarAngle = Mathf.Abs(currentHandlebarAngle / maxHandlebarAngle);
            float handlebarDirection = currentHandlebarAngle / maxHandlebarAngle;
            
            float speedLeanFactor = speedToLeanFactor.Evaluate(normalizedSpeed);
            float handlebarLeanInfluence = handlebarToLeanFactor.Evaluate(normalizedHandlebarAngle);
            
            float kinematicLean = -handlebarDirection * maxLeanAngle * speedLeanFactor * handlebarLeanInfluence;
            
            // Reduce lean when drifting
            if (movementController.DriftInput)
            {
                kinematicLean *= 0.7f; // Bikes lean less when sliding
            }
            
            // Geometric lean from head tube angle
            float headAngleRad = headTubeAngle * Mathf.Deg2Rad;
            float handlebarAngleRad = currentHandlebarAngle * Mathf.Deg2Rad;
            float geometricLean = Mathf.Sin(handlebarAngleRad) * Mathf.Sin(headAngleRad) * Mathf.Rad2Deg;
            
            // Combine leans
            return kinematicLean + (geometricLean * speedLeanFactor);
        }
        
        private float CalculateVehicleRoll()
        {
            // Trikes and quads roll away from turns (like cars)
            float speed = movementController.CurrentSpeed;
            float normalizedSpeed = movementController.NormalizedSpeed;
            
            if (Mathf.Abs(speed) < 0.1f) return 0f;
            
            // Calculate lateral G-force
            float turnRate = movementController.TurnInput * movementController.turnSpeed * Mathf.Deg2Rad;
            float lateralAccel = speed * turnRate;
            
            // Roll is opposite of turn direction
            float rollAngle = Mathf.Atan2(lateralAccel, 9.81f) * Mathf.Rad2Deg;
            rollAngle = Mathf.Clamp(rollAngle, -15f, 15f); // Limit roll for stability
            
            return rollAngle * movementController.TurnInput;
        }
        
        private void UpdateSuspension()
        {
            // Visual suspension compression affects body position
            if (suspensionType == SuspensionType.Rigid) return;
            
            float frontCompression = 0f;
            float rearCompression = 0f;
            int frontWheels = 0;
            int rearWheels = 0;
            
            // Average compression for front/rear
            foreach (var wheel in wheels)
            {
                if (wheel.suspensionPoint == null) continue;
                
                float wheelZ = transform.InverseTransformPoint(wheel.suspensionPoint.position).z;
                if (wheelZ > 0) // Front
                {
                    if (suspensionType != SuspensionType.Rigid)
                    {
                        frontCompression += wheel.suspensionCompression;
                        frontWheels++;
                    }
                }
                else // Rear
                {
                    if (suspensionType == SuspensionType.FullSuspension)
                    {
                        rearCompression += wheel.suspensionCompression;
                        rearWheels++;
                    }
                }
                
                // Position wheel based on compression
                if (wheel.wheelTransform != null && wheel.isGrounded)
                {
                    Vector3 wheelPos = wheel.suspensionPoint.position;
                    wheelPos.y = wheel.groundHit.point.y + wheelRadius;
                    wheel.wheelTransform.position = wheelPos;
                }
            }
            
            // Calculate pitch from suspension difference
            if (frontWheels > 0) frontCompression /= frontWheels;
            if (rearWheels > 0) rearCompression /= rearWheels;
            
            float compressionDiff = frontCompression - rearCompression;
            float pitchFromSuspension = compressionDiff * 10f; // Tune this multiplier
            
            currentPitch = Mathf.Lerp(currentPitch, pitchFromSuspension, frameTime * 5f);
        }
        
        private void UpdateWheelRotation()
        {
            foreach (var wheel in wheels)
            {
                if (wheel.wheelTransform == null) continue;
                
                if (wheel.isGrounded)
                {
                    // Wheel rotation matches ground speed
                    float distanceTraveled = (transform.position - previousPosition).magnitude;
                    float rotationDelta = (distanceTraveled / (2 * Mathf.PI * wheelRadius)) * 360f;
                    
                    // Account for direction
                    if (Vector3.Dot(transform.position - previousPosition, transform.forward) < 0)
                        rotationDelta = -rotationDelta;
                    
                    wheel.currentRotation += rotationDelta;
                    wheel.rotationVelocity = rotationDelta / frameTime;
                }
                else if (wheel.isPowered && movementController.AccelerationInput > 0.1f)
                {
                    // Powered wheel spins up in air
                    float targetVelocity = movementController.maxSpeed / (2 * Mathf.PI * wheelRadius) * 360f;
                    float spinUpRate = airborneWheelSpinCurve.Evaluate(movementController.AccelerationInput);
                    
                    wheel.rotationVelocity = Mathf.Lerp(wheel.rotationVelocity, targetVelocity, spinUpRate * frameTime);
                    wheel.currentRotation += wheel.rotationVelocity * frameTime;
                }
                else
                {
                    // Non-powered airborne wheel maintains momentum with slight decay
                    wheel.rotationVelocity *= (1f - 0.5f * frameTime);
                    wheel.currentRotation += wheel.rotationVelocity * frameTime;
                }
                
                // Apply rotation
                wheel.wheelTransform.localRotation = Quaternion.Euler(wheel.currentRotation, 0, 0);
            }
        }
        
        private void UpdatePedals()
        {
            if (pedals == null) return;

            // Only rotate the pedals if the player is actively accelerating forward.
            if (movementController.AccelerationInput > 0.1f && movementController.CurrentSpeed > 0)
            {
                float wheelRotationSpeed = (movementController.CurrentSpeed / (wheelRadius * 2 * Mathf.PI)) * 360f;
                float pedalRotationSpeed = wheelRotationSpeed / pedalGearRatio;
                currentPedalRotation += pedalRotationSpeed * frameTime;
                pedals.localRotation = Quaternion.Euler(currentPedalRotation, 0, 0);
            }
        }
        
        private void UpdateRider()
        {
            if (rider == null) return;
            
            // Simple rider position shifts based on acceleration/braking
            Vector3 riderOffset = Vector3.zero;
            
            if (movementController.AccelerationInput > 0.1f)
            {
                riderOffset.z = -0.05f; // Lean back when accelerating
            }
            else if (movementController.BrakeInput)
            {
                riderOffset.z = 0.05f; // Lean forward when braking
            }
            
            // Shift weight into turns
            riderOffset.x = -currentLean * 0.001f; // Subtle shift
            
            rider.localPosition = Vector3.Lerp(rider.localPosition, riderOffset, frameTime * 3f);
        }
        
        private void UpdateLookAtTarget()
        {
            if (lookAtTarget == null || handlebar == null)
            {
                return;
            }

            // This method ensures the lookAtTarget follows the handlebars' general direction
            // without inheriting the bike's lean (roll) or the handlebar's head-tube pitch.
            // It stays level with the world's horizon.

            // 1. Calculate the target's desired rotation.
            // We combine the bike's root 'up' direction (yaw) with the handlebar's turning angle.
            // This creates a rotation that only has yaw, keeping it flat.
            float bikeYaw = transform.eulerAngles.y;
            float totalYaw = bikeYaw + currentHandlebarAngle;
            Quaternion targetWorldRotation = Quaternion.Euler(0, totalYaw, 0);

            // 2. Calculate the target's desired position.
            // We start at the handlebar's pivot point.
            // Then we add the offset, but rotated by our "flat" target rotation.
            // This makes the target swing with the handlebars without dipping during leans.
            Vector3 worldOffset = targetWorldRotation * initialLookAtOffset;
            Vector3 targetWorldPosition = handlebar.position + worldOffset;

            // 3. Apply the final position and rotation.
            lookAtTarget.position = targetWorldPosition;
            lookAtTarget.rotation = targetWorldRotation;
        }
        
        #endregion
        
        #region Stopping State
        
        private void EnterStoppingState()
        {
            isInStoppingState = true;
            stoppingLeanAmount = currentLean;
            stoppingHandlebarAngle = currentHandlebarAngle;
            stoppingProgress = 0f;
        }
        
        private void HandleStoppingState()
        {
            stoppingProgress += stoppingLeanRecoverySpeed * frameTime;
            
            if (stoppingProgress >= 1f)
            {
                isInStoppingState = false;
                return;
            }
            
            float recoveryFactor = stoppingLeanRecovery.Evaluate(stoppingProgress);
            currentLean = stoppingLeanAmount * recoveryFactor;
        }
        
        #endregion
        
        #region Setup Helpers
        
        private void InitializeDefaultCurves()
        {
            // Your exact curve settings from the JSON
            if (speedToLeanFactor == null || speedToLeanFactor.keys.Length == 0)
            {
                speedToLeanFactor = new AnimationCurve(
                    new Keyframe(0f, 0f, 5f, 5f),
                    new Keyframe(0.2f, 1f, 2.1875f, 2.1875f),
                    new Keyframe(1f, 0.5f, -0.625f, -0.625f)
                );
            }
            
            if (handlebarToLeanFactor == null || handlebarToLeanFactor.keys.Length == 0)
            {
                handlebarToLeanFactor = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 1f),
                    new Keyframe(1f, 1f, 1f, 0f)
                );
            }
            
            if (stoppingLeanRecovery == null || stoppingLeanRecovery.keys.Length == 0)
            {
                stoppingLeanRecovery = new AnimationCurve(
                    new Keyframe(0f, 1f, 0f, -0.2f),
                    new Keyframe(0.4f, 0.9f, -0.3f, -0.8f),
                    new Keyframe(0.8f, 0.3f, -1.2f, -1f),
                    new Keyframe(1f, 0f, -0.5f, 0f)
                );
            }
            
            if (suspensionCompressionCurve == null || suspensionCompressionCurve.keys.Length == 0)
            {
                suspensionCompressionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }
            
            if (airborneWheelSpinCurve == null || airborneWheelSpinCurve.keys.Length == 0)
            {
                airborneWheelSpinCurve = new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.5f, 0.3f),
                    new Keyframe(1f, 1f)
                );
            }
        }
        
        private void AutoConfigureWheels()
        {
            // This would be called in editor or at runtime to auto-setup wheels
            // For now, just a placeholder
            Debug.LogWarning("Auto-configure wheels not implemented. Please set up wheels manually.");
        }
        
        #endregion
        
        #region Event Handlers
        
        private void HandleStateChanged(BikeMovementController.MovementState oldState, BikeMovementController.MovementState newState)
        {
            // React to state changes for visual effects
            if (newState == BikeMovementController.MovementState.Crashed)
            {
                // Add crash visual effects here
                // e.g., particle effects, screen shake, etc.
            }
        }
        
        #endregion
        
        #region Debug
        
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            
            foreach (var wheel in wheels)
            {
                if (wheel.suspensionPoint == null) continue;
                
                // Draw suspension rays
                Gizmos.color = wheel.isGrounded ? Color.green : Color.red;
                Vector3 rayStart = wheel.suspensionPoint.position;
                Vector3 rayEnd = rayStart - transform.up * (wheel.suspensionTravel + wheelRadius + 0.1f);
                Gizmos.DrawLine(rayStart, rayEnd);
                
                if (wheel.isGrounded)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(wheel.groundHit.point, 0.05f);
                }
                
                // Draw wheel position
                if (wheel.wheelTransform != null)
                {
                    Gizmos.color = wheel.isPowered ? Color.red : Color.blue;
                    Gizmos.DrawWireSphere(wheel.wheelTransform.position, wheelRadius);
                }
            }
        }
        
        #endregion
    }
}