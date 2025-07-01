using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RuinGenerator : MonoBehaviour
{
    public GameObject roomPrefab;
    public List<GameObject> corridorPrefabs; // 0 = Straight, 1 = Turn, 2 = T-Junction, 3 = Cross

    public int totalRooms = 5;
    public int extraConnections = 0;
    public int maxJunctions = 0;
    public float corridorLength = 10f;
    public Transform ruinContainer;

    private List<GameObject> placedRooms = new List<GameObject>();
    private List<Vector2Int> occupiedGrid = new List<Vector2Int>();
    private Dictionary<Vector2Int, GameObject> gridToRoom = new Dictionary<Vector2Int, GameObject>();
    private HashSet<(Vector2Int, Vector2Int)> connectedPairs = new HashSet<(Vector2Int, Vector2Int)>();

    private Vector2Int[] directions = new Vector2Int[]
    {
        new Vector2Int(0, 1),   // Up
        new Vector2Int(1, 0),   // Right
        new Vector2Int(0, -1),  // Down
        new Vector2Int(-1, 0),  // Left
    };

    private Vector2 worldGridStep; // how far rooms are spaced in world units

    void Start()
    {
        GenerateRuin();
    }

    void GenerateRuin()
    {
        // Use prefab size to define world spacing
        GameObject sizeSample = Instantiate(roomPrefab);
        RoomProfile profile = sizeSample.GetComponent<RoomProfile>();
        float roomWidth = profile.size.x;
        float roomHeight = profile.size.y;
        Destroy(sizeSample);

        worldGridStep = new Vector2(roomWidth + corridorLength, roomHeight + corridorLength);

        Vector2Int currentGridPos = Vector2Int.zero;
        Vector3 currentWorldPos = Vector3.zero;

        GameObject startRoom = Instantiate(roomPrefab, ruinContainer);
        startRoom.transform.position = currentWorldPos;
        placedRooms.Add(startRoom);
        MarkOccupiedCell(currentGridPos, startRoom);
        gridToRoom[currentGridPos] = startRoom;

        int placed = 1;
        int safetyCounter = 0;

        while (placed < totalRooms && safetyCounter < 500)
        {
            safetyCounter++;

            Vector2Int direction = directions[Random.Range(0, directions.Length)];
            Vector2Int nextGridPos = currentGridPos + direction;

            if (!CanPlaceRoom(nextGridPos))
                continue;

            Vector3 nextWorldPos = currentWorldPos + new Vector3(
                direction.x * worldGridStep.x,
                direction.y * worldGridStep.y,
                0f
            );

            GameObject newRoom = Instantiate(roomPrefab, ruinContainer);
            newRoom.transform.position = nextWorldPos;
            placedRooms.Add(newRoom);
            MarkOccupiedCell(nextGridPos, newRoom);
            gridToRoom[nextGridPos] = newRoom;

            connectedPairs.Add((currentGridPos, nextGridPos));
            connectedPairs.Add((nextGridPos, currentGridPos));

            Transform fromAnchor = FindAnchor(gridToRoom[currentGridPos], DirectionToAnchorName(direction));
            Transform toAnchor = FindAnchor(newRoom, DirectionToAnchorName(-direction));

            if (fromAnchor != null && toAnchor != null)
            {
                CreateCorridorBetweenAnchors(fromAnchor, toAnchor, direction);
                DisableDoorAtAnchor(gridToRoom[currentGridPos], DirectionToDoorName(direction));
                DisableDoorAtAnchor(newRoom, DirectionToDoorName(-direction));
            }

            currentGridPos = nextGridPos;
            currentWorldPos = nextWorldPos;
            placed++;
        }

        Debug.Log($"Placed {placed} rooms (attempts: {safetyCounter})");
    }

    void MarkOccupiedCell(Vector2Int gridPos, GameObject room)
    {
        occupiedGrid.Add(gridPos);
        gridToRoom[gridPos] = room;
    }

    bool CanPlaceRoom(Vector2Int gridPos)
    {
        return !occupiedGrid.Contains(gridPos);
    }

    void CreateCorridorBetweenAnchors(Transform fromAnchor, Transform toAnchor, Vector2Int direction)
    {
        Vector3 start = fromAnchor.position;
        Vector3 end = toAnchor.position;
        Vector3 dir = (end - start).normalized;

        float corridorLength = Vector3.Distance(start, end);
        float segmentLength = 1f;
        int segmentCount = Mathf.RoundToInt(corridorLength / segmentLength);

        Vector3 initialPosition = start + (dir * segmentLength * 0.5f);

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject segment = Instantiate(corridorPrefabs[0], ruinContainer);
            segment.transform.position = initialPosition + dir * segmentLength * i;

            if (Mathf.Abs(dir.x) > 0.1f)
                segment.transform.rotation = Quaternion.Euler(0, 0, 90);
            else
                segment.transform.rotation = Quaternion.identity;
        }
    }

    Transform FindAnchor(GameObject obj, string anchorName)
    {
        foreach (var t in obj.GetComponentsInChildren<Transform>())
            if (t.name == anchorName) return t;
        return null;
    }

    void DisableDoorAtAnchor(GameObject room, string doorName)
    {
        Transform door = room.transform.Find(doorName);
        if (door != null)
            door.gameObject.SetActive(false);
    }

    string DirectionToAnchorName(Vector2Int dir)
    {
        if (dir == Vector2Int.up) return "Anchor_Top";
        if (dir == Vector2Int.right) return "Anchor_Right";
        if (dir == Vector2Int.down) return "Anchor_Bottom";
        if (dir == Vector2Int.left) return "Anchor_Left";
        return "Anchor_Unknown";
    }

    string DirectionToDoorName(Vector2Int dir)
    {
        if (dir == Vector2Int.up) return "Door_Top";
        if (dir == Vector2Int.right) return "Door_Right";
        if (dir == Vector2Int.down) return "Door_Bottom";
        if (dir == Vector2Int.left) return "Door_Left";
        return "Door_Unknown";
    }
}
