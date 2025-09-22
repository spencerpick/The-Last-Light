// Plain-old struct describing a hit so we can pass consistent info around.
using UnityEngine;

public struct HitInfo
{
    public float damage;
    public Vector2 knockback;      // world-space impulse (dir * force)
    public Vector2 hitPoint;       // contact point (for VFX/SFX)
    public GameObject attacker;    // who caused the hit
    public AttackData attackData;  // data used for this hit

    public HitInfo(float damage, Vector2 knockback, Vector2 hitPoint, GameObject attacker, AttackData data)
    {
        this.damage = damage;
        this.knockback = knockback;
        this.hitPoint = hitPoint;
        this.attacker = attacker;
        this.attackData = data;
    }
}
