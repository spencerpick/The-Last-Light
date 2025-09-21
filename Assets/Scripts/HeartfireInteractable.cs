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

    bool isLit;

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

    void OnTriggerStay2D(Collider2D other)
    {
        if (isLit) return;
        if (!other || !other.CompareTag(playerTag)) return;
        if (Input.GetKeyDown(interactKey))
        {
            isLit = true;
            ApplySprite();
            Debug.Log("GAME OVER");
        }
    }
}


