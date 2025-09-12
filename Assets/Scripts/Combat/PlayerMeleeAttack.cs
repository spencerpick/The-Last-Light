using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerMeleeAttack : MonoBehaviour
{
    [Header("Attack")]
    public AttackData attack;
    public LayerMask hittableLayers;
    public Transform pivot;

    [Header("Animator")]
    public string attackTrigger = "Attack";
    public string dirXParam = "MoveX";        // set to your param names
    public string dirYParam = "MoveY";
    public string attackingBool = "IsAttacking"; // <-- using your name

    [Header("Debug")]
    public bool drawHitboxGizmo = true;
    public Color gizmoColor = new Color(1f, 0.4f, 0.1f, 0.25f);

    Animator animator;
    float nextReadyTime;
    readonly Collider2D[] hits = new Collider2D[16];
    readonly HashSet<int> hitThisSwing = new HashSet<int>();
    Vector2 lastFacing = Vector2.right;

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (!pivot) pivot = transform;
    }

    void Update()
    {
        // TEMP input – replace with your input system when ready
        if (Input.GetButtonDown("Fire1"))
            TryStartAttack();
    }

    public void TryStartAttack()
    {
        if (!attack) { Debug.LogWarning("PlayerMeleeAttack: No AttackData assigned.", this); return; }
        if (Time.time < nextReadyTime) return;

        nextReadyTime = Time.time + attack.cooldown;
        hitThisSwing.Clear();

        if (!string.IsNullOrEmpty(attackingBool)) animator.SetBool(attackingBool, true); // lock movement transitions
        if (!string.IsNullOrEmpty(attackTrigger)) animator.SetTrigger(attackTrigger);

        if (attack.swingSfx) AudioSource.PlayClipAtPoint(attack.swingSfx, pivot.position, 0.9f);
    }

    // Animation Event at the IMPACT frame
    public void AnimationHitWindow()
    {
        if (!attack) return;

        Vector2 facing = GetFacing();
        if (facing.sqrMagnitude < 0.0001f) facing = Vector2.right;

        Vector2 center = (Vector2)pivot.position + facing.normalized * attack.forwardOffset;
        int count = Physics2D.OverlapBoxNonAlloc(center, attack.boxSize, 0f, hits, hittableLayers);

        for (int i = 0; i < count; i++)
        {
            var c = hits[i];
            if (!c) continue;

            int id = c.attachedRigidbody ? c.attachedRigidbody.GetInstanceID() : c.GetInstanceID();
            if (hitThisSwing.Contains(id)) continue;
            hitThisSwing.Add(id);

            IDamageable dmg = c.GetComponentInParent<IDamageable>();
            if (dmg == null) continue;

            Vector2 kb = facing.normalized * attack.knockbackForce;
            var info = new HitInfo(attack.damage, kb, c.ClosestPoint(center), gameObject, attack);
            bool accepted = dmg.ReceiveHit(in info);

            if (accepted && attack.hitSfx)
                AudioSource.PlayClipAtPoint(attack.hitSfx, info.hitPoint, 1.0f);
        }
    }

    // Animation Event near the LAST frame
    public void AnimationAttackEnd()
    {
        if (!string.IsNullOrEmpty(attackingBool))
            animator.SetBool(attackingBool, false); // unlock movement transitions
    }

    Vector2 GetFacing()
    {
        float x = !string.IsNullOrEmpty(dirXParam) ? animator.GetFloat(dirXParam) : 0f;
        float y = !string.IsNullOrEmpty(dirYParam) ? animator.GetFloat(dirYParam) : 0f;
        Vector2 f = new Vector2(x, y);

        if (f.sqrMagnitude > 0.0001f)
            lastFacing = new Vector2(
                Mathf.Approximately(f.x, 0f) ? 0f : Mathf.Sign(f.x),
                Mathf.Approximately(f.y, 0f) ? 0f : Mathf.Sign(f.y)
            );

        return lastFacing;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawHitboxGizmo || !attack) return;
        Transform p = pivot ? pivot : transform;

        Vector2 facing = Application.isPlaying ? GetFacing() : Vector2.right;
        Vector2 center = (Vector2)p.position + facing.normalized * attack.forwardOffset;

        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(new Vector3(center.x, center.y, 0f),
                        new Vector3(attack.boxSize.x, attack.boxSize.y, 0.1f));
    }
#endif
}
