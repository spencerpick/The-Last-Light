using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class EnemyWander2D : MonoBehaviour
{
    // ─────────────────────── Movement
    [Header("Movement")]
    public float moveSpeed = 1.75f;
    public Vector2 segmentTimeRange = new Vector2(1.0f, 2.0f); // seconds for each wander segment
    public LayerMask obstacleMask = ~0; // set in Inspector
    public bool freezeUntilBound = true;

    // ─────────────────────── Room binding
    [Header("Room Binding")]
    public bool autoFindBounds = true;
    public string roomTag = "RoomBounds";
    public bool requireFullContainment = false; // else: center in room is enough
    public float insidePadding = 0.02f;
    public bool rebindIfOutside = true;
    public float bindRetryInterval = 0.15f;

    // ─────────────────────── Stuck watchdog
    [Header("Stuck Watchdog")]
    [Tooltip("How far we must travel over the window to consider ourselves NOT stuck.")]
    public float minProgressDistance = 0.35f;
    [Tooltip("Window of time over which we check progress.")]
    public float stuckWindowSeconds = 1.2f;
    [Tooltip("How far to scan for open space when choosing a new direction.")]
    public float scoutDistance = 3.0f;
    [Tooltip("Skin to keep BoxCasts from grazing walls.")]
    public float castSkin = 0.02f;

    // ─────────────────────── Animator / Visuals
    [Header("Animator / Visuals")]
    public Animator animator;                // auto-found
    public SpriteRenderer spriteRenderer;    // auto-found
    public bool flipSpriteOnX = false;
    public bool rotateTransformToDir = false;
    public float rotateDegreesPerSecond = 720f;
    public string paramX = "DirX";
    public string paramY = "DirY";
    public string paramSpeed = "Speed";

    static readonly string[] AltX = { "DirX", "MoveX", "Horizontal" };
    static readonly string[] AltY = { "DirY", "MoveY", "Vertical" };
    static readonly string[] AltS = { "Speed", "speed" };

    // internals
    Rigidbody2D rb;
    Collider2D bodyCollider;
    Collider2D boundRoom;
    Vector2 dir = Vector2.zero;
    Vector2 lastVisualDir = Vector2.right;
    float segmentTimer = 0f;

    // animator param cache
    readonly HashSet<string> animParams = new HashSet<string>();
    bool hasAnyAnimatorParams = false;

    // watchdog
    Vector2 progressStartPos;
    float progressTimer;

    void Reset() { TryAutoWire(); }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        if (rb) rb.freezeRotation = true;

        TryAutoWire();
        RebuildAnimatorCache();

        PickNewDirection();
        ApplyVisuals(Vector2.zero);

        progressStartPos = transform.position;
        progressTimer = 0f;

        Debug.Log($"[EnemyWander2D] ({name}) Awake at ({transform.position.x:F2}, {transform.position.y:F2}). AutoFind={autoFindBounds}, Tag='{roomTag}'.");
    }

    void OnEnable()
    {
        if (autoFindBounds) StartCoroutine(BindLoop());
    }

    // ─────────────────────── Binding
    IEnumerator BindLoop()
    {
        while (boundRoom == null)
        {
            TryResolveRoomBounds();
            if (boundRoom != null) yield break;
            yield return new WaitForSeconds(bindRetryInterval);
        }
    }

    void TryResolveRoomBounds()
    {
        var all = GameObject.FindGameObjectsWithTag(roomTag);
        Debug.Log($"[EnemyWander2D] ({name}) TryResolveRoomBounds at ({transform.position.x:F2}, {transform.position.y:F2}). Tagged objects: {all.Length}");

        var center = (Vector2)bodyCollider.bounds.center;
        var half = bodyCollider.bounds.extents;

        foreach (var go in all)
        {
            var col = go.GetComponent<Collider2D>();
            if (!col || !col.enabled) continue;

            bool contains = requireFullContainment ? BodyInside(col, center, half) : CenterInside(col, center);
            Debug.Log($"    · Candidate BoxCollider2D on {GetHierarchyPath(go.transform)} -> {(contains ? "CONTAINS body" : "nope")}");

            if (contains)
            {
                boundRoom = col;
                float area = col.bounds.size.x * col.bounds.size.y;
                Debug.Log($"[EnemyWander2D] ({name}) Auto-bound to '{GetHierarchyPath(go.transform)}' (area {area:F1}).");
                break;
            }
        }
    }

    static Rect RectXY(Bounds b) => new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
    bool CenterInside(Collider2D room, Vector2 center) => RectXY(room.bounds).Contains(center);
    bool BodyInside(Collider2D room, Vector2 center, Vector2 extents)
    {
        var r = RectXY(room.bounds);
        float xMin = center.x - extents.x + insidePadding;
        float xMax = center.x + extents.x - insidePadding;
        float yMin = center.y - extents.y + insidePadding;
        float yMax = center.y + extents.y - insidePadding;
        return xMin >= r.xMin && xMax <= r.xMax && yMin >= r.yMin && yMax <= r.yMax;
    }
    static bool AABBInside(Bounds container, Vector2 center, Vector2 extents)
    {
        var r = RectXY(container);
        float xMin = center.x - extents.x, xMax = center.x + extents.x;
        float yMin = center.y - extents.y, yMax = center.y + extents.y;
        return xMin >= r.xMin && xMax <= r.xMax && yMin >= r.yMin && yMax <= r.yMax;
    }

    // ─────────────────────── Movement
    void FixedUpdate()
    {
        if (animator == null) { TryAutoWire(); RebuildAnimatorCache(); }

        // Rebind if we drifted out
        if (rebindIfOutside && boundRoom != null)
        {
            var c = (Vector2)bodyCollider.bounds.center;
            var e = bodyCollider.bounds.extents;
            if (!AABBInside(boundRoom.bounds, c, e))
            {
                boundRoom = null;
                if (autoFindBounds) StartCoroutine(BindLoop());
            }
        }

        if (freezeUntilBound && boundRoom == null)
        {
            ApplyVisuals(Vector2.zero);
            if (rb) rb.velocity = Vector2.zero;
            return;
        }

        // Stuck watchdog: are we making progress?
        progressTimer += Time.fixedDeltaTime;
        if (progressTimer >= stuckWindowSeconds)
        {
            float dist = Vector2.Distance(progressStartPos, (Vector2)transform.position);
            if (dist < minProgressDistance)
            {
                // We’re probably oscillating → pick a smarter direction
                dir = ChooseBestCardinalDirection(avoidReverse: true);
                segmentTimer = Random.Range(segmentTimeRange.x, segmentTimeRange.y);
            }
            progressStartPos = transform.position;
            progressTimer = 0f;
        }

        // normal segment timing
        segmentTimer -= Time.fixedDeltaTime;
        if (segmentTimer <= 0f) PickNewDirection();

        // Bounce on room boundary, but prefer a smarter choice
        if (boundRoom != null)
        {
            Vector2 nextCenter = (Vector2)bodyCollider.bounds.center + dir * moveSpeed * Time.fixedDeltaTime;
            var ext = bodyCollider.bounds.extents;
            if (!AABBInside(boundRoom.bounds, nextCenter, ext))
            {
                dir = ChooseBestCardinalDirection(avoidReverse: true);
                segmentTimer = Random.Range(segmentTimeRange.x, segmentTimeRange.y);
            }
        }

        // Obstacle ahead? (short probe)
        if (ProbeBlocked(dir, distance: Mathf.Max(bodyCollider.bounds.extents.magnitude * 0.6f, 0.35f)))
        {
            dir = ChooseBestCardinalDirection(avoidReverse: true);
            segmentTimer = Random.Range(segmentTimeRange.x * 0.5f, segmentTimeRange.y);
        }

        // Move
        Vector2 delta = dir * moveSpeed * Time.fixedDeltaTime;
        if (rb) rb.velocity = delta / Time.fixedDeltaTime;
        else transform.position += (Vector3)delta;

        ApplyVisuals(dir);
    }

    void PickNewDirection()
    {
        // keep 4-way for your sprites
        int r = Random.Range(0, 4);
        dir = r == 0 ? Vector2.up : r == 1 ? Vector2.down : r == 2 ? Vector2.left : Vector2.right;
        segmentTimer = Random.Range(segmentTimeRange.x, segmentTimeRange.y);
    }

    // ─────────────────────── Smart direction choosing
    Vector2 ChooseBestCardinalDirection(bool avoidReverse)
    {
        Vector2[] candidates = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        float bestScore = -1f;
        Vector2 best = -dir; // decent fallback

        foreach (var c in candidates)
        {
            float free = EstimateFreeDistance(c);
            // tiny preference to keep general heading, and penalty for straight reverse to reduce ping-pong
            float dot = Vector2.Dot(c, dir);
            if (avoidReverse && dot < -0.8f) free *= 0.7f;
            else if (dot > 0.8f) free *= 1.05f;

            if (free > bestScore)
            {
                bestScore = free;
                best = c;
            }
        }

        // If everything looks cramped, just rotate 90° off current heading
        if (bestScore < 0.2f)
        {
            best = Mathf.Abs(dir.x) > 0.1f ? (Random.value < 0.5f ? Vector2.up : Vector2.down)
                                           : (Random.value < 0.5f ? Vector2.left : Vector2.right);
        }
        return best;
    }

    float EstimateFreeDistance(Vector2 d)
    {
        // limit by room first
        float roomLimit = boundRoom ? DistanceToRoomEdgeAlong(d) : scoutDistance;

        // then obstacles via BoxCastAll
        var center = (Vector2)bodyCollider.bounds.center;
        var size = bodyCollider.bounds.size - new Vector3(castSkin, castSkin, 0f);
        float maxDist = Mathf.Min(roomLimit, scoutDistance);
        float hitDist = maxDist;

        var hits = Physics2D.BoxCastAll(center, size, 0f, d, maxDist, obstacleMask);
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;
            if (h.collider == bodyCollider) continue;
            if (boundRoom != null && h.collider == boundRoom) continue; // ignore the room walls; roomLimit already handles them
            if (h.distance < hitDist) hitDist = h.distance;
        }
        // keep a tiny safety margin so we don't touch
        return Mathf.Max(0f, hitDist - castSkin);
    }

    bool ProbeBlocked(Vector2 d, float distance)
    {
        var center = (Vector2)bodyCollider.bounds.center;
        var size = bodyCollider.bounds.size - new Vector3(castSkin, castSkin, 0f);
        var hits = Physics2D.BoxCastAll(center, size, 0f, d, distance, obstacleMask);
        foreach (var h in hits)
        {
            if (!h.collider) continue;
            if (h.collider == bodyCollider) continue;
            if (boundRoom != null && h.collider == boundRoom) continue;
            return true;
        }
        return false;
    }

    float DistanceToRoomEdgeAlong(Vector2 d)
    {
        if (boundRoom == null) return scoutDistance;

        Vector2 c = bodyCollider.bounds.center;
        Vector2 e = bodyCollider.bounds.extents;
        var b = boundRoom.bounds;

        float maxX = d.x > 0f ? (b.max.x - (c.x + e.x)) : (d.x < 0f ? ((c.x - e.x) - b.min.x) : float.PositiveInfinity);
        float maxY = d.y > 0f ? (b.max.y - (c.y + e.y)) : (d.y < 0f ? ((c.y - e.y) - b.min.y) : float.PositiveInfinity);

        // project to distance along direction (handle pure axis cases)
        if (d.x == 0f) return Mathf.Max(0f, maxY);
        if (d.y == 0f) return Mathf.Max(0f, maxX);
        float alongX = maxX / Mathf.Abs(d.x);
        float alongY = maxY / Mathf.Abs(d.y);
        return Mathf.Max(0f, Mathf.Min(alongX, alongY));
    }

    // ─────────────────────── Visuals/Animator
    void ApplyVisuals(Vector2 desiredDir)
    {
        if (animator == null) { TryAutoWire(); RebuildAnimatorCache(); }

        Vector2 face = desiredDir.sqrMagnitude > 0.0001f ? desiredDir.normalized : lastVisualDir;
        if (desiredDir.sqrMagnitude > 0.0001f) lastVisualDir = face;

        if (animator && hasAnyAnimatorParams)
        {
            TrySetFloat(paramX, face.x);
            TrySetFloat(paramY, face.y);
            TrySetFloat(paramSpeed, desiredDir.sqrMagnitude);

            foreach (var n in AltX) TrySetFloat(n, face.x);
            foreach (var n in AltY) TrySetFloat(n, face.y);
            foreach (var n in AltS) TrySetFloat(n, desiredDir.sqrMagnitude);
        }

        if (flipSpriteOnX && spriteRenderer)
        {
            if (Mathf.Abs(face.x) > 0.01f) spriteRenderer.flipX = face.x < 0f;
        }

        if (rotateTransformToDir && desiredDir.sqrMagnitude > 0.0001f)
        {
            float targetAngle = Mathf.Atan2(face.y, face.x) * Mathf.Rad2Deg - 90f;
            float angle = Mathf.MoveTowardsAngle(transform.eulerAngles.z, targetAngle,
                                                 rotateDegreesPerSecond * Time.fixedDeltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    void TryAutoWire()
    {
        if (!animator) animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        if (!spriteRenderer) spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>(true);
    }

    void RebuildAnimatorCache()
    {
        animParams.Clear();
        hasAnyAnimatorParams = false;
        if (animator == null) return;
        foreach (var p in animator.parameters)
        {
            animParams.Add(p.name);
            hasAnyAnimatorParams = true;
        }
    }

    void TrySetFloat(string name, float value)
    {
        if (string.IsNullOrEmpty(name) || animator == null) return;
        if (animParams.Contains(name)) animator.SetFloat(name, value);
    }

    // ─────────────────────── Utils & Gizmos
    static string GetHierarchyPath(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
        return sb.ToString();
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (boundRoom != null)
        {
            Gizmos.color = new Color(0, 1, 0, 0.15f);
            var b = boundRoom.bounds;
            Gizmos.DrawCube(b.center, new Vector3(b.size.x, b.size.y, 0.01f));
        }
        if (bodyCollider != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(bodyCollider.bounds.center,
                bodyCollider.bounds.center + (Vector3)(dir.normalized * 0.5f));
        }
    }
#endif
}
