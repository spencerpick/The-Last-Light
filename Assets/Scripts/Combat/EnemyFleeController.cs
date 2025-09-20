using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Plug-in flee brain that layers on top of your existing stack:
/// - Health2D (to read HP)
/// - EnemyChaseAttack2D (we reuse its A* by swapping its "player" target temporarily)
/// - Animator (we set IsRunning true while fleeing)
///
/// Behavior:
/// � When HP falls below threshold (or when TriggerFlee() is called), we:
///   - Pick a far "safe" point = an Anchor_* transform (from your room prefabs) that is FAR from the player
///     and not near the current position.
///   - Spawn/position a hidden dummy Transform there and temporarily set chase.player = dummy.
///   - Boost speed, disable attack, force path usage, set IsRunning = true.
/// � After a flee duration window, we restore the original target (real player), speed, and attack.
/// � If no anchors found, fall back to steer-away mode with obstacle probes so it won�t get stuck.
/// </summary>
[DisallowMultipleComponent]
public class EnemyFleeController : MonoBehaviour
{
    [Header("Wiring (auto if left empty)")]
    public Health2D health;
    public EnemyChaseAttack2D chase;
    public Animator animator;

    [Header("Flee Trigger")]
    [Tooltip("Start fleeing when currentHealth / maxHealth <= this fraction.")]
    [Range(0.05f, 0.95f)] public float lowHealthFraction = 0.35f;
    [Tooltip("Can this enemy flee multiple times if it dips below the threshold again?")]
    public bool canFleeMultipleTimes = false;

    [Header("Flee Window")]
    [Tooltip("Seconds to keep fleeing before resuming normal behavior.")]
    public Vector2 fleeDurationRange = new Vector2(2.8f, 4.0f);
    [Tooltip("Extra speed while fleeing (applied to chase.chaseSpeed).")]
    public float runSpeedMultiplier = 1.5f;
    [Tooltip("Set Animator Bool while fleeing.")]
    public string isRunningParam = "IsRunning";
    [Tooltip("Avoid anchors within this distance of the serpent to reduce 'same room' picks.")]
    public float minAnchorDistanceFromSelf = 4.0f;
    [Tooltip("Prefer anchors at least this far from the player (encourages opposite-side rooms).")]
    public float preferAnchorFartherThanPlayer = 6.0f;

    [Header("Anchor Discovery")]
    [Tooltip("We consider transforms whose name starts with this (e.g., Anchor_Left/Right/Top/Bottom).")]
    public string anchorNamePrefix = "Anchor_";
    [Tooltip("LayerMask used for obstacle probing when in fallback flee.")]
    public LayerMask obstacleMask = ~0;
    [Tooltip("Fallback probe distance for wall-avoidance when not using anchors.")]
    public float steerProbeDistance = 0.7f;

    [Header("Integrations")]
    [Tooltip("Disable attacking during flee by detaching AttackData temporarily.")]
    public bool disableAttackDuringFlee = true;
    [Tooltip("Force path usage during flee (helps commit to the flee route).")]
    public bool forcePathAlwaysDuringFlee = true;

    [Header("Pathfinding During Flee")]
    [Tooltip("Auto expand EnemyChaseAttack2D.pathWorldSize so the grid spans from serpent to anchor.")]
    public bool autoExpandWorldSizeDuringFlee = true;
    [Tooltip("Extra margin added around the start/goal bounds when expanding world size.")]
    public float worldSizeMargin = 2f;
    [Tooltip("Minimum world size while fleeing to avoid tiny grids.")]
    public Vector2 minWorldSizeDuringFlee = new Vector2(8f, 8f);
    [Tooltip("Temporarily enable chase.debugDraw while fleeing to mirror chase visuals.")]
    public bool enableChaseDebugDrawDuringFlee = true;
    [Tooltip("Also enable chase.debugLog while fleeing to see A* diagnostics.")]
    public bool enableChaseDebugLogDuringFlee = true;
    [Tooltip("Clamp the maximum world size when auto-expanding.")]
    public Vector2 maxWorldSizeDuringFlee = new Vector2(64f, 64f);

