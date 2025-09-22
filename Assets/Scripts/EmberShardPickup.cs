// Little collectible shard; optionally glows and does a tiny hover so it feels alive.
using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class EmberShardPickup : MonoBehaviour
{
    [Header("Visuals")]
    public SpriteRenderer spriteRenderer;   // optional; auto if null
    public Light2D glowLight;               // optional; auto if null
    public Color glowColor = new Color(1f, 0.75f, 0.2f, 1f);

    [Header("Pickup")] 
    public int amount = 1;
    public string playerTag = "Player";
    public KeyCode interactKey = KeyCode.E;
    public float hoverBobAmplitude = 0.05f;
    public float hoverBobSpeed = 2.5f;
    public AudioClip pickupSfx;
    [Range(0f,1f)] public float pickupVolume = 0.9f;
    public bool useHoverBob = true;

    Vector3 baseLocalPos;
    Vector3 lastDesiredLocal;
    bool playerInRange;

    void Awake()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (!glowLight) glowLight = GetComponentInChildren<Light2D>();
        if (glowLight)
        {
            glowLight.color = glowColor;
            glowLight.intensity = Mathf.Max(0.6f, glowLight.intensity);
        }
        baseLocalPos = transform.localPosition;
    }

    void Update()
    {
        if (!useHoverBob) return;

        // tiny hover/bob for life
        if (hoverBobAmplitude > 0f && hoverBobSpeed > 0f)
        {
            float y = Mathf.Sin(Time.time * hoverBobSpeed) * hoverBobAmplitude;
            Vector3 desiredLocal = baseLocalPos + new Vector3(0f, y, 0f);

            // If user moved it in play mode, adopt new base local position instead of snapping back
            if ((transform.localPosition - lastDesiredLocal).sqrMagnitude > 0.000001f && lastDesiredLocal != Vector3.zero)
            {
                baseLocalPos = transform.localPosition - new Vector3(0f, y, 0f);
                desiredLocal = baseLocalPos + new Vector3(0f, y, 0f);
            }

            transform.localPosition = desiredLocal;
            lastDesiredLocal = desiredLocal;
        }
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
        if (playerInRange && Input.GetKeyDown(interactKey))
            TryPickup();
    }

    void TryPickup()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddEmberFragments(Mathf.Max(1, amount));
        }
        if (pickupSfx)
            AudioSource.PlayClipAtPoint(pickupSfx, transform.position, pickupVolume);
        Destroy(gameObject);
    }
}


