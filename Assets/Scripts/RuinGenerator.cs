using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RuinGenerator : MonoBehaviour
{
    public List<GameObject> roomPrefabs;
    public List<GameObject> corridorPrefabs; // 0 = Straight, 1 = Turn, 2 = T-Junction, 3 = Cross

    public int totalRooms = 5;
    public int extraConnections = 0;
    public int maxJunctions = 0;
    public int fixedCorridorLength = 5;
    public int seed = -1; // -1 means random
    public Transform ruinContainer;

    private List<GameObject> placedRooms = new List<GameObject>();
    private List<Vector2Int> occupiedGrid = new List<Vector2Int>();
    private Dictionary<Vector2Int, GameObject> gridToRoom = new Dictionary<Vector2Int, GameObject>();
    private HashSet<(Vector2Int, Vector2Int)> connectedPairs = new HashSet<(Vector2Int, Vector2Int)>();

    private Vector2Int[] directions = new Vector2Int[]
    {
        new Vector2Int(0, 1),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(-1, 0),
    };

    void Start()
    {
        int actualSeed = seed >= 0 ? seed : System.Environment.TickCount;
        Random.InitState(actualSeed);
        Debug.Log($"Using seed: {actualSeed}");
        GenerateRuin();
    }

    void GenerateRuin()
    {
        GameObject startRoom = Instantiate(roomPrefabs[Random.Range(0, roomPrefabs.Count)], ruinContainer);
        RoomProfile startProfile = startRoom.GetComponent<RoomProfile>();
        Vector2Int startSize = startProfile.size;

        startRoom.transform.position = Vector3.zero;
        placedRooms.Add(startRoom);
        MarkOccupiedCells(Vector2Int.zero, startSize, startRoom);

        int placed = 1;
        int safetyCounter = 0;

        while (placed < totalRooms && safetyCounter < 500)
        {
            safetyCounter++;

            GameObject currentRoom = placedRooms[Random.Range(0, placedRooms.Count)];
            RoomProfile currentProfile = currentRoom.GetComponent<RoomProfile>();
            Vector2Int currentSize = currentProfile.size;

            Vector2Int direction = directions[Random.Range(0, directions.Length)];
            Debug.Log($"Trying to attach new room from: {currentRoom.name} in direction {direction}");

            GameObject newRoomPrefab = roomPrefabs[Random.Range(0, roomPrefabs.Count)];
            GameObject tempRoom = Instantiate(newRoomPrefab);
            RoomProfile tempProfile = tempRoom.GetComponent<RoomProfile>();
            Vector2Int newSize = tempProfile.size;
            Destroy(tempRoom);

            Transform fromAnchor = FindAnchor(currentRoom, DirectionToAnchorName(direction));
            GameObject newRoom = Instantiate(newRoomPrefab, ruinContainer);
            Transform toAnchor = FindAnchor(newRoom, DirectionToAnchorName(-direction));

            if (fromAnchor == null || toAnchor == null)
            {
                Destroy(newRoom);
                Debug.Log("Missing anchor — skipping.");
                continue;
            }

            Vector3 desiredToAnchorWorldPos = fromAnchor.position + (Vector3)(Vector2)direction * fixedCorridorLength;
            Vector3 actualToAnchorWorldPos = newRoom.transform.position + toAnchor.localPosition;
            Vector3 offsetToAlign = desiredToAnchorWorldPos - actualToAnchorWorldPos;
            newRoom.transform.position += offsetToAlign;

            // Calculate room's bottom-left corner to get the new grid position
            Vector2 roomCenter = new Vector2(newRoom.transform.position.x, newRoom.transform.position.y);
            Vector2 bottomLeft = roomCenter - new Vector2(newSize.x / 2f, newSize.y / 2f);
            Vector2Int nextGridPos = new Vector2Int(Mathf.RoundToInt(bottomLeft.x), Mathf.RoundToInt(bottomLeft.y));

            if (!CanPlaceRoom(nextGridPos, newSize))
            {
                Destroy(newRoom);
                Debug.Log($"Room at {nextGridPos} would overlap — skipping.");
                continue;
            }

            placedRooms.Add(newRoom);
            MarkOccupiedCells(nextGridPos, newSize, newRoom);
            connectedPairs.Add((nextGridPos, nextGridPos)); // self-entry to track occupancy

            CreateCorridorBetweenAnchors(fromAnchor, toAnchor, direction);
            DisableDoorAtAnchor(currentRoom, DirectionToDoorName(direction));
            DisableDoorAtAnchor(newRoom, DirectionToDoorName(-direction));

            placed++;
            Debug.Log($"Room placed at {nextGridPos}. Total placed: {placed}");
        }

        Debug.Log($"Finished. Placed {placed} rooms after {safetyCounter} attempts.");
    }

    void MarkOccupiedCells(Vector2Int basePos, Vector2Int size, GameObject room)
    {
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int pos = basePos + new Vector2Int(x, y);
                occupiedGrid.Add(pos);
                gridToRoom[pos] = room;
            }
    }

    bool CanPlaceRoom(Vector2Int basePos, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                if (occupiedGrid.Contains(basePos + new Vector2Int(x, y)))
                    return false;
        return true;
    }

    void CreateCorridorBetweenAnchors(Transform fromAnchor, Transform toAnchor, Vector2Int direction)
    {
        Vector3 start = fromAnchor.position;
        Vector3 end = toAnchor.position;
        Vector3 dir = (end - start).normalized;

        float segmentLength = 1f;
        float corridorLength = Vector3.Distance(start, end);
        int segmentCount = Mathf.CeilToInt(corridorLength / segmentLength);

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