    [Header("Debug Visualization")]
    [Tooltip("Show debug gizmos for fleeing path and target anchor.")]
    public bool showDebugGizmos = true;
    [Tooltip("Color for the fleeing path line (similar to chase pathfinding).")]
    public Color fleePathColor = Color.cyan;
    [Tooltip("Color for the target anchor marker.")]
    public Color targetAnchorColor = Color.yellow;
    [Tooltip("Size of the target anchor marker.")]
    public float anchorMarkerSize = 0.3f;
    [Tooltip("Show all available anchors as small markers.")]
    public bool showAllAnchors = false;
    [Tooltip("Color for all available anchors (smaller markers).")]
    public Color allAnchorsColor = new Color(1f, 1f, 1f, 0.3f);

    [Header("Debug Logging")]
    [Tooltip("Log chosen anchor and path details to the Console while fleeing.")]
    public bool logFleeDebug = true;

    // Internals
    Transform originalTarget;
    Transform dummyTarget;
    bool isFleeing = false;
    bool hasFledOnce = false;
    AttackData cachedAttackData;
    float cachedChaseSpeed;
    bool cachedForcePathAlways;
    Vector2 cachedPathWorldSize;
    bool cachedUseGridPathfinding;
    bool cachedDebugDraw;
    bool cachedDebugLog;
    int isRunningHash;

    void Awake()
    {
        if (!health) health = GetComponent<Health2D>();
        if (!chase) chase = GetComponent<EnemyChaseAttack2D>();
        if (!animator) animator = GetComponentInChildren<Animator>() ?? GetComponent<Animator>();

        isRunningHash = !string.IsNullOrEmpty(isRunningParam) ? Animator.StringToHash(isRunningParam) : 0;

        // Prepare dummy target holder
        var go = new GameObject($"{name}_FleeTarget");
        go.hideFlags = HideFlags.HideInHierarchy;
        dummyTarget = go.transform;
    }

    void OnDestroy()
    {
        if (dummyTarget) Destroy(dummyTarget.gameObject);
    }

    void Update()
    {
        if (isFleeing || health == null || chase == null) return;
        if (lowHealthFraction <= 0f) return;

        float frac = health.currentHealth / Mathf.Max(1f, health.maxHealth);
        if (frac <= lowHealthFraction)
        {
            if (!hasFledOnce || canFleeMultipleTimes)
            {
                StartCoroutine(FleeRoutine());
            }
        }
    }

    void FixedUpdate()
    {
        // Draw fleeing path debug lines (same as EnemyChaseAttack2D does in FixedUpdate)
        if (isFleeing && showDebugGizmos && chase != null && chase.path != null && chase.path.Count > 0)
        {
            for (int i = 0; i < chase.path.Count - 1; i++)
            {
                Debug.DrawLine(chase.path[i], chase.path[i + 1], fleePathColor, Time.fixedDeltaTime);
            }
        }
    }

    /// <summary>Call this from anywhere (e.g., Health2D OnDamaged event) to force a flee now.</summary>
    public void TriggerFlee()
    {
        if (!isFleeing && (!hasFledOnce || canFleeMultipleTimes))
            StartCoroutine(FleeRoutine());
    }

