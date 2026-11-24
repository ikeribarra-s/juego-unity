using System;
using System.Collections.Generic;
using UnityEngine;
using Entry = ChunkLibrary.Entry;

/// <summary>
/// Authored chunk integration (CH_* prefabs, ArtSource/Environment/Chunks/README.md §7–8).
///
/// Chunks are stamped into the tile grid from their <c>mask_north_up</c> (rotated with the prefab): <c>#</c> stays
/// rock, <c>. o D</c> become floor owned by the chunk (<c>_chunkOwner</c>), <c>o</c> also blocks the navigation flood.
/// A 1-tile rock border around every chunk is protected from all carving (<c>_noCarve</c>) except a 3-tile stub in
/// front of each door, so procedural floor only ever touches a chunk at its doors. The generator never builds
/// floor / walls / ceilings / décor on chunk tiles — the chunk brings its own.
///
/// Where chunks go:
///  • before the sections are carved: the Extraction Gate (in a candidate section), Chucho's den (+ the chewed
///    corridor glued to its door) and 1–2 themed rooms / set pieces per section (tags: themes, weight, min/max),
///    kept off the section centre so the procedural rooms and corridors wrap around them;
///  • in the empty space between sections: the 3 Generator Alcoves (replace the generator rooms) and the dead ends
///    (DeadEnd corridors, Storage, Mausoleum — replace the dead-end rooms);
///  • on straight 4-wide procedural corridors through rock: corridor chunks (straight / B / collapsed / trap /
///    chewed / sewer…), each kept only if the 2×2 navigation flood still reaches as much floor.
/// Every free door gets a stub + an A* corridor to the main network; chunks whose doors end up unreachable are removed.
/// All picks use the seeded <c>_rng</c>.
/// </summary>
public partial class MultiSectionDungeonGenerator
{
    [Header("Authored chunks (CH_*)")]
    [SerializeField] private bool UseChunks = true;
    [Tooltip("Chunk data imported from chunks_manifest.json (Fluffterror › Living Map › Build Chunk Library).")]
    [SerializeField] private ChunkLibrary Chunks;
    [Tooltip("Themed rooms / set pieces stamped into each section (min..max).")]
    [SerializeField] private Vector2Int ChunksPerSection = new(1, 2);
    [Tooltip("Max corridor chunks laid over straight procedural corridors.")]
    [SerializeField, Min(0)] private int CorridorChunks = 8;
    [Tooltip("Min tile distance between two corridor chunks.")]
    [SerializeField, Min(8)] private int CorridorChunkSpacing = 18;
    [Tooltip("Keep chunks this many tiles away from the section centre (corridors cross there).")]
    [SerializeField, Min(0)] private int ChunkCentreKeepOut = 6;
    [Tooltip("The same chunk is never placed again within this many tiles (no twin rooms next to each other).")]
    [SerializeField, Min(0)] private int SameChunkSpacing = 70;

    private class ChunkDoor
    {
        public Vector2 Pos;          // grid-local (x, z) of the socket, on the footprint edge
        public Vector2Int Dir;       // outward, cardinal
        public bool Linked;          // faces another chunk's door
    }

    private class ChunkPlacement
    {
        public Entry E;
        public RectInt Rect;         // rotated footprint in tiles
        public int Yaw;
        public SectionType Theme;
        public SectionInfo Section;  // null = between sections
        public GameObject Go;
        public readonly List<ChunkDoor> Doors = new();
        public readonly List<RectInt> Blockers = new();
        public bool Removed;
        public float FitHeight = -1f;   // grid mode: tallest neighbour across the doors (collar target)
        public Transform Parent;        // grid mode: Map/Section_X
        public Vector2 Centre => new(Rect.x + Rect.width * 0.5f, Rect.y + Rect.height * 0.5f);
    }

    private int[,] _chunkOwner;      // 0 = none, else index + 1 into _chunks
    private bool[,] _noCarve;        // chunk tiles + their rock border (minus door stubs)
    private bool[,] _chunkClaim;     // _noCarve + door stubs (placement overlap test)
    private readonly List<ChunkPlacement> _chunks = new();
    private readonly Dictionary<Entry, int> _chunkCounts = new();
    private ChunkPlacement _gateChunk, _denChunk;
    private Transform _chunksRoot;
    private readonly List<Vector3> _hidePoints = new();
    private readonly List<Vector3> _waterPoints = new();
    private Vector3 _denPoint;
    private bool _hasDen;

    private bool ChunksOn => UseChunks && Chunks != null && Chunks.Entries.Length > 0;
    private bool IsChunkTile(int x, int y) => _chunkOwner != null && InBounds(x, y) && _chunkOwner[x, y] != 0;
    private bool NoCarve(int x, int y) => _noCarve != null && InBounds(x, y) && _noCarve[x, y];

    private void ResetChunkState()
    {
        _chunks.Clear();
        _chunkCounts.Clear();
        _gateChunk = _denChunk = null;
        _chunksRoot = null;
        _hidePoints.Clear();
        _waterPoints.Clear();
        _hasDen = false;
    }

    private void AllocChunkGrids()
    {
        _chunkOwner = new int[_width, _height];
        _noCarve = new bool[_width, _height];
        _chunkClaim = new bool[_width, _height];
    }

    // ── geometry helpers ─────────────────────────────────────────────────────

    private static Vector2Int RotatedSize(Entry e, int yaw) => yaw % 180 == 0 ? e.Size : new Vector2Int(e.Size.y, e.Size.x);

    /// <summary>Unrotated local tile → offset inside the rotated footprint (clockwise yaw, +Z north).</summary>
    private static Vector2Int LocalToFoot(int lx, int lz, Vector2Int size, int yaw) => (yaw % 360) switch
    {
        90 => new Vector2Int(lz, size.x - 1 - lx),
        180 => new Vector2Int(size.x - 1 - lx, size.y - 1 - lz),
        270 => new Vector2Int(size.y - 1 - lz, lx),
        _ => new Vector2Int(lx, lz),
    };

