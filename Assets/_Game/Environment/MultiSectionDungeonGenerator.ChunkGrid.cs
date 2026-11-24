using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Entry = ChunkLibrary.Entry;

/// <summary>
/// CHUNK-GRID mode: the whole map is built from authored chunk prefabs on an 8 m cell grid (README §1), so refining a
/// prefab refines every map. Pipeline:
///  1. sections = blocks of <see cref="SectionCells"/>² cells (themes from the bag), 1-cell link lanes between them;
///  2. section graph = spanning tree + loops (same rules as Living Map S5) → one link cell per edge in the lane;
///  3. per section, "pre-rooms" with fixed door segments: objectives (Extraction Gate, Chucho's den + chewed corridor,
///     generator alcoves spread over different sections), one landmark-capable room, sometimes another big room;
///  4. per section a random spanning-tree maze over the remaining cells (fill <see cref="CellFill"/>), forced to reach every
///     room door and link cell, plus a few loops → each used cell gets a N/E/S/W door mask;
///  5. selection: pairs of straight cells may merge into a 1×2 room (Crypt, Library, Pit Bridge …), every other cell
///     gets a 1×1 chunk whose rotated doors equal its mask (tags: theme, weight, max; base corridors unlimited and
///     swapped to their theme-height variant; never the same chunk as a neighbour when there is a choice);
///  6. stamping (prefab instances under Map/Section_X/Cell_x_z_Name), collars fitted to the tallest neighbour, sockets
///     wired by the shared chunk code. A cell no chunk fits gets a procedural 4 m corridor (logged as an authoring gap).
/// </summary>
public partial class MultiSectionDungeonGenerator
{
    [Header("Chunk grid (whole map from prefabs)")]
    [Tooltip("Build the map entirely from authored chunks on an 8 m grid (procedural geometry only as a fallback).")]
    [SerializeField] private bool ChunkGridMode = true;
    [Tooltip("Cells per section side (8 m each).")]
    [SerializeField, Range(3, 8)] private int SectionCells = 5;
    [Tooltip("Share of a section's free cells the maze uses (the rest stays solid rock).")]
    [SerializeField, Range(0.3f, 1f)] private float CellFill = 0.68f;
    [Tooltip("Chance to add a loop between two adjacent used cells that aren't linked yet.")]
    [SerializeField, Range(0f, 1f)] private float CellLoopChance = 0.12f;
    [Tooltip("Chance that two aligned straight cells become one 1×2 room/set piece.")]
    [SerializeField, Range(0f, 1f)] private float MergeRoomChance = 0.55f;
    [Tooltip("Extra big rooms per section besides the landmark room (min..max).")]
    [SerializeField] private Vector2Int ExtraRoomsPerSection = new(0, 1);
    [Tooltip("Share of corridor chunks whose torch socket gets a torch (rooms always light their suggested sockets).")]
    [SerializeField, Range(0f, 1f)] private float GridCorridorTorchChance = 0.3f;

    private const int DN = 1, DE = 2, DS = 4, DW = 8;

    private class GridCell
    {
        public int Section = -1;          // -1 = link lane
        public bool Used, InRoom, Required;
        public int Mask;
        public ChunkPlacement Chunk;
    }

    private GridCell[,] _cells;
    private int _cw, _ch;
    private bool _gridActive;
    private readonly List<string> _gridGaps = new();   // "<mask> in <theme>" with no chunk → procedural fallback

    private bool GridOn => ChunkGridMode && ChunksOn;

    // ── layout ───────────────────────────────────────────────────────────────

