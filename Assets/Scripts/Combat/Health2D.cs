// Basic HP with brief i-frames, knockback, and a couple of UnityEvents for hooks.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class Health2D : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [Min(1f)] public float maxHealth = 5f;
    public float currentHealth = 5f;

    [Header("Invulnerability")]
    [Tooltip("Brief invulnerability after taking a hit.")]
    [Min(0f)] public float hurtIFrames = 0.10f;

    [Header("Death")]
    [Tooltip("Destroy GameObject after death (seconds). 0 = destroy immediately.")]
    [Min(0f)] public float destroyDelay = 0f;
    [Tooltip("Optional Animator to drive death visuals.")]
    public Animator animator;
    [Tooltip("Animator trigger or bool to set when dying (optional).")]
    public string deathTrigger = "Die";
    public string deathBool = "";

    [Header("FX / Feedback")]
    public HitFlash hitFlash;
    public bool applyKnockback = true;
    [Header("Audio (Damage/Hit)")]
    [Tooltip("Sound played when THIS entity takes damage.")]
    public AudioClip hitTakenSfx;
    [Range(0f,1f)] public float hitTakenVolume = 0.9f;
    [Tooltip("Pitch variance for repeated impacts (min..max)")]
    public Vector2 hitTakenPitchRange = new Vector2(0.98f, 1.05f);
    [Tooltip("Minimum seconds between playing 'hit taken' sounds for this entity.")]
    public float hitTakenSoundCooldown = 3f;
    float nextHitTakenSoundTime;

    [Header("Events")]
    public UnityEvent onDamaged;
    public UnityEvent onHealed;
    public UnityEvent onDied;

    // internal
    float iFrameTimer = 0f;
    bool dead = false;
    Rigidbody2D rb;
    readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
    Color[] baseColors;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!hitFlash) hitFlash = GetComponentInChildren<HitFlash>(true);
        if (currentHealth <= 0f) currentHealth = maxHealth;

        // cache renderers (for HitFlash if needed)
        if (renderers.Count == 0)
            renderers.AddRange(GetComponentsInChildren<SpriteRenderer>(true));
        baseColors = new Color[renderers.Count];
        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i]) baseColors[i] = renderers[i].color;
    }

    void Update()
    {
        if (iFrameTimer > 0f) iFrameTimer -= Time.deltaTime;
    }

    public bool ReceiveHit(in HitInfo hit)
    {
        if (dead) return false;
        if (iFrameTimer > 0f) return false;

        // damage
        float dmg = Mathf.Max(0f, hit.damage);
        if (dmg <= 0f) return false;

        currentHealth = Mathf.Max(0f, currentHealth - dmg);
        iFrameTimer = hurtIFrames;

        // feedback
        if (hitFlash) hitFlash.Flash();

        if (applyKnockback && rb)
            rb.AddForce(hit.knockback, ForceMode2D.Impulse);

        // Audio for taking damage
        if (hitTakenSfx && Time.time >= nextHitTakenSoundTime)
        {
            float dur = Audio.OneShotAudio.Play(transform.position, hitTakenSfx, hitTakenVolume, hitTakenPitchRange.x, hitTakenPitchRange.y);
            nextHitTakenSoundTime = Time.time + Mathf.Max(dur, hitTakenSoundCooldown);
        }

        onDamaged?.Invoke();

        // ① LOG when the PLAYER is hit (your ask #1)
        if (CompareTag("Player"))
        {
            Debug.Log($"[Player] Took {dmg} damage → {currentHealth}/{maxHealth} HP left.");
        }

        if (currentHealth <= 0f)
        {
            Die();
        }

        return true;
    }

    public void Heal(float amount)
    {
        if (dead) return;
        float before = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + Mathf.Max(0f, amount));
        if (currentHealth > before) onHealed?.Invoke();
    }

    void Die()
    {
        if (dead) return;
        dead = true;

        onDied?.Invoke();

        // Animator flags if provided
        if (animator)
        {
            if (!string.IsNullOrEmpty(deathTrigger)) animator.SetTrigger(deathTrigger);
            if (!string.IsNullOrEmpty(deathBool)) animator.SetBool(deathBool, true);
        }

        // Disable colliders so corpses don’t block
        foreach (var c in GetComponentsInChildren<Collider2D>(true))
            c.enabled = false;

        // Finally, destroy (immediate or delayed)
        if (destroyDelay <= 0f) Destroy(gameObject);
        else Destroy(gameObject, destroyDelay);
    }

    void OnValidate()
    {
        if (maxHealth < 1f) maxHealth = 1f;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!hitFlash) hitFlash = GetComponentInChildren<HitFlash>(true);
    }
}
