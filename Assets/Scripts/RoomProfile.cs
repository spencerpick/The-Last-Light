using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class RoomProfile : MonoBehaviour
{
    [Tooltip("How large each grid tile is in world units. Set to 1 if each Unity unit = 1 tile.")]
    public float gridUnitSize = 1f;

    [Tooltip("Calculated size of this room in grid tiles.")]
    public Vector2Int size; // Auto-calculated

    void Awake()
    {
        CalculateSizeFromCollider();
    }

    void CalculateSizeFromCollider()
    {
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box == null)
        {
            Debug.LogWarning($"No BoxCollider2D found on {gameObject.name}");
            return;
        }

        Vector2 localSize = box.size;
        Vector3 lossyScale = transform.lossyScale;

        float worldWidth = localSize.x * lossyScale.x;
        float worldHeight = localSize.y * lossyScale.y;

        int gridWidth = Mathf.CeilToInt(worldWidth / gridUnitSize);
        int gridHeight = Mathf.CeilToInt(worldHeight / gridUnitSize);

        size = new Vector2Int(gridWidth, gridHeight);
        Debug.Log($"{gameObject.name} auto-calculated size: {size} (World Size: {worldWidth} x {worldHeight})");
    }
}
