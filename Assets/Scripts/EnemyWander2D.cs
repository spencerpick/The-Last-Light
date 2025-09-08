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

    [Header("Radius bias (soft, optional)")]
    [SerializeField] Transform home;                   // if null, uses start position
    [SerializeField] float wanderRadius = 4f;
    [Tooltip("Start biasing back to the centre after % of the radius.")]
    [SerializeField] float softBound = 0.8f;

    [Header("Room bounds (hard)")]
    [Tooltip("Collider that outlines the room (Box/Composite/etc). Keep as non-trigger.")]
    [SerializeField] Collider2D roomBounds;
    [Tooltip("How close to the bounds before we start preferring inward directions.")]
    [SerializeField] float boundsWarnMargin = 0.25f;
    [Tooltip("How far ahead we look when deciding if a step would leave the room.")]
    [SerializeField] float boundsProbe = 0.6f;
    [Tooltip("If true, snap the enemy back inside if it ever escapes the bounds.")]
    [SerializeField] bool clampInsideBounds = true;

    [Header("Wall avoidance (uses collider shape-casts)")]
    [SerializeField] LayerMask obstacleMask;           // set to your Walls/Decor layers
    [SerializeField] float castDistance = 0.28f;       // how far ahead to cast the collider
    [SerializeField] float castSideInset = 0.06f;      // fraction of collider half-size for side casts (note: central cast only with rb.Cast)
    [SerializeField] float turnCooldown = 0.15f;       // min time between turns

    [Header("Stuck detection")]
    [SerializeField] float stuckSpeed = 0.05f;         // if moving but speed < this
    [SerializeField] float stuckTime = 0.35f;          // for this long => turn

    [Header("Animator params")]
    [SerializeField] string speedParam = "Speed";
    [SerializeField] string dirXParam = "DirX";
    [SerializeField] string dirYParam = "DirY";
    [SerializeField] float faceSmooth = 0.06f;

    Rigidbody2D rb;
    Animator anim;
    Collider2D col;

    Vector2 homePos;
    Vector2 currentDir = Vector2.down;                 // current *cardinal* travel direction
    Vector2 desiredVel, currentVel;
    float segmentLeft;
    float idleTimer;
    float lastTurnTime;
    float stuckTimer;
    Vector2 lastPos;

    // Facing fed to the blend tree
    Vector2 faceDir = Vector2.down;
    Vector2 faceVel;

    ContactFilter2D filter;
    RaycastHit2D[] hits = new RaycastHit2D[6];

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
    }

    /// <summary>Call right after you spawn him to make this room his “home”.</summary>
    public void SetHomeToHere()
    {
        homePos = rb.position;
        if (home != null) home.position = rb.position;
    }

    void FixedUpdate()
    {
        Vector2 pos = rb.position;

        // Optional: clamp back inside hard bounds if we ever slipped out
        if (roomBounds && clampInsideBounds)
            rb.position = ClampInsideRoom(rb.position);

        // Idle pause
        if (idleTimer > 0f)
        {
            idleTimer -= Time.fixedDeltaTime;
            desiredVel = Vector2.zero;
            ApplyMovementAndAnimation();
            return;
        }

        // Soft radius bias (nudge back toward centre)
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

        // Hard room-bounds bias/turn: if we're close to the edge or next step exits, pick inward
        if (roomBounds && Time.time - lastTurnTime > turnCooldown)
        {
            if (NearBounds(pos, boundsWarnMargin) || StepWouldExit(pos, currentDir))
            {
                Vector2 inward = InwardCardinal(pos);
                // Try to go inward if it’s not directly blocked; else try a safe perpendicular that stays inside
                if (inward != Vector2.zero && inward != -currentDir && !StepWouldExit(pos, inward))
                {
                    currentDir = inward;
                    SnapFace();
                    lastTurnTime = Time.time;
                    segmentLeft = segmentDistance;
                }
                else
                {
                    // Pick a perpendicular that keeps us inside (fallback to reverse if needed)
                    Vector2 perpa = (currentDir == Vector2.up || currentDir == Vector2.down) ? Vector2.left : Vector2.up;
                    Vector2 perpb = -perpa;

                    if (!StepWouldExit(pos, perpa)) { currentDir = perpa; SnapFace(); lastTurnTime = Time.time; segmentLeft = segmentDistance; }
                    else if (!StepWouldExit(pos, perpb)) { currentDir = perpb; SnapFace(); lastTurnTime = Time.time; segmentLeft = segmentDistance; }
                    else if (!StepWouldExit(pos, -currentDir)) { currentDir = -currentDir; SnapFace(); lastTurnTime = Time.time; segmentLeft = segmentDistance; }
                    // else keep currentDir; we’ll rely on wall/stuck logic below
                }
            }
        }

        // Advance along current segment
        float step = Mathf.Min(segmentLeft, moveSpeed * Time.fixedDeltaTime);
        segmentLeft -= step;
        desiredVel = currentDir * moveSpeed;

        // Hard block check using the actual collider shape (central cast via rb.Cast)
        if (BlockedAhead(pos, currentDir))
        {
            TurnOnBlock(pos);
            segmentLeft = segmentDistance;
            SnapFace();
            lastTurnTime = Time.time;
        }

        // Stuck detection (trying to move but barely progressing)
        float delta = (pos - lastPos).magnitude;
        lastPos = pos;
        bool tryingToMove = desiredVel.sqrMagnitude > 0.001f;
        if (tryingToMove && delta / Time.fixedDeltaTime < stuckSpeed)
        {
            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer >= stuckTime && Time.time - lastTurnTime > turnCooldown)
            {
                TurnOnBlock(pos);
                segmentLeft = segmentDistance;
                SnapFace();
                lastTurnTime = Time.time;
                stuckTimer = 0f;
            }
        }
        else stuckTimer = 0f;

        // End of segment → choose next + optional idle
        if (segmentLeft <= 0f)
        {
            segmentLeft = segmentDistance;

            // Choose next with a preference to stay inside the room if we have bounds
            currentDir = roomBounds ? ChooseNextCardinalInside(currentDir, pos)
                                    : ChooseNextCardinal(currentDir, pos);

            SnapFace();
            lastTurnTime = Time.time;

            if (Random.value < 0.35f)
                idleTimer = Random.Range(minIdle, maxIdle);
        }

        ApplyMovementAndAnimation();
    }

    // ===== Movement + Animator =====
    void ApplyMovementAndAnimation()
    {
        currentVel = Vector2.MoveTowards(currentVel, desiredVel, acceleration * Time.fixedDeltaTime);
        rb.velocity = currentVel;

        float speed = currentVel.magnitude;
        anim.SetFloat(speedParam, speed, 0.05f, Time.deltaTime);

        // Face is driven from the intended direction (instant turns; no moonwalk)
        Vector2 targetFace = (speed > 0.1f) ? currentDir : faceDir;
        faceDir = Vector2.SmoothDamp(faceDir, targetFace, ref faceVel, faceSmooth);
        if (faceDir.sqrMagnitude > 0.0001f) faceDir.Normalize();

        anim.SetFloat(dirXParam, faceDir.x, 0.08f, Time.deltaTime);
        anim.SetFloat(dirYParam, faceDir.y, 0.08f, Time.deltaTime);
        anim.speed = 1f;
    }

    void SnapFace()
    {
        faceDir = currentDir;
        faceVel = Vector2.zero;
    }

    // ===== Shape-cast based blocking (central shape cast via Rigidbody2D.Cast) =====
    bool BlockedAhead(Vector2 pos, Vector2 dir)
    {
        int count = rb.Cast(dir, filter, hits, castDistance);
        return count > 0;
    }

    // ===== Direction helpers (no-bounds) =====
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
        // forward bias > perpendiculars > 180 if boxed in
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

        return -current; // fully boxed in
    }

    // ===== Direction helpers (bounds-aware) =====
    Vector2 ChooseNextCardinalInside(Vector2 current, Vector2 pos)
    {
        Vector2[] order = current == Vector2.up
            ? new[] { Vector2.up, Vector2.left, Vector2.right, Vector2.down }
            : current == Vector2.down
                ? new[] { Vector2.down, Vector2.right, Vector2.left, Vector2.up }
                : current == Vector2.left
                    ? new[] { Vector2.left, Vector2.down, Vector2.up, Vector2.right }
                    : new[] { Vector2.right, Vector2.up, Vector2.down, Vector2.left };

        // 1) Prefer directions that DO NOT exit bounds
        foreach (var d in order)
            if (!StepWouldExit(pos, d) && !BlockedAhead(rb.position, d))
                return d;

        // 2) If everything would exit, prefer the inward cardinal
        Vector2 inward = InwardCardinal(pos);
        if (inward != Vector2.zero && !BlockedAhead(rb.position, inward))
            return inward;

        // 3) Fallback to normal logic
        return ChooseNextCardinal(current, pos);
    }

    // ===== Bounds utilities =====
    bool NearBounds(Vector2 pos, float margin)
    {
        if (!roomBounds) return false;
        var b = roomBounds.bounds;
        // Distance to each side
        float left = (pos.x - b.min.x);
        float right = (b.max.x - pos.x);
        float down = (pos.y - b.min.y);
        float up = (b.max.y - pos.y);
        float minEdge = Mathf.Min(left, right, up, down);
        return minEdge < margin;
    }

    bool StepWouldExit(Vector2 pos, Vector2 dir)
    {
        if (!roomBounds) return false;
        Vector2 probe = pos + dir.normalized * boundsProbe;
        return !roomBounds.bounds.Contains(probe);
    }

    Vector2 InwardCardinal(Vector2 pos)
    {
        if (!roomBounds) return Vector2.zero;
        Vector2 towardCentre = (Vector2)roomBounds.bounds.center - pos;
        return CardinalFromVector(towardCentre);
    }

    Vector2 ClampInsideRoom(Vector2 pos)
    {
        if (!roomBounds) return pos;

        Bounds rbounds = roomBounds.bounds;
        // respect our collider size so we don't clip into walls:
        Vector2 ext = col.bounds.extents;

        float clampedX = Mathf.Clamp(pos.x, rbounds.min.x + ext.x, rbounds.max.x - ext.x);
        float clampedY = Mathf.Clamp(pos.y, rbounds.min.y + ext.y, rbounds.max.y - ext.y);
        return new Vector2(clampedX, clampedY);
    }

    // ===== Turn on block =====
    void TurnOnBlock(Vector2 pos)
    {
        // prefer turning left/right over 180
        Vector2 left = (currentDir == Vector2.up || currentDir == Vector2.down) ? Vector2.left : Vector2.up;
        Vector2 right = -left;

        // If bounds exist, prefer options that remain inside
        if (roomBounds)
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
        // Soft radius gizmo
        Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
        Vector2 c = home ? (Vector2)home.position : (Vector2)transform.position;
        Gizmos.DrawWireSphere((Vector3)c, wanderRadius);

        // Room bounds gizmo (margin + probe)
        if (roomBounds)
        {
            var b = roomBounds.bounds;
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
            // inner rectangle representing warn margin
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