    private static Vector2 Rot(Vector2 v, int yaw)
    {
        Vector3 r = Quaternion.Euler(0f, yaw, 0f) * new Vector3(v.x, 0f, v.y);
        return new Vector2(r.x, r.z);
    }

    private static Vector2Int Cardinal(Vector2 v) => Mathf.Abs(v.x) > Mathf.Abs(v.y)
        ? new Vector2Int(v.x > 0 ? 1 : -1, 0) : new Vector2Int(0, v.y > 0 ? 1 : -1);

    private List<ChunkDoor> DoorsFor(Entry e, RectInt rect, int yaw)
    {
        var list = new List<ChunkDoor>();
        Vector2 c = new(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);
        foreach (var d in e.Doors)
        {
            Vector2 p = c + Rot(new Vector2(d.LocalPos.x, d.LocalPos.z), yaw);
            list.Add(new ChunkDoor { Pos = p, Dir = Cardinal(Rot(new Vector2(d.LocalForward.x, d.LocalForward.z), yaw)) });
        }
        return list;
    }

    /// <summary>The 4 tiles of a door's opening, <paramref name="depth"/> tiles outside the footprint (0 = first row out).</summary>
    private static IEnumerable<Vector2Int> DoorRow(ChunkDoor d, int depth)
    {
        Vector2 right = new(d.Dir.y, -d.Dir.x);
        for (int k = 0; k < 4; k++)
        {
            Vector2 p = d.Pos + right * (k - 1.5f) + (Vector2)d.Dir * (depth + 0.5f);
            yield return new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
        }
    }

    private static Vector2Int DoorOutside(ChunkDoor d, int depth)
    {
        Vector2 p = d.Pos + (Vector2)d.Dir * (depth + 0.5f);
        return new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
    }

    // ── fitting + stamping ───────────────────────────────────────────────────

    /// <summary>Footprint + 1-tile border + door stubs are free (no other chunk claim; optionally no floor) and inside <paramref name="area"/>.</summary>
    private bool ChunkFits(Entry e, RectInt rect, int yaw, RectInt area, bool requireRock)
    {
        if (rect.xMin - 1 < area.xMin || rect.yMin - 1 < area.yMin || rect.xMax + 1 > area.xMax || rect.yMax + 1 > area.yMax) return false;
        for (int x = rect.xMin - 1; x <= rect.xMax; x++)
            for (int y = rect.yMin - 1; y <= rect.yMax; y++)
            {
                if (!InBounds(x, y) || _chunkClaim[x, y]) return false;
                if (requireRock && _floor[x, y]) return false;
            }
        foreach (var d in DoorsFor(e, rect, yaw))
            for (int depth = 1; depth <= 3; depth++)
                foreach (var t in DoorRow(d, depth))
                    if (!InBounds(t.x, t.y) || _chunkClaim[t.x, t.y] && !IsChunkDoorTile(t)) return false;
        return true;
    }

    private bool IsChunkDoorTile(Vector2Int t)
    {
        int o = IsChunkTile(t.x, t.y) ? _chunkOwner[t.x, t.y] - 1 : -1;
        if (o < 0) return false;
        var c = _chunks[o];
        var lt = FootToLocal(t - c.Rect.position, c.E, c.Yaw);
        return c.E.MaskAt(lt.x, lt.y) == 'D';
    }

    private static Vector2Int FootToLocal(Vector2Int f, Entry e, int yaw)
    {
        for (int lx = 0; lx < e.Size.x; lx++)
            for (int lz = 0; lz < e.Size.y; lz++)
                if (LocalToFoot(lx, lz, e.Size, yaw) == f) return new Vector2Int(lx, lz);
        return new Vector2Int(-1, -1);
    }

    private ChunkPlacement StampChunk(Entry e, Vector2Int at, int yaw, SectionType theme, SectionInfo section,
                                      Transform parent = null, string goName = null)
    {
        Vector2Int size = RotatedSize(e, yaw);
        var p = new ChunkPlacement { E = e, Rect = new RectInt(at, size), Yaw = yaw, Theme = theme, Section = section };
        _chunks.Add(p);
        int id = _chunks.Count;

        for (int lx = 0; lx < e.Size.x; lx++)
            for (int lz = 0; lz < e.Size.y; lz++)
            {
                Vector2Int f = LocalToFoot(lx, lz, e.Size, yaw);
                int x = at.x + f.x, y = at.y + f.y;
                if (!InBounds(x, y)) continue;
                char m = e.MaskAt(lx, lz);
                _chunkOwner[x, y] = id;
                _floor[x, y] = m != '#';
                _reserved[x, y] = true;
                _lane[x, y] = false;
                if (m == 'o') p.Blockers.Add(new RectInt(x, y, 1, 1));
            }
        for (int x = at.x - 1; x <= at.x + size.x; x++)
            for (int y = at.y - 1; y <= at.y + size.y; y++)
                if (InBounds(x, y)) { _noCarve[x, y] = true; _chunkClaim[x, y] = true; }

        p.Doors.AddRange(DoorsFor(e, p.Rect, yaw));
        foreach (var d in p.Doors)
        {
            // Door facing another chunk's door → linked (two chunks form one arched passage).
            Vector2Int first = DoorOutside(d, 0);
            if (IsChunkTile(first.x, first.y) && IsChunkDoorTile(first)) { d.Linked = true; MarkLinked(first); continue; }
            foreach (var t in DoorRow(d, 0))
                if (InBounds(t.x, t.y) && !IsChunkTile(t.x, t.y)) _noCarve[t.x, t.y] = false;   // the border opens at the door
            for (int depth = 1; depth <= 3; depth++)
                foreach (var t in DoorRow(d, depth))
                    if (InBounds(t.x, t.y)) _chunkClaim[t.x, t.y] = true;
        }

        _chunkCounts[e] = (_chunkCounts.TryGetValue(e, out int n) ? n : 0) + 1;

        // Prefab instance (sockets are read from it later).
        if (_chunksRoot == null) _chunksRoot = NewChild("Chunks");
        p.Parent = parent;
        if (e.Prefab != null)
        {
            GameObject go = InstantiatePrefab(e.Prefab);
            go.name = goName ?? e.Name;
            go.transform.SetParent(parent != null ? parent : _chunksRoot, false);
            go.transform.localPosition = new Vector3(p.Centre.x, 0f, p.Centre.y);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            p.Go = go;
        }
        return p;
    }

