using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Living Map Milestone 1 additions to the live generator (Docs/Design/LivingMap.md §11):
///  • S5  spanning tree of sections + a few extra loops + dead-end side rooms (replaces the full lattice),
///        with a connectivity check after the island prune;
///  • S3  kit-model dressing from Inspector-assigned <see cref="PropPalette"/>s (primitives stay the fallback);
///  • S6  one landmark per section, placed in the clearest spot near its centre;
///  • E5  drip points for the <see cref="SoundscapeController"/>;
///  • R4  extraction picked among candidates in different sections + start in a walking-distance band;
///  • navigation validation: a 2×2-tile clearance flood fill from the start — props that cut off floor
///        or an objective are removed (CharacterController radius 0.4 → 2 tiles = safe), and
///  • a <see cref="DungeonLayout"/> summary on the generated root for the runtime systems.
/// All randomness here still flows through the seeded <c>_rng</c> (or the mission RNG for placement).
/// </summary>
public partial class MultiSectionDungeonGenerator
{
    [Serializable]
    public class ThemeDressing
    {
        public SectionType Section;
        [Tooltip("Free-standing + against-wall kit props for this theme.")]
        public PropPalette FloorProps;
        [Tooltip("Props hung on wall faces (banners, windows, sconces) and ceiling corners (cobwebs).")]
        public PropPalette WallProps;
        [Tooltip("Landmark candidates for sections of this theme (one landmark per section).")]
        public PropPalette Landmarks;
        [Tooltip("Floor props per 100 free floor tiles.")]
        [Range(0f, 10f)] public float FloorDensity = 2f;
        [Tooltip("Wall props per 100 wall faces.")]
        [Range(0f, 20f)] public float WallDensity = 4f;
        [Tooltip("Colour of the landmark's own light (unshadowed, range-limited).")]
        public Color LandmarkLight = new(1f, 0.7f, 0.4f);
    }

    [Header("Living Map — layout (S5: routes, loops, dead ends)")]
    [Tooltip("Connect sections with a random spanning tree + a few extra loops instead of linking every neighbour.")]
    [SerializeField] private bool UseSpanningTree = true;
    [Tooltip("Chance for each non-tree neighbour link to be added back as a loop.")]
    [SerializeField, Range(0f, 1f)] private float ExtraLoopChance = 0.3f;
    [SerializeField, Min(0)] private int MaxExtraLoops = 4;
    [Tooltip("Guaranteed loops even when the chance rolls come up empty.")]
    [SerializeField, Min(0)] private int MinExtraLoops = 2;
    [Tooltip("Dead-end side rooms (single entrance) carved into the gaps — evidence prefers them.")]
    [SerializeField] private Vector2Int DeadEndRoomCount = new(2, 3);
    [Tooltip("Min/max side (tiles) of a dead-end room.")]
    [SerializeField] private Vector2Int DeadEndRoomSize = new(6, 9);
    [Tooltip("Min centre distance (tiles) between dead-end rooms and generator rooms.")]
    [SerializeField] private int DeadEndRoomSpacing = 28;

    [Header("Living Map — kit dressing (S3)")]
    [SerializeField] private bool UseKitDressing = true;
    [SerializeField] private ThemeDressing[] Dressing = Array.Empty<ThemeDressing>();
    [Tooltip("Kit swaps for the primitive builders (empty = keep the primitive).")]
    [SerializeField] private PropPalette KitHellPillars;
    [SerializeField] private PropPalette KitDeadTrees;
    [SerializeField] private PropPalette KitWindows;
    [SerializeField] private PropPalette KitCellBars;
    [SerializeField] private PropPalette KitWallPanels;
    [SerializeField] private PropPalette KitBonePiles;
    [SerializeField] private PropPalette KitCandles;
    [Tooltip("Colliders: only solid kit props get ONE BoxCollider from their bounds.")]
    [SerializeField] private bool KitColliders = true;

    [Header("Living Map — landmarks (S6)")]
    [SerializeField] private bool PlaceLandmarks = true;
    [SerializeField, Min(0f)] private float LandmarkLightIntensity = 6f;
    [SerializeField, Range(2f, 10f)] private float LandmarkLightRange = 9f;

    [Header("Living Map — soundscape (E5)")]
    [Tooltip("Drip emitter points per 100 floor tiles (Catacombs, Cavern, Gothic).")]
    [SerializeField, Range(0f, 5f)] private float DripDensity = 0.5f;

    [Header("Living Map — placement (R4)")]
    [SerializeField] private bool VariedPlacement = true;
    [Tooltip("How many extraction candidates (in different sections) the run chooses among.")]
    [SerializeField, Range(1, 6)] private int ExtractionCandidates = 3;
    [Tooltip("Weight of ExtractionPreferredSection when choosing candidate sections (others = 1).")]
    [SerializeField, Min(0f)] private float PreferredSectionWeight = 2f;
    [Tooltip("Player start walking distance from the extraction, as a fraction of the farthest walkable tile.")]
    [SerializeField] private Vector2 StartDistanceBand = new(0.35f, 0.6f);
    [Tooltip("How many evidence items try to land in dead-end rooms first.")]
    [SerializeField, Min(0)] private int EvidenceInDeadEnds = 1;

    [Header("Living Map — navigation check")]
    [SerializeField] private bool ValidateNavigation = true;
    [Tooltip("Inflate solid prop bounds by this (m) when marking blocked tiles.")]
    [SerializeField, Range(0f, 0.5f)] private float BlockerInflate = 0.2f;

