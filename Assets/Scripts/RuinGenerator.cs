using UnityEngine;
using System.Collections.Generic;
using TMPro;  // <<== ADD THIS

public class RuinGenerator : MonoBehaviour
{
    public GameObject Room_StartPrefab;
    public GameObject Room_Enemy1Prefab;
    public GameObject Room_Enemy2Prefab;
    public GameObject Room_TreasurePrefab;
    public GameObject Room_BossPrefab;
    public GameObject Room_HeartfirePrefab;
    public GameObject CorridorPrefab;

    public int totalRooms = 10;
    public int extraConnections = 2;
    [Header("Seed 0 = random (new every time)")]
    public int seed = 0; // <<== NEW

    [Header("UI")]
    public TMP_Text seedDisplay; // <<== ADD THIS

    private Dictionary<Vector2Int, GameObject> placedRooms = new Dictionary<Vector2Int, GameObject>();
    private List<Vector2Int> roomPositions = new List<Vector2Int>();
    private Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    void Start()
    {
        // Set seed for reproducible procedural generation
        if (seed == 0)
            seed = System.Environment.TickCount; // new random seed each play
        Random.InitState(seed);

        // Display seed at top center if reference is set
        if (seedDisplay != null)
        {
            seedDisplay.text = "Seed: " + seed.ToString();
        }

        GenerateDungeon();
    }

    void GenerateDungeon()
    {
        Vector2Int startPos = Vector2Int.zero;
        placedRooms[startPos] = Instantiate(Room_StartPrefab, GridToWorld(startPos), Quaternion.identity);
        roomPositions.Add(startPos);

        // Track parent for each room
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
            if (placedRooms.ContainsKey(nextPos)) continue; // skip if stuck

            // Pick room type
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

            // Place corridor between basePos and nextPos
            PlaceCorridor(basePos, nextPos);
        }

        // Optionally, add extra loops/connections between existing rooms
        int connectionsMade = 0;
        for (int i = 0; i < roomPositions.Count; i++)
        {
            foreach (var dir in directions)
            {
                Vector2Int neighbor = roomPositions[i] + dir;
                if (placedRooms.ContainsKey(neighbor))
                {
                    // Avoid duplicate/parent corridors
                    if (parentRoom.ContainsKey(neighbor) && parentRoom[neighbor] == roomPositions[i]) continue;
                    if (parentRoom.ContainsKey(roomPositions[i]) && parentRoom[roomPositions[i]] == neighbor) continue;

                    PlaceCorridor(roomPositions[i], neighbor);
                    connectionsMade++;
                    if (connectionsMade >= extraConnections) return;
                }
            }
        }
    }

    Vector3 GridToWorld(Vector2Int gridPos)
    {
        float spacing = 3f; // Make sure this matches your prefab size!
        return new Vector3(gridPos.x * spacing, gridPos.y * spacing, 0);
    }

    void PlaceCorridor(Vector2Int a, Vector2Int b)
    {
        float spacing = 3f; // Same as your GridToWorld
        float roomSize = 1f; // Match to your actual prefab size (try 1f if 2f is too short)

        Vector3 posA = GridToWorld(a);
        Vector3 posB = GridToWorld(b);

        if (a.y == b.y) // horizontal
        {
            float sign = Mathf.Sign(b.x - a.x);
            float corridorLength = spacing - roomSize;
            Vector3 corridorPos = posA + new Vector3(sign * (spacing / 2f), 0, 0);
            GameObject corridor = Instantiate(CorridorPrefab, corridorPos, Quaternion.identity);
            corridor.transform.localScale = new Vector3(corridorLength, 0.5f, 1f);
        }
        else if (a.x == b.x) // vertical
        {
            float sign = Mathf.Sign(b.y - a.y);
            float corridorLength = spacing - roomSize;
            Vector3 corridorPos = posA + new Vector3(0, sign * (spacing / 2f), 0);
            GameObject corridor = Instantiate(CorridorPrefab, corridorPos, Quaternion.identity);
            corridor.transform.localScale = new Vector3(0.5f, corridorLength, 1f);
        }
    }
}
