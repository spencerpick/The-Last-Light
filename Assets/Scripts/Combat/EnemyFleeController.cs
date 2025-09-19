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
/// • When HP falls below threshold (or when TriggerFlee() is called), we:
///   - Pick a far "safe" point = an Anchor_* transform (from your room prefabs) that is FAR from the player
///     and not near the current position.
///   - Spawn/position a hidden dummy Transform there and temporarily set chase.player = dummy.
///   - Boost speed, disable attack, force path usage, set IsRunning = true.
/// • After a flee duration window, we restore the original target (real player), speed, and attack.
/// • If no anchors found, fall back to steer-away mode with obstacle probes so it won’t get stuck.
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

    // Internals
    Transform originalTarget;
    Transform dummyTarget;
    bool isFleeing = false;
    bool hasFledOnce = false;
    AttackData cachedAttackData;
    float cachedChaseSpeed;
    bool cachedForcePathAlways;
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
            chase.player = dummyTarget;

            // Let the chase script handle movement along its path for the duration
            float t = 0f;
            while (t < fleeSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            // No anchors found — fallback flee that avoids walls
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
}
