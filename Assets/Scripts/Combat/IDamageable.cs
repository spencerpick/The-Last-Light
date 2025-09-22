// Minimal damage interface so different things can be hit uniformly.
public interface IDamageable
{
    /// <summary>Return false if the target ignores/refuses the hit (e.g., dead or invulnerable).</summary>
    bool ReceiveHit(in HitInfo hit);
}