    [SerializeField, HideInInspector] private int _lastSeed;
    /// <summary>The seed actually used by the last Generate (Seed, or the random one when Seed = 0).</summary>
    public int LastSeed => _lastSeed;

    // ── state ────────────────────────────────────────────────────────────────
    private bool[,] _blocked;
    private bool[,] _lane;           // room lanes inside Gothic/Catacomb sections (reserved, but a landmark may sit on one)
    private bool _markLanes;
    private class Blocker { public GameObject Go; public RectInt Tiles; }
    private readonly List<Blocker> _blockers = new();
    private readonly List<RectInt> _deadEndRooms = new();
    private readonly List<DungeonLayout.Section> _layoutSections = new();
    private readonly List<Vector3> _dripPoints = new();
    private readonly List<Vector2Int> _missionTargets = new();
    private Vector2Int _startTile, _extTile;
    private int _startWalk, _treeLinks, _loopLinks;
    private Transform _kitRoot, _landmarkRoot;
    private readonly Dictionary<GameObject, int> _usedLandmarks = new();   // prefab -> times used this run

    private void ResetLivingMapState()
    {
        _blockers.Clear();
        _deadEndRooms.Clear();
        _layoutSections.Clear();
        _dripPoints.Clear();
        _missionTargets.Clear();
        _usedLandmarks.Clear();
        _treeLinks = _loopLinks = 0;
        _startWalk = 0;
        _startTile = _extTile = default;
        _kitRoot = _landmarkRoot = null;
    }

    // =====================================================================
    // S5 — spanning tree + loops + dead ends
    // =====================================================================

    private void ConnectSectionsTree()
    {
        var grid = new SectionInfo[GridColumns, GridRows];
        foreach (SectionInfo s in _sections)
            if (s.Col >= 0 && s.Col < GridColumns && s.Row >= 0 && s.Row < GridRows) grid[s.Col, s.Row] = s;

        var edges = new List<(SectionInfo a, SectionInfo b)>();
        for (int col = 0; col < GridColumns; col++)
            for (int row = 0; row < GridRows; row++)
            {
                SectionInfo s = grid[col, row];
                if (s == null) continue;
                if (col + 1 < GridColumns && grid[col + 1, row] != null) edges.Add((s, grid[col + 1, row]));
                if (row + 1 < GridRows && grid[col, row + 1] != null) edges.Add((s, grid[col, row + 1]));
            }
        Shuffle(edges);

        // Kruskal with union-find over section indices.
        var parent = new int[_sections.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }

        var chosen = new List<(SectionInfo a, SectionInfo b)>();
        var rest = new List<(SectionInfo a, SectionInfo b)>();
        foreach (var e in edges)
        {
            int ra = Find(_sections.IndexOf(e.a)), rb = Find(_sections.IndexOf(e.b));
            if (ra != rb) { parent[ra] = rb; chosen.Add(e); }
            else rest.Add(e);
        }
        _treeLinks = chosen.Count;
        var skipped = new List<(SectionInfo a, SectionInfo b)>();
        foreach (var e in rest)
        {
            if (_loopLinks >= MaxExtraLoops) break;
            if (Roll(ExtraLoopChance)) { chosen.Add(e); _loopLinks++; }
            else skipped.Add(e);
        }
        // Always at least MinExtraLoops circuits (a pure tree has no "lose it around the loop" chases).
        for (int i = 0; i < skipped.Count && _loopLinks < Mathf.Min(MinExtraLoops, MaxExtraLoops); i++)
        {
            chosen.Add(skipped[i]); _loopLinks++;
        }

        foreach (var e in chosen) CarveCorridorL(e.a.Center, e.b.Center, reserve: true);
    }

    private void CarveDeadEndRooms()
    {
        _deadEndRooms.Clear();
        int want = _rng.Next(Mathf.Min(DeadEndRoomCount.x, DeadEndRoomCount.y), Mathf.Max(DeadEndRoomCount.x, DeadEndRoomCount.y) + 1);
        for (int n = 0; n < want; n++)
        {
            int w = _rng.Next(DeadEndRoomSize.x, DeadEndRoomSize.y + 1);
            int h = _rng.Next(DeadEndRoomSize.x, DeadEndRoomSize.y + 1);
            int[,] sum = BuildFloorPrefixSum();
            List<RectInt> spots = CollectEmptySpots(sum, w, h, 2);
            if (spots.Count == 0) spots = CollectEmptySpots(sum, w, h, 1);
            spots.RemoveAll(r => TooCloseToAnyRoom(r, DeadEndRoomSpacing));
            if (spots.Count == 0) break;
            RectInt room = spots[_rng.Next(spots.Count)];
            CarveRoomAndConnect(room);
            _deadEndRooms.Add(room);
        }
    }

    private bool TooCloseToAnyRoom(RectInt candidate, int spacing)
    {
        Vector2Int c = RectCenter(candidate);
        foreach (GeneratorRoom r in _generatorRooms)
        {
            Vector2Int o = RectCenter(r.Rect);
            if (Mathf.Abs(c.x - o.x) + Mathf.Abs(c.y - o.y) < spacing) return true;
        }
        foreach (RectInt r in _deadEndRooms)
        {
            Vector2Int o = RectCenter(r);
            if (Mathf.Abs(c.x - o.x) + Mathf.Abs(c.y - o.y) < spacing) return true;
        }
        return false;
    }