    private void MarkLinked(Vector2Int otherDoorTile)
    {
        int o = _chunkOwner[otherDoorTile.x, otherDoorTile.y] - 1;
        if (o < 0) return;
        foreach (var d in _chunks[o].Doors)
            foreach (var t in DoorRow(d, -1))   // the D ring tiles of that door
                if (t == otherDoorTile) d.Linked = true;
    }

    private void RemoveChunk(ChunkPlacement p)
    {
        if (p.Removed) return;
        p.Removed = true;
        int id = _chunks.IndexOf(p) + 1;
        for (int x = p.Rect.xMin; x < p.Rect.xMax; x++)
            for (int y = p.Rect.yMin; y < p.Rect.yMax; y++)
                if (InBounds(x, y) && _chunkOwner[x, y] == id) { _chunkOwner[x, y] = 0; _floor[x, y] = false; _reserved[x, y] = false; }
        foreach (var b in p.Blockers) MarkBlocked(b, false);
        if (p.Go != null) DestroyImmediateSafe(p.Go);
        if (_chunkCounts.TryGetValue(p.E, out int n)) _chunkCounts[p.E] = Mathf.Max(0, n - 1);
        if (p == _gateChunk) _gateChunk = null;
        if (p == _denChunk) _denChunk = null;
    }

    private int CountOf(Entry e) => _chunkCounts.TryGetValue(e, out int n) ? n : 0;
    private bool UnderMax(Entry e) => CountOf(e) < Mathf.Max(e.Max, e.Min);

    /// <summary>Theme-height variant: _H36 in Catacombs, _H50 in Gothic / Transition / Hell (README §8.2).</summary>
    private Entry Variant(Entry e, SectionType theme)
    {
        string suffix = theme == SectionType.Catacombs ? "_H36"
                      : theme == SectionType.GothicRuins || theme == SectionType.Transition || theme == SectionType.HellPillars ? "_H50" : null;
        if (suffix == null || e.Name.EndsWith("_H36") || e.Name.EndsWith("_H50")) return e;
        var v = Chunks.Find(e.Name + suffix);
        return v != null && v.Prefab != null ? v : e;
    }

    private Entry PickWeighted(List<Entry> pool)
    {
        float total = 0f;
        foreach (var e in pool) total += Mathf.Max(0f, e.Weight);
        if (total <= 0f) return null;
        double r = _rng.NextDouble() * total;
        foreach (var e in pool) { r -= Mathf.Max(0f, e.Weight); if (r <= 0) return e; }
        return pool[pool.Count - 1];
    }

    // ── 1) before carving: gate, den, themed rooms inside sections ──────────

    private void PlanSectionChunks()
    {
        if (!ChunksOn) return;

        // Extraction gate in a weighted candidate section (Graveyard ×PreferredSectionWeight).
        var gate = Chunks.Find("CH_Extraction_Gate");
        if (gate != null && gate.Prefab != null)
        {
            var order = WeightedSectionOrder(s => s.Type == ExtractionPreferredSection ? PreferredSectionWeight : 1f);
            foreach (var s in order) if ((_gateChunk = TryPlaceInSection(gate, s)) != null) break;
        }

        // Chucho's den (+ the chewed corridor glued to its door) — prefer a section far from the gate.
        var den = Chunks.Find("CH_SP_ChuchoDen");
        if (den != null && den.Prefab != null)
        {
            var order = WeightedSectionOrder(s => _gateChunk == null ? 1f :
                1f + Vector2.Distance(_gateChunk.Centre, s.Center) / 40f);
            foreach (var s in order)
            {
                if (s == _gateChunk?.Section) continue;
                _denChunk = TryPlaceInSection(den, s, attachChewed: true);
                if (_denChunk != null) break;
            }
        }

        // Themed rooms / set pieces.
        foreach (SectionInfo s in _sections)
        {
            int want = _rng.Next(ChunksPerSection.x, ChunksPerSection.y + 1);
            if (s == _gateChunk?.Section || s == _denChunk?.Section) want = Mathf.Max(0, want - 1);
            for (int k = 0; k < want; k++)
            {
                var pool = new List<Entry>();
                foreach (var e in Chunks.Entries)
                {
                    if (e == null || e.Prefab == null || e.Weight <= 0f || !UnderMax(e)) continue;
                    if (e.Role != "room" && e.Role != "setpiece" && e.Name != "CH_Junction_BrokenFloor") continue;
                    if (e.Name == "CH_SP_ChuchoDen" || e.Name == "CH_Room_Storage" || e.Name == "CH_Room_Mausoleum") continue; // den / dead ends handled elsewhere
                    if (e.IsAnyTheme && e.Size.y <= 8 && e.Doors.Length == 2 && IsStraightCorridorChunk(e)) continue;   // corridor-like set pieces go on corridors
                    if (!e.AllowsTheme(s.Type)) continue;
                    if (NearChunk(e.Name, s.Center, SameChunkSpacing)) continue;   // no twin rooms side by side
                    pool.Add(e);
                }
                // Themed chunks first (Any-theme ones only half as likely), never the same chunk twice in one section.
                for (int tries = 0; tries < 4 && pool.Count > 0; tries++)
                {
                    var pick = PickWeighted(pool);
                    if (pick == null) break;
                    if (pick.IsAnyTheme && _rng.NextDouble() < 0.5) { pool.Remove(pick); continue; }
                    if (TryPlaceInSection(pick, s) != null) break;
                    pool.Remove(pick);
                }
            }
        }
    }

