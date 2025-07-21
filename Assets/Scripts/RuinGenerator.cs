using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq; // For .ToList() on Dictionary.Keys

public class RuinGenerator : MonoBehaviour
{
    [Header("Room Prefabs")]
    public GameObject starterRoomPrefab;
    public List<GameObject> roomPrefabs;
    public List<GameObject> corridorPrefabs; // 0 = Straight, 1 = Turn, 2 = T-Junction, 3 = Cross

    [Header("Generation Settings")]
    public int totalRooms = 5;
    public int extraConnections = 0;
    public int maxJunctions = 0; // Not fully implemented, but kept for future
    public int minCorridorLength = 6; // In grid units
    public int seed = -1; // -1 means random

    [SerializeField]
    private int actualSeedUsed;

    [Header("Scene References")]
    public Transform ruinContainer;
    public GameObject playerPrefab;

    // Internal generation state
    private List<GameObject> placedRooms = new List<GameObject>();
    // gridOccupancyMap: Maps each individual grid cell (Vector2Int) to the GameObject (Room or Corridor) that occupies it.
    private Dictionary<Vector2Int, GameObject> gridOccupancyMap = new Dictionary<Vector2Int, GameObject>();
    // roomToGridOrigin: Maps each placed Room GameObject (the parent _Pivot object) to its bottom-left grid cell coordinate.
    private Dictionary<GameObject, Vector2Int> roomToGridOrigin = new Dictionary<GameObject, Vector2Int>();
    private HashSet<(Vector2Int, Vector2Int)> connectedPairs = new HashSet<(Vector2Int, Vector2Int)>(); // Stores grid origins of connected rooms

    private Vector2Int[] directions = new Vector2Int[]
    {
        new Vector2Int(0, 1),   // Up
        new Vector2Int(1, 0),   // Right
        new Vector2Int(0, -1),  // Down
        new Vector2Int(-1, 0),  // Left
    };

    void Start()
    {
        actualSeedUsed = seed >= 0 ? seed : System.Environment.TickCount;
        Random.InitState(actualSeedUsed);
        Debug.Log($"Using seed: {actualSeedUsed}");
        GenerateRuin();
    }

    void GenerateRuin()
    {
        // Place starter room
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

        // Player spawn
        Transform playerSpawn = null;
        // Use GetComponentsInChildren to find Player_Spawn anywhere in the prefab's children
        foreach (Transform t in startRoom.GetComponentsInChildren<Transform>(true)) // 'true' to include inactive objects
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
            Debug.Log("Player spawned successfully!"); // Added success message
        }
        else
        {
            Debug.LogWarning("Player spawn point or prefab not found! Player will not be spawned.");
            if (playerSpawn == null) Debug.LogWarning("Player_Spawn Transform was null.");
            if (playerPrefab == null) Debug.LogWarning("Player Prefab was null.");
        }

        int placedCount = 1;
        int safetyCounter = 0; // Prevents infinite loops if generation gets stuck

