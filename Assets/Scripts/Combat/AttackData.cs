using UnityEngine;

[CreateAssetMenu(fileName = "AttackData", menuName = "Combat/Attack Data", order = 0)]
public class AttackData : ScriptableObject
{
    [Header("Numbers")]
    [Min(0f)] public float damage = 1f;
    [Min(0f)] public float knockbackForce = 3f;
    [Min(0f)] public float cooldown = 0.25f;

    [Header("Hitbox shape (world units)")]
    [Tooltip("Width (X) and Height (Y) of the attack zone.")]
    public Vector2 boxSize = new Vector2(1.1f, 0.8f);

    [Tooltip("How far forward (local facing) the hitbox center sits from the player pivot.")]
    public float forwardOffset = 0.55f;

    [Header("FX (optional)")]
    public AudioClip swingSfx;
    public AudioClip hitSfx;
}
