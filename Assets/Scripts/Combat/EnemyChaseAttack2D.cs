using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyChaseAttack2D : MonoBehaviour
{
    public enum State { Wander, Chase, Attacking, Stunned }

    [Header("Target")]
    public Transform player;

    [Header("Movement")]
    public float chaseSpeed = 2.0f;
    public float detectionRadius = 6.0f;
    public float loseRadius = 8.0f;
    public float attackRange = 1.15f;
    [Tooltip("Inside this distance, stop pushing forward so we don't walk on the spot.")]
    public float holdDistance = 0.75f; // <= attackRange
    public LayerMask losBlockers;

    [Header("Facing Stabilization (no flicker)")]
    [Tooltip("Minimum time (seconds) between changes of facing direction.")]
    public float faceChangeCooldown = 0.15f;
    [Tooltip("Axis hysteresis in world units. The other axis must win by this margin to switch facing (prevents spam).")]
    public float faceAxisHysteresis = 0.20f;

    [Header("Avoidance (optional, mild)")]
    [Tooltip("If true, will briefly sidestep when LOS is blocked AND we are directly blocked ahead.")]
    public bool enableAvoidance = false;
    [Tooltip("How far ahead we check for an obstacle while chasing.")]
    public float avoidProbeDistance = 0.8f;
    [Tooltip("How far to offset left/right when testing side clearance.")]
    public float sideProbeOffset = 0.45f;
    [Tooltip("Commit to a chosen side step for this long to avoid jitter.")]
    public float sideCommitSeconds = 0.35f;

    [Header("Attack")]
    public AttackData attack;               // ScriptableObject
    public LayerMask hittableLayers;
    public Transform pivot;
    public string attackTrigger = "Attack";
    public Vector2 extraAttackDelayRange = new Vector2(0.2f, 0.6f);
    public float retreatSeconds = 0.3f;
    public float retreatSpeedMultiplier = 1.0f;

    [Header("On-Hit Stagger")]
    public float staggerSecondsOnHit = 0.3f;

    [Header("Animator Params (must match your controller)")]
    public string paramMoveX = "MoveX";
    public string paramMoveY = "MoveY";
    public string paramIsMoving = "IsMoving";
    public string paramSpeed = "Speed";
    public string paramIsAttacking = "IsAttacking";

    [Header("Animator state names (failsafe)")]
    public string attackStateName = "AttackBT";
    public int animatorLayerIndex = 0;
    [Range(0.5f, 1.1f)] public float attackFailSafeNormalized = 0.98f;
    public float attackFailSafeTimeout = 1.25f;

    [Header("Integration")]
    public EnemyWander2D wanderToToggle;

    [Header("Debug")]
    public bool verbose = false;

    // ───────── Internals
    Animator animator;
    Rigidbody2D rb;
    Collider2D bodyCol;
    State state = State.Wander;

    float nextReadyTime;
    float retreatTimer;
    float staggerTimer;
    float attackTimer;

    readonly Collider2D[] hitBuf = new Collider2D[8];
    readonly HashSet<int> hitThisSwing = new HashSet<int>();

    Vector2 lastFace = Vector2.right;     // current 4-dir facing used by Animator
    Vector2 prevPos;                      // for actual speed
    float lastFaceChangeTime = -999f;     // cooldown timer for facing changes

    // avoidance memory
    Vector2 committedSide = Vector2.zero;
    float sideTimer = 0f;

    // casts: ignore triggers, no layer mask (we ignore self & player manually)
    ContactFilter2D avoidFilter;

    const float EPS = 0.01f;

    void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        bodyCol = GetComponent<Collider2D>();
        if (!pivot) pivot = transform;

        if (!player)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }
        prevPos = transform.position;

        var hp = GetComponent<Health2D>();
        if (hp) hp.onDamaged.AddListener(OnDamagedStagger);

        avoidFilter = new ContactFilter2D { useLayerMask = false, useTriggers = false };
    }

    void OnEnable() => SetState(State.Wander);

    void FixedUpdate()
    {
        if (!player) { TryFindPlayer(); return; }

        // real speed (prevents walk-in-place visuals)
        float actualSpeed = ((Vector2)transform.position - prevPos).magnitude / Mathf.Max(Time.fixedDeltaTime, 1e-5f);
        prevPos = transform.position;

        // timers
        if (staggerTimer > 0f) staggerTimer -= Time.fixedDeltaTime;
        if (retreatTimer > 0f) retreatTimer -= Time.fixedDeltaTime;
        if (sideTimer > 0f) sideTimer -= Time.fixedDeltaTime; else committedSide = Vector2.zero;

        float dist = Vector2.Distance(transform.position, player.position);
        bool sees = HasLineOfSight();

        switch (state)
        {
            case State.Wander:
                if (dist <= detectionRadius && sees && staggerTimer <= 0f)
                    SetState(State.Chase);
                break;

            case State.Stunned:
                // Keep facing stable; show idle while sliding from knockback
                DriveAnim(false, lastFace, 0f);
                if (staggerTimer <= 0f) SetState(State.Chase);
                break;

            case State.Chase:
                {
                    if (dist > loseRadius || !sees) { SetState(State.Wander); break; }
                    if (staggerTimer > 0f) { SetState(State.Stunned); break; }

                    // Desired movement towards player (DIAGONAL allowed)
                    Vector2 moveDir = (player.position - transform.position);
                    if (moveDir.sqrMagnitude > 1e-6f) moveDir.Normalize(); else moveDir = Vector2.zero;

                    bool closeHold = dist <= Mathf.Max(holdDistance, 0.01f);
                    bool wantToMove = moveDir.sqrMagnitude > 0f && !closeHold;

                    // Optional avoidance: only when LOS is blocked AND we are blocked straight ahead
                    if (enableAvoidance && wantToMove && !HasLineOfSight() && CastBlocked(moveDir, avoidProbeDistance))
                        moveDir = ChooseSide(moveDir);

                    // Retreat after swing overrides chasing (move backwards a bit)
                    Vector2 velocityDir = (retreatTimer > 0f) ? (-lastFace) : moveDir;

                    rb.velocity = wantToMove ? velocityDir * (retreatTimer > 0f ? chaseSpeed * retreatSpeedMultiplier : chaseSpeed)
                                             : Vector2.zero;

                    // Update facing stably (4-dir), independent of diagonal movement
                    if (wantToMove)
                    {
                        Vector2 desiredFace = Snap4(moveDir); // quantize to 4-cardinal
                        desiredFace = ApplyFaceStabilization(desiredFace, lastFace, faceAxisHysteresis, faceChangeCooldown);
                        if (desiredFace != lastFace)
                        {
                            lastFace = desiredFace;
                            lastFaceChangeTime = Time.time;
                        }
                    }

                    // Drive animator from actual motion; facing from lastFace
                    DriveAnim(wantToMove && actualSpeed > 0.01f, lastFace, actualSpeed);

                    // Attack if allowed
                    bool canAttack = retreatTimer <= 0f && staggerTimer <= 0f && Time.time >= nextReadyTime;
                    if (canAttack && dist <= attackRange)
                        StartAttack(lastFace);
                    break;
                }

            case State.Attacking:
                rb.velocity = Vector2.zero;
                DriveAnim(false, lastFace, 0f);

                attackTimer += Time.fixedDeltaTime;
                var st = animator.GetCurrentAnimatorStateInfo(animatorLayerIndex);
                bool onAttack = st.IsName(attackStateName);
                bool clipEnded = onAttack && st.normalizedTime >= attackFailSafeNormalized;
                bool timedOut = attackTimer >= attackFailSafeTimeout;
                if (clipEnded || timedOut) ForceEndAttack();
                break;
        }
    }

    // ───────────────────── Facing helpers (stable 4-dir)
    Vector2 Snap4(Vector2 v)
    {
        // choose the nearest cardinal (no diagonal facing)
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y))
            return new Vector2(Mathf.Sign(v.x), 0f);
        else
            return new Vector2(0f, Mathf.Sign(v.y));
    }

    Vector2 ApplyFaceStabilization(Vector2 desired, Vector2 current, float axisHysteresisUnits, float cooldownSeconds)
    {
        // Cooldown: refuse to change facing too often
        if (Time.time < lastFaceChangeTime + Mathf.Max(0f, cooldownSeconds))
            return current;

        // Axis hysteresis: stick to current axis unless other axis wins by a margin
        bool currentIsHoriz = Mathf.Abs(current.x) > Mathf.Abs(current.y);
        Vector2 toPlayer = player ? (Vector2)(player.position - transform.position) : Vector2.zero;

        float ax = Mathf.Abs(toPlayer.x);
        float ay = Mathf.Abs(toPlayer.y);

        if (currentIsHoriz)
        {
            // stay horizontal unless vertical exceeds by margin
            if (ay <= ax + axisHysteresisUnits)
                return current;
        }
        else
        {
            // stay vertical unless horizontal exceeds by margin
            if (ax <= ay + axisHysteresisUnits)
                return current;
        }

        return desired;
    }

    // ───────────────────── Avoidance (optional & mild, committed)
    Vector2 ChooseSide(Vector2 forward)
    {
        if (committedSide.sqrMagnitude > 0.001f) return committedSide;

        Vector2 left = new Vector2(-forward.y, forward.x);
        Vector2 right = new Vector2(forward.y, -forward.x);

        float leftScore = SideClearance(forward, left);
        float rightScore = SideClearance(forward, right);

        committedSide = (leftScore >= rightScore) ? left : right;
        sideTimer = sideCommitSeconds;

        return committedSide;
    }

    bool CastBlocked(Vector2 dir, float dist)
    {
        if (dir.sqrMagnitude < 0.0001f || bodyCol == null) return false;
        var results = new RaycastHit2D[6];
        int hits = bodyCol.Cast(dir.normalized, avoidFilter, results, dist);
        for (int i = 0; i < hits; i++)
        {
            var h = results[i];
            if (!h.collider) continue;
            if (h.collider == bodyCol) continue;
            if (player && h.collider.transform == player) continue; // player isn’t an obstacle
            return true;
        }
        return false;
    }

    float SideClearance(Vector2 forward, Vector2 side)
    {
        if (bodyCol == null) return 0f;
        var results = new RaycastHit2D[6];

        // sideways peek
        int sideHits = bodyCol.Cast(side.normalized, avoidFilter, results, sideProbeOffset);
        float sideFree = sideHits == 0 ? sideProbeOffset : results[0].distance;

        // forward look
        int fHits = bodyCol.Cast(forward.normalized, avoidFilter, results, avoidProbeDistance);
        float fFree = fHits == 0 ? avoidProbeDistance : results[0].distance;

        // favor forward space heavily
        return fFree * 0.85f + sideFree * 0.15f;
    }

    // ───────────────────── State helpers, attacks, events
    void SetState(State s)
    {
        if (state == s) return;
        state = s;
        if (wanderToToggle) wanderToToggle.enabled = (state == State.Wander);
        if (verbose) Debug.Log($"[EnemyChaseAttack2D] {name} → {state}");
    }

    void StartAttack(Vector2 faceDir)
    {
        float extra = Random.Range(extraAttackDelayRange.x, extraAttackDelayRange.y);
        nextReadyTime = Time.time + (attack ? attack.cooldown : 0.5f) + Mathf.Max(0f, extra);

        hitThisSwing.Clear();
        attackTimer = 0f;

        if (faceDir.sqrMagnitude > EPS) lastFace = faceDir;

        if (!string.IsNullOrEmpty(paramIsAttacking)) animator.SetBool(paramIsAttacking, true);

        SetState(State.Attacking);
        DriveAnim(false, lastFace, 0f);
        if (!string.IsNullOrEmpty(attackTrigger)) animator.SetTrigger(attackTrigger);
    }

    public void AnimationHitWindow()
    {
        if (!attack) return;

        Vector2 dir = lastFace.sqrMagnitude > EPS ? lastFace.normalized : Vector2.right;
        Vector2 center = (Vector2)pivot.position + dir * attack.forwardOffset;

        int count = Physics2D.OverlapBoxNonAlloc(center, attack.boxSize, 0f, hitBuf, hittableLayers);
        for (int i = 0; i < count; i++)
        {
            var c = hitBuf[i];
            if (!c) continue;

            int id = c.attachedRigidbody ? c.attachedRigidbody.GetInstanceID() : c.GetInstanceID();
            if (hitThisSwing.Contains(id)) continue;
            hitThisSwing.Add(id);

            var dmg = c.GetComponentInParent<IDamageable>();
            if (dmg == null) continue;

            Vector2 kb = dir * attack.knockbackForce;
            var info = new HitInfo(attack.damage, kb, c.ClosestPoint(center), gameObject, attack);
            dmg.ReceiveHit(in info);
        }
    }

    public void AnimationAttackEnd() => ForceEndAttack();

    void ForceEndAttack()
    {
        if (!string.IsNullOrEmpty(paramIsAttacking))
            animator.SetBool(paramIsAttacking, false);

        retreatTimer = Mathf.Max(0f, retreatSeconds);

        if (state == State.Attacking)
        {
            SetState(State.Chase);
            prevPos = transform.position;
        }
    }

    void OnDamagedStagger()
    {
        if (staggerSecondsOnHit <= 0f) return;

        staggerTimer = staggerSecondsOnHit;

        if (!string.IsNullOrEmpty(paramIsAttacking))
            animator.SetBool(paramIsAttacking, false);

        if (state == State.Attacking) { state = State.Chase; }
        SetState(State.Stunned);
    }

    void DriveAnim(bool moving, Vector2 face, float speedMag)
    {
        if (!string.IsNullOrEmpty(paramMoveX)) animator.SetFloat(paramMoveX, face.x);
        if (!string.IsNullOrEmpty(paramMoveY)) animator.SetFloat(paramMoveY, face.y);
        if (!string.IsNullOrEmpty(paramIsMoving)) animator.SetBool(paramIsMoving, moving && speedMag > 0.01f);
        if (!string.IsNullOrEmpty(paramSpeed)) animator.SetFloat(paramSpeed, speedMag);
    }

    void TryFindPlayer()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p) player = p.transform;
    }

    bool HasLineOfSight()
    {
        if (losBlockers.value == 0) return true;
        Vector2 a = transform.position;
        Vector2 b = player ? (Vector2)player.position : a;
        var hit = Physics2D.Linecast(a, b, losBlockers);
        return !hit;
    }
}
