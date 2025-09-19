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
    [Tooltip("Inside this distance, stop pushing forward only if LOS is clear (prevents 'stuck at obstacle').")]
    public float holdDistance = 0.75f;
    [Tooltip("Which layers block line-of-sight to the player (your walls/props).")]
    public LayerMask losBlockers = ~0;

    [Header("Facing Stabilization (no flicker)")]
    public float faceChangeCooldown = 0.15f;
    public float faceAxisHysteresis = 0.20f;

    // ───────────────────────── PATHFINDING ─────────────────────────
    [Header("Pathfinding (runtime A*)")]
    public bool useGridPathfinding = true;
    public float pathCellSize = 0.32f;
    public Vector2 pathWorldSize = new Vector2(16f, 16f);
    public LayerMask pathObstacleMask = ~0;
    public bool pathIncludeTriggers = true;
    public bool pathAllowDiagonal = false;
    public float agentRadius = 0.20f;
    public float pathRecalcInterval = 0.35f;
    public float waypointReachRadius = 0.18f;
    [Tooltip("If movement toward a waypoint is blocked, probe this far and slide along the obstacle.")]
    public float pathSteerProbeDistance = 0.35f;
    [Tooltip("If true, steer toward the closest point on the current path segment (less jitter in tight spaces).")]
    public bool steerToSegment = true;
    [Tooltip("Lateral offset used when hugging around corners along a path segment.")]
    public float pathLateralOffset = 0.18f;
    [Header("Corner Escape")]
    [Tooltip("How long to commit to a corner nudge when stuck on a waypoint.")]
    public float cornerNudgeSeconds = 0.25f;
    [Tooltip("Probe distance to evaluate left/right clearance when stuck.")]
    public float cornerProbeDistance = 0.5f;
    [Tooltip("When stuck, probe all 8 directions and commit to the one that gets closest to the path.")]
    public bool useStuckEscape = true;
    [Tooltip("How long to commit to a stuck-escape direction.")]
    public float stuckEscapeSeconds = 0.4f;

    [Header("Fallback slide when no path")]
    public float slideProbeDistance = 0.8f;
    public float slideCommitSeconds = 0.4f;

    // ───────────────────────── ATTACK ─────────────────────────
    [Header("Attack")]
    public AttackData attack;
    public LayerMask hittableLayers;
    public Transform pivot;
    public string attackTrigger = "Attack";
    public Vector2 extraAttackDelayRange = new Vector2(0.2f, 0.6f);
    public float retreatSeconds = 0.3f;
    public float retreatSpeedMultiplier = 1.0f;

    [Header("On-Hit Stagger")]
    public float staggerSecondsOnHit = 0.3f;

    // ───────────────────────── ANIMATOR ─────────────────────────
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

    [Header("Debug / Test")]
    public bool forcePathAlways = false;
    public bool debugDraw = false;
    public bool debugLog = false;
    public bool drawPathGrid = true;
    public Color gridColor = new Color(0f, 1f, 1f, 0.06f);

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

    Vector2 lastFace = Vector2.right;
    Vector2 prevPos;
    float lastFaceChangeTime = -999f;

    // Path
    readonly List<Vector2> path = new List<Vector2>(64);
    int pathIndex = 0;
    float nextPathTime = 0f;
    Vector3 lastPlayerPos;
    float waypointStuckTimer = 0f;
    float lastWaypointDist = 999f;
    float cornerNudgeTimer = 0f;
    Vector2 cornerNudgeDir = Vector2.zero;
    float stuckEscapeTimer = 0f;
    Vector2 stuckEscapeDir = Vector2.zero;

    // sampling buffer
    readonly Collider2D[] overlapBuf = new Collider2D[32];

    // slide fallback
    Vector2 committedSlide = Vector2.zero;
    float slideTimer = 0f;

    ContactFilter2D castFilter;

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
        lastPlayerPos = player ? player.position : transform.position;

        var hp = GetComponent<Health2D>();
        if (hp) hp.onDamaged.AddListener(OnDamagedStagger);

        if (pathObstacleMask.value == 0) pathObstacleMask = losBlockers;

        castFilter = new ContactFilter2D { useLayerMask = true, useTriggers = pathIncludeTriggers };
        castFilter.SetLayerMask(pathObstacleMask);
    }

    void OnEnable() => SetState(State.Wander);

    void FixedUpdate()
    {
        if (!player) { TryFindPlayer(); return; }

        float actualSpeed = ((Vector2)transform.position - prevPos).magnitude / Mathf.Max(Time.fixedDeltaTime, 1e-5f);
        prevPos = transform.position;

        if (staggerTimer > 0f) staggerTimer -= Time.fixedDeltaTime;
        if (retreatTimer > 0f) retreatTimer -= Time.fixedDeltaTime;

        castFilter.useTriggers = pathIncludeTriggers;
        castFilter.SetLayerMask(pathObstacleMask);

        float dist = Vector2.Distance(transform.position, player.position);
        bool losClear = HasLineOfSight();

        switch (state)
        {
            case State.Wander:
                if (dist <= detectionRadius && staggerTimer <= 0f)
                    SetState(State.Chase);
                DriveAnim(false, lastFace, 0f);
                break;

            case State.Stunned:
                DriveAnim(false, lastFace, 0f);
                if (staggerTimer <= 0f) SetState(State.Chase);
                break;

            case State.Chase:
                {
                    if (dist > loseRadius) { ClearPath(); SetState(State.Wander); break; }
                    if (staggerTimer > 0f) { ClearPath(); SetState(State.Stunned); break; }

                    Vector2 moveDir = Vector2.zero;
                    bool usingPath = false;

                    bool wantPath = useGridPathfinding && (!losClear || forcePathAlways || (path.Count > 0));
                    if (wantPath)
                    {
                        RecalcPathIfNeeded();
                        if (path.Count > 0 && pathIndex < path.Count)
                        {
                            usingPath = true;
                            Vector2 target = path[pathIndex];

                            // Optional: if next node is roughly aligned and unobstructed, skip current node
                            if (pathIndex + 1 < path.Count)
                            {
                                Vector2 nextNode = path[pathIndex + 1];
                                if (IsLineClear((Vector2)transform.position, nextNode))
                                {
                                    // only skip if it shortens path meaningfully
                                    float dCur = Vector2.Distance(transform.position, target);
                                    float dNext = Vector2.Distance(transform.position, nextNode);
                                    if (dNext + pathCellSize * 0.25f <= dCur)
                                    {
                                        pathIndex++;
                                        target = path[pathIndex];
                                    }
                                }
                            }

                            Vector2 delta = target - (Vector2)transform.position;
                            float curDist = delta.magnitude;

                            if (curDist <= Mathf.Max(waypointReachRadius, pathCellSize * 0.35f))
                            {
                                pathIndex++;
                                if (pathIndex < path.Count)
                                {
                                    target = path[pathIndex];
                                    delta = target - (Vector2)transform.position;
                                    curDist = delta.magnitude;
                                }
                                waypointStuckTimer = 0f;
                                lastWaypointDist = 999f;
                            }

                            // Steer to closest point on current segment to stay in free space
                            if (steerToSegment && pathIndex > 0)
                            {
                                Vector2 a = path[pathIndex - 1];
                                Vector2 b = target;
                                Vector2 cp = ClosestPointOnSegment(a, b, (Vector2)transform.position);
                                Vector2 toCp = cp - (Vector2)transform.position;
                                if (toCp.sqrMagnitude > (pathCellSize * 0.15f) * (pathCellSize * 0.15f))
                                {
                                    delta = toCp;
                                }
                            }

                            moveDir = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector2.zero;

                            // Prefer moving along the segment direction (reduces oscillation near corners)
                            if (pathIndex > 0)
                            {
                                Vector2 a = path[pathIndex - 1];
                                Vector2 b = target;
                                Vector2 seg = (b - a);
                                if (seg.sqrMagnitude > 1e-6f)
                                {
                                    Vector2 segDir = seg.normalized;
                                    // If heading roughly toward the segment but blocked, try small lateral offsets along corridor
                                    if (IsBlocked(segDir, Mathf.Max(pathSteerProbeDistance, pathCellSize * 0.6f), out var blk))
                                    {
                                        Vector2 perp = new Vector2(-segDir.y, segDir.x);
                                        Vector2 tryL = (segDir + perp * Mathf.Sign(Vector2.Dot(perp, (Vector2)transform.position - a)) * 0.25f).normalized;
                                        if (!IsBlocked(tryL, Mathf.Max(pathSteerProbeDistance, pathCellSize * 0.6f), out _))
                                            moveDir = tryL;
                                        else
                                        {
                                            Vector2 tryR = (segDir - perp * 0.25f).normalized;
                                            if (!IsBlocked(tryR, Mathf.Max(pathSteerProbeDistance, pathCellSize * 0.6f), out _))
                                                moveDir = tryR;
                                        }
                                    }
                                    else
                                    {
                                        // If not blocked and we're close to the wall, bias slightly sideways to create clearance
                                        if (pathLateralOffset > 0f)
                                        {
                                            // sample which side has more free space
                                            Vector2 perp = new Vector2(-segDir.y, segDir.x);
                                            float lFree = EstimateFreeAlong(perp, pathSteerProbeDistance);
                                            float rFree = EstimateFreeAlong(-perp, pathSteerProbeDistance);
                                            if (Mathf.Abs(lFree - rFree) > 0.05f)
                                            {
                                                Vector2 bias = (lFree > rFree ? perp : -perp) * Mathf.Min(0.5f, pathLateralOffset);
                                                moveDir = (segDir + bias).normalized;
                                            }
                                            else moveDir = segDir;
                                        }
                                        else moveDir = segDir;
                                    }
                                }
                            }

                            // Waypoint progress watchdog: if we fail to reduce distance for a short time, try advancing
                            if (pathIndex < path.Count)
                            {
                                if (curDist < lastWaypointDist - 0.01f) { waypointStuckTimer = 0f; lastWaypointDist = curDist; }
                                else { waypointStuckTimer += Time.fixedDeltaTime; }

                                if (waypointStuckTimer >= 0.25f && pathIndex + 1 < path.Count)
                                {
                                    Vector2 nextNode = path[pathIndex + 1];
                                    if (IsLineClear((Vector2)transform.position, nextNode))
                                    {
                                        pathIndex++;
                                        target = path[pathIndex];
                                        delta = target - (Vector2)transform.position;
                                        curDist = delta.magnitude;
                                        waypointStuckTimer = 0f;
                                        lastWaypointDist = curDist + 1f;
                                    }
                                    else
                                    {
                                        // Try corner nudge first, then full stuck escape if that fails
                                        Vector2 a = path[Mathf.Max(0, pathIndex - 1)];
                                        Vector2 b = target;
                                        Vector2 seg = (b - a);
                                        Vector2 segDir = seg.sqrMagnitude > 1e-6f ? seg.normalized : (delta.sqrMagnitude > 0f ? delta.normalized : Vector2.right);
                                        Vector2 nudge = ChooseCornerNudge(segDir, Mathf.Max(cornerProbeDistance, pathCellSize * 0.8f));
                                        if (nudge.sqrMagnitude > 0f)
                                        {
                                            cornerNudgeDir = nudge;
                                            cornerNudgeTimer = Mathf.Max(0.05f, cornerNudgeSeconds);
                                        }
                                        else if (useStuckEscape)
                                        {
                                            Vector2 escape = FindBestEscapeDirection(target, Mathf.Max(cornerProbeDistance, pathCellSize * 0.8f));
                                            if (escape.sqrMagnitude > 0f)
                                            {
                                                stuckEscapeDir = escape;
                                                stuckEscapeTimer = Mathf.Max(0.1f, stuckEscapeSeconds);
                                            }
                                        }
                                    }
                                }
                            }

                            if (cornerNudgeTimer > 0f && cornerNudgeDir.sqrMagnitude > 0f)
                            {
                                cornerNudgeTimer -= Time.fixedDeltaTime;
                                moveDir = cornerNudgeDir;
                            }
                            else if (stuckEscapeTimer > 0f && stuckEscapeDir.sqrMagnitude > 0f)
                            {
                                stuckEscapeTimer -= Time.fixedDeltaTime;
                                moveDir = stuckEscapeDir;
                            }

                            // Micro-avoidance while following path: if immediate move is blocked, slide along obstacle
                            if (moveDir.sqrMagnitude > 0f && IsBlocked(moveDir, Mathf.Max(pathSteerProbeDistance, pathCellSize * 0.6f), out var wpHit))
                            {
                                var slide = ChooseSlide(moveDir, wpHit);
                                if (slide.sqrMagnitude > 0f) moveDir = slide;
                            }

                            if (!forcePathAlways && HasLineOfSight() && dist <= Mathf.Max(attackRange, holdDistance) * 1.25f)
                            {
                                ClearPath();
                                usingPath = false;
                            }
                            else
                            {
                                // path in use ⇒ no slide fallback
                                committedSlide = Vector2.zero;
                                slideTimer = 0f;
                            }
                        }
                    }

                    if (!usingPath)
                    {
                        ClearPath();

                        Vector2 toPlayer = player.position - transform.position;
                        moveDir = toPlayer.sqrMagnitude > 1e-6f ? toPlayer.normalized : Vector2.zero;

                        bool closeHold = (dist <= Mathf.Max(holdDistance, 0.01f)) && losClear;
                        if (closeHold) moveDir = Vector2.zero;

                        if (!losClear && moveDir.sqrMagnitude > 0f)
                        {
                            if (slideTimer <= 0f || committedSlide == Vector2.zero)
                            {
                                if (IsBlocked(moveDir, slideProbeDistance, out var hit))
                                {
                                    committedSlide = ChooseSlide(moveDir, hit);
                                    slideTimer = slideCommitSeconds;
                                    if (debugLog) Debug.Log($"[{name}] Slide fallback → {committedSlide}");
                                }
                            }
                            else slideTimer -= Time.fixedDeltaTime;

                            if (committedSlide != Vector2.zero)
                                moveDir = committedSlide;

                            if (HasLineOfSight())
                            {
                                committedSlide = Vector2.zero;
                                slideTimer = 0f;
                            }
                        }
                        else
                        {
                            committedSlide = Vector2.zero;
                            slideTimer = 0f;
                        }
                    }

                    float speed = (retreatTimer > 0f ? chaseSpeed * retreatSpeedMultiplier : chaseSpeed);
                    rb.velocity = moveDir * speed;

                    if (moveDir.sqrMagnitude > 0f)
                    {
                        Vector2 desiredFace = Snap4(moveDir);
                        desiredFace = ApplyFaceStabilization(desiredFace, lastFace, faceAxisHysteresis, faceChangeCooldown);
                        if (desiredFace != lastFace)
                        {
                            lastFace = desiredFace;
                            lastFaceChangeTime = Time.time;
                        }
                    }

                    DriveAnim(rb.velocity.sqrMagnitude > 0.0001f, lastFace, actualSpeed);

                    bool canAttack = retreatTimer <= 0f && staggerTimer <= 0f && Time.time >= nextReadyTime;
                    if (canAttack && dist <= attackRange)
                        StartAttack(lastFace);

                    if (debugDraw)
                    {
                        Debug.DrawLine(transform.position, player.position, losClear ? Color.green : Color.red, Time.fixedDeltaTime);
                        if (path.Count > 0)
                            for (int i = 0; i < path.Count - 1; i++)
                                Debug.DrawLine(path[i], path[i + 1], Color.cyan, Time.fixedDeltaTime);
                        if (committedSlide != Vector2.zero)
                            Debug.DrawLine(transform.position, (Vector2)transform.position + committedSlide, Color.yellow, Time.fixedDeltaTime);
                    }
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

    // ───────── Path helpers
    void RecalcPathIfNeeded()
    {
        if (!useGridPathfinding) return;

        bool targetMoved = (player.position - lastPlayerPos).sqrMagnitude > (pathCellSize * pathCellSize);
        float interval = forcePathAlways ? Mathf.Min(0.2f, pathRecalcInterval) : pathRecalcInterval;

        if (Time.time < nextPathTime && !targetMoved && path.Count > 0) return;

        lastPlayerPos = player.position;
        nextPathTime = Time.time + Mathf.Max(0.1f, interval);

        string diag;
        bool ok = LitePath.FindPath(
            start: transform.position,
            goal: player.position,
            cellSize: Mathf.Max(0.1f, pathCellSize),
            worldSize: new Vector2(Mathf.Max(4f, pathWorldSize.x), Mathf.Max(4f, pathWorldSize.y)),
            obstacleMask: pathObstacleMask.value == 0 ? losBlockers : pathObstacleMask,
            agentRadius: Mathf.Max(0.05f, agentRadius),
            allowDiagonal: pathAllowDiagonal,
            includeTriggers: pathIncludeTriggers,
            ignoreA: bodyCol,
            ignoreB: player,
            outPath: path,
            overlapBuf: overlapBuf,
            out diag
        );

        pathIndex = 0;
        if (debugLog) Debug.Log($"[{name}] Path {(ok ? "OK" : "FAIL")} – {diag}");
    }

    void ClearPath() { path.Clear(); pathIndex = 0; }

    // ───────── Slide helpers
    bool IsBlocked(Vector2 dir, float dist, out RaycastHit2D firstHit)
    {
        firstHit = default;
        if (dir.sqrMagnitude < 1e-6f || bodyCol == null) return false;

        var results = new RaycastHit2D[10];
        int hits = bodyCol.Cast(dir.normalized, castFilter, results, dist);
        float bestDist = float.MaxValue;
        RaycastHit2D best = default;

        for (int i = 0; i < hits; i++)
        {
            var h = results[i];
            if (!h.collider) continue;
            if (h.collider == bodyCol) continue;
            if (player && h.collider.transform == player) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; }
        }

        if (best.collider != null) { firstHit = best; return true; }
        return false;
    }

    Vector2 ChooseSlide(Vector2 desiredDir, RaycastHit2D hit)
    {
        Vector2 n = hit.normal.normalized;
        Vector2 t1 = new Vector2(-n.y, n.x);
        Vector2 t2 = -t1;
        float d1 = Vector2.Dot(t1, desiredDir);
        float d2 = Vector2.Dot(t2, desiredDir);
        Vector2 slide = (d1 >= d2) ? t1 : t2;
        if (Vector2.Dot(slide, desiredDir) < 0f) slide = -slide;
        return slide.normalized;
    }

    // ───────── Facing helpers
    Vector2 Snap4(Vector2 v)
    {
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y)) return new Vector2(Mathf.Sign(v.x), 0f);
        else return new Vector2(0f, Mathf.Sign(v.y));
    }

    Vector2 ApplyFaceStabilization(Vector2 desired, Vector2 current, float axisHysteresisUnits, float cooldownSeconds)
    {
        if (Time.time < lastFaceChangeTime + Mathf.Max(0f, cooldownSeconds))
            return current;

        bool currentIsHoriz = Mathf.Abs(current.x) > Mathf.Abs(current.y);
        Vector2 toPlayer = player ? (Vector2)(player.position - transform.position) : Vector2.zero;

        float ax = Mathf.Abs(toPlayer.x);
        float ay = Mathf.Abs(toPlayer.y);

        if (currentIsHoriz)
        {
            if (ay <= ax + axisHysteresisUnits) return current;
        }
        else
        {
            if (ax <= ay + axisHysteresisUnits) return current;
        }
        return desired;
    }

    // ───────── State / attack / utils
    void SetState(State s)
    {
        if (state == s) return;
        state = s;
        if (wanderToToggle) wanderToToggle.enabled = (state == State.Wander);
        if (debugLog) Debug.Log($"[{name}] STATE → {state}");
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

        if (debugLog) Debug.Log($"[{name}] ATTACK start");
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

        if (debugLog) Debug.Log($"[{name}] HIT WINDOW hits={hitThisSwing.Count}");
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
        ClearPath();
        committedSlide = Vector2.zero;
        slideTimer = 0f;
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

    bool IsLineClear(Vector2 a, Vector2 b)
    {
        if (pathObstacleMask.value == 0) return true;
        var hit = Physics2D.Linecast(a, b, pathObstacleMask);
        return !hit;
    }

    float EstimateFreeAlong(Vector2 dir, float distance)
    {
        if (bodyCol == null) return distance;
        var results = new RaycastHit2D[6];
        int hits = bodyCol.Cast(dir.normalized, castFilter, results, distance);
        float best = distance;
        for (int i = 0; i < hits; i++)
        {
            var h = results[i];
            if (!h.collider) continue;
            if (h.collider == bodyCol) continue;
            if (player && h.collider.transform == player) continue;
            if (h.distance < best) best = h.distance;
        }
        return Mathf.Max(0f, best);
    }

    Vector2 ChooseCornerNudge(Vector2 along, float probe)
    {
        Vector2 perp = new Vector2(-along.y, along.x);
        float leftFree = EstimateFreeAlong(perp, probe);
        float rightFree = EstimateFreeAlong(-perp, probe);
        // Prefer the freer side; if both tiny, return zero and let slide handle it
        if (leftFree < 0.05f && rightFree < 0.05f) return Vector2.zero;
        Vector2 chosen = (leftFree >= rightFree ? perp : -perp);
        return chosen.normalized;
    }

    Vector2 FindBestEscapeDirection(Vector2 target, float probe)
    {
        Vector2 current = transform.position;
        Vector2[] dirs = {
            Vector2.up, Vector2.down, Vector2.left, Vector2.right,
            new Vector2(1, 1).normalized, new Vector2(1, -1).normalized,
            new Vector2(-1, 1).normalized, new Vector2(-1, -1).normalized
        };
        
        float bestScore = -1f;
        Vector2 bestDir = Vector2.zero;
        
        foreach (var dir in dirs)
        {
            float free = EstimateFreeAlong(dir, probe);
            if (free < 0.1f) continue; // Skip blocked directions
            
            // Score: how much closer does this direction get us to target?
            Vector2 testPos = current + dir * Mathf.Min(free, probe * 0.5f);
            float distToTarget = Vector2.Distance(testPos, target);
            float currentDist = Vector2.Distance(current, target);
            float improvement = currentDist - distToTarget;
            
            // Prefer directions that get us closer to target
            float score = improvement + free * 0.1f; // Bonus for more free space
            if (score > bestScore)
            {
                bestScore = score;
                bestDir = dir;
            }
        }
        
        return bestDir;
    }

    static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude);
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawPathGrid || !useGridPathfinding || !player) return;
        Vector3 center = (transform.position + player.position) * 0.5f;
        Gizmos.color = gridColor;
        Gizmos.DrawCube(center, new Vector3(pathWorldSize.x, pathWorldSize.y, 0.01f));

        if (path != null && path.Count > 0)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < path.Count; i++)
                Gizmos.DrawSphere(path[i], 0.06f);
        }
    }
#endif
}
