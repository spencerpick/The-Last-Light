using UnityEngine;

[DisallowMultipleComponent]
public class EnemyDropOnDeath : MonoBehaviour
{
    [Header("Drop Settings")]
    [Range(0f,1f)] public float dropChance = 0.25f;
    public GameObject emberShardPrefab;   // assign the shard prefab
    public Vector2 dropOffsetJitter = new Vector2(0.1f, 0.1f);

    Health2D health;  // assumes a Health2D script that invokes a death event; if absent, we fall back to OnDisable
    bool handled;

    void Awake()
    {
        health = GetComponent<Health2D>();
        if (health)
        {
            // Try to hook a death callback if Health2D exposes a UnityEvent or C# event named onDeath
            var evt = health.GetType().GetField("onDeath");
            if (evt == null)
            {
                // no generic reflection; we’ll fallback to OnDisable as a simple heuristic if health gets destroyed on death
            }
        }
    }

    // Public API in case your Health2D can call it explicitly on death
    public void OnEnemyDied()
    {
        if (handled) return;
        handled = true;
        if (GameManager.Instance) GameManager.Instance.IncrementEnemiesKilled();
        TryDrop();
    }

    void OnDisable()
    {
        // Fallback: only drop if we are actually dead (health <= 0).
        // Prevents accidental drops when PCG spawns and destroys temporary room previews.
        if (handled) return;
        if (!gameObject.scene.isLoaded) return;
        if (health == null) return;
        if (health.currentHealth > 0f) return;

        if (GameManager.Instance) GameManager.Instance.IncrementEnemiesKilled();
        TryDrop();
        handled = true;
    }

    void TryDrop()
    {
        if (!emberShardPrefab) return;
        if (Random.value > Mathf.Clamp01(dropChance)) return;
        Vector3 pos = transform.position + new Vector3(Random.Range(-dropOffsetJitter.x, dropOffsetJitter.x), Random.Range(-dropOffsetJitter.y, dropOffsetJitter.y), 0f);
        Instantiate(emberShardPrefab, pos, Quaternion.identity);
    }
}


