using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight grid A* for 2D top-down. Builds a temporary grid centered between
/// start & goal, samples obstacles with OverlapBoxNonAlloc, then runs A*.
/// </summary>
public static class LitePath
{
    struct Node
    {
        public bool walkable;
    }

    static bool FindNearestWalkable(int sx, int sy, int cols, int rows, Node[] nodes, out int wx, out int wy)
    {
        // Spiral/diamond search increasing radius; prefer closer cells.
        // Scan out to full grid bounds if needed.
        int maxRadius = Mathf.Max(Mathf.Max(sx, cols - 1 - sx), Mathf.Max(sy, rows - 1 - sy));
        for (int r = 1; r <= Mathf.Max(1, maxRadius); r++)
        {
            int xMin = Mathf.Max(0, sx - r);
            int xMax = Mathf.Min(cols - 1, sx + r);
            int yMin = Mathf.Max(0, sy - r);
            int yMax = Mathf.Min(rows - 1, sy + r);

            // top & bottom rows
            for (int x = xMin; x <= xMax; x++)
            {
                int iy = yMin; int ix = x; int idx = iy * cols + ix; if (nodes[idx].walkable) { wx = ix; wy = iy; return true; }
                iy = yMax; ix = x; idx = iy * cols + ix; if (nodes[idx].walkable) { wx = ix; wy = iy; return true; }
            }
            // left & right cols (excluding corners already checked)
            for (int y = yMin + 1; y <= yMax - 1; y++)
            {
                int ix = xMin; int iy = y; int idx = iy * cols + ix; if (nodes[idx].walkable) { wx = ix; wy = iy; return true; }
                ix = xMax; iy = y; idx = iy * cols + ix; if (nodes[idx].walkable) { wx = ix; wy = iy; return true; }
            }
        }
        wx = sx; wy = sy; return false;
    }

    // 8-neighbour costs
    const float COST_STRAIGHT = 1f;
    const float COST_DIAGONAL = 1.41421356f;

    /// <summary>
    /// Returns world-space waypoints in <paramref name="outPath"/> if a path is found.
    /// </summary>
    public static bool FindPath(
        Vector2 start,
        Vector2 goal,
        float cellSize,
        Vector2 worldSize,
        LayerMask obstacleMask,
        float agentRadius,
        bool allowDiagonal,
        bool includeTriggers,
        Collider2D ignoreA,
        Transform ignoreB,
        List<Vector2> outPath,
        Collider2D[] overlapBuf,
        out string diag
    )
    {
        diag = "";
        outPath?.Clear();

        cellSize = Mathf.Max(0.05f, cellSize);
        worldSize.x = Mathf.Max(cellSize * 2f, worldSize.x);
        worldSize.y = Mathf.Max(cellSize * 2f, worldSize.y);

        // grid rect centered between start & goal
        Vector2 center = (start + goal) * 0.5f;
        Rect rect = new Rect(center - worldSize * 0.5f, worldSize);

        // quick reject if start or goal way outside rect
        if (!rect.Contains(start))
        {
            diag = "start outside grid";
            return false;
        }
        if (!rect.Contains(goal))
        {
            diag = "goal outside grid";
            return false;
        }

        int cols = Mathf.Clamp(Mathf.CeilToInt(worldSize.x / cellSize), 2, 512);
        int rows = Mathf.Clamp(Mathf.CeilToInt(worldSize.y / cellSize), 2, 512);

        // clamp to keep total nodes reasonable
        if (cols * rows > 100000)
        {
            diag = "grid too large";
            return false;
        }

        // world↔grid helpers
        Vector2 origin = rect.min; // bottom-left
        Vector2 CellCenter(int x, int y) => origin + new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
        bool WorldToGrid(Vector2 p, out int x, out int y)
        {
            x = Mathf.FloorToInt((p.x - origin.x) / cellSize);
            y = Mathf.FloorToInt((p.y - origin.y) / cellSize);
            return x >= 0 && y >= 0 && x < cols && y < rows;
        }
        int Idx(int x, int y) => y * cols + x;

        // sample obstacles
        var nodes = new Node[cols * rows];

        var cf = new ContactFilter2D
        {
            useLayerMask = true,
            useTriggers = includeTriggers
        };
        cf.SetLayerMask(obstacleMask);

        // Use a circle with radius≈agentRadius to sample obstacles. Box sampling with added radius can
        // be overly conservative when cellSize is small compared to agentRadius, leading to "no path".
        float clearance = Mathf.Max(0.01f, agentRadius - 0.01f);

        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                Vector2 c = CellCenter(x, y);
                int n = Physics2D.OverlapCircle(c, clearance, cf, overlapBuf);
                bool blocked = false;
                for (int i = 0; i < n; i++)
                {
                    var col = overlapBuf[i];
                    if (!col) continue;
                    if (ignoreA)
                    {
                        // Ignore any collider that belongs to the same root as ignoreA (handles child colliders)
                        var aRoot = ignoreA.transform.root;
                        if (col == ignoreA) continue;
                        if (col.transform && col.transform.root == aRoot) continue;
                        if (col.attachedRigidbody && ignoreA.attachedRigidbody && col.attachedRigidbody == ignoreA.attachedRigidbody) continue;
                    }
                    if (ignoreB && col.transform == ignoreB) continue;
                    blocked = true;
                    break;
                }
                nodes[Idx(x, y)].walkable = !blocked;
            }

