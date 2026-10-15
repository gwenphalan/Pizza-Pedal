using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages the pre-run route planning screen, allowing the player to order their destinations
/// to find the most efficient route.
/// </summary>
public class RoutePlannerUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The parent transform for the waypoint list items.")]
    [SerializeField] private Transform waypointListContainer;

    [Tooltip("The UI prefab for a single waypoint item in the list.")]
    [SerializeField] private GameObject waypointItemPrefab;

    [Tooltip("The button to confirm the final route.")]
    [SerializeField] private Button confirmButton;

    [Tooltip("The text field to display the total route distance.")]
    [SerializeField] private TextMeshProUGUI totalDistanceText;
    
    private List<Vector3> _destinations;
    
    private void Start()
    {
        confirmButton.onClick.AddListener(OnConfirmButtonClicked);
        gameObject.SetActive(false); // Start disabled
    }

    /// <summary>
    /// Populates the UI with a list of destinations and shows the planner.
    /// </summary>
    /// <param name="destinations">A list of world positions for the destinations.</param>
    public void Populate(List<Vector3> destinations)
    {
        _destinations = destinations;
        
        // Clear previous items
        foreach (Transform child in waypointListContainer)
        {
            Destroy(child.gameObject);
        }

        // Create new items
        for (int i = 0; i < destinations.Count; i++)
        {
            var item = Instantiate(waypointItemPrefab, waypointListContainer);
            // TODO: Set up item with name, icon, and drag-and-drop handlers
        }
        
        UpdateRouteMetrics();
        gameObject.SetActive(true);
    }
    
    /// <summary>
    /// Called when the order of waypoints is changed in the UI.
    /// </summary>
    private void OnOrderChanged()
    {
        // TODO: Get the new order of destinations from the UI
        UpdateRouteMetrics();
    }

    /// <summary>
    /// Recalculates the route metrics and updates the UI text.
    /// </summary>
    private void UpdateRouteMetrics()
    {
        // TODO: Implement metric calculation call
        float distance = NavigationManager.Instance.CalculateRouteDistance(_destinations);
        totalDistanceText.text = $"Total Distance: {distance:F1}m";
    }

    /// <summary>
    /// Called when the confirm button is clicked.
    /// </summary>
    private void OnConfirmButtonClicked()
    {
        NavigationManager.Instance.RequestPath(_destinations);
        gameObject.SetActive(false);
    }
} 