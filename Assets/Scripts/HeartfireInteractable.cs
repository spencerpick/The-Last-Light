// Small altar at the end: lets the player finish the run once they’ve got enough shards.
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class HeartfireInteractable : MonoBehaviour
{
    [Header("Visuals")]
    public SpriteRenderer spriteRenderer;   // auto-found if left null
    public Sprite extinguishedSprite;
    public Sprite litSprite;

    [Header("Interaction")]
    public KeyCode interactKey = KeyCode.E;
    public string playerTag = "Player";
    public float fadeToBlackSeconds = 5f;   // end-of-run fade duration
    [Header("Requirements")]
    public int requiredShards = 10;

    [Header("Audio")]
    public AudioClip activateSfx;
    [Range(0f,1f)] public float activateVolume = 1f;

    bool isLit;
    bool playerInRange;

    void Awake()
    {
        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        var col = GetComponent<Collider2D>();
        if (col) col.isTrigger = true;
        ApplySprite();
    }

    void ApplySprite()
    {
        if (!spriteRenderer) return;
        if (isLit && litSprite) spriteRenderer.sprite = litSprite;
        else if (!isLit && extinguishedSprite) spriteRenderer.sprite = extinguishedSprite;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other || !other.CompareTag(playerTag)) return;
        playerInRange = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other || !other.CompareTag(playerTag)) return;
        playerInRange = false;
    }

    void LateUpdate()
    {
        if (isLit) return;
        if (!playerInRange) return;
        if (!Input.GetKeyDown(interactKey)) return;

        // Check ember shard requirement
        int have = GameManager.Instance ? GameManager.Instance.GetEmberFragments() : 0;
        if (have < Mathf.Max(0, requiredShards))
        {
            Debug.Log($"Heartfire requires {requiredShards} Ember Shards. You currently have {have}.");
            return;
        }

        isLit = true;
        ApplySprite();
        if (activateSfx)
            Audio.OneShotAudio.Play(transform.position, activateSfx, activateVolume, 1f, 1f);
        if (GameManager.Instance)
            GameManager.Instance.TriggerEndRun(Mathf.Max(0.1f, fadeToBlackSeconds));
        else
            Debug.Log("GAME OVER");
    }
}