    private bool IsStraightCorridorChunk(Entry e)
    {
        if (e.Doors.Length != 2) return false;
        return (e.Doors[0].Side == 'N' && e.Doors[1].Side == 'S') || (e.Doors[0].Side == 'S' && e.Doors[1].Side == 'N');
    }

    private List<SectionInfo> WeightedSectionOrder(Func<SectionInfo, float> weight)
    {
        var pool = new List<SectionInfo>(_sections);
        var result = new List<SectionInfo>();
        while (pool.Count > 0)
        {
            float total = 0f;
            foreach (var s in pool) total += weight(s);
            double r = _rng.NextDouble() * total;
            int i = 0;
            for (; i < pool.Count - 1; i++) { r -= weight(pool[i]); if (r <= 0) break; }
            result.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return result;
    }

    /// <summary>Stamp <paramref name="e"/> inside section <paramref name="s"/> (before carving), doors facing inward,
    /// off the section centre. Optionally glue the chewed corridor to its first door.</summary>
    private ChunkPlacement TryPlaceInSection(Entry e, SectionInfo s, bool attachChewed = false)
    {
        RectInt area = new(s.Bounds.xMin + 1, s.Bounds.yMin + 1, s.Bounds.width - 2, s.Bounds.height - 2);
        var cands = new List<(Vector2Int at, int yaw)>();
        int[] yaws = { 0, 90, 180, 270 };
        Vector2 sc = s.Center;
        foreach (int yaw in yaws)
        {
            Vector2Int size = RotatedSize(e, yaw);
            for (int x = area.xMin + 1; x + size.x + 1 <= area.xMax; x++)
                for (int y = area.yMin + 1; y + size.y + 1 <= area.yMax; y++)
                {
                    var rect = new RectInt(x, y, size.x, size.y);
                    // Keep the centre clear (corridors cross there).
                    var keep = new RectInt(s.Center.x - ChunkCentreKeepOut, s.Center.y - ChunkCentreKeepOut, ChunkCentreKeepOut * 2, ChunkCentreKeepOut * 2);
                    if (rect.Overlaps(keep)) continue;
                    // At least one door faces the section centre, and every door opens inside the section
                    // (its stub lands on section floor, not in the rock between sections).
                    bool inward = false, inside = true;
                    foreach (var d in DoorsFor(e, rect, yaw))
                    {
                        if (Vector2.Dot((Vector2)d.Dir, sc - d.Pos) > 0f) inward = true;
                        Vector2Int o = DoorOutside(d, 4);
                        if (!area.Contains(o)) { inside = false; break; }
                    }
                    if (!inward || !inside) continue;
                    if (!ChunkFits(e, rect, yaw, area, requireRock: false)) continue;
                    if (attachChewed && !ChewedFits(e, rect, yaw, area)) continue;
                    cands.Add((new Vector2Int(x, y), yaw));
                }
        }
        if (cands.Count == 0) return null;
        var c = cands[_rng.Next(cands.Count)];
        var p = StampChunk(e, c.at, c.yaw, s.Type, s);
        if (attachChewed) AttachChewed(p, s);
        return p;
    }

    private bool ChewedFits(Entry den, RectInt rect, int yaw, RectInt area)
    {
        var chewed = Chunks.Find("CH_Corridor_Chewed");
        if (chewed == null || chewed.Prefab == null) return true;
        var d = DoorsFor(den, rect, yaw)[0];
        RectInt cr = ChewedRect(d);
        return ChunkFits(chewed, cr, yaw, area, false);
    }

    private static RectInt ChewedRect(ChunkDoor denDoor)
    {
        Vector2 c = denDoor.Pos + (Vector2)denDoor.Dir * 4f;
        return new RectInt(Mathf.RoundToInt(c.x - 4f), Mathf.RoundToInt(c.y - 4f), 8, 8);
    }

    private void AttachChewed(ChunkPlacement den, SectionInfo s)
    {
        var chewed = Chunks.Find("CH_Corridor_Chewed");
        if (chewed == null || chewed.Prefab == null || den.Doors.Count == 0) return;
        RectInt cr = ChewedRect(den.Doors[0]);
        // Same yaw: the chewed corridor's N door then faces the den's (S) door.
        StampChunk(chewed, cr.position, den.Yaw, s.Type, s);
    }

    // ── 2) between sections: generator alcoves + dead ends ───────────────────

    private bool PlaceGeneratorAlcoves()
    {
        var alcove = Chunks != null ? Chunks.Find("CH_Generator_Alcove") : null;
        if (!ChunksOn || alcove == null || alcove.Prefab == null) return false;
        _generatorRooms.Clear();
        for (int n = 0; n < GeneratorCount; n++)
        {
            var p = TryPlaceInGap(alcove, GeneratorRoomSpacing, SectionType.Transition);
            if (p == null) p = TryPlaceInGap(alcove, GeneratorRoomSpacing / 2, SectionType.Transition);
            if (p == null) { Debug.LogWarning($"[MultiSectionDungeon] Only {n}/{GeneratorCount} generator alcoves fit.", this); break; }
            var d = p.Doors[0];
            _generatorRooms.Add(new GeneratorRoom { Rect = p.Rect, Yaw = Mathf.Atan2(d.Dir.x, d.Dir.y) * Mathf.Rad2Deg, Chunk = p });
        }
        return true;
    }

    private bool PlaceDeadEndChunks()
    {
        if (!ChunksOn) return false;
        _deadEndRooms.Clear();
        int want = _rng.Next(Mathf.Min(DeadEndRoomCount.x, DeadEndRoomCount.y), Mathf.Max(DeadEndRoomCount.x, DeadEndRoomCount.y) + 1);
        var pool = new List<Entry>();
        foreach (var e in Chunks.Entries)
            if (e != null && e.Prefab != null && e.Doors.Length == 1 && (e.Name.StartsWith("CH_Corridor_DeadEnd") && !e.Name.Contains("_H") || e.Name == "CH_Room_Storage" || e.Name == "CH_Room_Mausoleum"))
                pool.Add(e);
        for (int n = 0; n < want && pool.Count > 0; n++)
        {
            // The first one is always a classic dead end (manifest: dead end ≥ 1).
            Entry pick = n == 0 ? pool.Find(e => e.Name == "CH_Corridor_DeadEnd") ?? PickWeighted(pool) : PickWeighted(pool);
            if (pick == null) break;
            if (!UnderMax(pick)) { pool.Remove(pick); n--; continue; }
            var p = TryPlaceInGap(pick, DeadEndRoomSpacing, SectionType.Transition, themed: true);
            if (p == null) { pool.Remove(pick); continue; }
            _deadEndRooms.Add(p.Rect);
        }
        return true;
    }

    /// <summary>Place in empty rock between sections, door turned toward the nearest section centre.
    /// <paramref name="themed"/>: swap to the theme-height variant of the nearest section.</summary>
    private ChunkPlacement TryPlaceInGap(Entry e, int spacing, SectionType theme, bool themed = false)
    {
        var area = new RectInt(SectionMargin, SectionMargin, _width - 2 * SectionMargin, _height - 2 * SectionMargin);
        var cands = new List<(Vector2Int at, int yaw, float score)>();
        foreach (int yaw in new[] { 0, 90, 180, 270 })
        {
            Vector2Int size = RotatedSize(e, yaw);
            for (int x = area.xMin + 2; x + size.x + 2 <= area.xMax; x += 1)
                for (int y = area.yMin + 2; y + size.y + 2 <= area.yMax; y += 1)
                {
                    var rect = new RectInt(x, y, size.x, size.y);
                    if (!GapClear(rect, 2)) continue;
                    if (TooCloseToChunkRooms(rect, spacing)) continue;
                    var d = DoorsFor(e, rect, yaw)[0];
                    Vector2Int outside = DoorOutside(d, 3);
                    Vector2Int target = NearestSectionCenter(outside);
                    float dist = Vector2.Distance(outside, target);
                    float facing = Vector2.Dot((Vector2)d.Dir, ((Vector2)(target - outside)).normalized);
                    if (facing < 0.3f) continue;
                    if (!ChunkFits(e, rect, yaw, area, requireRock: true)) continue;
                    cands.Add((new Vector2Int(x, y), yaw, dist));
                }
        }
        if (cands.Count == 0) return null;
        // Random among the closer half (short connecting corridors read better).
        cands.Sort((a, b) => a.score.CompareTo(b.score));
        int take = Mathf.Max(1, cands.Count / 2);
        var c = cands[_rng.Next(take)];
        Entry use = e;
        if (themed)
        {
            var d0 = DoorsFor(e, new RectInt(c.at, RotatedSize(e, c.yaw)), c.yaw)[0];
            var near = NearestSection(DoorOutside(d0, 3));
            if (near != null) { use = Variant(e, near.Type); theme = SectionType.Transition; }
        }
        return StampChunk(use, c.at, c.yaw, theme, null);
    }

    private bool GapClear(RectInt rect, int gap)
    {
        for (int x = rect.xMin - gap; x < rect.xMax + gap; x++)
            for (int y = rect.yMin - gap; y < rect.yMax + gap; y++)
                if (!InBounds(x, y) || _floor[x, y] || _chunkClaim[x, y]) return false;
        return true;
    }

    private bool TooCloseToChunkRooms(RectInt rect, int spacing)
    {
        Vector2 c = rect.center;
        foreach (var gr in _generatorRooms) if (Vector2.Distance(c, gr.Rect.center) < spacing) return true;
        foreach (var r in _deadEndRooms) if (Vector2.Distance(c, r.center) < spacing * 0.6f) return true;
        return false;
    }

    private SectionInfo NearestSection(Vector2Int from)
    {
        SectionInfo best = null; int bestD = int.MaxValue;
        foreach (SectionInfo s in _sections)
        {
            int d = (s.Center.x - from.x) * (s.Center.x - from.x) + (s.Center.y - from.y) * (s.Center.y - from.y);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    // ── 3) connect every free door to the main network ───────────────────────

    private void ConnectChunkDoors()
    {
        if (!ChunksOn || _chunks.Count == 0) return;
        bool[,] main = MainComponent();
        foreach (var p in _chunks)
        {
            if (p.Removed) continue;
            foreach (var d in p.Doors)
            {
                if (d.Linked) continue;
                // Stub: the 4-wide opening straight out for 3 tiles.
                for (int depth = 0; depth < 3; depth++)
                    foreach (var t in DoorRow(d, depth))
                        if (InBounds(t.x, t.y) && !IsChunkTile(t.x, t.y)) { _floor[t.x, t.y] = true; _reserved[t.x, t.y] = true; }
                Vector2Int from = DoorOutside(d, 2);
                if (TouchesMain(main, d)) continue;
                Vector2Int to = NearestMainTile(main, from, 90);
                if (to.x < 0) continue;
                CarveCorridorAStar(from, to, EncapsulateBounds(from, to, CorridorWidth + 8), reserve: true);
                main = MainComponent();
            }
        }
    }

    private bool TouchesMain(bool[,] main, ChunkDoor d)
    {
        for (int depth = 0; depth <= 3; depth++)
            foreach (var t in DoorRow(d, depth))
                if (InBounds(t.x, t.y) && main[t.x, t.y] && !IsChunkTile(t.x, t.y)) return true;
        return false;
    }

    private bool[,] MainComponent()
    {
        var main = new bool[_width, _height];
        if (_sections.Count == 0) return main;
        Vector2Int s0 = _sections[0].Center;
        if (!IsFloor(s0.x, s0.y)) return main;
        var st = new Stack<Vector2Int>();
        st.Push(s0); main[s0.x, s0.y] = true;
        while (st.Count > 0)
        {
            var p = st.Pop();
            Try(p.x + 1, p.y); Try(p.x - 1, p.y); Try(p.x, p.y + 1); Try(p.x, p.y - 1);
            void Try(int x, int y)
            {
                if (!IsFloor(x, y) || main[x, y]) return;
                main[x, y] = true; st.Push(new Vector2Int(x, y));
            }
        }
        return main;
    }

    private Vector2Int NearestMainTile(bool[,] main, Vector2Int from, int maxR)
    {
        for (int r = 1; r <= maxR; r++)
        {
            Vector2Int best = new(-1, -1); int bestD = int.MaxValue;
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    int x = from.x + dx, y = from.y + dy;
                    if (!InBounds(x, y) || !main[x, y] || IsChunkTile(x, y)) continue;
                    int d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = new Vector2Int(x, y); }
                }
            if (best.x >= 0) return best;
        }
        return new Vector2Int(-1, -1);
    }

    /// <summary>After the prune: a chunk whose every door leads nowhere (or whose tiles were pruned) is removed.</summary>
    private void ValidateChunks()
    {
        if (!ChunksOn) return;
        bool[,] main = MainComponent();
        int removed = 0;
        foreach (var p in _chunks)
        {
            if (p.Removed) continue;
            bool ok = false;
            foreach (var d in p.Doors)
                foreach (var t in DoorRow(d, -1))
                    if (InBounds(t.x, t.y) && main[t.x, t.y]) ok = true;
            if (!ok) { RemoveChunk(p); removed++; }
        }
        if (removed > 0) Debug.LogWarning($"[MultiSectionDungeon] Removed {removed} unreachable chunk(s).", this);
    }

    // ── 4) corridor chunks over straight procedural corridors ────────────────

    private void PlaceCorridorChunks()
    {
        if (!ChunksOn || CorridorChunks <= 0) return;
        var pool = new List<Entry>();
        foreach (var e in Chunks.Entries)
        {
            if (e == null || e.Prefab == null || e.Weight <= 0f || !IsStraightCorridorChunk(e)) continue;
            if (e.Name.Contains("_H36") || e.Name.Contains("_H50")) continue;   // variants are swapped in by theme
            if (!e.IsAnyTheme && !e.AllowsTheme(SectionType.Transition)) continue;
            pool.Add(e);
        }
        if (pool.Count == 0) return;

        // Candidate straight runs: interior 4 tiles all floor, side strips all rock, for the chunk length.
        var cands = new List<(Vector2Int at, bool alongZ, int len)>();
        for (int x = 2; x < _width - 10; x++)
            for (int y = 2; y < _height - 10; y++)
                foreach (bool alongZ in new[] { true, false })
                    foreach (int len in new[] { 8, 16 })
                        if (StraightRun(x, y, alongZ, len)) cands.Add((new Vector2Int(x, y), alongZ, len));
        Shuffle(cands);

        Vector2Int startProbe = _sections.Count > 0 ? _sections[0].Center : default;
        int reachedBefore = CountReached(ReachedTiles(startProbe));
        int placed = 0;
        foreach (var c in cands)
        {
            if (placed >= CorridorChunks) break;
            Vector2 centre = c.alongZ ? new Vector2(c.at.x + 4f, c.at.y + c.len * 0.5f) : new Vector2(c.at.x + c.len * 0.5f, c.at.y + 4f);
            bool tooClose = false;
            foreach (var p in _chunks)
                if (!p.Removed && Vector2.Distance(p.Centre, centre) < (p.E.Role == "corridor" ? CorridorChunkSpacing : 12)) { tooClose = true; break; }
            if (tooClose || !StraightRun(c.at.x, c.at.y, c.alongZ, c.len)) continue;

            var local = new List<Entry>();
            foreach (var e in pool)
            {
                if (e.Size.y != c.len || !UnderMax(e)) continue;
                if (NearChunk(e.Name, centre, 40f)) continue;   // vary neighbouring corridor chunks
                if (!string.IsNullOrEmpty(e.Near) && !NearChunk(e.Near, centre, 48f)) continue;
                local.Add(e);
            }
            var pick = PickWeighted(local);
            if (pick == null) continue;
            SectionInfo near = NearestSection(Vector2Int.RoundToInt(centre));
            var theme = SectionAtTile(Vector2Int.RoundToInt(centre));
            pick = Variant(pick, theme);
            int yaw = c.alongZ ? (_rng.Next(2) == 0 ? 0 : 180) : (_rng.Next(2) == 0 ? 90 : 270);
            var at = c.alongZ ? c.at : c.at;   // the run rect is exactly the footprint
            var p2 = StampChunk(pick, at, yaw, theme, null);
            foreach (var d in p2.Doors) d.Linked = true;   // both ends continue into the corridor already carved
            foreach (var b in p2.Blockers) MarkBlocked(b, true);
            int reached = CountReached(ReachedTiles(startProbe));
            if (reached < reachedBefore - 8 * c.len)
            {
                RemoveChunkRestoreCorridor(p2, c.at, c.alongZ, c.len);
                continue;
            }
            reachedBefore = reached;
            placed++;
        }
    }

    private SectionType SectionAtTile(Vector2Int t)
    {
        foreach (SectionInfo s in _sections) if (s.Bounds.Contains(t)) return s.Type;
        return SectionType.Transition;
    }

    private bool NearChunk(string name, Vector2 at, float radius)
    {
        foreach (var p in _chunks) if (!p.Removed && p.E.Name.StartsWith(name) && Vector2.Distance(p.Centre, at) <= radius) return true;
        return false;
    }

    /// <summary>A footprint (8 × len, along Z or X) whose middle 4-wide strip is floor end to end (and continues past
    /// both ends) and whose 2-tile side strips are rock — a straight corridor through solid rock, no chunk around.</summary>
    private bool StraightRun(int x0, int y0, bool alongZ, int len)
    {
        int w = 8;
        for (int a = -1; a <= len; a++)
            for (int c = 0; c < w; c++)
            {
                int x = alongZ ? x0 + c : x0 + a, y = alongZ ? y0 + a : y0 + c;
                if (!InBounds(x, y) || _chunkClaim[x, y]) return false;
                bool middle = c >= 2 && c <= 5;
                if (a < 0 || a >= len) { if (middle && !_floor[x, y]) return false; continue; }
                if (middle != _floor[x, y]) return false;
            }
        return true;
    }

    private void RemoveChunkRestoreCorridor(ChunkPlacement p, Vector2Int at, bool alongZ, int len)
    {
        RemoveChunk(p);
        for (int a = 0; a < len; a++)
            for (int c = 2; c <= 5; c++)
            {
                int x = alongZ ? at.x + c : at.x + a, y = alongZ ? at.y + a : at.y + c;
                _floor[x, y] = true; _reserved[x, y] = true;
            }
        for (int x = p.Rect.xMin - 1; x <= p.Rect.xMax; x++)
            for (int y = p.Rect.yMin - 1; y <= p.Rect.yMax; y++)
                if (InBounds(x, y) && !IsChunkTile(x, y)) { _noCarve[x, y] = false; _chunkClaim[x, y] = false; }
    }

    // ── 5) after geometry: fit, sockets → systems ────────────────────────────

    private void FinishChunks()
    {
        if (!ChunksOn) return;
        int torches = 0;
        foreach (var p in _chunks)
        {
            if (p.Removed || p.Go == null) continue;
            var ac = p.Go.GetComponent<AuthoredChunk>();
            float h = p.FitHeight >= 0f ? p.FitHeight : WallHeightFor(p.Theme == SectionType.Transition ? SectionType.GothicRuins : p.Theme);
            bool openSky = p.Theme == SectionType.Graveyard && !_gridActive;   // grid maps: every chunk keeps its ceiling, no roof caps
            if (ac != null) ac.FitToTheme(openSky && p.FitHeight < 0f ? GraveyardWallHeight : h, openSky);

            // Solid 'o' tiles → navigation blockers (never removed by the fixer).
            foreach (var b in p.Blockers) { MarkBlocked(b, true); _blockers.Add(new Blocker { Go = null, Tiles = b }); }

            foreach (Transform t in p.Go.transform)
            {
                string n = t.name;
                if (n.StartsWith("SOCKET_Drip")) _dripPoints.Add(new Vector3(t.position.x, _root.position.y + 0.05f, t.position.z));
                else if (n.StartsWith("SOCKET_Hide")) _hidePoints.Add(t.position);
                else if (n.StartsWith("SOCKET_Den")) { _denPoint = t.position; _hasDen = true; }
                else if (n.StartsWith("VOL_Water")) _waterPoints.Add(t.position);
                else if (n.StartsWith("VOL_Trap") && t.GetComponent<TrapKillVolume>() == null)
                {
                    t.gameObject.AddComponent<TrapKillVolume>();
                    if (t.TryGetComponent(out Collider col)) col.isTrigger = true;
                }
            }
            // Water / trap volumes can be nested; scan deeper for the trap trigger too.
            foreach (var col in p.Go.GetComponentsInChildren<Collider>(true))
                if (col.name.StartsWith("VOL_Trap") && col.GetComponent<TrapKillVolume>() == null)
                {
                    col.isTrigger = true;
                    col.gameObject.AddComponent<TrapKillVolume>();
                }

            // Extraction gate lifts when every generator runs.
            if (p == _gateChunk)
            {
                Transform gate = FindChildDeep(p.Go.transform, "_Gate", endsWith: true);
                if (gate != null && p.Go.GetComponent<ExtractionGateLift>() == null)
                    p.Go.AddComponent<ExtractionGateLift>().Setup(gate);
            }

            torches += LightChunkTorches(p);
            MarkStaticDeep(p.Go);
        }
        Debug.Log($"[MultiSectionDungeon] Chunks: {LiveChunkCount()} placed ({ChunkSummary()}), {torches} chunk torches.", this);
    }

    private int LightChunkTorches(ChunkPlacement p)
    {
        if (TorchPrefab == null || p.E.TorchesSuggested <= 0) return 0;
        // Grid maps are mostly corridors: light only some of them (darkness is a feature, and it keeps the light budget).
        if (_gridActive && p.E.Role == "corridor" && !Roll(GridCorridorTorchChance)) return 0;
        var sockets = new List<Transform>();
        foreach (Transform t in p.Go.transform) if (t.name.StartsWith("SOCKET_Torch")) sockets.Add(t);
        sockets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        int n = 0;
        foreach (var s in sockets)
        {
            if (n >= p.E.TorchesSuggested) break;
            GameObject t = InstantiatePrefab(TorchPrefab);
            t.transform.SetParent(_detailsRoot, false);
            Torch.ApplySpawnScale(t);
            t.transform.position = s.position;
            Vector3 fwd = s.forward; fwd.y = 0f;
            t.transform.rotation = Quaternion.LookRotation(fwd.sqrMagnitude > 0.01f ? fwd : Vector3.forward) * TorchPrefab.transform.localRotation;
            if (s.localPosition.y >= 1.6f)
                WallMount.PushOffWall(t.transform, s.position, fwd, TorchWallGap);
            else
                WallMount.PlaceInFrontOf(t.transform, s.position, Vector3.up, 0f);   // free-standing brazier
            n++;
        }
        return n;
    }

    private static Transform FindChildDeep(Transform root, string suffix, bool endsWith)
    {
        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
            if (endsWith ? c.name.EndsWith(suffix) : c.name == suffix) return c;
        return null;
    }

    private int LiveChunkCount()
    {
        int n = 0;
        foreach (var p in _chunks) if (!p.Removed) n++;
        return n;
    }

    private string ChunkSummary()
    {
        var counts = new SortedDictionary<string, int>();
        foreach (var p in _chunks)
            if (!p.Removed) counts[p.E.Name.Replace("CH_", "")] = (counts.TryGetValue(p.E.Name.Replace("CH_", ""), out int n) ? n : 0) + 1;
        var parts = new List<string>();
        foreach (var kv in counts) parts.Add(kv.Value > 1 ? $"{kv.Key}×{kv.Value}" : kv.Key);
        return string.Join(", ", parts);
    }

    /// <summary>All chunk sockets of a kind in placed chunks, world space, with their chunk.</summary>
    private List<(Transform socket, ChunkPlacement chunk)> ChunkSockets(string kind)
    {
        var list = new List<(Transform, ChunkPlacement)>();
        foreach (var p in _chunks)
        {
            if (p.Removed || p.Go == null) continue;
            foreach (Transform t in p.Go.transform)
                if (t.name.StartsWith("SOCKET_" + kind)) list.Add((t, p));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Item2.E.Name + a.Item1.name + a.Item2.Rect, b.Item2.E.Name + b.Item1.name + b.Item2.Rect));
        return list;
    }

