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
    [Min(0f)] public float destroyDelay = 0.2f;

    [Tooltip("Extra behaviours to disable on death (AI scripts, movement, etc.). Optional.")]
    public Behaviour[] disableOnDeath;

    [Tooltip("Extra colliders to disable on death (if you don’t want to auto-disable all). Optional.")]
    public Collider2D[] collidersOnDeath;

    [Header("Animation (optional)")]
    public Animator animator;
    public string hurtTrigger = "Hurt";
    public string deathTrigger = "Die";

    [Header("Events")]
    public UnityEvent<float, float> onDamaged; // (newHealth, damageDealt)
    public UnityEvent onDied;

    [Header("Debug")]
    public bool verboseLogging = true;

    float iFrameTimer;
    bool dead;

    Rigidbody2D rb;

    void Reset()
    {
        animator = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();
        // Attempt to set sensible defaults
        if (currentHealth <= 0f) currentHealth = maxHealth > 0 ? maxHealth : 5f;
    }

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody2D>();

        if (currentHealth <= 0f) currentHealth = maxHealth;

        if (verboseLogging)
            Debug.Log($"[Health2D] ({name}) Awake: {currentHealth}/{maxHealth} HP");
    }

    void Update()
    {
        if (iFrameTimer > 0f) iFrameTimer -= Time.deltaTime;
    }

    public bool ReceiveHit(in HitInfo hit)
    {
        if (dead)
        {
            if (verboseLogging) Debug.Log($"[Health2D] ({name}) Hit ignored: already dead.");
            return false;
        }
        if (iFrameTimer > 0f)
        {
            if (verboseLogging) Debug.Log($"[Health2D] ({name}) Hit ignored: i-frames {iFrameTimer:F2}s");
            return false;
        }

        // Apply damage
        float newHealth = Mathf.Max(0f, currentHealth - hit.damage);
        float dealt = currentHealth - newHealth;
        currentHealth = newHealth;
        iFrameTimer = hurtIFrames;

        // Knockback
        if (rb && hit.knockback.sqrMagnitude > 0.0001f)
            rb.AddForce(hit.knockback, ForceMode2D.Impulse);

        // Anim (optional)
        if (animator && !string.IsNullOrEmpty(hurtTrigger) && currentHealth > 0f)
            animator.SetTrigger(hurtTrigger);

        // Log each hit
        if (verboseLogging)
            Debug.Log($"[Health2D] ({name}) Took {dealt} dmg → {currentHealth}/{maxHealth} HP");

        onDamaged?.Invoke(currentHealth, dealt);

        if (currentHealth <= 0f)
            Die();

        return true;
    }

    void Die()
    {
        if (dead) return;
        dead = true;

        // Stop any motion immediately
        if (rb)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false; // switch off physics so it can’t move anymore
        }

        // Disable all colliders so it can’t be hit again / block anything
        var allCols = GetComponentsInChildren<Collider2D>(includeInactive: true);
        foreach (var c in allCols) c.enabled = false;

        // Extra, user-specified colliders
        if (collidersOnDeath != null)
            foreach (var c in collidersOnDeath) if (c) c.enabled = false;

        // Disable commonly-problematic behaviours automatically (except this Health2D)
        var allBehaviours = GetComponentsInChildren<Behaviour>(includeInactive: true);
        foreach (var b in allBehaviours)
        {
            if (!b) continue;
            if (b == this) continue; // keep Health2D alive to finish destruction
            // Keep Animator on for death anim if provided below
            if (animator && b == animator) continue;
            b.enabled = false;
        }

        // Then disable any extra scripts you explicitly listed
        if (disableOnDeath != null)
            foreach (var b in disableOnDeath) if (b) b.enabled = false;

        // Play death anim (optional)
        if (animator && !string.IsNullOrEmpty(deathTrigger))
            animator.SetTrigger(deathTrigger);

        if (verboseLogging)
            Debug.Log($"[Health2D] ({name}) Died. Destroy in {destroyDelay:0.00}s");

        onDied?.Invoke();

        // Finally, destroy the GameObject (immediate or delayed)
        if (destroyDelay <= 0f) Destroy(gameObject);
        else Destroy(gameObject, destroyDelay);
    }

    void OnValidate()
    {
        if (maxHealth < 1f) maxHealth = 1f;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (rb == null) rb = GetComponent<Rigidbody2D>();
    }
}
