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
    [Tooltip("Destroy GameObject after death (seconds). 0 = don't destroy.")]
    [Min(0f)] public float destroyDelay = 0f;

    [Tooltip("Disable these behaviours on death (AI, movement, etc).")]
    public Behaviour[] disableOnDeath;

    [Tooltip("Disable these colliders on death.")]
    public Collider2D[] collidersOnDeath;

    [Header("Events")]
    public UnityEvent<float, float> onDamaged; // (newHealth, damageDealt)
    public UnityEvent onDied;

    [Header("Animator (optional)")]
    public string hurtTrigger = "Hurt";
    public string deathTrigger = "Die";

    float iFrameTimer;
    bool dead;

    Rigidbody2D rb;
    Animator animator;

    void Awake()
    {
        if (currentHealth <= 0f) currentHealth = maxHealth;
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (iFrameTimer > 0f) iFrameTimer -= Time.deltaTime;
    }

    public bool ReceiveHit(in HitInfo hit)
    {
        if (dead) return false;
        if (iFrameTimer > 0f) return false;

        // Apply damage
        float newHealth = Mathf.Max(0f, currentHealth - hit.damage);
        float dealt = currentHealth - newHealth;
        currentHealth = newHealth;
        iFrameTimer = hurtIFrames;

        // Knockback
        if (rb && hit.knockback.sqrMagnitude > 0.0001f)
            rb.AddForce(hit.knockback, ForceMode2D.Impulse);

        // Anim
        if (animator && !string.IsNullOrEmpty(hurtTrigger) && currentHealth > 0f)
            animator.SetTrigger(hurtTrigger);

        onDamaged?.Invoke(currentHealth, dealt);

        if (currentHealth <= 0f)
            Die();

        return true;
    }

    void Die()
    {
        if (dead) return;
        dead = true;

        if (animator && !string.IsNullOrEmpty(deathTrigger))
            animator.SetTrigger(deathTrigger);

        if (rb) rb.velocity = Vector2.zero;

        if (disableOnDeath != null)
            foreach (var b in disableOnDeath) if (b) b.enabled = false;

        if (collidersOnDeath != null)
            foreach (var c in collidersOnDeath) if (c) c.enabled = false;

        onDied?.Invoke();

        if (destroyDelay > 0f)
            Destroy(gameObject, destroyDelay);
    }
}