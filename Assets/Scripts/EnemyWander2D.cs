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

    [Header("Radius bounds")]
    [SerializeField] Transform home;                   // if null, uses start position
    [SerializeField] float wanderRadius = 4f;
    [Tooltip("Start biasing back to the centre after % of the radius.")]
    [SerializeField] float softBound = 0.8f;

    [Header("Wall avoidance (uses collider shape-casts)")]
    [SerializeField] LayerMask obstacleMask;           // set to your Walls layer(s)
    [SerializeField] float castDistance = 0.28f;       // how far ahead to cast the collider
    [SerializeField] float castSideInset = 0.06f;      // fraction of collider half-size for side casts
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

        // Advance along current segment
        float step = Mathf.Min(segmentLeft, moveSpeed * Time.fixedDeltaTime);
        segmentLeft -= step;
        desiredVel = currentDir * moveSpeed;

        // Hard block check using the actual collider shape
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
            currentDir = ChooseNextCardinal(currentDir, pos);
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

    // ===== Shape-cast based blocking =====
    bool BlockedAhead(Vector2 pos, Vector2 dir)
    {
        if (CastFrom(pos, dir)) return true;

        // side casts: offset by a fraction of the collider’s extents
        Bounds b = col.bounds;
        Vector2 sideAxis = (dir == Vector2.up || dir == Vector2.down) ? Vector2.right : Vector2.up;
        float inset = castSideInset * ((dir == Vector2.up || dir == Vector2.down) ? b.extents.x : b.extents.y);

        if (CastFrom(pos + sideAxis * inset, dir)) return true;
        if (CastFrom(pos - sideAxis * inset, dir)) return true;

        return false;
    }

    bool CastFrom(Vector2 origin, Vector2 dir)
    {
        // Rigidbody2D.Cast signature: (direction, ContactFilter2D, results[], distance)
        // We move the body to the origin temporarily by using the rb.position for the cast.
        // Note: Cast ignores the 'origin' parameter, it uses rb.position internally.
        // To approximate origin we only need direction + distance here.
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

    void TurnOnBlock(Vector2 pos)
    {
        // prefer turning left/right over 180
        Vector2 left  = (currentDir == Vector2.up || currentDir == Vector2.down) ? Vector2.left : Vector2.up;
        Vector2 right = -left;

        if (!BlockedAhead(pos, left))  { currentDir = left;  return; }
        if (!BlockedAhead(pos, right)) { currentDir = right; return; }
        currentDir = -currentDir;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
        Vector2 c = home ? (Vector2)home.position : (Vector2)transform.position;
        Gizmos.DrawWireSphere(c, wanderRadius);
    }
#endif
}
