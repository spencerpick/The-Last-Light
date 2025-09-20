using UnityEngine;

/// <summary>
/// Eventless hit window driver for EnemyChaseAttack2D.
/// It watches the Animator's attack state and calls the enemy's
/// AnimationHitWindow() and AnimationAttackEnd() based on normalized time,
/// so you don't have to rely on Animation Events at all.
/// </summary>
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class EnemyAttackHitTimeline : MonoBehaviour
{
    [Header("Who to drive")]
    public EnemyChaseAttack2D enemy;         // auto-found if left null
    public Animator animator;                // auto-found if left null

    [Header("Attack state to watch")]
    public string attackStateName = "AttackBT";
    public int animatorLayerIndex = 0;

    [Header("Hit window (normalized time on attack clip)")]
    [Range(0f, 1f)] public float hitWindowStart = 0.35f;
    [Range(0f, 1f)] public float hitWindowEnd = 0.55f;

    [Header("End/failsafe")]
    [Range(0f, 1.2f)] public float endAtNormalized = 0.98f;   // when to force end if the clip doesn't call it

    bool hitFiredThisSwing;

    void Awake()
    {
        if (!animator) animator = GetComponent<Animator>();
        if (!enemy) enemy    = GetComponent<EnemyChaseAttack2D>();
    }

    void OnEnable()
    {
        hitFiredThisSwing = false;
    }

    void FixedUpdate()
    {
        if (!animator || !enemy) return;

        var st = animator.GetCurrentAnimatorStateInfo(animatorLayerIndex);
        if (!st.IsName(attackStateName))
        {
            // if we left attack, reset for next swing
            hitFiredThisSwing = false;
            return;
        }

        float t = st.normalizedTime; // 0..1 (can exceed 1 if looping, but our attack shouldn't loop)
        // Fire the hit exactly once per swing when we enter the window.
        if (!hitFiredThisSwing && t >= hitWindowStart && t <= hitWindowEnd)
        {
            enemy.AnimationHitWindow();
            hitFiredThisSwing = true;
        }

        // End attack near the end of the clip as a failsafe.
        if (t >= endAtNormalized)
        {
            enemy.AnimationAttackEnd();
            hitFiredThisSwing = false;
        }
    }
}