    IEnumerator FleeRoutine()
    {
        isFleeing = true;
        hasFledOnce = true;

        // Cache + prep
        originalTarget = chase.player;
        cachedChaseSpeed = chase.chaseSpeed;
        cachedForcePathAlways = chase.forcePathAlways;
        chase.externalControlActive = true;
        if (disableAttackDuringFlee)
        {
            cachedAttackData = chase.attack;
            chase.attack = null; // prevents attack attempts from doing anything
        }
        chase.chaseSpeed = Mathf.Max(0.1f, cachedChaseSpeed) * Mathf.Max(1f, runSpeedMultiplier);
        if (forcePathAlwaysDuringFlee) chase.forcePathAlways = true;

        // Animator: run ON
        if (isRunningHash != 0) animator.SetBool(isRunningHash, true);

        // Pick an anchor far from player & current position
        Transform anchor = PickBestAnchor();
        float fleeSeconds = Random.Range(fleeDurationRange.x, fleeDurationRange.y);

        if (anchor != null)
        {
            // Use A* by redirecting EnemyChaseAttack2D to a dummy target placed at the anchor
            dummyTarget.position = anchor.position;

            // Cache + adjust pathfinding envelope so grid spans from current pos to anchor
            cachedPathWorldSize = chase.pathWorldSize;
            cachedUseGridPathfinding = chase.useGridPathfinding;
            cachedDebugDraw = chase.debugDraw;
            cachedDebugLog = chase.debugLog;
            if (enableChaseDebugDrawDuringFlee) chase.debugDraw = true;
            if (enableChaseDebugLogDuringFlee) chase.debugLog = true;
            chase.useGridPathfinding = true;

            if (autoExpandWorldSizeDuringFlee)
            {
                Vector2 start = transform.position;
                Vector2 goal = dummyTarget.position;
                float w = Mathf.Abs(goal.x - start.x) + worldSizeMargin * 2f;
                float h = Mathf.Abs(goal.y - start.y) + worldSizeMargin * 2f;
                w = Mathf.Clamp(w, minWorldSizeDuringFlee.x, maxWorldSizeDuringFlee.x);
                h = Mathf.Clamp(h, minWorldSizeDuringFlee.y, maxWorldSizeDuringFlee.y);
                chase.pathWorldSize = new Vector2(w, h);
            }

            chase.player = dummyTarget;

            if (logFleeDebug)
            {
                Debug.Log($"[FLEE:{name}] Anchor='{anchor.name}' pos={anchor.position}  dist={(Vector2.Distance(transform.position, anchor.position)).ToString("F2")}  pathWorldSize={chase.pathWorldSize}  cellSize={chase.pathCellSize}");
            }

            // Let the chase script handle movement along its path for the duration
            chase.ForceChaseCurrentTarget(clearExistingPath: true);
            float t = 0f;
            int prevCount = -1;
            int prevIndex = -1;
            float nextLog = 0f;
            while (t < fleeSeconds)
            {
                t += Time.deltaTime;
                // Snapshot path periodically or when it changes
                if (chase != null && chase.path != null)
                {
                    if (chase.path.Count != prevCount || chase.pathIndex != prevIndex || Time.time >= nextLog)
                    {
                        prevCount = chase.path.Count;
                        prevIndex = chase.pathIndex;
                        nextLog = Time.time + 0.75f;
                        if (logFleeDebug)
                        {
                            var preview = "";
                            int n = Mathf.Min(chase.path.Count, 6);
                            for (int i = 0; i < n; i++)
                            {
                                preview += i == 0 ? $"{chase.path[i].ToString()}" : $" -> {chase.path[i].ToString()}";
                            }
                            Debug.Log($"[FLEE:{name}] pathCount={chase.path.Count} pathIndex={chase.pathIndex} useGrid={chase.useGridPathfinding} forcePathAlways={chase.forcePathAlways} debugDraw={chase.debugDraw} preview=[{preview}] ");
                        }
                        // trigger recalculation next frame in case we're still empty
                        if (chase.path.Count == 0)
                            chase.ForcePathRecalcNow();
                    }
                }
                yield return null;
            }
        }
        else
        {
            // No anchors found – fallback flee that avoids walls
            if (logFleeDebug)
                Debug.Log($"[FLEE:{name}] No anchors found. Using fallback flee.");
            float t = 0f;
            while (t < fleeSeconds)
            {
                t += Time.deltaTime;
                DoFallbackFleeStep();
                yield return new WaitForFixedUpdate();
            }
        }

        // Restore behavior
        chase.player = originalTarget;
        chase.chaseSpeed = cachedChaseSpeed;
        if (disableAttackDuringFlee) chase.attack = cachedAttackData;
        if (forcePathAlwaysDuringFlee) chase.forcePathAlways = cachedForcePathAlways;
        if (autoExpandWorldSizeDuringFlee) chase.pathWorldSize = cachedPathWorldSize;
        chase.useGridPathfinding = cachedUseGridPathfinding;
        if (enableChaseDebugDrawDuringFlee) chase.debugDraw = cachedDebugDraw;
        if (enableChaseDebugLogDuringFlee) chase.debugLog = cachedDebugLog;
        chase.externalControlActive = false;
        if (enableChaseDebugLogDuringFlee) chase.debugLog = cachedDebugLog;

        // Animator: run OFF
        if (isRunningHash != 0) animator.SetBool(isRunningHash, false);

        isFleeing = false;
    }

