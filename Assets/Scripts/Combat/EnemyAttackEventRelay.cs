using UnityEngine;

public class EnemyAttackEventRelay : MonoBehaviour
{
    private EnemyChaseAttack2D enemy;

    void Awake()
    {
        enemy = GetComponent<EnemyChaseAttack2D>() ?? GetComponentInParent<EnemyChaseAttack2D>();
    }

    // Animation Events must be public and have NO parameters
    public void AnimationHitWindow() { if (enemy) enemy.AnimationHitWindow(); }
    public void AnimationAttackEnd() { if (enemy) enemy.AnimationAttackEnd(); }
}
