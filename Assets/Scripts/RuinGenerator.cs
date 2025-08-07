// FULL RuinGenerator.cs – FORBIDDEN ADJACENCY FULL EDGE CHECK SUPPORT

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[System.Serializable]
public class RoomTypeDefinition
{
    public string type;
    public int minCount;
    public int maxCount = -1;
    public float weight;
}

[Serializable]
public struct ForbiddenAdjacency
{
    public string typeA;
    public string typeB;
}

public class RuinGenerator : MonoBehaviour
{
    [Header("Room Prefabs")]
    public GameObject starterRoomPrefab;
    public List<GameObject> roomPrefabs;
    public List<GameObject> corridorPrefabs;

    [Header("Generation Settings")]
    public int totalRooms = 5;
    public int extraConnections = 0;
    public int maxJunctions = 0;
    public int minCorridorLength = 6;
    public int seed = -1;
    [SerializeField] private int actualSeedUsed;

    [Header("Scene References")]
    public Transform ruinContainer;
    public GameObject playerPrefab;

    [Header("Room Type Settings")]
    public List<RoomTypeDefinition> roomTypeDefinitions;

    [Header("Adjacency Rules")]
    public List<ForbiddenAdjacency> forbiddenAdjacencies;

    private List<GameObject> placedRooms = new List<GameObject>();
    private Dictionary<Vector2Int, GameObject> gridOccupancyMap = new Dictionary<Vector2Int, GameObject>();
    private Dictionary<GameObject, Vector2Int> roomToGridOrigin = new Dictionary<GameObject, Vector2Int>();
    private HashSet<(Vector2Int, Vector2Int)> connectedPairs = new HashSet<(Vector2Int, Vector2Int)>();

    private Vector2Int[] directions = new Vector2Int[] {
        new Vector2Int(0, 1),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(-1, 0),
    };

    private Dictionary<string, int> placedTypeCounts = new Dictionary<string, int>();
    private List<string> plannedRoomTypes = new List<string>();

    void Start()
    {
        actualSeedUsed = seed >= 0 ? seed : System.Environment.TickCount;
        UnityEngine.Random.InitState(actualSeedUsed);
        Debug.Log($"Using seed: {actualSeedUsed}");
        PreparePlannedRoomTypes();
        GenerateRuin();
    }

    void PreparePlannedRoomTypes()
    {
        plannedRoomTypes.Clear();
        placedTypeCounts.Clear();

        int totalMin = roomTypeDefinitions.Sum(r => r.minCount);
        if (totalMin > totalRooms)
        {
            Debug.LogError($"totalRooms ({totalRooms}) is less than sum of minCounts ({totalMin})");
            return;
        }
        foreach (var def in roomTypeDefinitions)
        {
            placedTypeCounts[def.type] = 0;
            for (int i = 0; i < def.minCount; i++)
                plannedRoomTypes.Add(def.type);
        }
        int remaining = totalRooms - totalMin;
        for (int i = 0; i < remaining; i++)
        {
            var eligible = roomTypeDefinitions
                .Where(def => def.maxCount == -1 || plannedRoomTypes.Count(x => x == def.type) < def.maxCount)
                .ToList();
            if (eligible.Count == 0) break;
            float totalWeight = eligible.Sum(e => e.weight);
            float rand = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0;
            foreach (var def in eligible)
            {
                cumulative += def.weight;
                if (rand <= cumulative)
                {
                    plannedRoomTypes.Add(def.type);
                    break;
                }
            }
        }
        Shuffle(plannedRoomTypes);
        Debug.Log("Planned Room Types: " + string.Join(", ", plannedRoomTypes));
    }

    GameObject GetRandomRoomPrefabForType(string type)
    {
        var matching = roomPrefabs.Where(p => p.CompareTag(type)).ToList();
        if (matching.Count > 0)
            return matching[UnityEngine.Random.Range(0, matching.Count)];
        return null;
    }

    bool IsForbiddenAdjacent(string typeA, string typeB)
    {
        foreach (var pair in forbiddenAdjacencies)
        {
            if ((pair.typeA == typeA && pair.typeB == typeB) ||
                (pair.typeA == typeB && pair.typeB == typeA))
                return true;
        }
        return false;
    }

