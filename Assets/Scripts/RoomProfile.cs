using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class RoomProfile : MonoBehaviour
{
    [Tooltip("How large each grid tile is in world units. Set to 1 if each Unity unit = 1 tile.")]
    public float gridUnitSize = 1f;

    [Tooltip("Calculated size of this room in grid tiles (width, height).")]
    public Vector2Int size; // Auto-calculated

    // This will be set by the generator when the room is placed.
    // Represents the bottom-left grid coordinate of the room.
    [HideInInspector] // Hide in Inspector as it's set programmatically
    public Vector2Int gridOrigin;

    void Awake()
    {
        CalculateSizeFromCollider();
    }

    void CalculateSizeFromCollider()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null)
        {
            Debug.LogWarning($"No BoxCollider2D found on {gameObject.name}. Cannot calculate room size.");
            return;
        }

        // Get the world size of the collider
        // Make sure the collider is set up to encompass the entire room area in world units.
        Vector2 worldSize = new Vector2(box.size.x * transform.lossyScale.x, box.size.y * transform.lossyScale.y);

        // Convert world size to grid units, rounding up to ensure full coverage.
        int gridWidth = Mathf.CeilToInt(worldSize.x / gridUnitSize);
        int gridHeight = Mathf.CeilToInt(worldSize.y / gridUnitSize);

        size = new Vector2Int(gridWidth, gridHeight);
        // Debug.Log($"{gameObject.name} auto-calculated size: {size} (World Size: {worldSize.x} x {worldSize.y})");
    }
}