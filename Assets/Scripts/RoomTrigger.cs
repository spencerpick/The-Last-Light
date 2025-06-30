using UnityEngine;

public class RoomTrigger : MonoBehaviour
{
    public enum RoomType { Start, Combat, Treasure, Boss, End, Enemy1, Enemy2, Heartfire }
    public RoomType roomType;

    [Tooltip("Unique identifier for this specific room instance.")]
    public string roomID;

    private void Awake()
    {
        // Generate unique ID if not set manually
        if (string.IsNullOrEmpty(roomID))
        {
            roomID = System.Guid.NewGuid().ToString();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            GameManager.Instance.EnterRoom(this);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            GameManager.Instance.ExitRoom(this);
        }
    }
}