    // The magic: checks ALL edge cells of candidate room for forbidden neighbors
    bool HasForbiddenNeighbor(Vector2Int candidateOrigin, Vector2Int size, string candidateType)
    {
        foreach (var dir in directions)
        {
            List<Vector2Int> edgeCells = GetEdgeCells(candidateOrigin, size, dir);
            foreach (var cell in edgeCells)
            {
                Vector2Int neighbor = cell + dir;
                if (gridOccupancyMap.TryGetValue(neighbor, out GameObject neighborObj))
                {
                    if (roomToGridOrigin.ContainsKey(neighborObj))
                    {
                        string neighborType = neighborObj.tag;
                        if (IsForbiddenAdjacent(candidateType, neighborType))
                        {
                            // Debug.Log($"Blocked: {candidateType} at {candidateOrigin} has forbidden neighbor {neighborType} at {neighbor}");
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    // Gets all grid cells along a specific edge of a room
    List<Vector2Int> GetEdgeCells(Vector2Int origin, Vector2Int size, Vector2Int direction)
    {
        List<Vector2Int> edge = new List<Vector2Int>();
        if (direction == Vector2Int.up)
            for (int x = 0; x < size.x; x++) edge.Add(new Vector2Int(origin.x + x, origin.y + size.y - 1));
        else if (direction == Vector2Int.down)
            for (int x = 0; x < size.x; x++) edge.Add(new Vector2Int(origin.x + x, origin.y));
        else if (direction == Vector2Int.left)
            for (int y = 0; y < size.y; y++) edge.Add(new Vector2Int(origin.x, origin.y + y));
        else if (direction == Vector2Int.right)
            for (int y = 0; y < size.y; y++) edge.Add(new Vector2Int(origin.x + size.x - 1, origin.y + y));
        return edge;
    }

    void GenerateRuin()
    {
        GameObject startRoom = Instantiate(starterRoomPrefab, ruinContainer);
        RoomProfile startProfile = startRoom.GetComponentInChildren<RoomProfile>();
        if (startProfile == null)
        {
            Debug.LogError("Starter room prefab (or its children) must have a RoomProfile component!");
            return;
        }

        Vector2Int startGridOrigin = Vector2Int.zero;
        startRoom.transform.position = WorldPosFromGridOrigin(startGridOrigin, startProfile.gridUnitSize);

        placedRooms.Add(startRoom);
        roomToGridOrigin[startRoom] = startGridOrigin;
        MarkOccupiedCells(startGridOrigin, startProfile.size, startRoom);

        Transform playerSpawn = null;
        foreach (Transform t in startRoom.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Player_Spawn")
            {
                playerSpawn = t;
                break;
            }
        }

        if (playerSpawn != null && playerPrefab != null)
        {
            Instantiate(playerPrefab, playerSpawn.position, Quaternion.identity);
            Debug.Log("Player spawned successfully!");
        }
        else
        {
            Debug.LogWarning("Player spawn point or prefab not found! Player will not be spawned.");
            if (playerSpawn == null) Debug.LogWarning("Player_Spawn Transform was null.");
            if (playerPrefab == null) Debug.LogWarning("Player Prefab was null.");
        }

        int placedCount = 1;
        int safetyCounter = 0;
        List<string> remainingTypes = new List<string>(plannedRoomTypes);

        while (placedCount < totalRooms && safetyCounter < 1000)
        {
            safetyCounter++;
            GameObject currentRoom = placedRooms[UnityEngine.Random.Range(0, placedRooms.Count)];
            RoomProfile currentProfile = currentRoom.GetComponentInChildren<RoomProfile>();
            if (currentProfile == null) continue;
            Vector2Int currentRoomGridOrigin = GetRoomGridOrigin(currentRoom);

            List<Vector2Int> dirList = new List<Vector2Int>(directions);
            Shuffle(dirList);
            bool placedRoomThisIteration = false;

            List<string> candidateTypes = roomTypeDefinitions
                .Where(def => def.maxCount == -1 || placedTypeCounts[def.type] < def.maxCount)
                .Select(def => def.type).ToList();
            Shuffle(candidateTypes);

            foreach (Vector2Int direction in dirList)
            {
                foreach (string tryType in candidateTypes)
                {
                    GameObject newRoomPrefab = GetRandomRoomPrefabForType(tryType);
                    if (newRoomPrefab == null) continue;

                    GameObject tempRoom = Instantiate(newRoomPrefab);
                    RoomProfile tempProfile = tempRoom.GetComponentInChildren<RoomProfile>();
                    if (tempProfile == null) { Destroy(tempRoom); continue; }
                    Vector2Int newRoomSize = tempProfile.size;

                    Transform currentRoomExitAnchor = FindAnchor(currentRoom, DirectionToAnchorName(direction));
                    Transform newRoomEntryAnchor = FindAnchor(tempRoom, DirectionToAnchorName(-direction));
                    if (currentRoomExitAnchor == null || newRoomEntryAnchor == null) { Destroy(tempRoom); continue; }

                    tempRoom.transform.position = Vector3.zero;
                    Vector3 offsetFromPivotToAnchor = newRoomEntryAnchor.position;
                    Vector3 desiredNewRoomEntryAnchorWorldPos = currentRoomExitAnchor.position + (Vector3)(Vector2)direction * minCorridorLength * tempProfile.gridUnitSize;
                    Vector3 proposedNewRoomWorldPos = desiredNewRoomEntryAnchorWorldPos - offsetFromPivotToAnchor;
                    Vector2Int proposedNextGridOrigin = GridOriginFromWorldPos(proposedNewRoomWorldPos, tempProfile.gridUnitSize);

                    if (!CanPlaceRoom(proposedNextGridOrigin, newRoomSize))
                    {
                        Destroy(tempRoom);
                        continue;
                    }

                    // FULL EDGE ADJACENCY CHECK
                    if (HasForbiddenNeighbor(proposedNextGridOrigin, newRoomSize, tryType))
                    {
                        Destroy(tempRoom);
                        continue;
                    }

                    GameObject newRoom = Instantiate(newRoomPrefab, ruinContainer);
                    newRoom.transform.position = proposedNewRoomWorldPos;

                    placedRooms.Add(newRoom);
                    roomToGridOrigin[newRoom] = proposedNextGridOrigin;
                    MarkOccupiedCells(proposedNextGridOrigin, newRoomSize, newRoom);

                    connectedPairs.Add((currentRoomGridOrigin, proposedNextGridOrigin));
                    connectedPairs.Add((proposedNextGridOrigin, currentRoomGridOrigin));

                    CreateCorridorBetweenAnchors(currentRoomExitAnchor, FindAnchor(newRoom, DirectionToAnchorName(-direction)), direction, newRoom.GetComponentInChildren<RoomProfile>().gridUnitSize);
                    DisableDoorAtAnchor(currentRoom, DirectionToDoorName(direction));
                    DisableDoorAtAnchor(newRoom, DirectionToDoorName(-direction));

                    Destroy(tempRoom);

                    placedTypeCounts[tryType] += 1;
                    Debug.Log($"[PCG] Placed {tryType}: now {placedTypeCounts[tryType]} (max {roomTypeDefinitions.First(def => def.type == tryType).maxCount})");

                    placedCount++;
                    placedRoomThisIteration = true;
                    break;
                }
                if (placedRoomThisIteration) break;
            }
            if (!placedRoomThisIteration)
            {
                Debug.Log($"Failed to place a room from {currentRoom.name} in any direction. Retrying.");
                continue;
            }
        }

        AddExtraConnections();
        Debug.Log($"Placed {placedCount} rooms (attempts: {safetyCounter})");
        Debug.Log("Final room type counts: " + string.Join(", ", placedTypeCounts.Select(kv => $"{kv.Key}:{kv.Value}")));
    }

    void AddExtraConnections()
    {
        int extrasAdded = 0;
        for (int i = 0; i < placedRooms.Count; i++)
        {
            GameObject roomA = placedRooms[i];
            RoomProfile profileA = roomA.GetComponentInChildren<RoomProfile>();
            if (profileA == null) continue;
            Vector2Int roomAGridOrigin = GetRoomGridOrigin(roomA);

            for (int j = i + 1; j < placedRooms.Count; j++)
            {
                GameObject roomB = placedRooms[j];
                RoomProfile profileB = roomB.GetComponentInChildren<RoomProfile>();
                if (profileB == null) continue;
                Vector2Int roomBGridOrigin = GetRoomGridOrigin(roomB);

                if (connectedPairs.Contains((roomAGridOrigin, roomBGridOrigin))) continue;

                foreach (Vector2Int dir in directions)
                {
                    Transform fromAnchor = FindAnchor(roomA, DirectionToAnchorName(dir));
                    Transform toAnchor = FindAnchor(roomB, DirectionToAnchorName(-dir));
                    if (fromAnchor == null || toAnchor == null) continue;

                    float tolerance = 0.1f * profileA.gridUnitSize;
                    bool isAxiallyAligned = false;

                    if (Mathf.Abs(fromAnchor.position.x - toAnchor.position.x) < tolerance)
                    {
                        if ((dir == Vector2Int.up && fromAnchor.position.y < toAnchor.position.y) ||
                            (dir == Vector2Int.down && fromAnchor.position.y > toAnchor.position.y))
                            isAxiallyAligned = true;
                    }
                    else if (Mathf.Abs(fromAnchor.position.y - toAnchor.position.y) < tolerance)
                    {
                        if ((dir == Vector2Int.right && fromAnchor.position.x < toAnchor.position.x) ||
                            (dir == Vector2Int.left && fromAnchor.position.x > toAnchor.position.x))
                            isAxiallyAligned = true;
                    }

                    if (!isAxiallyAligned) continue;

                    float requiredDistance = minCorridorLength * profileA.gridUnitSize;
                    float actualDistance = Vector3.Distance(fromAnchor.position, toAnchor.position);

                    if (Mathf.Abs(actualDistance - requiredDistance) < 0.5f * profileA.gridUnitSize)
                    {
                        bool pathClear = CheckCorridorPathClear(fromAnchor.position, toAnchor.position, profileA.gridUnitSize, roomA, roomB);
                        if (pathClear)
                        {
                            CreateCorridorBetweenAnchors(fromAnchor, toAnchor, dir, profileA.gridUnitSize);
                            DisableDoorAtAnchor(roomA, DirectionToDoorName(dir));
                            DisableDoorAtAnchor(roomB, DirectionToDoorName(-dir));
                            connectedPairs.Add((roomAGridOrigin, roomBGridOrigin));
                            connectedPairs.Add((roomBGridOrigin, roomAGridOrigin));
                            extrasAdded++;
                            if (extrasAdded >= extraConnections) return;
                        }
                    }
                }
            }
        }
    }

    bool CheckCorridorPathClear(Vector3 startWorld, Vector3 endWorld, float gridUnitSize, GameObject roomA, GameObject roomB)
    {
        Vector3 directionVector = (endWorld - startWorld).normalized;
        float segmentLength = gridUnitSize;
        Vector3 checkStartWorld = startWorld + directionVector * (segmentLength * 0.5f);
        Vector3 checkEndWorld = endWorld - directionVector * (segmentLength * 0.5f);
        int numSteps = Mathf.RoundToInt(Vector3.Distance(checkStartWorld, checkEndWorld) / segmentLength);
        if (Vector3.Distance(checkStartWorld, checkEndWorld) > 0.01f && numSteps == 0) numSteps = 1;

        for (int i = 0; i < numSteps; i++)
        {
            Vector3 currentCheckPos = checkStartWorld + directionVector * (i * segmentLength);
            Vector2Int checkGridPos = GridOriginFromWorldPos(currentCheckPos, gridUnitSize);
            if (gridOccupancyMap.TryGetValue(checkGridPos, out GameObject occupiedObject))
            {
                if (occupiedObject != roomA && occupiedObject != roomB)
                    return false;
            }
        }
        return true;
    }

    void MarkOccupiedCells(Vector2Int baseGridOrigin, Vector2Int size, GameObject roomParent)
    {
        for (int x = baseGridOrigin.x; x < baseGridOrigin.x + size.x; x++)
        {
            for (int y = baseGridOrigin.y; y < baseGridOrigin.y + size.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                gridOccupancyMap[cell] = roomParent;
            }
        }
    }

    bool CanPlaceRoom(Vector2Int proposedBaseGridOrigin, Vector2Int size)
    {
        for (int x = proposedBaseGridOrigin.x; x < proposedBaseGridOrigin.x + size.x; x++)
        {
            for (int y = proposedBaseGridOrigin.y; y < proposedBaseGridOrigin.y + size.y; y++)
            {
                if (gridOccupancyMap.ContainsKey(new Vector2Int(x, y)))
                    return false;
            }
        }
        return true;
    }

    void Shuffle<T>(List<T> list)
    {
        int n = list.Count;
        for (int i = 0; i < n - 1; i++)
        {
            int j = UnityEngine.Random.Range(i, n);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
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
        if (door == null)
        {
            foreach (var t in room.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == doorName)
                {
                    door = t;
                    break;
                }
            }
        }
        if (door != null)
            door.gameObject.SetActive(false);
        else
            Debug.LogWarning($"Door '{doorName}' not found on room '{room.name}' or its children.");
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

    Vector2Int GetRoomGridOrigin(GameObject room)
    {
        if (roomToGridOrigin.TryGetValue(room, out Vector2Int origin))
            return origin;
        Debug.LogError($"Room {room.name} not found in roomToGridOrigin!");
        return Vector2Int.zero;
    }

    Vector3 WorldPosFromGridOrigin(Vector2Int gridOrigin, float gridUnitSize)
    {
        return new Vector3(gridOrigin.x * gridUnitSize, gridOrigin.y * gridUnitSize, 0);
    }

    Vector2Int GridOriginFromWorldPos(Vector3 worldPos, float gridUnitSize)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / gridUnitSize),
            Mathf.FloorToInt(worldPos.y / gridUnitSize)
        );
    }

    void CreateCorridorBetweenAnchors(Transform fromAnchor, Transform toAnchor, Vector2Int direction, float gridUnitSize)
    {
        Vector3 start = fromAnchor.position;
        Vector3 end = toAnchor.position;
        Vector3 dirNormalized = (end - start).normalized;
        float segmentLengthWorld = gridUnitSize;
        float corridorLengthWorld = Vector3.Distance(start, end);
        int segmentCount = Mathf.RoundToInt(corridorLengthWorld / segmentLengthWorld);
        Vector3 currentCorridorWorldPos = start + dirNormalized * (segmentLengthWorld * 0.5f);

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject segment = Instantiate(corridorPrefabs[0], ruinContainer);
            segment.transform.position = currentCorridorWorldPos;

            if (Mathf.Abs(dirNormalized.x) > Mathf.Abs(dirNormalized.y))
                segment.transform.rotation = Quaternion.Euler(0, 0, 90);
            else
                segment.transform.rotation = Quaternion.identity;

            Vector2Int corridorGridPos = GridOriginFromWorldPos(currentCorridorWorldPos, gridUnitSize);
            gridOccupancyMap[corridorGridPos] = segment;

            currentCorridorWorldPos += dirNormalized * segmentLengthWorld;
        }
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            float debugGridUnitSize = 1f;
            if (starterRoomPrefab != null)
            {
                RoomProfile starterProfile = starterRoomPrefab.GetComponentInChildren<RoomProfile>();
                if (starterProfile != null) debugGridUnitSize = starterProfile.gridUnitSize;
            }
            else if (roomPrefabs.Count > 0)
            {
                RoomProfile firstRoomProfile = roomPrefabs[0].GetComponentInChildren<RoomProfile>();
                if (firstRoomProfile != null) debugGridUnitSize = firstRoomProfile.gridUnitSize;
            }

            foreach (KeyValuePair<Vector2Int, GameObject> entry in gridOccupancyMap)
            {
                Vector2Int gridPos = entry.Key;
                GameObject occupiedObject = entry.Value;
                Vector3 worldCellCenter = new Vector3(
                    gridPos.x * debugGridUnitSize + debugGridUnitSize * 0.5f,
                    gridPos.y * debugGridUnitSize + debugGridUnitSize * 0.5f,
                    0
                );

                Gizmos.DrawWireCube(worldCellCenter, new Vector3(debugGridUnitSize, debugGridUnitSize, 0.1f));

                bool isRoomParent = roomToGridOrigin.ContainsKey(occupiedObject);

                Gizmos.color = isRoomParent ? Color.red : Color.green;
                Gizmos.DrawCube(worldCellCenter, new Vector3(debugGridUnitSize * 0.7f, debugGridUnitSize * 0.7f, 0.1f));

                if (isRoomParent && roomToGridOrigin[occupiedObject] == gridPos)
                {
                    Gizmos.color = Color.blue;
                    Gizmos.DrawCube(worldCellCenter, new Vector3(debugGridUnitSize * 0.8f, debugGridUnitSize * 0.8f, 0.1f));
                }
            }

            Gizmos.color = Color.yellow;
            foreach (GameObject room in placedRooms)
            {
                foreach (var t in room.GetComponentsInChildren<Transform>())
                {
                    if (t.name.StartsWith("Anchor_"))
                        Gizmos.DrawSphere(t.position, debugGridUnitSize * 0.1f);
                }
            }
        }
    }
}
