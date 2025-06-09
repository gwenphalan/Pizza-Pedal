using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;

public class BicycleCameraManager : MonoBehaviour
{
    [Header("Core References")]
    [Tooltip("The bicycle controller script on the player object.")]
    [SerializeField] private BicycleController bicycleController;

    [Header("Virtual Camera References")]
    [SerializeField] private CinemachineCamera thirdPersonCam;
    [SerializeField] private CinemachineCamera fpvCam;
    [SerializeField] private CinemachineCamera orbitCam;
    [SerializeField] private CinemachineCamera topDownCam;

    [Header("Dynamic Camera Settings")]
    [Tooltip("How much the FOV increases with speed in Third Person mode.")]
    [SerializeField] private float fovSpeedFactor = 5f;
    [Tooltip("How far the camera looks ahead into turns in Third Person mode.")]
    [SerializeField] private float lookAheadFactor = 2f;

    [Header("Recentering")]
    [Tooltip("If true, the camera view snaps back to its default position on release.")]
    [SerializeField] private bool resetViewOnRelease = true;

    // Private state
    private ThirdPersonFollow thirdPersonFollow;
    private float initialFov;

    public enum CameraMode { ThirdPerson, FirstPerson, Orbit, TopDown }
    private CameraMode currentMode = CameraMode.ThirdPerson;

    private void Awake()
    {
        if (bicycleController == null)
        {
            Debug.LogError("BicycleCameraManager: BicycleController reference is missing!");
            enabled = false;
            return;
        }

        if (thirdPersonCam != null)
        {
            thirdPersonFollow = thirdPersonCam.GetComponent<ThirdPersonFollow>();
            initialFov = thirdPersonCam.Lens.FieldOfView;
        }
    }

    private void Start()
    {
        SwitchMode(currentMode);
    }

    // This method is called by the PlayerInput component when the drag action is CANCELED.
    public void OnDragViewCanceled(InputAction.CallbackContext ctx)
    {
        if (ctx.canceled && resetViewOnRelease && currentMode == CameraMode.ThirdPerson)
        {
            var panAndTilt = thirdPersonCam.GetComponent<PanAndTilt>();
            if (panAndTilt != null)
            {
                panAndTilt.Recenter();
            }
        }
    }

    private void Update()
    {
        if (currentMode == CameraMode.ThirdPerson)
        {
            UpdateThirdPersonCam();
        }
    }

    private void UpdateThirdPersonCam()
    {
        if (thirdPersonCam == null || thirdPersonFollow == null) return;

        // Dynamic FOV based on speed
        float speedPercent = bicycleController.CurrentSpeed / bicycleController.maxSpeed;
        thirdPersonCam.Lens.FieldOfView = Mathf.Lerp(
            initialFov,
            initialFov + fovSpeedFactor,
            speedPercent
        );

        // Dynamic Look-Ahead based on turn input
        thirdPersonFollow.FollowOffset.x = Mathf.Lerp(
            thirdPersonFollow.FollowOffset.x,
            bicycleController.TurnInput * lookAheadFactor,
            Time.deltaTime * 5f
        );
    }

    // Public method to be called by UI buttons or other scripts to change modes.
    public void SwitchMode(CameraMode newMode)
    {
        currentMode = newMode;
        if (thirdPersonCam != null) thirdPersonCam.Priority.Value = (newMode == CameraMode.ThirdPerson) ? 10 : 0;
        if (fpvCam != null) fpvCam.Priority.Value = (newMode == CameraMode.FirstPerson) ? 10 : 0;
        if (orbitCam != null) orbitCam.Priority.Value = (newMode == CameraMode.Orbit) ? 10 : 0;
        if (topDownCam != null) topDownCam.Priority.Value = (newMode == CameraMode.TopDown) ? 10 : 0;
    }
}