    private Vector2Int WorldToTile(Vector3 world)
    {
        Vector3 l = _root.InverseTransformPoint(world);
        return new Vector2Int(Mathf.FloorToInt(l.x), Mathf.FloorToInt(l.z));
    }

    /// <summary>Section landmarks from chunk Landmark sockets (Pillar Hall, Ritual, Bell Tower, Den…).</summary>
    private bool TryChunkLandmark(SectionInfo s, DungeonLayout.Section info, ThemeDressing d)
    {
        if (!ChunksOn) return false;
        foreach (var (socket, chunk) in ChunkSockets("Landmark"))
        {
            if (chunk.Section != s || string.IsNullOrEmpty(chunk.E.DisplayName)) continue;
            info.HasLandmark = true;
            info.LandmarkName = chunk.E.DisplayName;
            info.LandmarkPosition = socket.position;
            if (LandmarkLightIntensity > 0f)
            {
                var lg = new GameObject("LandmarkLight");
                lg.transform.SetParent(chunk.Go.transform, false);
                float y = Mathf.Min(socket.localPosition.y + 2.4f, chunk.E.WallHeight - 0.5f);
                lg.transform.localPosition = new Vector3(socket.localPosition.x, y, socket.localPosition.z);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = d != null ? d.LandmarkLight : new Color(1f, 0.7f, 0.4f);
                l.intensity = LandmarkLightIntensity * 0.8f;
                l.range = LandmarkLightRange;
                l.shadows = LightShadows.None;
                lg.AddComponent<ShadowlessLight>();
                _spawnedLights++;
            }
            return true;
        }
        return false;
    }

