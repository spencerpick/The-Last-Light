// Spawns enemies when the player enters the room (or walks into a bounds), with optional fade-in.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemySpawnPoint : MonoBehaviour
{
    [Header("Who")]
    public List<GameObject> enemyPrefabs = new List<GameObject>();
    public List<float> weights = new List<float>();

    [Header("When")]
    public bool spawnOnPlayerEnterRoom = true;
    public bool spawnOnStart = false;
    public bool spawnOnlyOnce = true;
    public float spawnOnStartDelay = 0f; // optional delay to wait for PCG placement
    public bool logSpawns = false;       // debug logging
    public bool logWhyNotSpawning = false; // verbose gating reasons
    public RoomTrigger roomOverride;     // optional manual room reference
    public bool spawnWhenPlayerInsideBounds = true; // alternative to RoomTrigger: player position test

    [Header("Spawn FX")]
    public bool applyFadeInOnSpawn = true;
    public float fadeInDuration = 2.5f;
    public float fadeInStartAlpha = 0f;
    public Color fadeInStartTint = Color.black;

    [Header("How Many")]
    public int minCount = 1;
    public int maxCount = 1;

    [Header("Where")]
    public bool autoSizeToRoom = true;  // auto-detect room bounds
    public float edgePadding = 0.5f;    // shrink usable area away from walls
    public float manualRadius = 0f;     // used when auto=false
    public LayerMask avoidMask = 0;     // avoid overlapping walls
    public int maxPositionTries = 12;

    RoomTrigger myRoom;
    bool hasSpawned;
    Bounds roomBounds;
    bool boundsReady;
    float nextVerboseLogTime;

    void Awake()
    {
        ResolveRoom(force: true, log: false);
        if (maxCount < minCount) maxCount = minCount;
        if (!myRoom && autoSizeToRoom)
        {
            Debug.LogWarning($"[EnemySpawnPoint] No RoomTrigger found in parents for '{name}'. Falling back to manual radius.", this);
            autoSizeToRoom = false;
        }
        if (autoSizeToRoom) RebuildRoomBounds();
    }

    void Start()
    {
        if (spawnOnStart)
        {
            if (spawnOnStartDelay > 0f)
                StartCoroutine(SpawnAfterDelay(spawnOnStartDelay));
            else
                TrySpawn();
        }
    }

    System.Collections.IEnumerator SpawnAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        TrySpawn();
    }

    void Update()
    {
        if (hasSpawned && spawnOnlyOnce) return;

        bool spawned = false;

        if (spawnOnPlayerEnterRoom)
        {
            // Re-resolve room if null (handles late parenting after PCG)
            if (myRoom == null)
                ResolveRoom(force: true, log: logWhyNotSpawning);

            var gm = GameManager.Instance;
            if (gm == null)
            {
                if (logWhyNotSpawning && Time.time >= nextVerboseLogTime)
                {
                    Debug.Log("[EnemySpawnPoint] Waiting(Room): GameManager.Instance is null", this);
                    nextVerboseLogTime = Time.time + 1f;
                }
            }
            else if (myRoom == null)
            {
                if (logWhyNotSpawning && Time.time >= nextVerboseLogTime)
                {
                    Debug.Log("[EnemySpawnPoint] Waiting(Room): No RoomTrigger found in parents. Either add RoomTrigger or disable 'Spawn On Player Enter Room' and enable 'Spawn When Player Inside Bounds'", this);
                    nextVerboseLogTime = Time.time + 1f;
                }
            }
            else if (gm.CurrentRoom != myRoom)
            {
                if (logWhyNotSpawning && Time.time >= nextVerboseLogTime)
                {
                    string cur = gm.CurrentRoom ? $"{gm.CurrentRoom.name} (id={gm.CurrentRoom.roomID})" : "<none>";
                    Debug.Log($"[EnemySpawnPoint] Waiting(Room): CurrentRoom={cur} != myRoom={myRoom.name} (id={myRoom.roomID})", this);
                    nextVerboseLogTime = Time.time + 1f;
                }
            }
            else
            {
                TrySpawn();
                spawned = true;
            }
        }

        if (!spawned && spawnWhenPlayerInsideBounds)
        {
            // Bounds-based activation without RoomTrigger
            if (autoSizeToRoom && !boundsReady) RebuildRoomBounds();
            Transform player = FindPlayer();
            if (player == null)
            {
                if (logWhyNotSpawning && Time.time >= nextVerboseLogTime)
                {
                    Debug.Log("[EnemySpawnPoint] Waiting(Bounds): No Player found (tag 'Player')", this);
                    nextVerboseLogTime = Time.time + 1f;
                }
            }
            else if (IsInsideActivationArea(player.position))
            {
                TrySpawn();
            }
            else if (logWhyNotSpawning && Time.time >= nextVerboseLogTime)
            {
                Debug.Log($"[EnemySpawnPoint] Waiting(Bounds): Player {player.position} not inside area", this);
                nextVerboseLogTime = Time.time + 1f;
            }
        }
    }

    Transform FindPlayer()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        return go ? go.transform : null;
    }

    bool IsInsideActivationArea(Vector3 worldPos)
    {
        if (autoSizeToRoom && boundsReady)
        {
            Bounds b = roomBounds;
            b.Expand(new Vector3(-edgePadding * 2f, -edgePadding * 2f, 0f));
            return b.Contains(worldPos);
        }
        else
        {
            float r = Mathf.Max(0.01f, manualRadius);
            Vector2 d = (Vector2)(worldPos - transform.position);
            return d.sqrMagnitude <= r * r;
        }
    }

    void ResolveRoom(bool force, bool log)
    {
        if (!force && myRoom) return;
        myRoom = roomOverride ? roomOverride : GetComponentInParent<RoomTrigger>();
        if (myRoom) return;

        // Look for any Collider2D up the chain to help the user wire things correctly
        Transform t = transform;
        Collider2D foundCol = null;
        Transform foundColOwner = null;
        while (t != null)
        {
            var col = t.GetComponent<Collider2D>();
            if (col)
            {
                foundCol = col;
                foundColOwner = t;
                break;
            }
            t = t.parent;
        }
        if (log)
        {
            if (foundCol)
            {
                Debug.Log($"[EnemySpawnPoint] Found parent Collider2D on '{foundColOwner.name}' (isTrigger={foundCol.isTrigger}). Add RoomTrigger to this object and set isTrigger=true to drive CurrentRoom.", this);
            }
            else
            {
                Debug.Log("[EnemySpawnPoint] No RoomTrigger or Collider2D found in parents. Place spawner under a room root that has Collider2D (IsTrigger) + RoomTrigger.", this);
            }
        }
    }

    public void TrySpawn()
    {
        if (hasSpawned && spawnOnlyOnce) return;
        if (autoSizeToRoom && !boundsReady) RebuildRoomBounds();

        int count = Random.Range(minCount, Mathf.Max(minCount, maxCount) + 1);
        if (logSpawns)
        {
            Debug.Log($"[EnemySpawnPoint] Spawning {count} from '{name}' at {transform.position} (autoSize={autoSizeToRoom}, room={(myRoom? myRoom.name: "<none>")})", this);
        }
        for (int i = 0; i < count; i++)
        {
            var prefab = ChoosePrefab();
            if (!prefab) continue;
            Vector3 pos = FindSpawnPosition();
            if (logSpawns)
            {
                Debug.Log($"[EnemySpawnPoint]  -> #{i+1} '{prefab.name}' at {pos}", this);
            }
            var go = Instantiate(prefab, pos, Quaternion.identity);
            if (applyFadeInOnSpawn && go)
            {
                var fade = go.AddComponent<SpawnFadeIn>();
                fade.duration = Mathf.Max(0f, fadeInDuration);
                fade.startAlpha = Mathf.Clamp01(fadeInStartAlpha);
                fade.startTint = fadeInStartTint;
            }
        }
        hasSpawned = true;
    }

    GameObject ChoosePrefab()
    {
        if (enemyPrefabs == null || enemyPrefabs.Count == 0) return null;
        if (weights == null || weights.Count != enemyPrefabs.Count)
            return enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];

        float sum = 0f;
        for (int i = 0; i < weights.Count; i++) sum += Mathf.Max(0f, weights[i]);
        if (sum <= 0f) return enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];
        float r = Random.value * sum;
        for (int i = 0; i < enemyPrefabs.Count; i++)
        {
            r -= Mathf.Max(0f, weights[i]);
            if (r <= 0f) return enemyPrefabs[i];
        }
        return enemyPrefabs[enemyPrefabs.Count - 1];
    }

    Vector3 FindSpawnPosition()
    {
        if (!autoSizeToRoom)
        {
            if (manualRadius <= 0.001f) return transform.position;
            for (int t = 0; t < Mathf.Max(1, maxPositionTries); t++)
            {
                Vector2 off = Random.insideUnitCircle * manualRadius;
                Vector3 p = transform.position + new Vector3(off.x, off.y, 0f);
                if (avoidMask.value == 0 || !Physics2D.OverlapCircle(p, 0.15f, avoidMask))
                    return p;
            }
            return transform.position;
        }

        if (!boundsReady) return transform.position;

        Bounds b = roomBounds;
        b.Expand(new Vector3(-edgePadding * 2f, -edgePadding * 2f, 0f));
        if (b.size.x <= 0.1f || b.size.y <= 0.1f) return transform.position;

        for (int t = 0; t < Mathf.Max(1, maxPositionTries); t++)
        {
            float x = Random.Range(b.min.x, b.max.x);
            float y = Random.Range(b.min.y, b.max.y);
            Vector3 p = new Vector3(x, y, 0f);
            if (avoidMask.value == 0 || !Physics2D.OverlapCircle(p, 0.15f, avoidMask))
                return p;
        }
        return b.center;
    }

    void RebuildRoomBounds()
    {
        boundsReady = false;
        if (myRoom == null) ResolveRoom(force: true, log: false);
        Transform root = myRoom ? myRoom.transform : transform.root;
        if (!root) return;

        bool any = false;
        Bounds b = new Bounds(root.position, Vector3.one * 0.1f);

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (!r || !r.enabled) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        // If no renderers provided bounds, try Collider2D bounds as a fallback
        if (!any)
        {
            var cols = root.GetComponentsInChildren<Collider2D>(true);
            foreach (var c in cols)
            {
                if (!c) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
        }

        if (any)
        {
            roomBounds = b;
            boundsReady = true;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.35f);
        if (autoSizeToRoom && boundsReady)
        {
            Bounds b = roomBounds;
            b.Expand(new Vector3(-edgePadding * 2f, -edgePadding * 2f, 0f));
            Gizmos.DrawCube(b.center, b.size);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, manualRadius));
        }
    }
#endif
}

