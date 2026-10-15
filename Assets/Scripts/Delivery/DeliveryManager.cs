using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the overall delivery system, including objectives, timers, and player progress.
/// </summary>
public class DeliveryManager : MonoBehaviour
{
    [System.Serializable]
    public class DeliveryObjective {
        public string name = "Billy's House";
        public string address = "123 Main St";
        public Transform markerPosition;
    }
    public ObjectiveManager objectiveManager;
    public ObjectiveMarkerData dropoffMarkerData;
    public ObjectiveMarkerData pickupMarkerData;
    public Transform pickupMarkerPosition;
    public RoutePlannerUI routePlannerUI;

    [Header("Path Colors")]
    public Color pickupPathColor = Color.yellow;
    public Color dropoffPathColor = Color.cyan;

    public List<DeliveryObjective> availableObjectives = new List<DeliveryObjective>();

    private List<DeliveryObjective> activeDeliveries = new List<DeliveryObjective>();
    public int maxActiveObjectives = 3;

    private Objective _pickupObjective;
    private DeliveryObjective _currentObjective;

    public Transform destinationMarkerPosition;

    #region Unity Lifecycle
    
    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// </summary>
    private void Awake()
    {
        // if no objective manager, get it from the scene
        if (objectiveManager == null) {
            objectiveManager = FindFirstObjectByType<ObjectiveManager>();
        }
    }

    /// <summary>
    /// Start is called before the first frame update.
    /// </summary>
    private void Start()
    {
        // if no active objectives, init the pickup objective
        if (activeDeliveries.Count == 0) {
            _pickupObjective = objectiveManager.CreateObjective(pickupMarkerPosition.position, pickupMarkerData, OnPickupObjectiveComplete);
            destinationMarkerPosition = pickupMarkerPosition;
            NavigationManager.Instance.RequestPath(new List<Vector3> { destinationMarkerPosition.position }, pickupPathColor);
        }
    }

    /// <summary>
    /// Update is called once per frame.
    /// </summary>
    private void Update()
    {
        // This is no longer needed now that we are using events.
    }

    private void OnPickupObjectiveComplete(Objective objective) {
        _pickupObjective = null;
        
        activeDeliveries.Clear();

        // Pick a random amount of objectives from the available objectives, and add them to the activeDeliveries
        int numToPickup = Random.Range(1, maxActiveObjectives + 1);
        for (int i = 0; i < numToPickup; i++) {
            DeliveryObjective deliveryObjective = availableObjectives[Random.Range(0, availableObjectives.Count)];
            activeDeliveries.Add(deliveryObjective);
        }

        // Create the marker for the first active delivery and set it as the current objective
        if (activeDeliveries.Count > 0)
        {
            _currentObjective = activeDeliveries[0];
            destinationMarkerPosition = _currentObjective.markerPosition;
            objectiveManager.CreateObjective(destinationMarkerPosition.position, dropoffMarkerData, OnDropoffObjectiveComplete);
            
            // Request the path for the first destination
            NavigationManager.Instance.RequestPath(new List<Vector3> { destinationMarkerPosition.position }, dropoffPathColor);
        }
    }

    private void OnDropoffObjectiveComplete(Objective objective) {
        // Remove the completed objective from the active deliveries
        if (_currentObjective != null)
        {
            activeDeliveries.Remove(_currentObjective);
        }
        
        _currentObjective = null;

        // if there are more active deliveries, create a new marker for the next one
        if (activeDeliveries.Count > 0) {
            DeliveryObjective deliveryObjective = activeDeliveries[0];
            Objective newObjective = objectiveManager.CreateObjective(deliveryObjective.markerPosition.position, dropoffMarkerData, OnDropoffObjectiveComplete);
            destinationMarkerPosition = deliveryObjective.markerPosition;
            _currentObjective = deliveryObjective;
            
            // Request a path to the next destination
            NavigationManager.Instance.RequestPath(new List<Vector3> { destinationMarkerPosition.position }, dropoffPathColor);
        }
        // If there are no more active deliveries, create a new pickup objective
        else {
            _pickupObjective = objectiveManager.CreateObjective(pickupMarkerPosition.position, pickupMarkerData, OnPickupObjectiveComplete);
            destinationMarkerPosition = pickupMarkerPosition;

            // Request a path back to the pickup location
            NavigationManager.Instance.RequestPath(new List<Vector3> { destinationMarkerPosition.position }, pickupPathColor);
        }
    }

    // 

    #endregion
} 