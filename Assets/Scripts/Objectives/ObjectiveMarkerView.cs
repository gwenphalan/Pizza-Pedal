using UnityEngine;

/// <summary>
/// Handles the visual representation and behavior of an objective marker in the scene.
/// This component should be placed on the root of the objective marker prefab.
/// </summary>
public class ObjectiveMarkerView : MonoBehaviour
{
    [Header("Animation")]
    [Tooltip("The child transform holding the 3D mesh to animate. This will be rotated and bobbed.")]
    public Transform iconTransform;

    [Tooltip("How fast the icon rotates around the Y-axis.")]
    public float rotationSpeed = 50f;

    [Tooltip("How fast the icon bobs up and down.")]
    public float bobSpeed = 2f;

    [Tooltip("How high the icon bobs from its starting position.")]
    public float bobHeight = 0.25f;

    [Header("Collection Zone")]
    [Tooltip("The radius (X and Z) of the box-shaped collection zone.")]
    public float collectionRadius = 2f;

    [Tooltip("The height (Y) of the box-shaped collection zone.")]
    public float collectionHeight = 3f;

    private Vector3 _initialIconPosition;

    private void Awake()
    {
        if (iconTransform != null)
        {
            _initialIconPosition = iconTransform.localPosition;
        }
    }

    private void Update()
    {
        if (iconTransform == null) return;

        // Apply rotation
        iconTransform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

        // Apply bobbing animation
        float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        iconTransform.localPosition = _initialIconPosition + new Vector3(0, bobOffset, 0);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 1, 0, 0.5f); // Green, semi-transparent
        Vector3 zoneSize = new Vector3(collectionRadius * 2, collectionHeight, collectionRadius * 2);
        Gizmos.DrawWireCube(transform.position, zoneSize);
    }
}