    /// <summary>After the prune: every section, generator room and dead-end room must still be floor and
    /// reachable from the first section. Repairs with a corridor + re-prune if not (shouldn't happen).</summary>
    private void ValidateLayoutConnectivity()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (_sections.Count == 0) return;
            int[,] dist = WalkDistances(_sections[0].Center);
            var broken = new List<SectionInfo>();
            foreach (SectionInfo s in _sections)
                if (!IsFloor(s.Center.x, s.Center.y) || dist[s.Center.x, s.Center.y] < 0) broken.Add(s);
            int lostRooms = 0;
            foreach (GeneratorRoom g in _generatorRooms) { var c = RectCenter(g.Rect); if (!IsFloor(c.x, c.y)) lostRooms++; }
            foreach (RectInt r in _deadEndRooms) { var c = RectCenter(r); if (!IsFloor(c.x, c.y)) lostRooms++; }

            if (broken.Count == 0)
            {
                if (lostRooms > 0) Debug.LogWarning($"[MultiSectionDungeon] {lostRooms} side room(s) were pruned as islands.", this);
                Debug.Log($"[MultiSectionDungeon] Layout: {_treeLinks} tree links + {_loopLinks} loops, " +
                          $"{_deadEndRooms.Count} dead-end rooms, {_generatorRooms.Count} generator rooms — all {_sections.Count} sections connected.", this);
                return;
            }
            Debug.LogWarning($"[MultiSectionDungeon] {broken.Count} section(s) disconnected — repairing.", this);
            foreach (SectionInfo s in broken)
            {
                SectionInfo best = null; int bestD = int.MaxValue;
                foreach (SectionInfo o in _sections)
                {
                    if (broken.Contains(o)) continue;
                    int d = Mathf.Abs(o.Center.x - s.Center.x) + Mathf.Abs(o.Center.y - s.Center.y);
                    if (d < bestD) { bestD = d; best = o; }
                }
                if (best != null) CarveCorridorL(s.Center, best.Center, reserve: true);
            }
            PruneFloorIslands();
        }
    }

    // =====================================================================
    // S3 — kit dressing
    // =====================================================================

    private ThemeDressing DressingFor(SectionType t)
    {
        if (Dressing == null) return null;
        foreach (var d in Dressing) if (d != null && d.Section == t) return d;
        return null;
    }

    private Transform KitRoot => _kitRoot != null ? _kitRoot : (_kitRoot = NewChild("Kit"));

    /// <summary>Instantiate a kit entry: compose yaw × offset × the model's own root rotation, random scale,
    /// strip imported colliders, shadows per entry.</summary>
    private GameObject SpawnKit(PropPalette.Entry e, Transform parent, float yaw)
    {
        GameObject go = InstantiatePrefab(e.Prefab);
        go.transform.SetParent(parent, false);
        Quaternion modelRot = e.Prefab.transform.localRotation;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(e.RotationOffset) * modelRot;
        float s = RandomRange(Mathf.Min(e.ScaleRange.x, e.ScaleRange.y), Mathf.Max(e.ScaleRange.x, e.ScaleRange.y));
        if (s <= 0f) s = 1f;
        go.transform.localScale = e.Prefab.transform.localScale * s;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediateSafe(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = e.CastShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    private static bool WorldBounds(GameObject go, out Bounds b)
    {
        b = default; bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>Move so the bounds' XZ centre is on <paramref name="localXZ"/> and the bottom rests at floor + yOffset.</summary>
    private void SnapToFloor(GameObject go, Vector3 localXZ, float yOffset)
    {
        if (!WorldBounds(go, out Bounds b)) { go.transform.localPosition = localXZ + Vector3.up * yOffset; return; }
        Vector3 target = _root.TransformPoint(new Vector3(localXZ.x, yOffset, localXZ.z));
        Vector3 delta = new(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
        go.transform.position += delta;
    }

    /// <summary>Tile rect (grid space) covered by a world bounds, inflated.</summary>
    private RectInt TilesOf(Bounds b, float inflate)
    {
        Vector3 mn = _root.InverseTransformPoint(b.min), mx = _root.InverseTransformPoint(b.max);
        float x0 = Mathf.Min(mn.x, mx.x) - inflate, x1 = Mathf.Max(mn.x, mx.x) + inflate;
        float z0 = Mathf.Min(mn.z, mx.z) - inflate, z1 = Mathf.Max(mn.z, mx.z) + inflate;
        // A tile is covered when its centre lies inside the rect.
        int tx0 = Mathf.CeilToInt(x0 - 0.5f), tx1 = Mathf.FloorToInt(x1 - 0.5f);
        int tz0 = Mathf.CeilToInt(z0 - 0.5f), tz1 = Mathf.FloorToInt(z1 - 0.5f);
        if (tx1 < tx0) { int c = Mathf.FloorToInt((x0 + x1) * 0.5f); tx0 = tx1 = c; }
        if (tz1 < tz0) { int c = Mathf.FloorToInt((z0 + z1) * 0.5f); tz0 = tz1 = c; }
        return new RectInt(tx0, tz0, tx1 - tx0 + 1, tz1 - tz0 + 1);
    }

    private bool FootprintFree(RectInt r, bool allowWallNeighbours)
    {
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
            {
                if (!IsFloor(x, y)) { if (allowWallNeighbours) continue; return false; }
                if (_reserved[x, y] || _blocked[x, y]) return false;
            }
        // 1-tile ring: no other blocker touching (keeps props from fusing into walls of furniture).
        for (int x = r.xMin - 1; x <= r.xMax; x++)
            for (int y = r.yMin - 1; y <= r.yMax; y++)
            {
                if (x >= r.xMin && x < r.xMax && y >= r.yMin && y < r.yMax) continue;
                if (InBounds(x, y) && _blocked[x, y]) return false;
            }
        return true;
    }

    private void RegisterBlocker(GameObject go)
    {
        if (_blocked == null || go == null || !WorldBounds(go, out Bounds b)) return;
        RectInt t = TilesOf(b, BlockerInflate);
        _blockers.Add(new Blocker { Go = go, Tiles = t });
        MarkBlocked(t, true);
    }

    private void MarkBlocked(RectInt t, bool v)
    {
        for (int x = t.xMin; x < t.xMax; x++)
            for (int y = t.yMin; y < t.yMax; y++)
                if (InBounds(x, y)) _blocked[x, y] = v;
    }

    private void AddKitCollider(GameObject go)
    {
        if (!KitColliders || !WorldBounds(go, out Bounds wb)) return;
        var box = go.AddComponent<BoxCollider>();
        // Size in the root's local space (handles the FBX -90° X + ×100 scale on the root).
        Vector3 c = go.transform.InverseTransformPoint(wb.center);
        Vector3 ex = wb.extents;
        Vector3 ax = go.transform.InverseTransformVector(new Vector3(ex.x, 0, 0));
        Vector3 ay = go.transform.InverseTransformVector(new Vector3(0, ex.y, 0));
        Vector3 az = go.transform.InverseTransformVector(new Vector3(0, 0, ex.z));
        Vector3 size = new(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                           Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                           Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
        box.center = c;
        box.size = size * 2f;
    }

    private void MarkStaticDeep(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) MarkStatic(r.gameObject);
        MarkStatic(go);
    }

    /// <summary>Place a floor/against-wall kit prop at a tile if its footprint is free. Returns null otherwise.</summary>
    private GameObject TryPlaceFloorKit(PropPalette.Entry e, Transform parent, Vector2Int tile, float yaw,
                                        WallEdge? wall = null, string name = null)
    {
        GameObject go = SpawnKit(e, parent, yaw);
        if (name != null) go.name = name;
        Vector3 at = TileLocal(tile.x, tile.y);
        SnapToFloor(go, at, e.YOffset);
        if (wall.HasValue)
        {
            WallEdge w = wall.Value;
            WallMount.PushOffWall(go.transform, _root.TransformPoint(w.Surface), _root.TransformDirection(w.IntoRoom), 0.03f);
        }
        if (!WorldBounds(go, out Bounds b)) { DestroyImmediateSafe(go); return null; }
        RectInt t = TilesOf(b, e.Solid ? BlockerInflate : 0f);
        if (!FootprintFree(t, wall.HasValue)) { DestroyImmediateSafe(go); return null; }
        if (e.Solid)
        {
            AddKitCollider(go);
            _blockers.Add(new Blocker { Go = go, Tiles = t });
            MarkBlocked(t, true);
        }
        MarkStaticDeep(go);
        return go;
    }

    /// <summary>Hang a kit prop on a wall face (banner, window, bars, cobweb). Never blocks navigation.</summary>
    private GameObject PlaceWallKit(PropPalette.Entry e, Transform parent, WallEdge w)
    {
        float wh = WallHeightFor(w.Section);
        GameObject go = SpawnKit(e, parent, Quaternion.LookRotation(w.IntoRoom).eulerAngles.y);
        float bottom = e.Placement == PropPalette.Mount.Ceiling ? 0f : e.YOffset;
        SnapToFloor(go, w.Surface, bottom);
        if (WorldBounds(go, out Bounds b))
        {
            float h = b.size.y;
            if (e.Placement == PropPalette.Mount.Ceiling)
                go.transform.position += Vector3.up * (wh - 0.05f - h - e.YOffset);
            else if (b.min.y + h > _root.position.y + wh - 0.1f)
            {
                // Too tall for this wall: lower it, then shrink if it still pokes through the ceiling.
                float room = wh - 0.1f;
                if (h > room - 0.2f) { float k = (room - 0.2f) / h; go.transform.localScale *= k; SnapToFloor(go, w.Surface, 0.2f); }
                else go.transform.position += Vector3.up * (room - (b.min.y - _root.position.y) - h);
            }
        }
        WallMount.PushOffWall(go.transform, _root.TransformPoint(w.Surface), _root.TransformDirection(w.IntoRoom), 0.02f);
        MarkStaticDeep(go);
        return go;
    }

    private void DressWithKit()
    {
        if (!UseKitDressing || Dressing == null) return;
        foreach (var d in Dressing)
        {
            if (d == null) continue;
            DressFloors(d);
            DressWalls(d);
        }
    }

    private void DressFloors(ThemeDressing d)
    {
        if (d.FloorProps == null || !d.FloorProps.IsValid || d.FloorDensity <= 0f) return;

        // Free tiles of this theme (off corridors/objectives).
        var free = new List<Vector2Int>();
        for (int x = 1; x < _width - 1; x++)
            for (int y = 1; y < _height - 1; y++)
                if (_floor[x, y] && _section[x, y] == d.Section && !_reserved[x, y] && !_blocked[x, y]) free.Add(new Vector2Int(x, y));
        if (free.Count == 0) return;

        int target = Mathf.RoundToInt(free.Count * d.FloorDensity / 100f);
        Transform parent = KitRoot;

        // 1) Against-wall furniture along wall edges (faces into the room).
        if (d.FloorProps.Has(PropPalette.Mount.AgainstWall))
        {
            List<WallEdge> edges = CollectWallEdges(t => t == d.Section);
            Shuffle(edges);
            int wallTarget = Mathf.RoundToInt(target * 0.55f), placed = 0;
            foreach (WallEdge e in edges)
            {
                if (placed >= wallTarget) break;
                if (_reserved[e.Cx, e.Cy] || _blocked[e.Cx, e.Cy]) continue;
                var entry = d.FloorProps.Pick(_rng, PropPalette.Mount.AgainstWall);
                if (entry == null) break;
                float yaw = Quaternion.LookRotation(e.IntoRoom).eulerAngles.y;
                if (TryPlaceFloorKit(entry, parent, new Vector2Int(e.Cx, e.Cy), yaw, e) != null) placed++;
            }
            target -= placed;
        }

        // 2) Free-standing clutter.
        Shuffle(free);
        int done = 0;
        for (int i = 0; i < free.Count && done < target; i++)
        {
            Vector2Int t = free[i];
            if (_blocked[t.x, t.y]) continue;
            var entry = d.FloorProps.Pick(_rng, PropPalette.Mount.Floor);
            if (entry == null) break;
            float yaw = entry.RandomYaw ? _rng.Next(0, 4) * 90f + RandomRange(-12f, 12f) : 0f;
            if (TryPlaceFloorKit(entry, parent, t, yaw) != null) done++;
        }
    }

    private void DressWalls(ThemeDressing d)
    {
        if (d.WallProps == null || !d.WallProps.IsValid || d.WallDensity <= 0f) return;
        List<WallEdge> edges = CollectWallEdges(t => t == d.Section);
        Shuffle(edges);
        int target = Mathf.RoundToInt(edges.Count * d.WallDensity / 100f);
        var placed = new List<Vector3>();
        bool roofed = CeilingEnabledFor(d.Section);
        int n = 0;
        foreach (WallEdge e in edges)
        {
            if (n >= target) break;
            bool tooClose = false;
            foreach (var p in placed) if ((p - e.Surface).sqrMagnitude < 9f) { tooClose = true; break; }
            if (tooClose) continue;
            var entry = d.WallProps.Pick(_rng, null, x => x.Placement == PropPalette.Mount.Wall || (roofed && x.Placement == PropPalette.Mount.Ceiling));
            if (entry == null) break;
            PlaceWallKit(entry, KitRoot, e);
            placed.Add(e.Surface);
            n++;
        }
    }

    // Kit swaps used by the primitive builders. Return true if the kit handled it.
    private bool KitFloorSwap(PropPalette pal, Vector3 localPos, float yaw, string name, bool register = true)
    {
        if (!UseKitDressing || pal == null || !pal.IsValid) return false;
        var e = pal.Pick(_rng);
        GameObject go = SpawnKit(e, _decorRoot, yaw);
        go.name = name;
        SnapToFloor(go, localPos, e.YOffset);
        if (e.Solid) { AddKitCollider(go); if (register) RegisterBlocker(go); }
        MarkStaticDeep(go);
        return true;
    }

    private bool KitWallSwap(PropPalette pal, WallEdge w)
    {
        if (!UseKitDressing || pal == null || !pal.IsValid) return false;
        var e = pal.Pick(_rng, null, x => x.Placement == PropPalette.Mount.Wall || x.Placement == PropPalette.Mount.Ceiling);
        if (e == null) return false;
        PlaceWallKit(e, _detailsRoot, w);
        return true;
    }

    // =====================================================================
    // S6 — landmarks
    // =====================================================================

    private void PlaceSectionLandmarks()
    {
        _layoutSections.Clear();
        int[,] avail = BuildAvailablePrefixSum();
        int reachedBefore = CountReached(ReachedTiles(_startTile));
        foreach (SectionInfo s in _sections)
        {
            var info = new DungeonLayout.Section { Type = s.Type, Rect = s.Bounds };
            _layoutSections.Add(info);
            if (!PlaceLandmarks) continue;
            ThemeDressing d = DressingFor(s.Type);
            if (d == null || d.Landmarks == null || !d.Landmarks.IsValid) continue;

            // Least-used landmark of this theme first, so the run repeats as little as possible.
            int least = int.MaxValue;
            foreach (var x in d.Landmarks.Entries)
                if (x != null && x.Prefab != null) least = Mathf.Min(least, _usedLandmarks.TryGetValue(x.Prefab, out int u) ? u : 0);
            var entry = d.Landmarks.Pick(_rng, null, x => (_usedLandmarks.TryGetValue(x.Prefab, out int u) ? u : 0) == least) ?? d.Landmarks.Pick(_rng);
            if (entry == null) continue;

            // Measure the footprint once to know the clear radius it needs.
            GameObject go = SpawnKit(entry, _landmarkRoot, _rng.Next(0, 4) * 90f);
            go.name = "Landmark_" + (string.IsNullOrEmpty(entry.DisplayName) ? entry.Prefab.name : entry.DisplayName.Replace(" ", ""));
            SnapToFloor(go, TileLocal(s.Center.x, s.Center.y), entry.YOffset);
            WorldBounds(go, out Bounds b);
            int r = Mathf.CeilToInt(Mathf.Max(b.size.x, b.size.z) * 0.5f) + 2;   // + a walkable ring

            // Candidates near the centre; room lanes are allowed (rooms are threaded by them), but a spot is
            // only kept if the flood fill from the start still reaches as much floor as before.
            bool placed = false;
            foreach (Vector2Int spot in FindLandmarkSpots(avail, s, r))
            {
                SnapToFloor(go, TileLocal(spot.x, spot.y), entry.YOffset);
                WorldBounds(go, out b);
                RectInt tiles = TilesOf(b, BlockerInflate);
                MarkBlocked(tiles, true);
                int reached = CountReached(ReachedTiles(_startTile));
                int footprint = tiles.width * tiles.height;
                if (reached >= reachedBefore - footprint - 6 && TargetsReached())
                {
                    reachedBefore = reached;
                    _blockers.Add(new Blocker { Go = null, Tiles = tiles });   // Go = null: never removed by the fixer
                    if (entry.Solid) AddKitCollider(go);
                    MarkStaticDeep(go);
                    ReserveArea(spot, Mathf.Max(1, r - 2));   // nothing else lands on it
                    placed = true;
                    break;
                }
                MarkBlocked(tiles, false);
                foreach (var other in _blockers) MarkBlocked(other.Tiles, true);
            }
            if (!placed) { DestroyImmediateSafe(go); continue; }
            avail = BuildAvailablePrefixSum();

            // Its own readable light: centred just above the prop (under the ceiling) and marked
            // ShadowlessLight so DynamicLightBudget never gives it shadows — with shadows the prop's own top
            // (the Bell's beam) blacked out the prop, and a side light left the far side unlit.
            Light ownLight = go.GetComponentInChildren<Light>();
            if (ownLight == null && LandmarkLightIntensity > 0f)
            {
                var lg = new GameObject("LandmarkLight");
                lg.transform.SetParent(go.transform, true);
                float ceiling = _root.position.y + (CeilingEnabledFor(s.Type) ? WallHeightFor(s.Type) - 0.4f : 99f);
                lg.transform.position = new Vector3(b.center.x, Mathf.Min(b.max.y + 0.3f, ceiling), b.center.z);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = d.LandmarkLight;
                l.intensity = LandmarkLightIntensity;
                l.range = LandmarkLightRange;
                l.shadows = LightShadows.None;
                lg.AddComponent<ShadowlessLight>();
                _spawnedLights++;
            }
            else if (ownLight != null && ownLight.GetComponent<ShadowlessLight>() == null)
                ownLight.gameObject.AddComponent<ShadowlessLight>();

            _usedLandmarks[entry.Prefab] = (_usedLandmarks.TryGetValue(entry.Prefab, out int used) ? used : 0) + 1;
            info.HasLandmark = true;
            info.LandmarkName = string.IsNullOrEmpty(entry.DisplayName) ? entry.Prefab.name : entry.DisplayName;
            info.LandmarkPosition = b.center;
        }
    }

    private static int CountReached(bool[,] reached)
    {
        if (reached == null) return 0;
        int n = 0;
        foreach (bool v in reached) if (v) n++;
        return n;
    }

    private bool TargetsReached()
    {
        bool[,] reached = ReachedTiles(_startTile);
        foreach (var t in _missionTargets) if (InBounds(t.x, t.y) && _floor[t.x, t.y] && !NearReached(reached, t, 2)) return false;
        return true;
    }

    // Prefix sum of tiles a landmark may cover: floor, not blocked, not reserved (room lanes allowed).
    private int[,] BuildAvailablePrefixSum()
    {
        var sum = new int[_width + 1, _height + 1];
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
            {
                int v = _floor[x, y] && !_blocked[x, y] && (!_reserved[x, y] || _lane[x, y]) ? 1 : 0;
                sum[x + 1, y + 1] = sum[x, y + 1] + sum[x + 1, y] - sum[x, y] + v;
            }
        return sum;
    }

    private List<Vector2Int> FindLandmarkSpots(int[,] avail, SectionInfo s, int r)
    {
        var all = new List<(int score, Vector2Int p)>();
        RectInt b = s.Bounds;
        for (int rr = r; rr >= 3 && all.Count == 0; rr--)
        {
            int side = 2 * rr + 1;
            for (int x = b.xMin + rr; x < b.xMax - rr; x++)
                for (int y = b.yMin + rr; y < b.yMax - rr; y++)
                {
                    if (RectFloorCount(avail, new RectInt(x - rr, y - rr, side, side)) != side * side) continue;
                    // Close to the centre; prefer spots off the lanes.
                    int lane = 0;
                    for (int dx = -rr + 2; dx <= rr - 2; dx++)
                        for (int dy = -rr + 2; dy <= rr - 2; dy++)
                            if (_lane[x + dx, y + dy]) lane++;
                    all.Add((Mathf.Abs(x - s.Center.x) + Mathf.Abs(y - s.Center.y) + lane * 2, new Vector2Int(x, y)));
                }
        }
        all.Sort((a, c) => a.score != c.score ? a.score.CompareTo(c.score) : (a.p.x * 10000 + a.p.y).CompareTo(c.p.x * 10000 + c.p.y));
        var result = new List<Vector2Int>();
        foreach (var a in all)
        {
            bool near = false;
            foreach (var q in result) if (Mathf.Abs(q.x - a.p.x) + Mathf.Abs(q.y - a.p.y) < 3) { near = true; break; }
            if (near) continue;
            result.Add(a.p);
            if (result.Count >= 8) break;
        }
        return result;
    }

    // =====================================================================
    // E5 — drip points
    // =====================================================================

    private void CollectDripPoints()
    {
        _dripPoints.Clear();
        if (DripDensity <= 0f) return;
        foreach (SectionInfo s in _sections)
        {
            if (s.Type != SectionType.Catacombs && s.Type != SectionType.Cavern && s.Type != SectionType.GothicRuins) continue;
            var tiles = new List<Vector2Int>();
            for (int x = s.Bounds.xMin; x < s.Bounds.xMax; x++)
                for (int y = s.Bounds.yMin; y < s.Bounds.yMax; y++)
                    if (IsFloor(x, y)) tiles.Add(new Vector2Int(x, y));
            int n = Mathf.RoundToInt(tiles.Count * DripDensity / 100f * (s.Type == SectionType.GothicRuins ? 0.4f : 1f));
            for (int i = 0; i < n && tiles.Count > 0; i++)
            {
                Vector2Int t = tiles[_rng.Next(tiles.Count)];
                _dripPoints.Add(_root.TransformPoint(TileLocal(t.x, t.y) + Vector3.up * 0.05f));
            }
        }
    }

    // =====================================================================
    // R4 — varied extraction + start
    // =====================================================================

    private bool TryPickExtractionVaried(int[,] sum, ExtractionZone ext, System.Random rng, out Vector2Int tile)
    {
        int clear = ExtractionClearance(ext);
        for (int r = clear; r >= Mathf.Max(2, clear - 2); r--)
        {
            // Valid spots grouped by section.
            var bySection = new Dictionary<SectionInfo, List<Vector2Int>>();
            foreach (SectionInfo s in _sections)
            {
                var list = new List<Vector2Int>();
                for (int x = s.Bounds.xMin; x < s.Bounds.xMax; x++)
                    for (int y = s.Bounds.yMin; y < s.Bounds.yMax; y++)
                    {
                        if (!IsFloor(x, y) || _section[x, y] == SectionType.Transition) continue;
                        if (!IsClearSquare(sum, x, y, r) || NearGenerator(x, y, r + 2)) continue;
                        list.Add(new Vector2Int(x, y));
                    }
                if (list.Count > 0) bySection[s] = list;
            }
            if (bySection.Count == 0) continue;

            // Weighted pick of up to N candidate sections, then one of them.
            var pool = new List<SectionInfo>(bySection.Keys);
            pool.Sort((a, b) => (a.Row * 100 + a.Col).CompareTo(b.Row * 100 + b.Col)); // deterministic order
            var candidates = new List<Vector2Int>();
            for (int k = 0; k < ExtractionCandidates && pool.Count > 0; k++)
            {
                float total = 0f;
                foreach (var s in pool) total += s.Type == ExtractionPreferredSection ? PreferredSectionWeight : 1f;
                double roll = rng.NextDouble() * total;
                int idx = 0;
                for (; idx < pool.Count - 1; idx++)
                {
                    roll -= pool[idx].Type == ExtractionPreferredSection ? PreferredSectionWeight : 1f;
                    if (roll <= 0) break;
                }
                var list = bySection[pool[idx]];
                candidates.Add(list[rng.Next(list.Count)]);
                pool.RemoveAt(idx);
            }
            tile = candidates[rng.Next(candidates.Count)];
            return true;
        }
        tile = default;
        return false;
    }

    private Vector2Int PickPlayerStartVaried(int[,] sum, int[,] fromExt, Vector2Int ext, int extClear, System.Random rng)
    {
        int maxD = 0;
        for (int x = 0; x < _width; x++) for (int y = 0; y < _height; y++) maxD = Mathf.Max(maxD, fromExt[x, y]);
        int lo = Mathf.Max(extClear + 2, Mathf.RoundToInt(maxD * StartDistanceBand.x));
        int hi = Mathf.Max(lo, Mathf.RoundToInt(maxD * StartDistanceBand.y));

        var spots = new List<Vector2Int>();
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
            {
                int d = fromExt[x, y];
                if (d < lo || d > hi || !IsClearSquare(sum, x, y, 1)) continue;
                if (_section[x, y] == SectionType.Transition) continue;                // start inside a themed area
                if (NearGenerator(x, y, 3) || InDeadEndRoom(x, y, 2)) continue;
                spots.Add(new Vector2Int(x, y));
            }
        if (spots.Count > 0) return spots[rng.Next(spots.Count)];
        return PickPlayerStartTile(sum, fromExt, ext, extClear, rng);   // old rule as fallback
    }

    private bool InDeadEndRoom(int x, int y, int pad = 0)
    {
        foreach (RectInt r in _deadEndRooms)
            if (x >= r.xMin - pad && x < r.xMax + pad && y >= r.yMin - pad && y < r.yMax + pad) return true;
        return false;
    }

    // =====================================================================
    // Navigation validation
    // =====================================================================

    /// <summary>
    /// Flood fill with a 2×2-tile body (≥ 2 m wide gaps; the CharacterController is 0.8 m wide) from the
    /// player start over floor tiles not covered by solid props. Any pocket of free floor or objective it
    /// can't reach is opened by removing the solid prop(s) next to it.
    /// </summary>
    private void ValidateNavigationAndFix()
    {
        if (!ValidateNavigation || _blocked == null) return;
        int removed = 0, iterations = 0, pocketsLeft = 0;
        while (iterations++ < 60)
        {
            bool[,] reached = ReachedTiles(_startTile);
            if (reached == null) return;

            // Find unreached free tiles (and objectives) grouped into pockets.
            var seen = new bool[_width, _height];
            Blocker victim = null; int victimScore = 0;
            pocketsLeft = 0;
            for (int x = 1; x < _width - 1; x++)
                for (int y = 1; y < _height - 1; y++)
                {
                    if (seen[x, y] || !_floor[x, y] || _blocked[x, y] || reached[x, y]) continue;
                    List<Vector2Int> pocket = FloodPocket(x, y, reached, seen);
                    bool important = pocket.Count >= 10 || PocketHasTarget(pocket);
                    if (!important) continue;
                    pocketsLeft++;
                    Blocker b = BestBlockerFor(pocket, out int score);
                    if (b != null && score > victimScore) { victim = b; victimScore = score; }
                }
            if (victim == null) break;
            MarkBlocked(victim.Tiles, false);
            _blockers.Remove(victim);
            foreach (var other in _blockers) MarkBlocked(other.Tiles, true); // overlaps stay blocked
            if (victim.Go != null) DestroyImmediateSafe(victim.Go);
            removed++;
        }

        // Final report on objectives.
        bool[,] final = ReachedTiles(_startTile);
        int missing = 0;
        foreach (var t in _missionTargets) if (InBounds(t.x, t.y) && _floor[t.x, t.y] && !NearReached(final, t, 2)) missing++;
        string msg = $"[MultiSectionDungeon] Navigation: {_blockers.Count} solid props, removed {removed} that cut off floor, " +
                     $"{pocketsLeft} unreachable pocket(s) left, objectives unreachable: {missing}.";
        if (missing > 0) Debug.LogWarning(msg, this); else Debug.Log(msg, this);
    }

    private bool[,] ReachedTiles(Vector2Int start)
    {
        // Block (x,y) = tiles x..x+1, y..y+1.
        bool Free(int x, int y) => InBounds(x, y) && _floor[x, y] && !_blocked[x, y];
        bool BlockOk(int x, int y) => Free(x, y) && Free(x + 1, y) && Free(x, y + 1) && Free(x + 1, y + 1);

        // Nearest valid block to the start.
        Vector2Int s0 = new(-1, -1);
        for (int r = 0; r < 6 && s0.x < 0; r++)
            for (int dx = -r; dx <= r && s0.x < 0; dx++)
                for (int dy = -r; dy <= r && s0.x < 0; dy++)
                    if (BlockOk(start.x + dx, start.y + dy)) s0 = new Vector2Int(start.x + dx, start.y + dy);
        if (s0.x < 0) { Debug.LogWarning("[MultiSectionDungeon] Navigation check: start has no 2×2 clearance.", this); return null; }

        var visited = new bool[_width, _height];
        var reached = new bool[_width, _height];
        var q = new Queue<Vector2Int>();
        visited[s0.x, s0.y] = true; q.Enqueue(s0);
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            reached[p.x, p.y] = reached[p.x + 1, p.y] = reached[p.x, p.y + 1] = reached[p.x + 1, p.y + 1] = true;
            Visit(p.x + 1, p.y); Visit(p.x - 1, p.y); Visit(p.x, p.y + 1); Visit(p.x, p.y - 1);
            void Visit(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= _width - 1 || ny >= _height - 1 || visited[nx, ny] || !BlockOk(nx, ny)) return;
                visited[nx, ny] = true; q.Enqueue(new Vector2Int(nx, ny));
            }
        }
        return reached;
    }

    private List<Vector2Int> FloodPocket(int sx, int sy, bool[,] reached, bool[,] seen)
    {
        var list = new List<Vector2Int>();
        var st = new Stack<Vector2Int>();
        st.Push(new Vector2Int(sx, sy)); seen[sx, sy] = true;
        while (st.Count > 0)
        {
            var p = st.Pop(); list.Add(p);
            Try(p.x + 1, p.y); Try(p.x - 1, p.y); Try(p.x, p.y + 1); Try(p.x, p.y - 1);
            void Try(int x, int y)
            {
                if (!InBounds(x, y) || seen[x, y] || !_floor[x, y] || _blocked[x, y] || reached[x, y]) return;
                seen[x, y] = true; st.Push(new Vector2Int(x, y));
            }
        }
        return list;
    }

    private bool PocketHasTarget(List<Vector2Int> pocket)
    {
        foreach (var t in _missionTargets)
            foreach (var p in pocket)
                if (Mathf.Abs(p.x - t.x) <= 1 && Mathf.Abs(p.y - t.y) <= 1) return true;
        return false;
    }

    private bool NearReached(bool[,] reached, Vector2Int t, int r)
    {
        if (reached == null) return false;
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                if (InBounds(t.x + dx, t.y + dy) && reached[t.x + dx, t.y + dy]) return true;
        return false;
    }

    // The blocker touching the pocket the most (ties → bigger pocket wins through the caller's max).
    private Blocker BestBlockerFor(List<Vector2Int> pocket, out int score)
    {
        Blocker best = null; score = 0;
        foreach (var b in _blockers)
        {
            int touch = 0;
            RectInt r = b.Tiles;
            foreach (var p in pocket)
                if (p.x >= r.xMin - 1 && p.x <= r.xMax && p.y >= r.yMin - 1 && p.y <= r.yMax) touch++;
            if (touch == 0 || b.Go == null) continue;
            int s = touch + (PocketHasTarget(pocket) ? 1000 : 0) + pocket.Count;
            if (s > score) { score = s; best = b; }
        }
        return best;
    }

    // =====================================================================
    // Runtime summary
    // =====================================================================

    private void WriteLayout(int seed)
    {
        var layout = _root.gameObject.AddComponent<DungeonLayout>();
        layout.Set(seed, _layoutSections.ToArray(), _deadEndRooms.ToArray(), _dripPoints.ToArray(),
                   TileWorld(_extTile), TileWorld(_startTile), _startWalk, _treeLinks + _loopLinks, _loopLinks);
    }
}
