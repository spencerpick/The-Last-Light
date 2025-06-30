using UnityEngine;
using System.Collections.Generic;
using TMPro;

public class RuinGenerator : MonoBehaviour
{
    public GameObject Room_StartPrefab;
    public GameObject Room_Enemy1Prefab;
    public GameObject Room_Enemy2Prefab;
    public GameObject Room_TreasurePrefab;
    public GameObject Room_BossPrefab;
    public GameObject Room_HeartfirePrefab;
    public GameObject CorridorPrefab;
    public GameObject PlayerPrefab;  // <--- ADD THIS

    public int totalRooms = 10;
    public int extraConnections = 2;

    [Header("Seed 0 = random (new every time)")]
    public int seed = 0;

    [Header("UI")]
    public TMP_Text seedDisplay;

    private Dictionary<Vector2Int, GameObject> placedRooms = new Dictionary<Vector2Int, GameObject>();
    private List<Vector2Int> roomPositions = new List<Vector2Int>();
    private Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    // To remember where the start room is
    private Vector3 startRoomWorldPos;

    void Start()
    {
        if (seed == 0)
            seed = System.Environment.TickCount;
        Random.InitState(seed);

        if (seedDisplay != null)
        {
            seedDisplay.text = "Seed: " + seed.ToString();
        }

        GenerateDungeon();
    }

    void GenerateDungeon()
    {
        Vector2Int startPos = Vector2Int.zero;
        var startRoom = Instantiate(Room_StartPrefab, GridToWorld(startPos), Quaternion.identity);
        placedRooms[startPos] = startRoom;
        roomPositions.Add(startPos);
        startRoomWorldPos = GridToWorld(startPos); // Save for player spawn

        Dictionary<Vector2Int, Vector2Int> parentRoom = new Dictionary<Vector2Int, Vector2Int>();
        parentRoom[startPos] = startPos;

        for (int i = 1; i < totalRooms; i++)
        {
            Vector2Int basePos = roomPositions[Random.Range(0, roomPositions.Count)];
            Vector2Int dir = directions[Random.Range(0, directions.Length)];
            Vector2Int nextPos = basePos + dir;
            int attempts = 0;
            while (placedRooms.ContainsKey(nextPos) && attempts < 10)
            {
                basePos = roomPositions[Random.Range(0, roomPositions.Count)];
                dir = directions[Random.Range(0, directions.Length)];
                nextPos = basePos + dir;
                attempts++;
            }
            if (placedRooms.ContainsKey(nextPos)) continue;

            GameObject prefab;
            if (i == totalRooms - 1)
                prefab = Room_HeartfirePrefab;
            else if (i == totalRooms - 2)
                prefab = Room_BossPrefab;
            else
            {
                int roll = Random.Range(0, 3);
                if (roll == 0) prefab = Room_Enemy1Prefab;
                else if (roll == 1) prefab = Room_Enemy2Prefab;
                else prefab = Room_TreasurePrefab;
            }

            placedRooms[nextPos] = Instantiate(prefab, GridToWorld(nextPos), Quaternion.identity);
            roomPositions.Add(nextPos);
            parentRoom[nextPos] = basePos;

            PlaceCorridor(basePos, nextPos);
        }

        int connectionsMade = 0;
        for (int i = 0; i < roomPositions.Count; i++)
        {
            foreach (var dir in directions)
            {
                Vector2Int neighbor = roomPositions[i] + dir;
                if (placedRooms.ContainsKey(neighbor))
                {
                    if (parentRoom.ContainsKey(neighbor) && parentRoom[neighbor] == roomPositions[i]) continue;
                    if (parentRoom.ContainsKey(roomPositions[i]) && parentRoom[roomPositions[i]] == neighbor) continue;

                    PlaceCorridor(roomPositions[i], neighbor);
                    connectionsMade++;
                    if (connectionsMade >= extraConnections) break;
                }
            }
        }

        // Remove room walls based on surrounding connections
        foreach (var pos in roomPositions)
        {
            GameObject room = placedRooms[pos];
            foreach (var dir in directions)
            {
                Vector2Int neighbor = pos + dir;
                if (placedRooms.ContainsKey(neighbor))
                {
                    string wallToDisable = DirectionToWallName(dir);
                    DisableWall(room, wallToDisable);
                }
            }
        }

        // --- SPAWN THE PLAYER in the center of the Start Room
        if (PlayerPrefab != null)
        {
            GameObject player = Instantiate(PlayerPrefab, startRoomWorldPos, Quaternion.identity);

            // Check what room the player is inside and manually call EnterRoom
            Collider2D[] overlapping = Physics2D.OverlapCircleAll(startRoomWorldPos, 0.1f);
            foreach (var col in overlapping)
            {
                RoomTrigger room = col.GetComponent<RoomTrigger>();
                if (room != null)
                {
                    GameManager.Instance.EnterRoom(room);
                    break;
                }
            }
        }

        else
        {
            Debug.LogWarning("PlayerPrefab not assigned in RuinGenerator.");
        }
    }

    Vector3 GridToWorld(Vector2Int gridPos)
    {
        float spacing = 3f;
        return new Vector3(gridPos.x * spacing, gridPos.y * spacing, 0);
    }

    void PlaceCorridor(Vector2Int a, Vector2Int b)
    {
        float spacing = 3f;
        float roomSize = 1f;

        Vector3 posA = GridToWorld(a);
        Vector3 posB = GridToWorld(b);
        GameObject corridor = null;

        if (a.y == b.y) // horizontal
        {
            float sign = Mathf.Sign(b.x - a.x);
            float corridorLength = spacing - roomSize;
            Vector3 corridorPos = posA + new Vector3(sign * (spacing / 2f), 0, 0);
            corridor = Instantiate(CorridorPrefab, corridorPos, Quaternion.identity);
            corridor.transform.localScale = new Vector3(corridorLength, 0.5f, 1f);

            DisableWall(corridor, "Wall_Left");
            DisableWall(corridor, "Wall_Right");
        }
        else if (a.x == b.x) // vertical
        {
            float sign = Mathf.Sign(b.y - a.y);
            float corridorLength = spacing - roomSize;
            Vector3 corridorPos = posA + new Vector3(0, sign * (spacing / 2f), 0);
            corridor = Instantiate(CorridorPrefab, corridorPos, Quaternion.identity);
            corridor.transform.localScale = new Vector3(0.5f, corridorLength, 1f);

            DisableWall(corridor, "Wall_Top");
            DisableWall(corridor, "Wall_Bottom");
        }
    }

    void DisableWall(GameObject obj, string wallName)
    {
        Transform wall = obj.transform.Find(wallName);
        if (wall != null)
        {
            wall.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"Wall '{wallName}' not found on {obj.name}");
        }
    }

    string DirectionToWallName(Vector2Int dir)
    {
        if (dir == Vector2Int.up) return "Wall_Top";
        if (dir == Vector2Int.down) return "Wall_Bottom";
        if (dir == Vector2Int.left) return "Wall_Left";
        if (dir == Vector2Int.right) return "Wall_Right";
        return "";
    }
}
