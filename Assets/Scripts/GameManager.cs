using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public RoomTrigger CurrentRoom { get; private set; }

    private float runTime;
    private int emberFragments;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        runTime += Time.deltaTime;
    }

    public void EnterRoom(RoomTrigger room)
    {
        // Enter room if:
        // - it's the first room
        // - OR it's a different roomID (even if same type)
        if (CurrentRoom == null || CurrentRoom.roomID != room.roomID)
        {
            CurrentRoom = room;
            Debug.Log($"Entered Room ID: {room.roomID}, Type: {room.roomType}");
        }
    }

    public void ExitRoom(RoomTrigger room)
    {
        // Only clear if we’re exiting the currently tracked room
        if (CurrentRoom != null && CurrentRoom.roomID == room.roomID)
        {
            Debug.Log($"Exited Room ID: {room.roomID}, Type: {room.roomType}");
            CurrentRoom = null;
        }
    }

    public float GetRunTime() => runTime;

    public int GetEmberFragments() => emberFragments;

    public void AddEmberFragments(int amount) => emberFragments += amount;

    public void ResetRun()
    {
        runTime = 0;
        emberFragments = 0;
        CurrentRoom = null;
    }
}