    // ---------- Anchor Selection ----------
    Transform PickBestAnchor()
    {
        // find all "Anchor_*" transforms in the scene
        var allTransforms = FindObjectsOfType<Transform>();
        var anchors = new List<Transform>();

        foreach (var t in allTransforms)
        {
            if (t == null) continue;
            if (!t.gameObject.activeInHierarchy) continue;
            if (t.name.StartsWith(anchorNamePrefix)) anchors.Add(t);
        }

        if (anchors.Count == 0) return null;

        Vector3 self = transform.position;
        Vector3 playerPos = (chase.player ? chase.player.position : self);

        // rank by: far from player + far from self, with simple exclusions to avoid "same room"
        var ranked = anchors
            .Where(a =>
            {
                float dSelf = Vector2.Distance(a.position, self);
                return dSelf >= minAnchorDistanceFromSelf; // avoid anchors right next to us
            })
            .Select(a =>
            {
                float dPlayer = Vector2.Distance(a.position, playerPos);
                float dSelf = Vector2.Distance(a.position, self);
                // score: weight player-distance heavily, then self-distance
                float score = dPlayer * 1.4f + dSelf * 0.6f;
                // soft preference: make sure it's not extremely close to the player
                if (dPlayer < preferAnchorFartherThanPlayer) score *= 0.5f;
                return (anchor: a, score);
            })
            .OrderByDescending(x => x.score)
            .ToList();

        return ranked.Count > 0 ? ranked[0].anchor : null;
    }

    // ---------- Fallback flee without anchors: steer away + avoid walls ----------
    void DoFallbackFleeStep()
    {
        // simple steer opposite the player with obstacle avoidance
        if (chase == null || chase.player == null) return;
        var rb = GetComponent<Rigidbody2D>();
        if (!rb) return;

        Vector2 away = (Vector2)(transform.position - chase.player.position);
        if (away.sqrMagnitude < 1e-4f) away = Random.insideUnitCircle.normalized;
        away.Normalize();

        // wall probe: if blocked ahead, try perpendiculars
        if (Physics2D.Raycast(transform.position, away, steerProbeDistance, obstacleMask))
        {
            Vector2 left = new Vector2(-away.y, away.x).normalized;
            Vector2 right = -left;
            if (!Physics2D.Raycast(transform.position, left, steerProbeDistance * 0.8f, obstacleMask))
                away = left;
            else if (!Physics2D.Raycast(transform.position, right, steerProbeDistance * 0.8f, obstacleMask))
                away = right;
            else
                away = -away; // last resort: reverse
        }

        float speed = Mathf.Max(0.1f, chase.chaseSpeed);
        rb.velocity = away * speed;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;

        // Draw all available anchors if enabled
        if (showAllAnchors)
        {
            var allTransforms = FindObjectsOfType<Transform>();
            Gizmos.color = allAnchorsColor;
            foreach (var t in allTransforms)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (t.name.StartsWith(anchorNamePrefix))
                {
                    Gizmos.DrawWireSphere(t.position, anchorMarkerSize * 0.3f);
                }
            }
        }

        // Draw fleeing path and target anchor when fleeing
        if (isFleeing && chase != null)
        {
            // Draw waypoint spheres (same size as EnemyChaseAttack2D)
            if (chase.path != null && chase.path.Count > 0)
            {
                Gizmos.color = fleePathColor;
                for (int i = 0; i < chase.path.Count; i++)
                {
                    Gizmos.DrawSphere(chase.path[i], 0.06f);
                }
                
                // Draw current path index indicator
                if (chase.pathIndex < chase.path.Count)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawSphere(chase.path[chase.pathIndex], 0.08f);
                }
            }

            // Draw target anchor marker
            if (dummyTarget != null)
            {
                Gizmos.color = targetAnchorColor;
                Gizmos.DrawWireSphere(dummyTarget.position, anchorMarkerSize);
                
                // Draw a cross marker for extra visibility
                float crossSize = anchorMarkerSize * 0.5f;
                Vector3 pos = dummyTarget.position;
                Gizmos.DrawLine(pos + Vector3.left * crossSize, pos + Vector3.right * crossSize);
                Gizmos.DrawLine(pos + Vector3.up * crossSize, pos + Vector3.down * crossSize);
            }
            
            // Debug info: show current target
            if (chase.player != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(chase.player.position, 0.2f);
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        // Always show current flee status
        if (isFleeing)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }
#endif
}
