using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public RoomTrigger CurrentRoom { get; private set; }

    private float runTime;
    private int emberFragments;
    private int enemiesKilled;
    private bool runEnded;

    [Header("End Run UI")]
    public EndRunUI endRunUI; // assign in scene (on a Canvas)

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
        if (!runEnded)
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
        // Only clear if we�re exiting the currently tracked room
        if (CurrentRoom != null && CurrentRoom.roomID == room.roomID)
        {
            Debug.Log($"Exited Room ID: {room.roomID}, Type: {room.roomType}");
            CurrentRoom = null;
        }
    }

    public float GetRunTime() => runTime;

    public int GetEmberFragments() => emberFragments;

    public void AddEmberFragments(int amount) => emberFragments += amount;

    public int GetEnemiesKilled() => enemiesKilled;
    public void IncrementEnemiesKilled(int amount = 1) => enemiesKilled += Mathf.Max(1, amount);

    public void ResetRun()
    {
        runTime = 0;
        emberFragments = 0;
        enemiesKilled = 0;
        runEnded = false;
        CurrentRoom = null;
    }

    // Trigger the end-of-run flow: fade to black and show stats
    public void TriggerEndRun(float fadeSeconds)
    {
        if (runEnded) return;
        runEnded = true;
        if (endRunUI)
        {
            endRunUI.StartEndRun(Mathf.Max(0.1f, fadeSeconds), runTime, emberFragments, enemiesKilled);
        }
        else
        {
            Debug.Log($"END RUN — time:{runTime:F1}s shards:{emberFragments} kills:{enemiesKilled}");
        }
    }
}
