using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RuinGenerator : MonoBehaviour
{
    public GameObject roomPrefab;
    public List<GameObject> corridorPrefabs; // 0 = Straight, 1 = Turn, 2 = T-Junction, 3 = Cross

    public int totalRooms = 10;
    public int extraConnections = 2;
    public int maxJunctions = 3;
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

    void Start()
    {
        GenerateRuin();
    }

    void GenerateRuin()
    {
        Vector2Int currentPos = Vector2Int.zero;
        GameObject startRoom = Instantiate(roomPrefab, ruinContainer);
        startRoom.transform.position = Vector3.zero;
        placedRooms.Add(startRoom);
        occupiedGrid.Add(currentPos);
        gridToRoom[currentPos] = startRoom;

        for (int i = 1; i < totalRooms; i++)
        {
            Vector2Int direction = directions[Random.Range(0, directions.Length)];
            Vector2Int nextPos = currentPos + direction;

            if (occupiedGrid.Contains(nextPos))
            {
                i--;
                continue;
            }

            GameObject newRoom = Instantiate(roomPrefab, ruinContainer);
            newRoom.transform.position = new Vector3(nextPos.x * 16f, nextPos.y * 16f, 0);
            placedRooms.Add(newRoom);
            occupiedGrid.Add(nextPos);
            gridToRoom[nextPos] = newRoom;

            connectedPairs.Add((currentPos, nextPos));
            connectedPairs.Add((nextPos, currentPos));

            Transform fromAnchor = FindAnchor(startRoom, DirectionToAnchorName(direction));
            Transform toAnchor = FindAnchor(newRoom, DirectionToAnchorName(-direction));
            if (fromAnchor != null && toAnchor != null)
            {
                CreateCorridorBetweenAnchors(fromAnchor, toAnchor, direction);
                DisableDoorAtAnchor(startRoom, DirectionToDoorName(direction));
                DisableDoorAtAnchor(newRoom, DirectionToDoorName(-direction));
            }

            currentPos = nextPos;
            startRoom = newRoom;
        }

        AddExtraConnections();
        AddJunctions(); // NEW
    }

    void AddExtraConnections()
    {
        int added = 0;
        int attempts = 0;

        while (added < extraConnections && attempts < 100)
        {
            Vector2Int a = occupiedGrid[Random.Range(0, occupiedGrid.Count)];
            Vector2Int dir = directions[Random.Range(0, directions.Length)];
            Vector2Int b = a + dir;

            if (occupiedGrid.Contains(b) && !connectedPairs.Contains((a, b)))
            {
                GameObject roomA = gridToRoom[a];
                GameObject roomB = gridToRoom[b];

                Transform anchorA = FindAnchor(roomA, DirectionToAnchorName(dir));
                Transform anchorB = FindAnchor(roomB, DirectionToAnchorName(-dir));

                if (anchorA != null && anchorB != null)
                {
                    connectedPairs.Add((a, b));
                    connectedPairs.Add((b, a));

                    CreateCorridorBetweenAnchors(anchorA, anchorB, dir);
                    DisableDoorAtAnchor(roomA, DirectionToDoorName(dir));
                    DisableDoorAtAnchor(roomB, DirectionToDoorName(-dir));
                    added++;
                }
            }

            attempts++;
        }
    }

    void AddJunctions()
    {
        // Placeholder: In future, detect empty grid cells between 3–4 rooms or corridors
        // and spawn a junction prefab accordingly.
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
            GameObject segment = Instantiate(corridorPrefabs[0], ruinContainer); // straight for now
            segment.transform.position = initialPosition + dir * segmentLength * i;

            if (Mathf.Abs(dir.x) > 0.1f)
                segment.transform.rotation = Quaternion.Euler(0, 0, 90); // horizontal
            else
                segment.transform.rotation = Quaternion.identity; // vertical
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