        // Inflate obstacles by 1 ring to add safety margin near walls/props.
        // This helps agents clear corners instead of scraping along edges.
        var inflated = new bool[cols * rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int i = Idx(x, y);
                if (!nodes[i].walkable)
                {
                    inflated[i] = true;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                            inflated[Idx(nx, ny)] = true;
                        }
                }
            }
        for (int i = 0; i < inflated.Length; i++)
            if (inflated[i]) nodes[i].walkable = false;

        // locate start & goal cells
        if (!WorldToGrid(start, out int sx, out int sy)) { diag = "start to grid failed"; return false; }
        if (!WorldToGrid(goal, out int gx, out int gy)) { diag = "goal to grid failed"; return false; }

        // If start/goal cell is blocked, snap to nearest walkable cell within a small radius.
        // This avoids failures when the agent is flush against an obstacle or slightly overlapping.
        int startIdx = Idx(sx, sy);
        int goalIdx = Idx(gx, gy);

        if (!nodes[startIdx].walkable)
        {
            bool found = FindNearestWalkable(sx, sy, cols, rows, nodes, out int nsx, out int nsy);
            if (!found) { diag = "start blocked"; return false; }
            sx = nsx; sy = nsy; startIdx = Idx(sx, sy);
        }
        if (!nodes[goalIdx].walkable)
        {
            bool found = FindNearestWalkable(gx, gy, cols, rows, nodes, out int ngx, out int ngy);
            if (!found) { diag = "goal blocked"; return false; }
            gx = ngx; gy = ngy; goalIdx = Idx(gx, gy);
        }

        // A*
        var open = new SimpleMinHeap(cols * rows);
        var came = new int[cols * rows]; for (int i = 0; i < came.Length; i++) came[i] = -1;
        var gCost = new float[cols * rows]; for (int i = 0; i < gCost.Length; i++) gCost[i] = float.PositiveInfinity;
        var closed = new bool[cols * rows];

        gCost[startIdx] = 0f;
        open.Push(startIdx, Heuristic(sx, sy, gx, gy, allowDiagonal));

        // neighbours (dx,dy,cost)
        (int dx, int dy, float c)[] neigh = allowDiagonal
            ? new (int, int, float)[]{ (1,0,COST_STRAIGHT), (-1,0,COST_STRAIGHT), (0,1,COST_STRAIGHT), (0,-1,COST_STRAIGHT),
                                     (1,1,COST_DIAGONAL), (1,-1,COST_DIAGONAL), (-1,1,COST_DIAGONAL), (-1,-1,COST_DIAGONAL) }
            : new (int, int, float)[] { (1, 0, COST_STRAIGHT), (-1, 0, COST_STRAIGHT), (0, 1, COST_STRAIGHT), (0, -1, COST_STRAIGHT) };

        while (open.Count > 0)
        {
            int current = open.Pop();
            if (closed[current]) continue;
            closed[current] = true;
            if (current == goalIdx) break;

            int cx = current % cols;
            int cy = current / cols;

            for (int i = 0; i < neigh.Length; i++)
            {
                int nx = cx + neigh[i].dx;
                int ny = cy + neigh[i].dy;
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;

                int ni = Idx(nx, ny);
                if (closed[ni] || !nodes[ni].walkable) continue;

                // optional corner clipping prevention for diagonals
                if (allowDiagonal && neigh[i].dx != 0 && neigh[i].dy != 0)
                {
                    int ix = Idx(cx + neigh[i].dx, cy);
                    int iy = Idx(cx, cy + neigh[i].dy);
                    if (!nodes[ix].walkable || !nodes[iy].walkable) continue;
                }

                float tentative = gCost[current] + neigh[i].c;
                if (tentative < gCost[ni])
                {
                    gCost[ni] = tentative;
                    came[ni] = current;
                    float f = tentative + Heuristic(nx, ny, gx, gy, allowDiagonal);
                    open.Push(ni, f);
                }
            }
        }

        if (came[goalIdx] == -1)
        {
            diag = "no path";
            return false;
        }

        // reconstruct
        var rev = new List<int>(128);
        int t = goalIdx;
        rev.Add(t);
        while (t != startIdx)
        {
            t = came[t];
            if (t < 0) break;
            rev.Add(t);
        }
        rev.Reverse();

        outPath.Capacity = Mathf.Max(outPath.Capacity, rev.Count);
        outPath.Clear();
        for (int i = 0; i < rev.Count; i++)
        {
            int ix = rev[i] % cols;
            int iy = rev[i] / cols;
            outPath.Add(CellCenter(ix, iy));
        }

        diag = $"ok ({rev.Count} nodes)";
        return true;
    }

    static float Heuristic(int x, int y, int gx, int gy, bool diagonal)
    {
        int dx = Mathf.Abs(x - gx);
        int dy = Mathf.Abs(y - gy);
        if (!diagonal) return (dx + dy) * COST_STRAIGHT;
        int m = Mathf.Min(dx, dy);
        return m * COST_DIAGONAL + (Mathf.Max(dx, dy) - m) * COST_STRAIGHT;
    }

    /// <summary> Tiny binary-min-heap (index, priority) for A*. </summary>
    class SimpleMinHeap
    {
        struct Item { public int i; public float p; }
        readonly List<Item> a;
        public int Count => a.Count;
        public SimpleMinHeap(int cap) { a = new List<Item>(cap); }
        public void Push(int i, float p)
        {
            a.Add(new Item { i = i, p = p });
            int c = a.Count - 1;
            while (c > 0)
            {
                int pIdx = (c - 1) >> 1;
                if (a[pIdx].p <= a[c].p) break;
                (a[pIdx], a[c]) = (a[c], a[pIdx]);
                c = pIdx;
            }
        }
        public int Pop()
        {
            int last = a.Count - 1;
            var root = a[0].i;
            a[0] = a[last];
            a.RemoveAt(last);
            int p = 0;
            while (true)
            {
                int l = p * 2 + 1, r = l + 1, s = p;
                if (l < a.Count && a[l].p < a[s].p) s = l;
                if (r < a.Count && a[r].p < a[s].p) s = r;
                if (s == p) break;
                (a[p], a[s]) = (a[s], a[p]);
                p = s;
            }
            return root;
        }
    }
}
