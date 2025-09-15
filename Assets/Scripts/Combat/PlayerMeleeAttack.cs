using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerMeleeAttack : MonoBehaviour
{
    [Header("Attack")]
    public AttackData attack;
    public LayerMask hittableLayers;
    public Transform pivot;

    [Header("Animator Params")]
    public string attackTrigger = "Attack";
    public string dirXParam = "MoveX";         // set to your param names
    public string dirYParam = "MoveY";
    public string attackingBool = "IsAttacking";

    [Header("Debug")]
    public bool verboseLogging = true;
    public bool drawHitboxGizmo = true;
    public Color gizmoColor = new Color(1f, 0.4f, 0.1f, 0.25f);

    Animator animator;
    float nextReadyTime;
    readonly Collider2D[] hits = new Collider2D[16];
    readonly HashSet<int> hitThisSwing = new HashSet<int>();
    Vector2 lastFacing = Vector2.right;

    // Helper to prefix logs
    void Log(string msg)
    {
        if (!verboseLogging) return;
        Debug.Log($"[PlayerMeleeAttack] ({name}) {msg}", this);
    }

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (!pivot) pivot = transform;

        Log($"Awake. attackTrigger='{attackTrigger}', attackingBool='{attackingBool}', Dir=('{dirXParam}','{dirYParam}').");
        if (!attack) Debug.LogWarning("[PlayerMeleeAttack] AttackData is NOT assigned.", this);
    }

    void Update()
    {
        // TEMP input – replace with your input system when ready
        if (Input.GetButtonDown("Fire1"))
        {
            Log("Fire1 pressed.");
            TryStartAttack();
        }
    }

    public void TryStartAttack()
    {
        if (!attack)
        {
            Debug.LogWarning("[PlayerMeleeAttack] No AttackData assigned.", this);
            return;
        }

        float now = Time.time;
        bool isAttacking = !string.IsNullOrEmpty(attackingBool) && animator.GetBool(attackingBool);

        if (now < nextReadyTime)
        {
            Log($"TryStartAttack BLOCKED by cooldown. now={now:F2} readyAt={nextReadyTime:F2}");
            return;
        }

        Log($"TryStartAttack OK. isAttacking(before)={isAttacking}  cooldown={attack.cooldown:F2}");

        nextReadyTime = now + attack.cooldown;
        hitThisSwing.Clear();

        if (!string.IsNullOrEmpty(attackingBool))
        {
            animator.SetBool(attackingBool, true);
            Log("Set IsAttacking = TRUE");
        }

        if (!string.IsNullOrEmpty(attackTrigger))
        {
            animator.SetTrigger(attackTrigger);
            Log("Set Trigger: Attack");
        }

        if (attack.swingSfx)
            AudioSource.PlayClipAtPoint(attack.swingSfx, pivot.position, 0.9f);
    }

    // Animation Event at the IMPACT frame
    public void AnimationHitWindow()
    {
        if (!attack) return;

        Vector2 facing = GetFacing();
        if (facing.sqrMagnitude < 0.0001f) facing = Vector2.right;

        Vector2 center = (Vector2)pivot.position + facing.normalized * attack.forwardOffset;

        int count = Physics2D.OverlapBoxNonAlloc(center, attack.boxSize, 0f, hits, hittableLayers);
        Log($"HitWindow fired. facing={facing} center={center} size={attack.boxSize} hits={count}");

        for (int i = 0; i < count; i++)
        {
            var c = hits[i];
            if (!c) continue;

            int id = c.attachedRigidbody ? c.attachedRigidbody.GetInstanceID() : c.GetInstanceID();
            if (hitThisSwing.Contains(id))
            {
                Log($" - Skipping duplicate collider: {c.name} (id {id})");
                continue;
            }
            hitThisSwing.Add(id);

            IDamageable dmg = c.GetComponentInParent<IDamageable>();
            if (dmg == null)
            {
                Log($" - Collider {c.name} has no IDamageable on parent.");
                continue;
            }

            Vector2 kb = facing.normalized * attack.knockbackForce;
            var info = new HitInfo(attack.damage, kb, c.ClosestPoint(center), gameObject, attack);
            bool accepted = dmg.ReceiveHit(in info);

            Log($" - Hit {c.name}  accepted={accepted}  dmg={attack.damage}  kb={kb}");
            if (accepted && attack.hitSfx)
                AudioSource.PlayClipAtPoint(attack.hitSfx, info.hitPoint, 1.0f);
        }
    }

    // Animation Event near the LAST frame
    public void AnimationAttackEnd()
    {
        if (!string.IsNullOrEmpty(attackingBool))
        {
            animator.SetBool(attackingBool, false);
            Log("EndAttack event: Set IsAttacking = FALSE");
        }
    }

    Vector2 GetFacing()
    {
        float x = 0f, y = 0f;
        if (!string.IsNullOrEmpty(dirXParam)) x = animator.GetFloat(dirXParam);
        if (!string.IsNullOrEmpty(dirYParam)) y = animator.GetFloat(dirYParam);

        Vector2 f = new Vector2(x, y);
        if (f.sqrMagnitude > 0.0001f)
        {
            lastFacing = new Vector2(
                Mathf.Approximately(f.x, 0f) ? 0f : Mathf.Sign(f.x),
                Mathf.Approximately(f.y, 0f) ? 0f : Mathf.Sign(f.y)
            );
        }

        Log($"GetFacing -> anim({dirXParam}={x:F2}, {dirYParam}={y:F2}) -> {lastFacing}");
        return lastFacing;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawHitboxGizmo || attack == null) return;

        Vector2 facing = Application.isPlaying ? GetFacing() : Vector2.right;
        Vector2 center = ((pivot ? (Vector2)pivot.position : (Vector2)transform.position)
                         + facing.normalized * (attack ? attack.forwardOffset : 0.5f));

        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(new Vector3(center.x, center.y, 0f),
                        new Vector3(attack.boxSize.x, attack.boxSize.y, 0.1f));
    }
#endif
}