    // ── mission objects in chunk sockets ─────────────────────────────────────

    /// <summary>A player spawn socket (not the den's creature spawn, not the exit) in the start walking-distance band.</summary>
    private Transform PickChunkSpawn(int[,] fromExt, System.Random rng)
    {
        if (!ChunksOn) return null;
        int maxD = 0;
        for (int x = 0; x < _width; x++) for (int y = 0; y < _height; y++) maxD = Mathf.Max(maxD, fromExt[x, y]);
        int lo = Mathf.RoundToInt(maxD * StartDistanceBand.x), hi = Mathf.RoundToInt(maxD * StartDistanceBand.y);
        var ok = new List<Transform>();
        foreach (var (s, c) in ChunkSockets("Spawn"))
        {
            if (c == _denChunk || c == _gateChunk) continue;
            Vector2Int t = WorldToTile(s.position);
            if (!InBounds(t.x, t.y)) continue;
            int d = fromExt[t.x, t.y];
            if (d >= lo && d <= hi) ok.Add(s);
        }
        return ok.Count > 0 ? ok[rng.Next(ok.Count)] : null;
    }

    /// <summary>Evidence in chunk sockets (shelves, sarcophagi, pit bottoms…): one per chunk, spread out, far enough from the start.</summary>
    private List<Transform> PickChunkEvidence(int[,] fromStart, int count, System.Random rng)
    {
        var result = new List<Transform>();
        if (!ChunksOn || count <= 0) return result;
        var cands = new List<(Transform s, ChunkPlacement c)>();
        foreach (var (s, c) in ChunkSockets("Evidence"))
        {
            if (c == _gateChunk || c.E.Name == "CH_Generator_Alcove") continue;
            Vector2Int t = WorldToTile(s.position);
            if (!InBounds(t.x, t.y) || fromStart[t.x, t.y] < EvidenceMinDistance * 0.66f) continue;
            cands.Add((s, c));
        }
        for (int i = cands.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (cands[i], cands[j]) = (cands[j], cands[i]); }
        float minSq = (EvidenceSpacing * 0.6f) * (EvidenceSpacing * 0.6f);
        var usedChunks = new HashSet<ChunkPlacement>();
        foreach (var (s, c) in cands)
        {
            if (result.Count >= count) break;
            if (usedChunks.Contains(c)) continue;
            bool near = false;
            foreach (var r in result) if ((r.position - s.position).sqrMagnitude < minSq) { near = true; break; }
            if (near) continue;
            result.Add(s); usedChunks.Add(c);
        }
        return result;
    }

    /// <summary>True for the gate / bell / sarcophagus lids / water and anything parented under them (e.g. the
    /// Meshy bell and effigy hero props nested at HERO_* sockets): those move or animate, so never static-batch them.</summary>
    private static bool IsMovingChunkPart(Transform t, Transform chunkRoot)
    {
        for (; t != null && t != chunkRoot; t = t.parent)
        {
            string n = t.name;
            if (n.EndsWith("_Gate") || n.EndsWith("_Bell") || n.Contains("_Lid_") || n.EndsWith("_Water")) return true;
        }
        return false;
    }

    /// <summary>Runtime generation only: static-batch the chunk geometry (editor generation relies on the static flags).</summary>
    private void CombineChunksAtRuntime()
    {
        if (!Application.isPlaying) return;
        var gos = new List<GameObject>();
        foreach (var cp in _chunks)
        foreach (var r in cp.Removed || cp.Go == null ? new MeshRenderer[0] : cp.Go.GetComponentsInChildren<MeshRenderer>())
        {
            if (IsMovingChunkPart(r.transform, cp.Go.transform)) continue;
            gos.Add(r.gameObject);
        }
        if (gos.Count > 0) StaticBatchingUtility.Combine(gos.ToArray(), _root.gameObject);
    }
}