        // Main room placement loop
        while (placedCount < totalRooms && safetyCounter < 500)
        {
            safetyCounter++;

            // Pick a random already-placed room to branch from
            GameObject currentRoom = placedRooms[Random.Range(0, placedRooms.Count)];
            RoomProfile currentProfile = currentRoom.GetComponentInChildren<RoomProfile>();
            if (currentProfile == null) // Add null check for safety
            {
                Debug.LogWarning($"Current room {currentRoom.name} lost its RoomProfile. Skipping.");
                continue;
            }
            Vector2Int currentRoomGridOrigin = GetRoomGridOrigin(currentRoom); // Get the stored grid origin

            List<Vector2Int> dirList = new List<Vector2Int>(directions);
            Shuffle(dirList);
            bool placedRoomThisIteration = false;

            foreach (Vector2Int direction in dirList)
            {
                // Instantiate a temporary room to get its profile and anchor data
                GameObject newRoomPrefab = roomPrefabs[Random.Range(0, roomPrefabs.Count)];
                GameObject tempRoom = Instantiate(newRoomPrefab);
                RoomProfile tempProfile = tempRoom.GetComponentInChildren<RoomProfile>();
                if (tempProfile == null)
                {
                    Debug.LogError("Room prefab (or its children) must have a RoomProfile component!");
                    Destroy(tempRoom);
                    continue;
                }
                Vector2Int newRoomSize = tempProfile.size;

                Transform currentRoomExitAnchor = FindAnchor(currentRoom, DirectionToAnchorName(direction));
                Transform newRoomEntryAnchor = FindAnchor(tempRoom, DirectionToAnchorName(-direction));

                if (currentRoomExitAnchor == null || newRoomEntryAnchor == null)
                {
                    Destroy(tempRoom);
                    continue;
                }

                // --- IMPORTANT FIX FOR proposedNewRoomWorldPos ---
                // Reset tempRoom's world position to (0,0,0) to find its anchor's offset from its own pivot correctly.
                // This means newRoomEntryAnchor.position will be the offset from the tempRoom's (0,0,0) pivot.
                tempRoom.transform.position = Vector3.zero;
                Vector3 offsetFromPivotToAnchor = newRoomEntryAnchor.position;
                // --- END FIX ---

                // Calculate the desired world position for the new room's entry anchor
                Vector3 desiredNewRoomEntryAnchorWorldPos = currentRoomExitAnchor.position + (Vector3)(Vector2)direction * minCorridorLength * tempProfile.gridUnitSize;

                // Calculate the proposed world position for the new room's pivot (bottom-left of the _Pivot object)
                Vector3 proposedNewRoomWorldPos = desiredNewRoomEntryAnchorWorldPos - offsetFromPivotToAnchor;

                // Convert this proposed world position to its equivalent grid origin
                Vector2Int proposedNextGridOrigin = GridOriginFromWorldPos(proposedNewRoomWorldPos, tempProfile.gridUnitSize);

                // Check if the area the new room would occupy is clear
                if (!CanPlaceRoom(proposedNextGridOrigin, newRoomSize))
                {
                    Destroy(tempRoom);
                    continue;
                }

                // If placement is valid, instantiate the actual room
                GameObject newRoom = Instantiate(newRoomPrefab, ruinContainer);
                newRoom.transform.position = proposedNewRoomWorldPos; // Set its world position directly

                placedRooms.Add(newRoom);
                roomToGridOrigin[newRoom] = proposedNextGridOrigin; // Store its grid origin
                MarkOccupiedCells(proposedNextGridOrigin, newRoomSize, newRoom); // Mark all its cells as occupied

                // Establish connection
                connectedPairs.Add((currentRoomGridOrigin, proposedNextGridOrigin));
                connectedPairs.Add((proposedNextGridOrigin, currentRoomGridOrigin));

                // Create corridor and disable doors
                CreateCorridorBetweenAnchors(currentRoomExitAnchor, FindAnchor(newRoom, DirectionToAnchorName(-direction)), direction, newRoom.GetComponentInChildren<RoomProfile>().gridUnitSize);
                DisableDoorAtAnchor(currentRoom, DirectionToDoorName(direction));
                DisableDoorAtAnchor(newRoom, DirectionToDoorName(-direction));

                Destroy(tempRoom); // Destroy the temporary room
                placedCount++;
                placedRoomThisIteration = true;
                break; // Move to placing the next room
            }

            if (!placedRoomThisIteration)
            {
                Debug.Log($"Failed to place a room from {currentRoom.name} in any direction. Retrying.");
                continue;
            }
        }

        // --- After all primary rooms are placed, add extra connections between adjacent rooms ---
        AddExtraConnections();