    private void LayoutChunkGrid()
    {
        int cols = Mathf.Max(1, GridColumns), rows = Mathf.Max(1, GridRows), sc = Mathf.Max(3, SectionCells);
        _cw = cols * sc + (cols - 1);
        _ch = rows * sc + (rows - 1);
        _width = _cw * 8;
        _height = _ch * 8;
        _cells = new GridCell[_cw, _ch];
        for (int x = 0; x < _cw; x++) for (int z = 0; z < _ch; z++) _cells[x, z] = new GridCell();

        var bag = BuildThemeBag(cols * rows);
        int i = 0;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                int cx0 = col * (sc + 1), cz0 = row * (sc + 1);
                var b = new RectInt(cx0 * 8, cz0 * 8, sc * 8, sc * 8);
                int idx = _sections.Count;
                _sections.Add(new SectionInfo
                {
                    Type = bag[i++], Bounds = b, Col = col, Row = row,
                    Entry = new Vector2Int(b.xMin, b.yMin + b.height / 2), Exit = new Vector2Int(b.xMax - 1, b.yMin + b.height / 2),
                });
                for (int x = 0; x < sc; x++) for (int z = 0; z < sc; z++) _cells[cx0 + x, cz0 + z].Section = idx;
            }
    }

    private RectInt SectionCellsRect(SectionInfo s) => new(s.Bounds.x / 8, s.Bounds.y / 8, s.Bounds.width / 8, s.Bounds.height / 8);

    private static int Bit(Vector2Int d) => d.y > 0 ? DN : d.x > 0 ? DE : d.y < 0 ? DS : DW;
    private static Vector2Int DirOf(int bit) => bit switch { DN => new(0, 1), DE => new(1, 0), DS => new(0, -1), _ => new(-1, 0) };
    private static int Opp(int bit) => bit switch { DN => DS, DS => DN, DE => DW, _ => DE };
    private static int RotBits(int m, int yaw)
    {
        for (int k = 0; k < (yaw / 90) % 4; k++)
            m = ((m & DN) != 0 ? DE : 0) | ((m & DE) != 0 ? DS : 0) | ((m & DS) != 0 ? DW : 0) | ((m & DW) != 0 ? DN : 0);
        return m;
    }
    private static int SideBit(char side) => side switch { 'N' => DN, 'E' => DE, 'S' => DS, _ => DW };
    private static int EntryBits(Entry e) { int m = 0; foreach (var d in e.Doors) m |= SideBit(d.Side); return m; }
    private static int BitCount(int m) { int n = 0; for (int b = 1; b <= 8; b <<= 1) if ((m & b) != 0) n++; return n; }
    private static string MaskName(int m)
    {
        int n = BitCount(m);
        return n == 1 ? "DeadEnd" : n == 2 ? ((m == (DN | DS) || m == (DE | DW)) ? "Straight" : "Corner") : n == 3 ? "T" : n == 4 ? "Cross" : "Empty";
    }

    private bool CellOk(int x, int z) => x >= 0 && z >= 0 && x < _cw && z < _ch;

    private void Link(Vector2Int a, Vector2Int b)
    {
        Vector2Int d = b - a;
        _cells[a.x, a.y].Mask |= Bit(d);
        _cells[b.x, b.y].Mask |= Bit(-d);
        _cells[a.x, a.y].Used = _cells[b.x, b.y].Used = true;
    }

    // ── build ────────────────────────────────────────────────────────────────

    private void BuildChunkGrid()
    {
        _gridGaps.Clear();
        var mapRoot = NewChild("Map");
        var sectionRoots = new Transform[_sections.Count];
        for (int i = 0; i < _sections.Count; i++)
        {
            var go = new GameObject($"Section_{i:00}_{_sections[i].Type}");
            go.transform.SetParent(mapRoot, false);
            sectionRoots[i] = go.transform;
        }
        var linksRoot = new GameObject("Links").transform; linksRoot.SetParent(mapRoot, false);
        Transform ParentOf(int section) => section >= 0 ? sectionRoots[section] : linksRoot;

        LinkSectionsOnGrid();
        PlaceGridRooms(ParentOf);
        foreach (SectionInfo s in _sections) GrowSectionMaze(s);
        SelectAndStampCells(ParentOf);
        FitGridCollars();
        Debug.Log($"[ChunkGrid] {_cw}×{_ch} cells, {LiveChunkCount()} chunks ({ChunkSummary()})" +
                  (_gridGaps.Count > 0 ? $" — procedural fallback for {_gridGaps.Count} cell(s): {string.Join(", ", _gridGaps)}" : ""), this);
    }

    /// <summary>Section graph (tree + loops) → one link cell in the lane between two linked sections.</summary>
    private void LinkSectionsOnGrid()
    {
        var edges = new List<(int a, int b)>();
        int cols = GridColumns, rows = GridRows;
        for (int i = 0; i < _sections.Count; i++)
        {
            var s = _sections[i];
            for (int j = 0; j < _sections.Count; j++)
            {
                var t = _sections[j];
                if ((t.Col == s.Col + 1 && t.Row == s.Row) || (t.Row == s.Row + 1 && t.Col == s.Col)) edges.Add((i, j));
            }
        }
        Shuffle(edges);
        var parent = new int[_sections.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        var chosen = new List<(int a, int b)>(); var rest = new List<(int a, int b)>();
        foreach (var e in edges) { int ra = Find(e.a), rb = Find(e.b); if (ra != rb) { parent[ra] = rb; chosen.Add(e); } else rest.Add(e); }
        _treeLinks = chosen.Count; _loopLinks = 0;
        foreach (var e in rest)
        {
            if (_loopLinks >= MaxExtraLoops) break;
            if (_loopLinks < MinExtraLoops || Roll(ExtraLoopChance)) { chosen.Add(e); _loopLinks++; }
        }

        int sc = SectionCells;
        foreach (var (a, b) in chosen)
        {
            var ra = SectionCellsRect(_sections[a]); var rb = SectionCellsRect(_sections[b]);
            bool horizontal = _sections[b].Col == _sections[a].Col + 1;
            int k = 1 + _rng.Next(Mathf.Max(1, sc - 2));   // not on the section's corner rows
            Vector2Int ca, gap, cb;
            if (horizontal) { ca = new(ra.xMax - 1, ra.y + k); gap = new(ra.xMax, ra.y + k); cb = new(rb.x, rb.y + k); }
            else { ca = new(ra.x + k, ra.yMax - 1); gap = new(ra.x + k, ra.yMax); cb = new(rb.x + k, rb.y); }
            Link(ca, gap); Link(gap, cb);
            _cells[ca.x, ca.y].Required = _cells[cb.x, cb.y].Required = true;
        }
    }

    // ── pre-rooms (fixed door segments) ──────────────────────────────────────

    private class RoomDoor { public Vector2Int Outside; public int BitToRoom; }

    /// <summary>Try to place <paramref name="e"/> in section <paramref name="si"/> (cells free, every door's outside
    /// cell inside the section, free and not a room). Returns the stamped chunk or null.</summary>
    private ChunkPlacement TryGridRoom(Entry e, int si, Func<int, Transform> parentOf, Func<Vector2Int, int, bool> extraOk = null)
    {
        var s = _sections[si]; var r = SectionCellsRect(s);
        var cands = new List<(Vector2Int at, int yaw, List<RoomDoor> doors)>();
        foreach (int yaw in new[] { 0, 90, 180, 270 })
        {
            Vector2Int sizeT = RotatedSize(e, yaw); Vector2Int sizeC = new(sizeT.x / 8, sizeT.y / 8);
            for (int x = r.xMin; x + sizeC.x <= r.xMax; x++)
                for (int z = r.yMin; z + sizeC.y <= r.yMax; z++)
                {
                    bool ok = true;
                    for (int i = 0; i < sizeC.x && ok; i++) for (int j = 0; j < sizeC.y && ok; j++)
                    { var c = _cells[x + i, z + j]; if (c.InRoom || c.Used || c.Required) ok = false; }
                    if (!ok) continue;
                    var rect = new RectInt(x * 8, z * 8, sizeT.x, sizeT.y);
                    var doors = new List<RoomDoor>();
                    foreach (var d in DoorsFor(e, rect, yaw))
                    {
                        Vector2 o = d.Pos + (Vector2)d.Dir * 4f;
                        var oc = new Vector2Int(Mathf.FloorToInt(o.x / 8f), Mathf.FloorToInt(o.y / 8f));
                        if (!r.Contains(oc) || _cells[oc.x, oc.y].InRoom || (oc.x >= x && oc.x < x + sizeC.x && oc.y >= z && oc.y < z + sizeC.y)) { ok = false; break; }
                        doors.Add(new RoomDoor { Outside = oc, BitToRoom = Bit(-d.Dir) });
                    }
                    if (!ok || (extraOk != null && !extraOk(new Vector2Int(x, z), yaw))) continue;
                    // The section's free cells must still join every door / link cell (else the prune cuts it off).
                    var reqs = new List<Vector2Int>(); foreach (var d in doors) reqs.Add(d.Outside);
                    if (!SectionStaysConnected(r, new RectInt(x, z, sizeC.x, sizeC.y), reqs, null)) continue;
                    cands.Add((new Vector2Int(x, z), yaw, doors));
                }
        }
        if (cands.Count == 0) return null;
        var c0 = cands[_rng.Next(cands.Count)];
        Vector2Int sz = RotatedSize(e, c0.yaw);
        for (int i = 0; i < sz.x / 8; i++) for (int j = 0; j < sz.y / 8; j++) _cells[c0.at.x + i, c0.at.y + j].InRoom = true;
        foreach (var d in c0.doors)
        {
            var cell = _cells[d.Outside.x, d.Outside.y];
            cell.Mask |= d.BitToRoom; cell.Used = true; cell.Required = true;
        }
        var p = StampChunk(e, new Vector2Int(c0.at.x * 8, c0.at.y * 8), c0.yaw, s.Type, s, parentOf(si), $"Cell_{c0.at.x}_{c0.at.y}_{e.Name}");
        for (int i = 0; i < sz.x / 8; i++) for (int j = 0; j < sz.y / 8; j++) _cells[c0.at.x + i, c0.at.y + j].Chunk = p;
        return p;
    }

    private void PlaceGridRooms(Func<int, Transform> parentOf)
    {
        // Extraction gate (weighted candidate sections).
        var gate = Chunks.Find("CH_Extraction_Gate");
        var order = WeightedSectionOrder(s => s.Type == ExtractionPreferredSection ? PreferredSectionWeight : 1f);
        if (gate?.Prefab != null) foreach (var s in order) if ((_gateChunk = TryGridRoom(gate, _sections.IndexOf(s), parentOf)) != null) break;

        // Den + chewed corridor glued to its door, far from the gate.
        var den = Chunks.Find("CH_SP_ChuchoDen"); var chewed = Chunks.Find("CH_Corridor_Chewed");
        if (den?.Prefab != null)
        {
            var order2 = WeightedSectionOrder(s => _gateChunk == null ? 1f : 1f + Vector2.Distance(_gateChunk.Centre, s.Center) / 30f);
            foreach (var s in order2)
            {
                if (s == _gateChunk?.Section) continue;
                _denChunk = TryGridRoom(den, _sections.IndexOf(s), parentOf);
                if (_denChunk == null) continue;
                if (chewed?.Prefab != null) GlueChewed(_denChunk, parentOf);
                break;
            }
        }

        // Generator alcoves: 3 different sections, spread apart (greedy farthest-first).
        var alcove = Chunks.Find("CH_Generator_Alcove");
        _generatorRooms.Clear();
        if (alcove?.Prefab != null)
        {
            var used = new HashSet<SectionInfo>();
            if (_gateChunk != null) used.Add(_gateChunk.Section);
            for (int n = 0; n < GeneratorCount; n++)
            {
                var pool = new List<SectionInfo>(_sections);
                pool.RemoveAll(used.Contains);
                pool.Sort((a, b) => FarScore(b).CompareTo(FarScore(a)));
                ChunkPlacement placed = null;
                foreach (var s in pool.GetRange(0, Mathf.Min(4, pool.Count)))
                    if ((placed = TryGridRoom(alcove, _sections.IndexOf(s), parentOf)) != null) { used.Add(s); break; }
                if (placed == null) { Debug.LogWarning($"[ChunkGrid] only {n}/{GeneratorCount} generator alcoves fit.", this); break; }
                var d = placed.Doors[0];
                _generatorRooms.Add(new GeneratorRoom { Rect = placed.Rect, Yaw = Mathf.Atan2(d.Dir.x, d.Dir.y) * Mathf.Rad2Deg, Chunk = placed });
            }
            float FarScore(SectionInfo s)
            {
                float best = float.MaxValue;
                foreach (var g in _generatorRooms) best = Mathf.Min(best, Vector2.Distance(g.Rect.center, s.Bounds.center));
                if (_gateChunk != null) best = Mathf.Min(best, Vector2.Distance(_gateChunk.Centre, s.Bounds.center) * 1.5f);
                return best == float.MaxValue ? (float)_rng.NextDouble() : best + (float)_rng.NextDouble();
            }
        }

        // One landmark room per section (+ sometimes another big room).
        for (int si = 0; si < _sections.Count; si++)
        {
            var s = _sections[si];
            bool hasLandmark = HasLandmarkChunk(s);
            if (!hasLandmark)
            {
                var pool = RoomPool(s, landmarkOnly: true);
                for (int t = 0; t < 4 && pool.Count > 0 && !hasLandmark; t++)
                {
                    var pick = PickWeighted(pool);
                    if (TryGridRoom(pick, si, parentOf) != null) hasLandmark = true; else pool.Remove(pick);
                }
            }
            int extra = _rng.Next(ExtraRoomsPerSection.x, ExtraRoomsPerSection.y + 1);
            for (int k = 0; k < extra; k++)
            {
                var pool = RoomPool(s, landmarkOnly: false);
                for (int t = 0; t < 3 && pool.Count > 0; t++)
                {
                    var pick = PickWeighted(pool);
                    if (TryGridRoom(pick, si, parentOf) != null) break;
                    pool.Remove(pick);
                }
            }
        }
    }

    private bool HasLandmarkChunk(SectionInfo s)
    {
        foreach (var p in _chunks)
            if (!p.Removed && p.Section == s && !string.IsNullOrEmpty(p.E.DisplayName) && HasSocket(p.E, "SOCKET_Landmark")) return true;
        return false;
    }

    private static bool HasSocket(Entry e, string prefix)
    {
        if (e.Prefab == null) return false;
        foreach (Transform t in e.Prefab.transform) if (t.name.StartsWith(prefix)) return true;
        return false;
    }

    /// <summary>Pre-room candidates: multi-cell rooms/set pieces (and landmark 1×1s when asked) allowed in this theme.</summary>
    private List<Entry> RoomPool(SectionInfo s, bool landmarkOnly)
    {
        var pool = new List<Entry>();
        foreach (var e in Chunks.Entries)
        {
            if (e?.Prefab == null || e.Weight <= 0f) continue;
            if (e.Role == "objective" || e.Name == "CH_SP_ChuchoDen" || e.Name == "CH_Corridor_Chewed") continue;
            if (e.Role != "room" && e.Role != "setpiece") continue;
            if (!e.AllowsTheme(s.Type)) continue;
            bool lm = !string.IsNullOrEmpty(e.DisplayName) && HasSocket(e, "SOCKET_Landmark");
            if (landmarkOnly ? !lm : e.Size == new Vector2Int(8, 8)) continue;   // 1×1 rooms come from mask selection
            int cap = Mathf.Max(e.Max, e.Min) + (landmarkOnly ? 2 : 0);          // a landmark room may repeat (far away) so every section gets one
            if (CountOf(e) >= cap) continue;
            if (NearChunk(e.Name, s.Bounds.center, SameChunkSpacing)) continue;
            pool.Add(e);
        }
        return pool;
    }

    private void GlueChewed(ChunkPlacement den, Func<int, Transform> parentOf)
    {
        var chewed = Chunks.Find("CH_Corridor_Chewed");
        var d = den.Doors[0];
        Vector2 o = d.Pos + (Vector2)d.Dir * 4f;
        var oc = new Vector2Int(Mathf.FloorToInt(o.x / 8f), Mathf.FloorToInt(o.y / 8f));
        var beyond = oc + d.Dir;
        int si = _sections.IndexOf(den.Section);
        var r = SectionCellsRect(den.Section);
        if (!r.Contains(beyond) || _cells[beyond.x, beyond.y].InRoom) return;
        var cell = _cells[oc.x, oc.y];
        if (cell.Mask != Bit(-d.Dir)) return;   // something else already connects here
        if (!SectionStaysConnected(r, new RectInt(oc.x, oc.y, 1, 1), new List<Vector2Int> { beyond }, oc)) return;
        // The chewed corridor fills the door cell (N/S doors along the door axis); the cell beyond becomes required.
        int yaw = d.Dir.y != 0 ? 0 : 90;
        cell.InRoom = true;
        var beyondCell = _cells[beyond.x, beyond.y];
        beyondCell.Mask |= Bit(-d.Dir); beyondCell.Used = true; beyondCell.Required = true;
        var p = StampChunk(chewed, new Vector2Int(oc.x * 8, oc.y * 8), yaw, den.Section.Type, den.Section, parentOf(si), $"Cell_{oc.x}_{oc.y}_{chewed.Name}");
        cell.Chunk = p;
    }

    /// <summary>With <paramref name="block"/> taken by a room, can every required cell of the section (plus
    /// <paramref name="extraReq"/>, minus <paramref name="ignore"/>) still reach each other through free cells?</summary>
    private bool SectionStaysConnected(RectInt r, RectInt block, List<Vector2Int> extraReq, Vector2Int? ignore)
    {
        bool Blocked(Vector2Int c) => _cells[c.x, c.y].InRoom || block.Contains(c);
        var req = new List<Vector2Int>();
        for (int x = r.xMin; x < r.xMax; x++)
            for (int z = r.yMin; z < r.yMax; z++)
            {
                var c = new Vector2Int(x, z);
                if (_cells[x, z].Required && !(ignore.HasValue && ignore.Value == c)) req.Add(c);
            }
        req.AddRange(extraReq);
        foreach (var c in req) if (Blocked(c)) return false;
        if (req.Count <= 1) return true;
        var seen = new HashSet<Vector2Int> { req[0] };
        var q = new Queue<Vector2Int>(); q.Enqueue(req[0]);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            {
                var n = c + d;
                if (!r.Contains(n) || seen.Contains(n) || Blocked(n)) continue;
                seen.Add(n); q.Enqueue(n);
            }
        }
        foreach (var c in req) if (!seen.Contains(c)) return false;
        return true;
    }

    // ── per-section maze ─────────────────────────────────────────────────────

    private void GrowSectionMaze(SectionInfo s)
    {
        var r = SectionCellsRect(s);
        var free = new List<Vector2Int>();
        for (int x = r.xMin; x < r.xMax; x++) for (int z = r.yMin; z < r.yMax; z++) if (!_cells[x, z].InRoom) free.Add(new Vector2Int(x, z));
        if (free.Count == 0) return;

        // Start from the required cell closest to the centre (else the free cell closest to it).
        Vector2 centre = r.center - new Vector2(0.5f, 0.5f);
        Vector2Int start = free[0]; float bestD = float.MaxValue;
        foreach (var c in free) { float d = Vector2.Distance(c, centre) - (_cells[c.x, c.y].Required ? 100f : 0f); if (d < bestD) { bestD = d; start = c; } }

        var inTree = new HashSet<Vector2Int> { start };
        _cells[start.x, start.y].Used = true;
        int target = Mathf.RoundToInt(free.Count * CellFill);
        var frontier = new List<(Vector2Int a, Vector2Int b)>();
        void AddFrontier(Vector2Int a)
        {
            foreach (var d in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            {
                var b = a + d;
                if (r.Contains(b) && !_cells[b.x, b.y].InRoom && !inTree.Contains(b)) frontier.Add((a, b));
            }
        }
        AddFrontier(start);
        while (inTree.Count < target && frontier.Count > 0)
        {
            int i = _rng.Next(frontier.Count);
            var (a, b) = frontier[i]; frontier.RemoveAt(i);
            if (inTree.Contains(b)) continue;
            Link(a, b); inTree.Add(b); AddFrontier(b);
        }

        // Every required cell (room doors, section links) joins the tree by a shortest path through free cells.
        foreach (var c in free)
        {
            if (!_cells[c.x, c.y].Required || inTree.Contains(c)) continue;
            var path = BfsToTree(c, inTree, r);
            if (path == null) { Debug.LogWarning($"[ChunkGrid] cell {c} can't reach its section's maze.", this); continue; }
            for (int i = 0; i + 1 < path.Count; i++) { Link(path[i], path[i + 1]); inTree.Add(path[i]); }
        }

        // A few loops.
        foreach (var a in inTree)
            foreach (var d in new[] { Vector2Int.right, Vector2Int.up })
            {
                var b = a + d;
                if (!inTree.Contains(b) || (_cells[a.x, a.y].Mask & Bit(d)) != 0) continue;
                if (Roll(CellLoopChance)) Link(a, b);
            }
    }

    private List<Vector2Int> BfsToTree(Vector2Int from, HashSet<Vector2Int> tree, RectInt r)
    {
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var q = new Queue<Vector2Int>(); q.Enqueue(from); prev[from] = from;
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (tree.Contains(c))
            {
                var path = new List<Vector2Int>();
                for (var p = c; ; p = prev[p]) { path.Add(p); if (p == from) break; }
                path.Reverse();
                return path;
            }
            foreach (var d in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            {
                var n = c + d;
                if (!r.Contains(n) || _cells[n.x, n.y].InRoom || prev.ContainsKey(n)) continue;
                prev[n] = c; q.Enqueue(n);
            }
        }
        return null;
    }

    // ── selection + stamping ─────────────────────────────────────────────────

    private static bool IsBaseCorridor(Entry e) =>
        e.Name.StartsWith("CH_Corridor_Straight") && !e.Name.EndsWith("_B") ||
        e.Name.StartsWith("CH_Corridor_Corner") && !e.Name.EndsWith("_B") ||
        e.Name.StartsWith("CH_Corridor_T") && !e.Name.EndsWith("_B") ||
        e.Name.StartsWith("CH_Corridor_Cross") || e.Name.StartsWith("CH_Corridor_DeadEnd");

    private SectionType CellTheme(Vector2Int c) => _cells[c.x, c.y].Section >= 0 ? _sections[_cells[c.x, c.y].Section].Type : SectionType.Transition;

    private void SelectAndStampCells(Func<int, Transform> parentOf)
    {
        var cellsList = new List<Vector2Int>();
        for (int x = 0; x < _cw; x++) for (int z = 0; z < _ch; z++) if (_cells[x, z].Used && !_cells[x, z].InRoom) cellsList.Add(new Vector2Int(x, z));
        Shuffle(cellsList);

        // a) 1×2 rooms over two aligned straight cells of the same section.
        foreach (var c in cellsList)
        {
            var cell = _cells[c.x, c.y];
            if (cell.Chunk != null || cell.Section < 0) continue;
            bool ns = cell.Mask == (DN | DS), ew = cell.Mask == (DE | DW);
            if (!ns && !ew) continue;
            var n = c + (ns ? Vector2Int.up : Vector2Int.right);
            if (!CellOk(n.x, n.y)) continue;
            var nc = _cells[n.x, n.y];
            if (nc.Chunk != null || nc.InRoom || nc.Section != cell.Section || nc.Mask != cell.Mask) continue;
            if (!Roll(MergeRoomChance)) continue;
            var s = _sections[cell.Section];
            var pool = new List<Entry>();
            foreach (var e in Chunks.Entries)
            {
                if (e?.Prefab == null || e.Weight <= 0f || e.Size != new Vector2Int(8, 16) || EntryBits(e) != (DN | DS)) continue;
                if (e.Role == "objective" || !e.AllowsTheme(s.Type) || !UnderMax(e) || NearChunk(e.Name, s.Bounds.center, SameChunkSpacing)) continue;
                pool.Add(e);
            }
            var pick = PickWeighted(pool);
            if (pick == null) continue;
            int yaw = ns ? (_rng.Next(2) == 0 ? 0 : 180) : (_rng.Next(2) == 0 ? 90 : 270);
            var p = StampChunk(pick, new Vector2Int(c.x * 8, c.y * 8), yaw, s.Type, s, parentOf(cell.Section), $"Cell_{c.x}_{c.y}_{pick.Name}");
            cell.Chunk = nc.Chunk = p;
        }

        // b) every other cell: a 1×1 chunk whose rotated doors equal the mask.
        cellsList.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));   // deterministic sweep
        foreach (var c in cellsList)
        {
            var cell = _cells[c.x, c.y];
            if (cell.Chunk != null || cell.Mask == 0) continue;
            SectionType theme = CellTheme(c);
            var cands = new List<(Entry e, int yaw)>();
            foreach (var e in Chunks.Entries)
            {
                if (e?.Prefab == null || e.Weight <= 0f || e.Size != new Vector2Int(8, 8)) continue;
                if (e.Role == "objective" || e.Name == "CH_Corridor_Chewed") continue;
                if (e.Name.EndsWith("_H36") || e.Name.EndsWith("_H50")) continue;   // swapped in below by theme
                if (!e.AllowsTheme(theme) && !(theme == SectionType.Transition && e.IsAnyTheme)) continue;
                if (!IsBaseCorridor(e) && !UnderMax(e)) continue;
                if (!IsBaseCorridor(e) && NearChunk(e.Name, new Vector2(c.x * 8 + 4, c.y * 8 + 4), 40f)) continue;
                int bits = EntryBits(e);
                foreach (int yaw in new[] { 0, 90, 180, 270 }) if (RotBits(bits, yaw) == cell.Mask) { cands.Add((e, yaw)); break; }
            }
            // No-repeat-neighbour: drop entries a neighbour already uses (if anything else is left).
            var neighbourNames = new HashSet<string>();
            foreach (var d in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            {
                var n = c + d;
                if (CellOk(n.x, n.y) && _cells[n.x, n.y].Chunk != null) neighbourNames.Add(BaseName(_cells[n.x, n.y].Chunk.E.Name));
            }
            var fresh = cands.FindAll(k => !neighbourNames.Contains(BaseName(k.e.Name)));
            if (fresh.Count > 0) cands = fresh;

            if (cands.Count == 0) { GridFallback(c, theme); continue; }
            float total = 0f; foreach (var k in cands) total += k.e.Weight;
            double roll = _rng.NextDouble() * total; var pick = cands[cands.Count - 1];
            foreach (var k in cands) { roll -= k.e.Weight; if (roll <= 0) { pick = k; break; } }
            Entry use = IsBaseCorridor(pick.e) ? Variant(pick.e, theme) : pick.e;
            // The variant has the same doors: recompute the yaw for it (identical sides).
            int yawUse = pick.yaw;
            var p = StampChunk(use, new Vector2Int(c.x * 8, c.y * 8), yawUse, theme, cell.Section >= 0 ? _sections[cell.Section] : null,
                               parentOf(cell.Section), $"Cell_{c.x}_{c.y}_{use.Name}");
            cell.Chunk = p;
            if (BitCount(cell.Mask) == 1 && (use.Name.StartsWith("CH_Corridor_DeadEnd") || use.Name == "CH_Room_Storage")) _deadEndRooms.Add(p.Rect);
        }
    }

    private static string BaseName(string n) => n.Replace("_H36", "").Replace("_H50", "");

    /// <summary>No chunk fits: a procedural 4 m corridor (centre + an arm per door) on the generator's own geometry.</summary>
    private void GridFallback(Vector2Int c, SectionType theme)
    {
        _gridGaps.Add($"{MaskName(_cells[c.x, c.y].Mask)} in {theme}");
        int x0 = c.x * 8, z0 = c.y * 8;
        void Floor(int x, int z) { if (InBounds(x, z)) { _floor[x, z] = true; _reserved[x, z] = true; } }
        for (int x = 2; x < 6; x++) for (int z = 2; z < 6; z++) Floor(x0 + x, z0 + z);
        int m = _cells[c.x, c.y].Mask;
        for (int k = 0; k < 2; k++)
            for (int w = 2; w < 6; w++)
            {
                if ((m & DN) != 0) Floor(x0 + w, z0 + 6 + k);
                if ((m & DS) != 0) Floor(x0 + w, z0 + k);
                if ((m & DE) != 0) Floor(x0 + 6 + k, z0 + w);
                if ((m & DW) != 0) Floor(x0 + k, z0 + w);
            }
    }

    /// <summary>Collar target per chunk = the tallest chunk across its doors (README §8.2 FitToTheme).</summary>
    private void FitGridCollars()
    {
        foreach (var p in _chunks)
        {
            if (p.Removed) continue;
            float h = p.E.WallHeight;
            foreach (var d in p.Doors)
            {
                Vector2Int t = DoorOutside(d, 0);
                if (!IsChunkTile(t.x, t.y)) { h = Mathf.Max(h, WallHeightFor(SectionAtTile(t))); continue; }   // procedural fallback neighbour
                var o = _chunks[_chunkOwner[t.x, t.y] - 1];
                h = Mathf.Max(h, o.E.WallHeight);
            }
            p.FitHeight = h;
        }
    }

    /// <summary>Grid validation after the prune: every used cell's chunk must be in the main floor region.</summary>
    private void ValidateChunkGrid()
    {
        bool[,] main = MainComponentFrom(_chunks.Count > 0 ? FirstChunkFloor() : Vector2Int.zero);
        int lost = 0;
        foreach (var p in _chunks)
        {
            if (p.Removed) continue;
            bool ok = false;
            for (int x = p.Rect.xMin; x < p.Rect.xMax && !ok; x++)
                for (int y = p.Rect.yMin; y < p.Rect.yMax && !ok; y++)
                    if (main[x, y]) ok = true;
            if (!ok) { lost++; RemoveChunk(p); }   // an island the prune cut off: drop its geometry too
        }
        string msg = $"[ChunkGrid] Connectivity: {LiveChunkCount() - lost}/{LiveChunkCount()} chunks in the main region, " +
                     $"{_treeLinks} section links + {_loopLinks} loops.";
        if (lost > 0) Debug.LogWarning(msg, this); else Debug.Log(msg, this);
    }

    private Vector2Int FirstChunkFloor()
    {
        foreach (var p in _chunks)
            if (!p.Removed)
                for (int x = p.Rect.xMin; x < p.Rect.xMax; x++)
                    for (int y = p.Rect.yMin; y < p.Rect.yMax; y++)
                        if (_floor[x, y]) return new Vector2Int(x, y);
        return Vector2Int.zero;
    }

    private bool[,] MainComponentFrom(Vector2Int s0)
    {
        var main = new bool[_width, _height];
        if (!IsFloor(s0.x, s0.y)) return main;
        var st = new Stack<Vector2Int>(); st.Push(s0); main[s0.x, s0.y] = true;
        while (st.Count > 0)
        {
            var p = st.Pop();
            Try(p.x + 1, p.y); Try(p.x - 1, p.y); Try(p.x, p.y + 1); Try(p.x, p.y - 1);
            void Try(int x, int y) { if (!IsFloor(x, y) || main[x, y]) return; main[x, y] = true; st.Push(new Vector2Int(x, y)); }
        }
        return main;
    }

    /// <summary>Authoring to-do: how many distinct 1×1 chunks fit each door mask per theme (rotations included).</summary>
    public string ChunkCoverageReport()
    {
        if (Chunks == null) return "no chunk library";
        var masks = new (string name, int mask)[] { ("DeadEnd", DS), ("Straight", DN | DS), ("Corner", DN | DE), ("T", DN | DE | DW), ("Cross", DN | DE | DS | DW) };
        var themes = (SectionType[])Enum.GetValues(typeof(SectionType));
        var sb = new StringBuilder("mask / theme: distinct chunks (names)\n");
        foreach (var (mname, mask) in masks)
            foreach (var t in themes)
            {
                var names = new List<string>();
                foreach (var e in Chunks.Entries)
                {
                    if (e?.Prefab == null || e.Size != new Vector2Int(8, 8) || e.Role == "objective" || e.Name == "CH_Corridor_Chewed") continue;
                    if (!e.AllowsTheme(t) && !(t == SectionType.Transition && e.IsAnyTheme)) continue;
                    int bits = EntryBits(e); bool fits = false;
                    foreach (int yaw in new[] { 0, 90, 180, 270 }) if (RotBits(bits, yaw) == mask) fits = true;
                    if (fits) names.Add(e.Name.Replace("CH_", ""));
                }
                sb.AppendLine($"{mname,-8} {t,-12} {names.Count}: {string.Join(", ", names)}");
            }
        return sb.ToString();
    }
}
