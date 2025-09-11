using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Collider2D))]
public class EnemyWander2D : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float moveSpeed = 1.9f;
    [SerializeField] float acceleration = 16f;        // velocity smoothing
    [SerializeField] float segmentDistance = 1.0f;     // straight run length (world units)
    [SerializeField] float minIdle = 0.4f;
    [SerializeField] float maxIdle = 1.1f;

    [Header("Soft radius bias (optional)")]
    [SerializeField] Transform home;                   // if null, uses start position
    [SerializeField] float wanderRadius = 4f;
    [Tooltip("Start biasing back to the centre after % of the radius.")]
    [SerializeField] float softBound = 0.8f;

    [Header("Room bounds (auto-bind via TAG on the collider object)")]
    [Tooltip("Looks ONLY on GameObjects tagged RoomBounds, and ONLY on colliders on that same object (not children).")]
    [SerializeField] bool autoFindRoomBounds = true;
    [SerializeField] string roomBoundsTag = "RoomBounds";
    [Tooltip("How close to the bounds before we start preferring inward directions.")]
    [SerializeField] float boundsWarnMargin = 0.25f;
    [Tooltip("How far ahead we look when deciding if a step would leave the room (world units).")]
    [SerializeField] float boundsProbe = 0.6f;
    [Tooltip("If true, snap the enemy back inside if it ever escapes the bounds.")]
    [SerializeField] bool clampInsideBounds = true;

    [Header("Obstacle sensing")]
    [SerializeField] LayerMask obstacleMask;           // include Walls/Decor etc. (NOT the Room layer)
    [SerializeField] float castDistance = 0.28f;       // how far ahead to cast the collider
    [SerializeField] float castSideInset = 0.06f;      // compatibility (unused by rb.Cast)
    [SerializeField] float turnCooldown = 0.15f;       // min time between turns

    [Header("Stuck detection")]
    [SerializeField] float stuckSpeed = 0.05f;
    [SerializeField] float stuckTime = 0.35f;

    [Header("Animator params")]
    [SerializeField] string speedParam = "Speed";
    [SerializeField] string dirXParam = "DirX";
    [SerializeField] string dirYParam = "DirY";
    [SerializeField] float faceSmooth = 0.06f;

    [Header("Debug")]
    [SerializeField] bool debugAutoBinding = true;     // turn on to see logs

    Rigidbody2D rb;
    Animator anim;
    Collider2D col;

    // Final room collider (once bound, we keep it)
    [SerializeField] Collider2D roomBounds;
    bool hasBounds => roomBounds != null;

    Vector2 homePos;
    Vector2 currentDir = Vector2.down;
    Vector2 desiredVel, currentVel;
    float segmentLeft;
    float idleTimer;
    float lastTurnTime;
    float stuckTimer;
    Vector2 lastPos;

    Vector2 faceDir = Vector2.down;
    Vector2 faceVel;

    ContactFilter2D filter;
    RaycastHit2D[] hits = new RaycastHit2D[6];

    int rebindTriesLeft = 15;
    float nextRebindAt;
    const float RebindInterval = 0.15f;

    // --------- Public API (spawner can set explicitly) ----------
    public void BindToRoom(Collider2D bounds)
    {
        if (bounds && ColliderLooksLikeRoom(bounds) && BodyFullyInside(bounds, rb.position, col.bounds.extents))
        {
            roomBounds = bounds;
            Debug.Log($"[EnemyWander2D] ({name}) Bound explicitly to room '{FullPath(roomBounds.transform)}'.");
            if (!home) homePos = (Vector2)roomBounds.bounds.center;
        }
        else
        {
            Debug.LogWarning($"[EnemyWander2D] ({name}) BindToRoom ignored (null / wrong type / not containing body).");
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        col = GetComponent<Collider2D>();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        filter = new ContactFilter2D { useLayerMask = true, layerMask = obstacleMask, useTriggers = false };

        homePos = home ? (Vector2)home.position : (Vector2)transform.position;

        currentDir = RandomCardinal();
        SnapFace();
        segmentLeft = segmentDistance;
        lastPos = rb.position;

        Physics2D.queriesStartInColliders = false;

        if (debugAutoBinding)
            Debug.Log($"[EnemyWander2D] ({name}) Awake at {rb.position}. AutoFind={autoFindRoomBounds}, Tag='{roomBoundsTag}'.");

        if (autoFindRoomBounds && !roomBounds)
            TryResolveRoomBounds();
    }

    void OnEnable()
    {
        if (autoFindRoomBounds && !roomBounds)
        {
            rebindTriesLeft = 15;
            nextRebindAt = Time.time + RebindInterval;
        }
    }

    void FixedUpdate()
    {
        // Retry a few times in case rooms spawn just after us
        if (autoFindRoomBounds && !roomBounds && rebindTriesLeft > 0 && Time.time >= nextRebindAt)
        {
            if (!TryResolveRoomBounds())
            {
                rebindTriesLeft--;
                nextRebindAt = Time.time + RebindInterval;
            }
        }

        Vector2 pos = rb.position;

        // Safety clamp (only if we have bounds)
        if (clampInsideBounds && hasBounds)
            rb.position = ClampInsideRoom(rb.position);

        // Idle pause
        if (idleTimer > 0f)
        {
            idleTimer -= Time.fixedDeltaTime;
            desiredVel = Vector2.zero;
            ApplyMovementAndAnimation();
            return;
        }

        // Soft radius bias
        Vector2 centre = home ? (Vector2)home.position : homePos;
        Vector2 toCentre = centre - pos;
        if (toCentre.magnitude > wanderRadius * softBound)
        {
            Vector2 want = CardinalFromVector(toCentre);
            if (want != Vector2.zero && want != -currentDir && Time.time - lastTurnTime > turnCooldown)
            {
                currentDir = want;
                SnapFace();
                lastTurnTime = Time.time;
            }
        }

        // Hard bounds steer (only when bound)
        if (hasBounds && Time.time - lastTurnTime > turnCooldown)
        {
            if (NearBounds(pos, boundsWarnMargin) || StepWouldExit(pos, currentDir))
            {
                Vector2 inward = InwardCardinal(pos);
                if (inward != Vector2.zero && inward != -currentDir && !StepWouldExit(pos, inward))
                {
                    if (debugAutoBinding) Debug.Log($"[EnemyWander2D] ({name}) Bounds steer -> inward {inward}.");
                    currentDir = inward;
                    SnapFace();
                    lastTurnTime = Time.time;
                    segmentLeft = segmentDistance;
                }
                else
                {
                    Vector2 perpa = (currentDir == Vector2.up || currentDir == Vector2.down) ? Vector2.left : Vector2.up;
                    Vector2 perpb = -perpa;

                    if (!StepWouldExit(pos, perpa)) { if (debugAutoBinding) Debug.Log($"[EnemyWander2D] ({name}) Bounds steer -> perpA {perpa}."); currentDir = perpa; SnapFace(); lastTurnTime = Time.time; segmentLeft = segmentDistance; }
                    else if (!StepWouldExit(pos, perpb)) { if (debugAutoBinding) Debug.Log($"[EnemyWander2D] ({name}) Bounds steer -> perpB {perpb}."); currentDir = perpb; SnapFace(); lastTurnTime = Time.time; segmentLeft = segmentDistance; }
                    else if (!StepWouldExit(pos, -currentDir)) { if (debugAutoBinding) Debug.Log($"[EnemyWander2D] ({name}) Bounds steer -> reverse {-currentDir}."); currentDir = -currentDir; SnapFace(); lastTurnTime = Time.time; segmentLeft = segmentDistance; }
                }
            }
        }

        // Advance along current segment
        float step = Mathf.Min(segmentLeft, moveSpeed * Time.fixedDeltaTime);
        segmentLeft -= step;
        desiredVel = currentDir * moveSpeed;

        // Obstacle turn
        if (BlockedAhead(pos, currentDir))
        {
            if (debugAutoBinding) Debug.Log($"[EnemyWander2D] ({name}) Hit obstacle ahead -> turning.");
            TurnOnBlock(pos);
            segmentLeft = segmentDistance;
            SnapFace();
            lastTurnTime = Time.time;
        }

        // Stuck watchdog
        float delta = (pos - lastPos).magnitude;
        lastPos = pos;
        bool tryingToMove = desiredVel.sqrMagnitude > 0.001f;
        if (tryingToMove && delta / Time.fixedDeltaTime < stuckSpeed)
        {
            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer >= stuckTime && Time.time - lastTurnTime > turnCooldown)
            {
                if (debugAutoBinding) Debug.Log($"[EnemyWander2D] ({name}) Stuck watchdog -> turning.");
                TurnOnBlock(pos);
                segmentLeft = segmentDistance;
                SnapFace();
                lastTurnTime = Time.time;
                stuckTimer = 0f;
            }
        }
        else stuckTimer = 0f;

        // End of segment -> choose next + idle
        if (segmentLeft <= 0f)
        {
            segmentLeft = segmentDistance;
            currentDir = hasBounds ? ChooseNextCardinalInside(currentDir, pos)
                                   : ChooseNextCardinal(currentDir, pos);
            if (debugAutoBinding) Debug.Log($"[EnemyWander2D] ({name}) Segment end -> new dir {currentDir}.");
            SnapFace();
            lastTurnTime = Time.time;

            if (Random.value < 0.35f)
                idleTimer = Random.Range(minIdle, maxIdle);
        }

        ApplyMovementAndAnimation();
    }

    // ---------- TAG-ONLY auto bind (requires roomBoundsTag on the collider object) ----------
    bool TryResolveRoomBounds()
    {
        Vector2 p = rb ? rb.position : (Vector2)transform.position;
        var tagged = GameObject.FindGameObjectsWithTag(roomBoundsTag);

        if (tagged.Length == 0)
        {
            Debug.LogWarning($"[EnemyWander2D] ({name}) No GameObjects found with tag '{roomBoundsTag}'. Did you tag the object that owns the OUTER Box/CompositeCollider2D?");
            return false;
        }

        if (debugAutoBinding) Debug.Log($"[EnemyWander2D] ({name}) TryResolveRoomBounds at {p}. Tagged objects: {tagged.Length}");

        Collider2D best = null;
        float bestScore = float.MaxValue; // prefer smaller area, then closer center

        foreach (var go in tagged)
        {
            // only colliders on THIS object (not in children)
            var cols = go.GetComponents<Collider2D>();
            if (cols == null || cols.Length == 0)
            {
                if (debugAutoBinding) Debug.Log($"  - '{FullPath(go.transform)}' has NO Collider2D on the tagged object (children are ignored).");
                continue;
            }

            foreach (var c in cols)
            {
                if (!c || !c.enabled || c.isTrigger) { if (debugAutoBinding) Debug.Log($"    · Skip (null/disabled/trigger) {c}"); continue; }
                if (!ColliderLooksLikeRoom(c)) { if (debugAutoBinding) Debug.Log($"    · Skip (not Box/Composite) {c.GetType().Name} on {FullPath(c.transform)}"); continue; }

                bool contains = BodyFullyInside(c, p, col.bounds.extents);
                if (debugAutoBinding) Debug.Log($"    · Candidate {c.GetType().Name} on {FullPath(c.transform)} -> {(contains ? "CONTAINS body" : "nope")}");

                if (!contains) continue;

                float area = c.bounds.size.x * c.bounds.size.y;
                float centerDist = ((Vector2)c.bounds.center - p).sqrMagnitude;
                float score = area + 0.001f * centerDist;

                if (score < bestScore) { bestScore = score; best = c; }
            }
        }

        if (best)
        {
            roomBounds = best;
            Debug.Log($"[EnemyWander2D] ({name}) Auto-bound to '{FullPath(roomBounds.transform)}' (area {roomBounds.bounds.size.x * roomBounds.bounds.size.y:0.0}).");
            if (!home) homePos = (Vector2)roomBounds.bounds.center;
            return true;
        }

        Debug.LogWarning($"[EnemyWander2D] ({name}) No valid RoomBounds collider contained the enemy's body (make sure the TAG is on the object with the OUTER Box/CompositeCollider2D).");
        return false;
    }

    static bool ColliderLooksLikeRoom(Collider2D c)
    {
        return c is BoxCollider2D || c is CompositeCollider2D;
    }

    static bool BodyFullyInside(Collider2D container, Vector2 pos, Vector2 bodyExtents)
    {
        var b = container.bounds;
        // require our whole body to fit, not just the pivot point
        bool insideX = pos.x >= (b.min.x + bodyExtents.x) && pos.x <= (b.max.x - bodyExtents.x);
        bool insideY = pos.y >= (b.min.y + bodyExtents.y) && pos.y <= (b.max.y - bodyExtents.y);
        return insideX && insideY;
    }

    static string FullPath(Transform t)
    {
        if (!t) return "<null>";
        System.Text.StringBuilder sb = new System.Text.StringBuilder(t.name);
        while (t.parent)
        {
            t = t.parent;
            sb.Insert(0, t.name + "/");
        }
        return sb.ToString();
    }

    // ===== Movement + Animator =====
    void ApplyMovementAndAnimation()
    {
        currentVel = Vector2.MoveTowards(currentVel, desiredVel, acceleration * Time.fixedDeltaTime);
        rb.velocity = currentVel;

        float speed = currentVel.magnitude;
        anim.SetFloat(speedParam, speed, 0.05f, Time.deltaTime);

        Vector2 targetFace = (speed > 0.1f) ? currentDir : faceDir;
        faceDir = Vector2.SmoothDamp(faceDir, targetFace, ref faceVel, faceSmooth);
        if (faceDir.sqrMagnitude > 0.0001f) faceDir.Normalize();

        anim.SetFloat(dirXParam, faceDir.x, 0.08f, Time.deltaTime);
        anim.SetFloat(dirYParam, faceDir.y, 0.08f, Time.deltaTime);
        anim.speed = 1f;
    }

    void SnapFace() { faceDir = currentDir; faceVel = Vector2.zero; }

    // ===== Obstacle cast via rigidbody =====
    bool BlockedAhead(Vector2 pos, Vector2 dir)
    {
        int count = rb.Cast(dir, filter, hits, castDistance);
        return count > 0;
    }

    // ===== Direction helpers =====
    static Vector2 RandomCardinal()
    {
        int r = Random.Range(0, 4);
        return r switch { 0 => Vector2.up, 1 => Vector2.down, 2 => Vector2.left, _ => Vector2.right };
    }

    static Vector2 CardinalFromVector(Vector2 v)
    {
        if (v == Vector2.zero) return Vector2.zero;
        return (Mathf.Abs(v.x) > Mathf.Abs(v.y)) ? new Vector2(Mathf.Sign(v.x), 0f)
                                                 : new Vector2(0f, Mathf.Sign(v.y));
    }

    Vector2 ChooseNextCardinal(Vector2 current, Vector2 pos)
    {
        Vector2[] order = current == Vector2.up
            ? new[] { Vector2.up, Vector2.left, Vector2.right, Vector2.down }
            : current == Vector2.down
                ? new[] { Vector2.down, Vector2.right, Vector2.left, Vector2.up }
                : current == Vector2.left
                    ? new[] { Vector2.left, Vector2.down, Vector2.up, Vector2.right }
                    : new[] { Vector2.right, Vector2.up, Vector2.down, Vector2.left };

        foreach (var d in order)
            if (!BlockedAhead(rb.position, d))
                return d;

        return -current;
    }

    Vector2 ChooseNextCardinalInside(Vector2 current, Vector2 pos)
    {
        Vector2[] order = current == Vector2.up
            ? new[] { Vector2.up, Vector2.left, Vector2.right, Vector2.down }
            : current == Vector2.down
                ? new[] { Vector2.down, Vector2.right, Vector2.left, Vector2.up }
                : current == Vector2.left
                    ? new[] { Vector2.left, Vector2.down, Vector2.up, Vector2.right }
                    : new[] { Vector2.right, Vector2.up, Vector2.down, Vector2.left };

        foreach (var d in order)
            if (!StepWouldExit(pos, d) && !BlockedAhead(rb.position, d))
                return d;

        Vector2 inward = InwardCardinal(pos);
        if (inward != Vector2.zero && !BlockedAhead(rb.position, inward))
            return inward;

        return ChooseNextCardinal(current, pos);
    }

    // ===== Bounds helpers =====
    bool NearBounds(Vector2 pos, float margin)
    {
        if (!hasBounds) return false;
        var b = roomBounds.bounds;
        float left = pos.x - b.min.x;
        float right = b.max.x - pos.x;
        float down = pos.y - b.min.y;
        float up = b.max.y - pos.y;
        return Mathf.Min(left, right, up, down) < margin;
    }

    bool StepWouldExit(Vector2 pos, Vector2 dir)
    {
        if (!hasBounds) return false;
        Bounds b = roomBounds.bounds;
        Vector2 ext = col.bounds.extents;
        Vector2 p = pos + dir.normalized * boundsProbe;

        bool insideX = p.x >= (b.min.x + ext.x) && p.x <= (b.max.x - ext.x);
        bool insideY = p.y >= (b.min.y + ext.y) && p.y <= (b.max.y - ext.y);
        return !(insideX && insideY);
    }

    Vector2 InwardCardinal(Vector2 pos)
    {
        if (!hasBounds) return Vector2.zero;
        Vector2 towardCentre = (Vector2)roomBounds.bounds.center - pos;
        return CardinalFromVector(towardCentre);
    }

    Vector2 ClampInsideRoom(Vector2 pos)
    {
        if (!hasBounds) return pos;

        Bounds rbounds = roomBounds.bounds;
        Vector2 ext = col.bounds.extents;

        float clampedX = Mathf.Clamp(pos.x, rbounds.min.x + ext.x, rbounds.max.x - ext.x);
        float clampedY = Mathf.Clamp(pos.y, rbounds.min.y + ext.y, rbounds.max.y - ext.y);
        return new Vector2(clampedX, clampedY);
    }

    void TurnOnBlock(Vector2 pos)
    {
        Vector2 left = (currentDir == Vector2.up || currentDir == Vector2.down) ? Vector2.left : Vector2.up;
        Vector2 right = -left;

        if (hasBounds)
        {
            if (!StepWouldExit(pos, left)  && !BlockedAhead(pos, left)) { currentDir = left; return; }
            if (!StepWouldExit(pos, right) && !BlockedAhead(pos, right)) { currentDir = right; return; }
            if (!StepWouldExit(pos, -currentDir)) { currentDir = -currentDir; return; }
        }
        else
        {
            if (!BlockedAhead(pos, left)) { currentDir = left; return; }
            if (!BlockedAhead(pos, right)) { currentDir = right; return; }
        }

        currentDir = -currentDir;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
        Vector2 c = home ? (Vector2)home.position : (Vector2)transform.position;
        Gizmos.DrawWireSphere((Vector3)c, wanderRadius);

        if (hasBounds)
        {
            var b = roomBounds.bounds;
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
            var innerMin = new Vector3(b.min.x + boundsWarnMargin, b.min.y + boundsWarnMargin, 0f);
            var innerMax = new Vector3(b.max.x - boundsWarnMargin, b.max.y - boundsWarnMargin, 0f);
            Vector3 p1 = new Vector3(innerMin.x, innerMin.y, 0);
            Vector3 p2 = new Vector3(innerMax.x, innerMin.y, 0);
            Vector3 p3 = new Vector3(innerMax.x, innerMax.y, 0);
            Vector3 p4 = new Vector3(innerMin.x, innerMax.y, 0);
            Debug.DrawLine(p1, p2, Gizmos.color);
            Debug.DrawLine(p2, p3, Gizmos.color);
            Debug.DrawLine(p3, p4, Gizmos.color);
            Debug.DrawLine(p4, p1, Gizmos.color);
        }
    }
#endif
}