        Debug.Log($"Placed {placedCount} rooms (attempts: {safetyCounter})");
    }

    void AddExtraConnections()
    {
        int extrasAdded = 0;
        // Iterate through all placed rooms
        for (int i = 0; i < placedRooms.Count; i++)
        {
            GameObject roomA = placedRooms[i];
            RoomProfile profileA = roomA.GetComponentInChildren<RoomProfile>();
            if (profileA == null) continue; // Safety check
            Vector2Int roomAGridOrigin = GetRoomGridOrigin(roomA);

            for (int j = i + 1; j < placedRooms.Count; j++) // Avoid redundant checks (A-B and B-A) and self-connection
            {
                GameObject roomB = placedRooms[j];
                RoomProfile profileB = roomB.GetComponentInChildren<RoomProfile>();
                if (profileB == null) continue; // Safety check
                Vector2Int roomBGridOrigin = GetRoomGridOrigin(roomB);

                // Skip if already connected
                if (connectedPairs.Contains((roomAGridOrigin, roomBGridOrigin)))
                {
                    continue;
                }

                foreach (Vector2Int dir in directions) // Iterate all 4 cardinal directions
                {
                    Transform fromAnchor = FindAnchor(roomA, DirectionToAnchorName(dir));
                    Transform toAnchor = FindAnchor(roomB, DirectionToAnchorName(-dir));

                    if (fromAnchor == null || toAnchor == null)
                    {
                        continue;
                    }

                    // --- NEW CRITICAL CHECK: Ensure anchors are axially aligned ---
                    // Allow a small tolerance for floating point inaccuracies
                    float tolerance = 0.1f * profileA.gridUnitSize; // 10% of a grid unit
                    bool isAxiallyAligned = false;

                    if (Mathf.Abs(fromAnchor.position.x - toAnchor.position.x) < tolerance) // Aligned vertically (X positions are very close)
                    {
                        // Check if the direction vector matches (up or down)
                        if ((dir == Vector2Int.up && fromAnchor.position.y < toAnchor.position.y) ||
                               (dir == Vector2Int.down && fromAnchor.position.y > toAnchor.position.y))
                        {
                            isAxiallyAligned = true;
                        }
                    }
                    else if (Mathf.Abs(fromAnchor.position.y - toAnchor.position.y) < tolerance) // Aligned horizontally (Y positions are very close)
                    {
                        // Check if the direction vector matches (right or left)
                        if ((dir == Vector2Int.right && fromAnchor.position.x < toAnchor.position.x) ||
                               (dir == Vector2Int.left && fromAnchor.position.x > toAnchor.position.x))
                        {
                            isAxiallyAligned = true;
                        }
                    }

                    if (!isAxiallyAligned)
                    {
                        continue; // Not a straight axial connection, skip
                    }
                    // --- END NEW CRITICAL CHECK ---


                    float requiredDistance = minCorridorLength * profileA.gridUnitSize;
                    float actualDistance = Vector3.Distance(fromAnchor.position, toAnchor.position);

                    // Also check if the actual distance is within an acceptable range for a straight corridor
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
                            if (extrasAdded >= extraConnections)
                                return;
                        }
                    }
                }
            }
        }
    }

    // Helper to check if the path between two anchor points is clear of other rooms/corridors
    // This assumes a straight line path for simplicity. For complex paths (turns),
    // you'd need a grid-based pathfinding algorithm here.
    bool CheckCorridorPathClear(Vector3 startWorld, Vector3 endWorld, float gridUnitSize, GameObject roomA, GameObject roomB)
    {
        Vector3 directionVector = (endWorld - startWorld).normalized;
        float segmentLength = gridUnitSize; // Check cell by cell

        // Adjust start and end points slightly to avoid checking cells already part of roomA or roomB.
        // We want to check the cells *between* the rooms.
        Vector3 checkStartWorld = startWorld + directionVector * (segmentLength * 0.5f);
        Vector3 checkEndWorld = endWorld - directionVector * (segmentLength * 0.5f);

        // Calculate the number of steps needed, ensuring we cover the full distance
        int numSteps = Mathf.RoundToInt(Vector3.Distance(checkStartWorld, checkEndWorld) / segmentLength); // Use RoundToInt for consistency

        // Debugging Aid: If numSteps is 0 but distance is positive, something is wrong with calculation.
        // Also ensure it's at least 1 if there's any distance.
        if (Vector3.Distance(checkStartWorld, checkEndWorld) > 0.01f && numSteps == 0) numSteps = 1;

        for (int i = 0; i < numSteps; i++)
        {
            Vector3 currentCheckPos = checkStartWorld + directionVector * (i * segmentLength);
            Vector2Int checkGridPos = GridOriginFromWorldPos(currentCheckPos, gridUnitSize); // Check the bottom-left of the grid cell

            if (gridOccupancyMap.TryGetValue(checkGridPos, out GameObject occupiedObject))
            {
                // If the cell is occupied by something other than roomA (the current room's parent) or roomB (the target room's parent), it's blocked
                if (occupiedObject != roomA && occupiedObject != roomB)
                {
                    // Debug.Log($"Path blocked at {checkGridPos} by {occupiedObject.name} while connecting {roomA.name} and {roomB.name}");
                    return false;
                }
            }
        }
        return true;
    }


    void Shuffle<T>(List<T> list)
    {
        int n = list.Count;
        for (int i = 0; i < n - 1; i++)
        {
            int j = Random.Range(i, n);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    // Marks all grid cells occupied by a room (the parent _Pivot object)
    void MarkOccupiedCells(Vector2Int baseGridOrigin, Vector2Int size, GameObject roomParent)
    {
        for (int x = baseGridOrigin.x; x < baseGridOrigin.x + size.x; x++)
        {
            for (int y = baseGridOrigin.y; y < baseGridOrigin.y + size.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (gridOccupancyMap.ContainsKey(cell))
                {
                    // This warning indicates an overlap detected by the grid map
                    Debug.LogWarning($"Cell {cell} already occupied by {gridOccupancyMap[cell].name}. Attempting to place {roomParent.name}.");
                }
                gridOccupancyMap[cell] = roomParent; // Store the parent GameObject
            }
        }
    }

    // Checks if the proposed area for a new room (the parent _Pivot object) is free
    bool CanPlaceRoom(Vector2Int proposedBaseGridOrigin, Vector2Int size)
    {
        for (int x = proposedBaseGridOrigin.x; x < proposedBaseGridOrigin.x + size.x; x++)
        {
            for (int y = proposedBaseGridOrigin.y; y < proposedBaseGridOrigin.y + size.y; y++)
            {
                if (gridOccupancyMap.ContainsKey(new Vector2Int(x, y)))
                {
                    return false; // Cell is already occupied
                }
            }
        }
        return true;
    }

    void CreateCorridorBetweenAnchors(Transform fromAnchor, Transform toAnchor, Vector2Int direction, float gridUnitSize)
    {
        Vector3 start = fromAnchor.position;
        Vector3 end = toAnchor.position;
        Vector3 dirNormalized = (end - start).normalized; // Should be purely axial now

        float segmentLengthWorld = gridUnitSize;
        float corridorLengthWorld = Vector3.Distance(start, end);
        int segmentCount = Mathf.RoundToInt(corridorLengthWorld / segmentLengthWorld);

        // Position the first segment half a segment length from the start anchor
        Vector3 currentCorridorWorldPos = start + dirNormalized * (segmentLengthWorld * 0.5f);

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject segment = Instantiate(corridorPrefabs[0], ruinContainer); // Still only straight corridors
            segment.transform.position = currentCorridorWorldPos;

            // Rotate based on direction (now guaranteed to be axial)
            // Note: The previous logic for rotation was correct for an assumed centered pivot of the corridor.
            // If your corridor prefab now has a bottom-left pivot, this rotation logic might need adjustment.
            // If it's 1x1, and its visual is (0,0)-(1,1) with BL pivot:
            // Vertical (dir.y != 0): Identity (no rotation)
            // Horizontal (dir.x != 0): Rotate by 90 around Z axis
            if (Mathf.Abs(dirNormalized.x) > Mathf.Abs(dirNormalized.y)) // Mostly horizontal (X changes)
                segment.transform.rotation = Quaternion.Euler(0, 0, 90); // Rotate 90 degrees for horizontal
            else // Mostly vertical (Y changes)
                segment.transform.rotation = Quaternion.identity; // No rotation for vertical

            // Mark the grid cell occupied by this corridor segment
            Vector2Int corridorGridPos = GridOriginFromWorldPos(currentCorridorWorldPos, gridUnitSize);
            if (gridOccupancyMap.ContainsKey(corridorGridPos))
            {
                Debug.LogWarning($"Corridor segment at {corridorGridPos} already occupied by {gridOccupancyMap[corridorGridPos].name}.");
            }
            gridOccupancyMap[corridorGridPos] = segment; // Mark the corridor segment as occupying this cell

            currentCorridorWorldPos += dirNormalized * segmentLengthWorld;
        }
    }

    // FindAnchor will look for anchors as direct children of the room's root.
    // If your anchors are nested deeper, you might need to adjust this.
    Transform FindAnchor(GameObject obj, string anchorName)
    {
        foreach (var t in obj.GetComponentsInChildren<Transform>()) // Search children too for anchors
            if (t.name == anchorName) return t;
        return null;
    }

    // DisableDoorAtAnchor will look for doors as direct children of the room's root.
    // If your doors are nested deeper, you might need to adjust this.
    void DisableDoorAtAnchor(GameObject room, string doorName)
    {
        Transform door = room.transform.Find(doorName); // Find only direct children
        if (door == null) // If not a direct child, try searching children recursively
        {
            foreach (var t in room.GetComponentsInChildren<Transform>(true)) // true to include inactive objects
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

    // --- Grid <-> World Position Conversion Helpers ---

    // Converts a grid origin (bottom-left cell) to world position for room placement
    Vector3 WorldPosFromGridOrigin(Vector2Int gridOrigin, float gridUnitSize)
    {
        return new Vector3(gridOrigin.x * gridUnitSize, gridOrigin.y * gridUnitSize, 0);
    }

    // Converts a world position (e.g., room's pivot) to its corresponding grid origin (bottom-left cell)
    Vector2Int GridOriginFromWorldPos(Vector3 worldPos, float gridUnitSize)
    {
        // Use FloorToInt to ensure we always get the bottom-left grid coordinate
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / gridUnitSize),
            Mathf.FloorToInt(worldPos.y / gridUnitSize)
        );
    }

    // Helper to get the stored grid origin of a room (the parent _Pivot object)
    Vector2Int GetRoomGridOrigin(GameObject room)
    {
        // We'll iterate the dictionary to find the value for the key (GameObject).
        // This is not the most efficient, but safe given the map is GameObject -> Vector2Int.
        foreach (var entry in roomToGridOrigin)
        {
            if (entry.Key == room)
            {
                return entry.Value;
            }
        }
        Debug.LogError($"Room {room.name} not found in roomToGridOrigin dictionary! This room's pivot might not have been correctly set or registered.");
        return Vector2Int.zero; // Fallback
    }

    // --- Debug Visualization ---
    void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            // Find a RoomProfile to get the gridUnitSize, assume consistent
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


            // Visualize occupied grid cells
            foreach (KeyValuePair<Vector2Int, GameObject> entry in gridOccupancyMap)
            {
                Vector2Int gridPos = entry.Key;
                GameObject occupiedObject = entry.Value;

                // Calculate world center of the grid cell for Gizmo drawing
                Vector3 worldCellCenter = new Vector3(
                    gridPos.x * debugGridUnitSize + debugGridUnitSize * 0.5f,
                    gridPos.y * debugGridUnitSize + debugGridUnitSize * 0.5f,
                    0
                );

                Gizmos.DrawWireCube(worldCellCenter, new Vector3(debugGridUnitSize, debugGridUnitSize, 0.1f));

                // Differentiate between rooms and corridors based on if they are stored in roomToGridOrigin
                bool isRoomParent = false;
                foreach (var roomEntry in roomToGridOrigin) // Check if the occupied object is one of our registered room parents
                {
                    if (roomEntry.Key == occupiedObject)
                    {
                        isRoomParent = true;
                        break;
                    }
                }

                if (isRoomParent)
                {
                    Gizmos.color = Color.red; // Room cells
                    // Draw a smaller cube at the room's origin for clarity
                    // (This check assumes the grid origin is one of the cells marked by the room)
                    if (roomToGridOrigin.ContainsKey(occupiedObject) && roomToGridOrigin[occupiedObject] == gridPos) // Added check for ContainsKey
                    {
                        Gizmos.color = Color.blue; // Room Origin
                        Gizmos.DrawCube(worldCellCenter, new Vector3(debugGridUnitSize * 0.8f, debugGridUnitSize * 0.8f, 0.1f));
                    }
                }
                else // It's likely a corridor segment or other non-room object marked as occupied
                {
                    Gizmos.color = Color.green; // Corridor cells
                }
                Gizmos.DrawCube(worldCellCenter, new Vector3(debugGridUnitSize * 0.7f, debugGridUnitSize * 0.7f, 0.1f));
            }

            // Visualize Anchors
            Gizmos.color = Color.yellow;
            foreach (GameObject room in placedRooms)
            {
                foreach (var t in room.GetComponentsInChildren<Transform>())
                {
                    if (t.name.StartsWith("Anchor_"))
                    {
                        Gizmos.DrawSphere(t.position, debugGridUnitSize * 0.1f);
                    }
                }
            }
        }
    }
}