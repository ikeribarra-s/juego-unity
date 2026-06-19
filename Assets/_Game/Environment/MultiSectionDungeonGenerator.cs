using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Procedural closed multi-section level. One global bool[,] floor grid is the
/// source of truth; a grid of themed sections (Gothic Ruins, Graveyard, Hell
/// Pillars, Catacombs, Cavern) each carve into it, then adjacency corridors stitch
/// the whole grid into a single closed, connected lattice. Geometry and dressing are
/// generated from primitives only (no external models, no Resources.Load).
/// Deterministic from Seed. Generate / Clear via the component context menu.
/// </summary>
public class MultiSectionDungeonGenerator : MonoBehaviour
{
    public enum SectionType { GothicRuins, Graveyard, HellPillars, Catacombs, Cavern, Transition }

    // The themes that can be placed in a grid cell (Transition is reserved for corridors).
    private static readonly SectionType[] PlaceableThemes =
    {
        SectionType.GothicRuins, SectionType.Graveyard, SectionType.HellPillars,
        SectionType.Catacombs, SectionType.Cavern,
    };

    private class SectionInfo
    {
        public SectionType Type;
        public RectInt Bounds;
        public Vector2Int Entry;
        public Vector2Int Exit;
        public int Col, Row;   // grid cell coordinates (for adjacency connection)
        public Vector2Int Center => new(Bounds.x + Bounds.width / 2, Bounds.y + Bounds.height / 2);
    }

    private struct WallEdge
    {
        public Vector3 Surface;   // local point on the inner wall face at floor level
        public Vector3 IntoRoom;  // unit normal pointing into the room
        public int Cx, Cy, Side;  // owning floor tile + side (0=+z,1=-z,2=+x,3=-x)
        public SectionType Section;
    }

    [Header("Layout")]
    [Tooltip("0 = a fresh random layout every Generate. Set a non-zero value to lock/reproduce one specific layout.")]
    [SerializeField] private int Seed = 0;
    [Tooltip("Lower bound for total map width; the grid usually drives the real size larger.")]
    [SerializeField] private int MapWidth = 140;
    [Tooltip("Lower bound for total map height; the grid usually drives the real size larger.")]
    [SerializeField] private int MapHeight = 70;
    [SerializeField] private int SectionMargin = 4;
    [SerializeField] private int SectionPadding = 8;
    [SerializeField] private int CorridorWidth = 4;
    [Tooltip("Gothic A* cost penalty for changing direction — higher = straighter corridors (0 = free to wind).")]
    [SerializeField] private int CorridorTurnPenalty = 4;
    [Tooltip("Gothic A* step cost for reusing an already-carved floor tile vs 10 for fresh rock — lower = corridors merge more aggressively.")]
    [SerializeField] private int CorridorReuseCost = 2;

    [Header("Grid (size & section count)")]
    [Tooltip("Number of section columns. With the default CellSize this drives total map width.")]
    [SerializeField] private int GridColumns = 10;
    [Tooltip("Number of section rows. With the default CellSize this drives total map height.")]
    [SerializeField] private int GridRows = 6;
    [Tooltip("Nominal footprint (tiles) of each grid cell's section before jitter. 10x6 cells of ~50 ≈ 16x the old map area.")]
    [SerializeField] private Vector2Int CellSize = new Vector2Int(50, 46);

    [Header("Layout Randomization")]
    [Tooltip("Vary section sizes, positions within their cell, and theme assignment each Generate so no two seeds share a footprint.")]
    [SerializeField] private bool RandomizeLayout = true;
    [Tooltip("How much each section's size can swing per Generate, as a fraction (0.25 = ±25%).")]
    [SerializeField, Range(0f, 0.6f)] private float SizeJitter = 0.2f;
    [Tooltip("Smallest section dimension allowed after jitter, so a section never collapses below a workable size.")]
    [SerializeField] private int MinSectionDimension = 18;

    [Header("Gothic (BSP)")]
    [SerializeField] private int GothicMinRoom = 8;
    [SerializeField] private int GothicMaxRoom = 18;
    [SerializeField] private int GothicMaxDepth = 4;

    [Header("Geometry")]
    [SerializeField] private float WallThickness = 0.5f;
    [SerializeField] private float GothicWallHeight = 5f;
    [SerializeField] private float GraveyardWallHeight = 1.4f;
    [SerializeField] private float HellWallHeight = 6.5f;
    [SerializeField] private float CatacombWallHeight = 3.6f;
    [SerializeField] private float CavernWallHeight = 7.5f;
    [SerializeField] private bool GothicCeiling = true;
    [SerializeField] private bool HellCeiling = true;
    [SerializeField] private bool CatacombCeiling = true;
    [SerializeField] private bool CavernCeiling = true;
    [SerializeField] private bool TransitionCeiling = true;
    [SerializeField] private string WallLayerName = "Walls";

    [Header("Decoration Density")]
    [SerializeField, Range(0f, 1f)] private float TorchDensity = 0.22f;
    [SerializeField, Range(0f, 1f)] private float GothicDetailDensity = 0.16f;
    [SerializeField, Range(0f, 1f)] private float GraveDensity = 0.18f;
    [SerializeField, Range(0f, 1f)] private float HellPillarDensity = 0.5f;
    [SerializeField] private int PillarSpacing = 4;
    [SerializeField, Range(0f, 1f)] private float LavaCrackDensity = 0.06f;
    [SerializeField, Range(0f, 1f)] private float CatacombBoneDensity = 0.16f;
    [SerializeField, Range(0f, 1f)] private float CavernRockDensity = 0.14f;

    [Header("Lighting Budget")]
    [Tooltip("Cap on real-time lights for the whole map. Raise for the larger grid (the map is ~16x the old area).")]
    [SerializeField] private int MaxRealtimeLights = 220;
    [SerializeField, Range(0f, 1f)] private float GraveyardLightDensity = 0.05f;
    [SerializeField, Range(0f, 1f)] private float HellLightDensity = 0.08f;
    [SerializeField] private bool AllowHeroShadows = true;
    [SerializeField] private int HeroShadowLightCount = 4;

    [Header("Light Colors")]
    [SerializeField] private Color[] GothicTorchColors =
    {
        new Color(1f, 0.55f, 0.22f),
        new Color(1f, 0.42f, 0.15f),
        new Color(1f, 0.68f, 0.30f)
    };
    [SerializeField] private Color[] GraveyardColors =
    {
        new Color(0.42f, 0.62f, 1f),
        new Color(0.45f, 1f, 0.65f)
    };
    [SerializeField] private Color GraveyardLanternColor = new Color(1f, 0.75f, 0.4f);
    [SerializeField] private Color[] HellColors =
    {
        new Color(1f, 0.25f, 0.07f),
        new Color(1f, 0.42f, 0.1f),
        new Color(1f, 0.12f, 0.05f)
    };

    [Header("Materials (optional — runtime fallbacks created if empty)")]
    [SerializeField] private Material GothicFloorMaterial;
    [SerializeField] private Material GothicWallMaterial;
    [SerializeField] private Material GothicCeilingMaterial;
    [SerializeField] private Material GraveyardGroundMaterial;
    [SerializeField] private Material GraveyardStoneMaterial;
    [SerializeField] private Material HellFloorMaterial;
    [SerializeField] private Material HellPillarMaterial;
    [SerializeField] private Material EmissiveLavaMaterial;
    [SerializeField] private Material CatacombFloorMaterial;
    [SerializeField] private Material CatacombWallMaterial;
    [SerializeField] private Material CavernFloorMaterial;
    [SerializeField] private Material CavernRockMaterial;
    [SerializeField] private Material EmissiveCrystalMaterial;

    [Header("Cavern crystal light colors")]
    [SerializeField] private Color[] CrystalColors =
    {
        new Color(0.35f, 0.8f, 1f),
        new Color(0.5f, 0.45f, 1f),
        new Color(0.3f, 1f, 0.85f)
    };

    [Header("Atmosphere")]
    [SerializeField] private bool ApplySceneFog = true;
    [SerializeField] private Color FogColor = new Color(0.04f, 0.04f, 0.06f);
    [Tooltip("ExponentialSquared fog. On a large map keep this low (~0.01) or you won't see far.")]
    [SerializeField] private float FogDensity = 0.012f;

    [Header("Ambient & Night Light")]
    [Tooltip("Drive scene ambient at Generate so the dungeon is actually visible.")]
    [SerializeField] private bool OverrideAmbient = true;
    [SerializeField] private Color AmbientColor = new Color(0.30f, 0.33f, 0.42f);
    [SerializeField, Range(0f, 4f)] private float AmbientIntensity = 1.4f;
    [Tooltip("A dim directional 'moon' so exterior (open-sky) areas read as night rather than black.")]
    [SerializeField] private bool AddMoonlight = true;
    [SerializeField] private Color MoonlightColor = new Color(0.5f, 0.6f, 0.9f);
    [SerializeField, Range(0f, 3f)] private float MoonlightIntensity = 0.8f;
    [SerializeField] private Vector3 MoonlightEuler = new Vector3(55f, 35f, 0f);
    [Tooltip("Extra soft cool fill light dropped into each exterior (Graveyard) section. 0 = off.")]
    [SerializeField, Range(0f, 3f)] private float ExteriorFillIntensity = 0.8f;

    [Header("Baking & Culling (editor only)")]
    [Tooltip("Flag generated geometry as static so occlusion culling / GI baking can use it. (Frustum culling is always on, no setup needed.)")]
    [SerializeField] private bool MarkStaticGeometry = true;
    [Tooltip("Run occlusion-culling bake at the end of Generate. Slow on a big map.")]
    [SerializeField] private bool ComputeOcclusionOnGenerate = false;
    [Tooltip("Run a lightmap bake at the end of Generate. Very slow on a big map; flickering torches stay realtime regardless.")]
    [SerializeField] private bool BakeLightingOnGenerate = false;

    [Header("Debug")]
    [SerializeField] private bool DebugDrawSectionBounds = true;

    private const string RootName = "_multiSectionRoot";

    private System.Random _rng;
    private int _width, _height;
    private bool[,] _floor;
    private bool[,] _reserved;       // protected traversal tiles (kept clear of obstacles)
    private SectionType[,] _section; // per floor tile (Transition where outside a section)
    private bool[,] _hasSection;
    private readonly List<Mesh> _builtMeshes = new(); // procedurally built floor/ceiling meshes (for lightmap UVs)

    private readonly List<SectionInfo> _sections = new();

    private Transform _root, _floorsRoot, _wallsRoot, _ceilingsRoot, _detailsRoot, _decorRoot, _lightsRoot;
    private int _spawnedLights;
    private int _spawnedHeroShadows;

    // runtime fallback materials
    private Material _mGothicFloor, _mGothicWall, _mGothicCeiling;
    private Material _mGraveGround, _mGraveStone;
    private Material _mHellFloor, _mHellPillar, _mLava;
    private Material _mCatacombFloor, _mCatacombWall, _mBone;
    private Material _mCavernFloor, _mCavernRock, _mCrystal;
    private Material _mWood, _mMetal, _mFlame;

    // ---------------------------------------------------------------------

    [ContextMenu("Generate")]
    public void Generate()
    {
        Clear();

        int seed = Seed != 0 ? Seed : System.Environment.TickCount;
        _rng = new System.Random(seed);
        _spawnedLights = 0;
        _spawnedHeroShadows = 0;
        _sections.Clear();

        LayoutSections();

        _floor = new bool[_width, _height];
        _reserved = new bool[_width, _height];
        _section = new SectionType[_width, _height];
        _hasSection = new bool[_width, _height];

        CreateRoots();
        EnsureMaterials();

        // Carve each section's floor into the global grid. Catacombs reuse the BSP room carver
        // (tight cellular layout); Cavern reuses the eroded open-area carver (organic cave).
        foreach (SectionInfo s in _sections)
        {
            switch (s.Type)
            {
                case SectionType.GothicRuins: CarveGothic(s); break;
                case SectionType.Catacombs:   CarveGothic(s); break;
                case SectionType.Graveyard:   CarveOpenArea(s); break;
                case SectionType.HellPillars: CarveOpenArea(s); break;
                case SectionType.Cavern:      CarveOpenArea(s); break;
            }
        }

        ConnectSections();
        PruneFloorIslands();
        AssignSectionOwnership();

        // Geometry from the grid.
        BuildFloors();
        BuildCeilings();
        BuildWalls();
        BuildCeilingSkirts();   // close the vertical gaps where ceiling heights change

        // Themed dressing.
        DecorateGothic();
        DecorateGraveyard();
        DecorateHell();
        DecorateCatacombs();
        DecorateCavern();

        // Atmosphere & lighting. If a DungeonLightingController owns the scene lighting, defer to
        // it (it applies ambient/fog/moon live) so the two don't fight over RenderSettings.
        bool externalLighting = FindAnyObjectByType<DungeonLightingController>() != null;
        if (!externalLighting)
        {
            if (ApplySceneFog) ApplyFog();
            ApplyAmbientAndSky();
        }

#if UNITY_EDITOR
        if (!Application.isPlaying && ComputeOcclusionOnGenerate) ComputeOcclusion();
        if (!Application.isPlaying && BakeLightingOnGenerate) BakeLighting();
#endif
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        Transform existing = transform.Find(RootName);
        if (existing != null)
            DestroyImmediateSafe(existing.gameObject);

        _root = _floorsRoot = _wallsRoot = _ceilingsRoot = null;
        _detailsRoot = _decorRoot = _lightsRoot = null;
        _mGothicFloor = _mGothicWall = _mGothicCeiling = null;
        _mGraveGround = _mGraveStone = null;
        _mHellFloor = _mHellPillar = _mLava = null;
        _mCatacombFloor = _mCatacombWall = _mBone = null;
        _mCavernFloor = _mCavernRock = _mCrystal = null;
        _mWood = _mMetal = _mFlame = null;
        _sections.Clear();
        _builtMeshes.Clear();
    }

    private void CreateRoots()
    {
        var go = new GameObject(RootName);
        go.transform.SetParent(transform, false);
        _root = go.transform;

        _floorsRoot = NewChild("Floors");
        _wallsRoot = NewChild("Walls");
        _ceilingsRoot = NewChild("Ceilings");
        _detailsRoot = NewChild("Details");
        _decorRoot = NewChild("Decor");
        _lightsRoot = NewChild("Lights");
    }

    private Transform NewChild(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root, false);
        return go.transform;
    }

    // ---------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------

    private void LayoutSections()
    {
        int cols = Mathf.Max(1, GridColumns);
        int rows = Mathf.Max(1, GridRows);
        int cellW = Mathf.Max(MinSectionDimension + 2, CellSize.x);
        int cellH = Mathf.Max(MinSectionDimension + 2, CellSize.y);

        // Fixed slot pitch so cells tile cleanly; jitter happens inside each slot.
        int pitchX = cellW + SectionPadding;
        int pitchY = cellH + SectionPadding;

        _width  = Mathf.Max(MapWidth,  SectionMargin * 2 + cols * pitchX - SectionPadding);
        _height = Mathf.Max(MapHeight, SectionMargin * 2 + rows * pitchY - SectionPadding);

        // Even, varied theme distribution: fill a bag with whole repeats of the placeable themes,
        // top up the remainder, then shuffle so each Generate scrambles which theme lands where.
        var bag = BuildThemeBag(cols * rows);

        int i = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int slotX = SectionMargin + col * pitchX;
                int slotY = SectionMargin + row * pitchY;
                _sections.Add(MakeSection(bag[i++], col, row, slotX, slotY, cellW, cellH));
            }
        }
    }

    // A shuffled list of length 'count' drawing evenly from PlaceableThemes.
    private List<SectionType> BuildThemeBag(int count)
    {
        var bag = new List<SectionType>(count);
        while (bag.Count < count)
            foreach (SectionType t in PlaceableThemes)
                if (bag.Count < count) bag.Add(t);
        if (RandomizeLayout) Shuffle(bag);
        return bag;
    }

    private SectionInfo MakeSection(SectionType type, int col, int row, int slotX, int slotY, int cellW, int cellH)
    {
        Vector2Int size = JitterSize(new Vector2Int(cellW, cellH));
        size.x = Mathf.Min(size.x, cellW);   // keep inside the slot so neighbours never touch
        size.y = Mathf.Min(size.y, cellH);

        int xSlack = cellW - size.x;
        int ySlack = cellH - size.y;
        int xOff = (RandomizeLayout && xSlack > 0) ? _rng.Next(0, xSlack + 1) : xSlack / 2;
        int yOff = (RandomizeLayout && ySlack > 0) ? _rng.Next(0, ySlack + 1) : ySlack / 2;

        int x = slotX + xOff;
        int y = slotY + yOff;
        var bounds = new RectInt(x, y, size.x, size.y);
        int cy = y + size.y / 2;
        return new SectionInfo
        {
            Type = type,
            Bounds = bounds,
            Col = col,
            Row = row,
            Entry = new Vector2Int(x, cy),
            Exit = new Vector2Int(x + size.x - 1, cy)
        };
    }

    // Swing a section's size by ±SizeJitter, clamped so it never collapses below a workable size.
    private Vector2Int JitterSize(Vector2Int baseSize)
    {
        if (!RandomizeLayout) return baseSize;
        float fx = 1f + (float)(_rng.NextDouble() * 2 - 1) * SizeJitter;
        float fy = 1f + (float)(_rng.NextDouble() * 2 - 1) * SizeJitter;
        return new Vector2Int(
            Mathf.Max(MinSectionDimension, Mathf.RoundToInt(baseSize.x * fx)),
            Mathf.Max(MinSectionDimension, Mathf.RoundToInt(baseSize.y * fy)));
    }

    // ---------------------------------------------------------------------
    // Carving
    // ---------------------------------------------------------------------

    private void CarveOpenArea(SectionInfo s)
    {
        RectInt b = s.Bounds;
        int w = b.width, h = b.height;

        // Organic footprint instead of a flat rectangle: erode the boundary with
        // deterministic noise (heavier toward the edge), then smooth with a couple
        // of cellular-automata passes. The interior core is far enough from every
        // edge that it is never eroded, so the section stays solid and the spine
        // (carved later, center-to-center) keeps the layout connected.
        var keep = new bool[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                keep[x, y] = true;

        int margin = Mathf.Clamp(Mathf.Min(w, h) / 4, 4, 10);
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int d = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                if (d >= margin) continue;
                float edgeT = 1f - (float)d / margin;        // 1 at edge -> 0 at margin
                if (_rng.NextDouble() < edgeT * edgeT * 0.85f)
                    keep[x, y] = false;
            }
        }

        for (int pass = 0; pass < 3; pass++)
        {
            var next = (bool[,])keep.Clone();
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int n = CountKeepNeighbors(keep, w, h, x, y);
                    if (keep[x, y]) next[x, y] = n >= 4;       // trim thin spikes
                    else next[x, y] = n >= 6;                  // fill small bays
                }
            }
            keep = next;
        }

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (keep[x, y]) SetFloor(b.xMin + x, b.yMin + y);
    }

    private int CountKeepNeighbors(bool[,] keep, int w, int h, int cx, int cy)
    {
        int n = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue; // off-section = empty
                if (keep[nx, ny]) n++;
            }
        }
        return n;
    }

    private void CarveGothic(SectionInfo s)
    {
        var rooms = new List<RectInt>();
        var rootNode = new BSPNode(s.Bounds);
        SplitBSP(rootNode, 0);

        var leaves = new List<BSPNode>();
        CollectLeaves(rootNode, leaves);

        foreach (BSPNode leaf in leaves)
        {
            int availW = leaf.Bounds.width - 2;
            int availH = leaf.Bounds.height - 2;
            if (availW < 4 || availH < 4) continue; // genuinely too small to host a room

            // Clamp the room to fit the leaf instead of skipping under-sized leaves.
            // This fills the Gothic footprint consistently (no large dead voids) so
            // the ruins read as densely as the open sections.
            int maxW = Mathf.Min(GothicMaxRoom, availW);
            int maxH = Mathf.Min(GothicMaxRoom, availH);
            int minW = Mathf.Min(GothicMinRoom, maxW);
            int minH = Mathf.Min(GothicMinRoom, maxH);

            int w = _rng.Next(minW, maxW + 1);
            int h = _rng.Next(minH, maxH + 1);
            int x = leaf.Bounds.xMin + 1 + _rng.Next(0, leaf.Bounds.width - w - 1);
            int y = leaf.Bounds.yMin + 1 + _rng.Next(0, leaf.Bounds.height - h - 1);

            var room = new RectInt(x, y, w, h);
            rooms.Add(room);
            for (int rx = room.xMin; rx < room.xMax; rx++)
                for (int ry = room.yMin; ry < room.yMax; ry++)
                    SetFloor(rx, ry);
        }

        // Chain the rooms so the whole section is connected (maze-like spine). A* (constrained
        // to the section bounds) is cheaper to reuse already-carved floor and to go straight, so
        // the ruins' corridors merge into existing rooms and read as an organic maze instead of
        // a fan of right-angle Ls. Stays inside the Gothic footprint so it never bleeds into a
        // neighbouring section or the padding between them.
        for (int i = 0; i < rooms.Count - 1; i++)
            CarveCorridorAStar(RectCenter(rooms[i]), RectCenter(rooms[i + 1]), s.Bounds);

        // Guarantee the section entry/exit reach the interior.
        Vector2Int c = s.Center;
        if (rooms.Count > 0) CarveCorridorAStar(c, RectCenter(rooms[0]), s.Bounds);
        CarveCorridorAStar(c, s.Entry, s.Bounds);
        CarveCorridorAStar(c, s.Exit, s.Bounds);
    }

    // Deterministic A* between two points, restricted to a bounding rect (no RNG, so output stays
    // seed-reproducible). Cost model: fresh rock = CorridorStepFresh, already-floor = CorridorReuseCost
    // (encourages merging), plus CorridorTurnPenalty on every direction change (keeps it from
    // zig-zagging). Carves a CorridorWidth band along the result; falls back to an L-shape if the
    // search fails (shouldn't, since both endpoints lie inside the rect).
    private const int CorridorStepFresh = 10;

    private void CarveCorridorAStar(Vector2Int a, Vector2Int b, RectInt bounds, bool reserve = false)
    {
        List<Vector2Int> path = FindCorridorPath(a, b, bounds);
        if (path != null)
        {
            foreach (Vector2Int t in path) CarveCorridorTile(t.x, t.y, reserve);
        }
        else
        {
            CarveCorridorL(a, b, reserve);
        }
    }

    private List<Vector2Int> FindCorridorPath(Vector2Int a, Vector2Int b, RectInt bounds)
    {
        int ox = bounds.xMin, oy = bounds.yMin, w = bounds.width, h = bounds.height, n = w * h;
        int Index(int x, int y) => (x - ox) * h + (y - oy);

        var g      = new int[n];
        var came   = new int[n];
        var dir    = new int[n];   // direction code (0..3) used to reach the tile; -1 = none
        var closed = new bool[n];
        for (int i = 0; i < n; i++) { g[i] = int.MaxValue; came[i] = -1; dir[i] = -1; }

        int hUnit = Mathf.Min(CorridorReuseCost, CorridorStepFresh);
        int Heur(int x, int y) => (Mathf.Abs(x - b.x) + Mathf.Abs(y - b.y)) * hUnit;

        int start = Index(a.x, a.y), goal = Index(b.x, b.y);
        g[start] = 0;

        var open = new MinHeap(n / 4 + 16);
        open.Push(start, Heur(a.x, a.y));

        // E, W, N, S — fixed order keeps the search deterministic.
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        bool found = false;
        while (open.Count > 0)
        {
            int cur = open.Pop();
            if (closed[cur]) continue;            // stale entry (lazy deletion)
            if (cur == goal) { found = true; break; }
            closed[cur] = true;

            int cx = ox + cur / h, cy = oy + cur % h;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d], ny = cy + dy[d];
                if (nx < bounds.xMin || nx >= bounds.xMax || ny < bounds.yMin || ny >= bounds.yMax) continue;
                int ni = Index(nx, ny);
                if (closed[ni]) continue;

                int step = IsFloor(nx, ny) ? CorridorReuseCost : CorridorStepFresh;
                if (dir[cur] != -1 && dir[cur] != d) step += CorridorTurnPenalty;

                int tentative = g[cur] + step;
                if (tentative < g[ni])
                {
                    g[ni]    = tentative;
                    came[ni] = cur;
                    dir[ni]  = d;
                    open.Push(ni, tentative + Heur(nx, ny));
                }
            }
        }

        if (!found) return null;

        var path = new List<Vector2Int>();
        for (int c = goal; c != -1; c = came[c])
            path.Add(new Vector2Int(ox + c / h, oy + c % h));
        return path;
    }

    // Carve a CorridorWidth × CorridorWidth block centred on a path tile, so corridors keep their
    // width through turns. Already-floor tiles are harmlessly re-set; SetFloor clamps to the grid.
    private void CarveCorridorTile(int x, int y, bool reserve)
    {
        int lo = -(CorridorWidth - 1) / 2;
        int hi = CorridorWidth / 2;
        for (int dx = lo; dx <= hi; dx++)
            for (int dy = lo; dy <= hi; dy++)
            {
                int cx = x + dx, cy = y + dy;
                SetFloor(cx, cy);
                if (reserve && InBounds(cx, cy)) _reserved[cx, cy] = true;
            }
    }

    // Minimal binary min-heap for A*. Supports duplicate pushes (lazy deletion via the closed set);
    // ties resolve by heap insertion order, which is deterministic, so generation stays seed-stable.
    private sealed class MinHeap
    {
        private int[] _items;
        private int[] _prio;
        private int   _count;

        public MinHeap(int capacity)
        {
            int c = Mathf.Max(capacity, 4);
            _items = new int[c];
            _prio  = new int[c];
        }

        public int Count => _count;

        public void Push(int item, int prio)
        {
            if (_count == _items.Length)
            {
                System.Array.Resize(ref _items, _count * 2);
                System.Array.Resize(ref _prio,  _count * 2);
            }
            _items[_count] = item;
            _prio[_count]  = prio;
            SiftUp(_count);
            _count++;
        }

        public int Pop()
        {
            int root = _items[0];
            _count--;
            _items[0] = _items[_count];
            _prio[0]  = _prio[_count];
            SiftDown(0);
            return root;
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_prio[i] >= _prio[p]) break;
                Swap(i, p);
                i = p;
            }
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                int l = 2 * i + 1, r = 2 * i + 2, s = i;
                if (l < _count && _prio[l] < _prio[s]) s = l;
                if (r < _count && _prio[r] < _prio[s]) s = r;
                if (s == i) break;
                Swap(i, s);
                i = s;
            }
        }

        private void Swap(int a, int b)
        {
            (_items[a], _items[b]) = (_items[b], _items[a]);
            (_prio[a],  _prio[b])  = (_prio[b],  _prio[a]);
        }
    }

    private void SplitBSP(BSPNode node, int depth)
    {
        if (depth >= GothicMaxDepth) return;

        bool canH = node.Bounds.height >= GothicMinRoom * 2 + 2;
        bool canV = node.Bounds.width >= GothicMinRoom * 2 + 2;
        if (!canH && !canV) return;

        bool horizontal;
        if (canH && canV)
        {
            float r = (float)node.Bounds.width / node.Bounds.height;
            horizontal = r >= 1.25f ? false : r <= 0.8f ? true : _rng.Next(0, 2) == 0;
        }
        else horizontal = canH;

        RectInt bnd = node.Bounds;
        if (horizontal)
        {
            int split = _rng.Next(GothicMinRoom + 1, bnd.height - GothicMinRoom);
            node.Left = new BSPNode(new RectInt(bnd.xMin, bnd.yMin, bnd.width, split));
            node.Right = new BSPNode(new RectInt(bnd.xMin, bnd.yMin + split, bnd.width, bnd.height - split));
        }
        else
        {
            int split = _rng.Next(GothicMinRoom + 1, bnd.width - GothicMinRoom);
            node.Left = new BSPNode(new RectInt(bnd.xMin, bnd.yMin, split, bnd.height));
            node.Right = new BSPNode(new RectInt(bnd.xMin + split, bnd.yMin, bnd.width - split, bnd.height));
        }

        SplitBSP(node.Left, depth + 1);
        SplitBSP(node.Right, depth + 1);
    }

    private void CollectLeaves(BSPNode node, List<BSPNode> outList)
    {
        if (node == null) return;
        if (node.Left == null && node.Right == null) { outList.Add(node); return; }
        CollectLeaves(node.Left, outList);
        CollectLeaves(node.Right, outList);
    }

    private class BSPNode
    {
        public RectInt Bounds;
        public BSPNode Left, Right;
        public BSPNode(RectInt b) { Bounds = b; }
    }

    private Vector2Int RectCenter(RectInt r) => new(r.x + r.width / 2, r.y + r.height / 2);

    private void ConnectSections()
    {
        // Link every section to its right and down grid neighbour. The result is a fully connected
        // lattice with many loops (not a single line), so the whole map is traversable and circuits
        // back on itself regardless of which theme sits where. PruneFloorIslands is the safety net.
        var grid = new SectionInfo[GridColumns, GridRows];
        foreach (SectionInfo s in _sections)
            if (s.Col >= 0 && s.Col < GridColumns && s.Row >= 0 && s.Row < GridRows)
                grid[s.Col, s.Row] = s;

        for (int col = 0; col < GridColumns; col++)
        {
            for (int row = 0; row < GridRows; row++)
            {
                SectionInfo s = grid[col, row];
                if (s == null) continue;
                if (col + 1 < GridColumns && grid[col + 1, row] != null)
                    CarveCorridorL(s.Center, grid[col + 1, row].Center, reserve: true);
                if (row + 1 < GridRows && grid[col, row + 1] != null)
                    CarveCorridorL(s.Center, grid[col, row + 1].Center, reserve: true);
            }
        }
    }

    private void CarveCorridorL(Vector2Int a, Vector2Int b, bool reserve = false)
    {
        if (_rng.Next(0, 2) == 0)
        {
            CarveH(a.x, b.x, a.y, reserve);
            CarveV(a.y, b.y, b.x, reserve);
        }
        else
        {
            CarveV(a.y, b.y, a.x, reserve);
            CarveH(a.x, b.x, b.y, reserve);
        }
    }

    private void CarveH(int xa, int xb, int y, bool reserve)
    {
        int lo = Mathf.Min(xa, xb), hi = Mathf.Max(xa, xb);
        for (int x = lo; x <= hi; x++) CarveBand(x, y, true, reserve);
    }

    private void CarveV(int ya, int yb, int x, bool reserve)
    {
        int lo = Mathf.Min(ya, yb), hi = Mathf.Max(ya, yb);
        for (int y = lo; y <= hi; y++) CarveBand(x, y, false, reserve);
    }

    private void CarveBand(int x, int y, bool horizontal, bool reserve)
    {
        int lo = -(CorridorWidth - 1) / 2;
        int hi = CorridorWidth / 2;
        for (int o = lo; o <= hi; o++)
        {
            int cx = horizontal ? x : x + o;
            int cy = horizontal ? y + o : y;
            SetFloor(cx, cy);
            if (reserve && InBounds(cx, cy)) _reserved[cx, cy] = true;
        }
    }

    /// <summary>
    /// Keep only the largest connected floor region; any floor not connected to it
    /// (e.g. a tiny pocket isolated by open-area erosion) is unreachable, so it is
    /// turned back into wall. Guarantees a single, fully-traversable layout.
    /// </summary>
    private void PruneFloorIslands()
    {
        var comp = new int[_width, _height];
        var sizes = new List<int> { 0 }; // index 0 unused
        var stack = new Stack<Vector2Int>();
        int id = 0;

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (!_floor[x, y] || comp[x, y] != 0) continue;
                id++;
                int size = 0;
                stack.Push(new Vector2Int(x, y));
                comp[x, y] = id;
                while (stack.Count > 0)
                {
                    Vector2Int p = stack.Pop();
                    size++;
                    PushComp(comp, stack, id, p.x + 1, p.y);
                    PushComp(comp, stack, id, p.x - 1, p.y);
                    PushComp(comp, stack, id, p.x, p.y + 1);
                    PushComp(comp, stack, id, p.x, p.y - 1);
                }
                sizes.Add(size);
            }
        }

        if (id <= 1) return;

        int biggest = 1;
        for (int i = 2; i <= id; i++)
            if (sizes[i] > sizes[biggest]) biggest = i;

        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                if (_floor[x, y] && comp[x, y] != biggest)
                {
                    _floor[x, y] = false;
                    _reserved[x, y] = false;
                }
            }
        }
    }

    private void PushComp(int[,] comp, Stack<Vector2Int> stack, int id, int x, int y)
    {
        if (!InBounds(x, y) || !_floor[x, y] || comp[x, y] != 0) return;
        comp[x, y] = id;
        stack.Push(new Vector2Int(x, y));
    }

    private void AssignSectionOwnership()
    {
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                if (!_floor[x, y]) continue;
                _hasSection[x, y] = true;
                _section[x, y] = SectionType.Transition;
                foreach (SectionInfo s in _sections)
                {
                    if (s.Bounds.Contains(new Vector2Int(x, y)))
                    {
                        _section[x, y] = s.Type;
                        break;
                    }
                }
            }
        }
    }

    private void SetFloor(int x, int y)
    {
        if (InBounds(x, y)) _floor[x, y] = true;
    }

    private bool IsFloor(int x, int y) => InBounds(x, y) && _floor[x, y];
    private bool InBounds(int x, int y) => x >= 0 && x < _width && y >= 0 && y < _height;

    // ---------------------------------------------------------------------
    // Floor / ceiling meshes (one mesh per section for cheap colliders)
    // ---------------------------------------------------------------------

    private void BuildFloors()
    {
        BuildHorizontalMesh(_floorsRoot, "GothicFloor",     SectionType.GothicRuins, 0f, true, FloorMatFor(SectionType.GothicRuins), true);
        BuildHorizontalMesh(_floorsRoot, "GraveyardGround", SectionType.Graveyard,   0f, true, FloorMatFor(SectionType.Graveyard),   true);
        BuildHorizontalMesh(_floorsRoot, "HellFloor",       SectionType.HellPillars, 0f, true, FloorMatFor(SectionType.HellPillars), true);
        BuildHorizontalMesh(_floorsRoot, "CatacombFloor",   SectionType.Catacombs,   0f, true, FloorMatFor(SectionType.Catacombs),   true);
        BuildHorizontalMesh(_floorsRoot, "CavernFloor",     SectionType.Cavern,      0f, true, FloorMatFor(SectionType.Cavern),      true);
        BuildHorizontalMesh(_floorsRoot, "TransitionFloor", SectionType.Transition,  0f, true, FloorMatFor(SectionType.Transition),  true);
    }

    private void BuildCeilings()
    {
        TryCeiling(GothicCeiling,     "GothicCeiling",     SectionType.GothicRuins);
        TryCeiling(HellCeiling,       "HellCeiling",       SectionType.HellPillars);
        TryCeiling(CatacombCeiling,   "CatacombCeiling",   SectionType.Catacombs);
        TryCeiling(CavernCeiling,     "CavernCeiling",     SectionType.Cavern);
        TryCeiling(TransitionCeiling, "TransitionCeiling", SectionType.Transition);
        // Graveyard is open to the sky — no ceiling.
    }

    private void TryCeiling(bool enabled, string name, SectionType type)
    {
        if (!enabled) return;
        BuildHorizontalMesh(_ceilingsRoot, name, type, WallHeightFor(type), false, CeilingMatFor(type), false);
    }

    private bool CeilingEnabledFor(SectionType type) => type switch
    {
        SectionType.GothicRuins => GothicCeiling,
        SectionType.HellPillars => HellCeiling,
        SectionType.Catacombs => CatacombCeiling,
        SectionType.Cavern => CavernCeiling,
        SectionType.Transition => TransitionCeiling,
        _ => false   // Graveyard is open to the sky
    };

    // Close the vertical gap where two adjacent roofed floor tiles have different ceiling heights:
    // the lower ceiling would otherwise leave an open band under the taller one. A tile bordering
    // an open-sky (Graveyard) section gets no skirt — that opening is intentional.
    private void BuildCeilingSkirts()
    {
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (!_floor[x, y]) continue;
                SectionType ta = _section[x, y];
                if (!CeilingEnabledFor(ta)) continue;
                float ha = WallHeightFor(ta);
                TrySkirt(x, y, ta, ha, x + 1, y, +1, true);
                TrySkirt(x, y, ta, ha, x - 1, y, -1, true);
                TrySkirt(x, y, ta, ha, x, y + 1, +1, false);
                TrySkirt(x, y, ta, ha, x, y - 1, -1, false);
            }
        }
    }

    private void TrySkirt(int x, int y, SectionType ta, float ha, int nx, int ny, int dir, bool alongX)
    {
        if (!IsFloor(nx, ny)) return;              // non-floor borders are handled by walls
        SectionType tb = _section[nx, ny];
        if (!CeilingEnabledFor(tb)) return;        // neighbour open to sky -> intended opening
        float hb = WallHeightFor(tb);
        if (ha <= hb) return;                      // emit once, only from the taller tile
        float gap = ha - hb;
        float cy = (ha + hb) * 0.5f;
        if (alongX)
        {
            float xEdge = dir > 0 ? x + 1 : x;
            CreateSkirt(new Vector3(xEdge, cy, y + 0.5f), new Vector3(WallThickness, gap, 1f), ta);
        }
        else
        {
            float zEdge = dir > 0 ? y + 1 : y;
            CreateSkirt(new Vector3(x + 0.5f, cy, zEdge), new Vector3(1f, gap, WallThickness), ta);
        }
    }

    private void CreateSkirt(Vector3 center, Vector3 size, SectionType type)
    {
        GameObject go = CreatePart(PrimitiveType.Cube, _wallsRoot, "CeilingSkirt", center, Vector3.zero, size, WallMatFor(type), collider: true);
        ApplyWallLayer(go);
        MarkStatic(go);
    }

    private void BuildHorizontalMesh(Transform parent, string name, SectionType type, float height, bool faceUp, Material mat, bool collider)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var uvs = new List<Vector2>();

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (!_floor[x, y] || _section[x, y] != type) continue;
                AddTileQuad(verts, tris, uvs, x, y, height, faceUp);
            }
        }

        if (verts.Count == 0) return;

        var mesh = new Mesh { name = name };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        _builtMeshes.Add(mesh);

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        MarkStatic(go);
    }

    private void AddTileQuad(List<Vector3> verts, List<int> tris, List<Vector2> uvs, int x, int y, float h, bool faceUp)
    {
        int s = verts.Count;
        if (faceUp)
        {
            verts.Add(new Vector3(x, h, y));
            verts.Add(new Vector3(x, h, y + 1));
            verts.Add(new Vector3(x + 1, h, y + 1));
            verts.Add(new Vector3(x + 1, h, y));
        }
        else
        {
            verts.Add(new Vector3(x, h, y));
            verts.Add(new Vector3(x + 1, h, y));
            verts.Add(new Vector3(x + 1, h, y + 1));
            verts.Add(new Vector3(x, h, y + 1));
        }
        uvs.Add(new Vector2(x, y));
        uvs.Add(new Vector2(x, y + 1));
        uvs.Add(new Vector2(x + 1, y + 1));
        uvs.Add(new Vector2(x + 1, y));
        tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
    }

    // ---------------------------------------------------------------------
    // Walls — run-merged per section (height + material from the owning tile)
    // ---------------------------------------------------------------------

    private void BuildWalls()
    {
        for (int y = 0; y < _height; y++)
        {
            BuildWallRunX(y, +1);
            BuildWallRunX(y, -1);
        }
        for (int x = 0; x < _width; x++)
        {
            BuildWallRunZ(x, +1);
            BuildWallRunZ(x, -1);
        }
    }

    private void BuildWallRunX(int y, int dz)
    {
        float half = WallThickness * 0.5f;
        int x = 0;
        while (x < _width)
        {
            if (!IsWallEdge(x, y, 0, dz)) { x++; continue; }
            SectionType type = _section[x, y];
            int xs = x;
            while (x < _width && IsWallEdge(x, y, 0, dz) && _section[x, y] == type) x++;

            // Cap a run end with half WallThickness only at a genuine corner (next tile is not a
            // collinear wall edge). When the next/prev tile IS a collinear wall edge of another
            // section type, butt-join exactly at the boundary so the two runs don't overlap and
            // z-fight on the shared wall face.
            float startX = xs - (CapBefore(xs, y, 0, dz) ? half : 0f);
            float endX   = x  + (CapAfter(x, y, 0, dz)   ? half : 0f);

            float zEdge = dz > 0 ? y + 1 : y;
            float wh = WallHeightFor(type);
            CreateWall(new Vector3((startX + endX) * 0.5f, wh * 0.5f, zEdge),
                       new Vector3(endX - startX, wh, WallThickness), type);
        }
    }

    private void BuildWallRunZ(int x, int dx)
    {
        float half = WallThickness * 0.5f;
        int y = 0;
        while (y < _height)
        {
            if (!IsWallEdge(x, y, dx, 0)) { y++; continue; }
            SectionType type = _section[x, y];
            int ys = y;
            while (y < _height && IsWallEdge(x, y, dx, 0) && _section[x, y] == type) y++;

            float startY = ys - (CapBefore(x, ys, dx, 0) ? half : 0f);
            float endY   = y  + (CapAfter(x, y, dx, 0)   ? half : 0f);

            float xEdge = dx > 0 ? x + 1 : x;
            float wh = WallHeightFor(type);
            CreateWall(new Vector3(xEdge, wh * 0.5f, (startY + endY) * 0.5f),
                       new Vector3(WallThickness, wh, endY - startY), type);
        }
    }

    // A run spans tiles [start, end) along the X axis (offset 0,dz) or Z axis (offset dx,0).
    // Cap the start if the tile just before the run is not a collinear wall edge (true corner);
    // skip the cap when it is (a different-type run continues collinearly — butt-join, no overlap).
    private bool CapBefore(int sx, int sy, int dx, int dz)
    {
        int px = dz != 0 ? sx - 1 : sx;       // step back along the run axis
        int py = dz != 0 ? sy : sy - 1;
        return !InBounds(px, py) || !IsWallEdge(px, py, dx, dz);
    }

    private bool CapAfter(int ex, int ey, int dx, int dz)
    {
        // (ex,ey) is the tile that broke the run.
        return !InBounds(ex, ey) || !IsWallEdge(ex, ey, dx, dz);
    }

    private bool IsWallEdge(int x, int y, int dx, int dz) => InBounds(x, y) && _floor[x, y] && !IsFloor(x + dx, y + dz);

    private float WallHeightFor(SectionType type) => type switch
    {
        SectionType.GothicRuins => GothicWallHeight,
        SectionType.Graveyard => GraveyardWallHeight,
        SectionType.HellPillars => HellWallHeight,
        SectionType.Catacombs => CatacombWallHeight,
        SectionType.Cavern => CavernWallHeight,
        _ => GothicWallHeight
    };

    private void CreateWall(Vector3 center, Vector3 size, SectionType type)
    {
        GameObject go = CreatePart(PrimitiveType.Cube, _wallsRoot, "Wall", center, Vector3.zero, size, WallMatFor(type), collider: true);
        ApplyWallLayer(go);
        MarkStatic(go);
    }

    private Material WallMatFor(SectionType type) => type switch
    {
        SectionType.HellPillars => HellPillarMat(),
        SectionType.Graveyard => GraveStoneMat(),
        SectionType.Catacombs => CatacombWallMat(),
        SectionType.Cavern => CavernRockMat(),
        _ => GothicWallMat()
    };

    private Material FloorMatFor(SectionType type) => type switch
    {
        SectionType.GothicRuins => GothicFloorMat(),
        SectionType.Graveyard => GraveGroundMat(),
        SectionType.HellPillars => HellFloorMat(),
        SectionType.Catacombs => CatacombFloorMat(),
        SectionType.Cavern => CavernFloorMat(),
        _ => GothicFloorMat()   // Transition
    };

    private Material CeilingMatFor(SectionType type) => type switch
    {
        SectionType.HellPillars => HellFloorMat(),
        SectionType.Catacombs => CatacombWallMat(),
        SectionType.Cavern => CavernRockMat(),
        _ => GothicCeilingMat()
    };

    private void ApplyWallLayer(GameObject go)
    {
        if (string.IsNullOrEmpty(WallLayerName)) return;
        int layer = LayerMask.NameToLayer(WallLayerName);
        if (layer < 0)
        {
            Debug.LogWarning($"[MultiSectionDungeonGenerator] Layer '{WallLayerName}' not found. " +
                             "Run Fluffterror > Setup > Add Walls Layer, then regenerate.", this);
            return;
        }
        go.layer = layer;
    }

    // ---------------------------------------------------------------------
    // Gothic decoration — torches, windows, cell bars, wall panels
    // ---------------------------------------------------------------------

    private void DecorateGothic()
    {
        List<WallEdge> edges = CollectWallEdges(t => t == SectionType.GothicRuins || t == SectionType.Transition);
        Shuffle(edges);

        var used = new HashSet<int>();
        PlaceAlongEdges(edges, used, TorchDensity, 4.5f, BuildTorch);
        PlaceAlongEdges(edges, used, GothicDetailDensity * 0.5f, 6f, BuildWindow);
        PlaceAlongEdges(edges, used, GothicDetailDensity * 0.35f, 8f, BuildCellBars);
        PlaceAlongEdges(edges, used, GothicDetailDensity, 3f, BuildWallPanel);
    }

    private List<WallEdge> CollectWallEdges(Func<SectionType, bool> accept)
    {
        var edges = new List<WallEdge>();
        float inset = WallThickness * 0.5f;

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                if (!_floor[x, y] || !accept(_section[x, y])) continue;
                SectionType st = _section[x, y];

                if (!IsFloor(x, y + 1)) edges.Add(MakeEdge(x, y, 0, new Vector3(x + 0.5f, 0, y + 1 - inset), new Vector3(0, 0, -1), st));
                if (!IsFloor(x, y - 1)) edges.Add(MakeEdge(x, y, 1, new Vector3(x + 0.5f, 0, y + inset), new Vector3(0, 0, 1), st));
                if (!IsFloor(x + 1, y)) edges.Add(MakeEdge(x, y, 2, new Vector3(x + 1 - inset, 0, y + 0.5f), new Vector3(-1, 0, 0), st));
                if (!IsFloor(x - 1, y)) edges.Add(MakeEdge(x, y, 3, new Vector3(x + inset, 0, y + 0.5f), new Vector3(1, 0, 0), st));
            }
        }
        return edges;
    }

    private WallEdge MakeEdge(int x, int y, int side, Vector3 surface, Vector3 into, SectionType st) =>
        new() { Surface = surface, IntoRoom = into, Cx = x, Cy = y, Side = side, Section = st };

    private void PlaceAlongEdges(List<WallEdge> edges, HashSet<int> used, float density, float minSpacing, Action<WallEdge> build)
    {
        if (density <= 0f) return;
        var placed = new List<Vector3>();
        float minSqr = minSpacing * minSpacing;

        foreach (WallEdge e in edges)
        {
            int key = EdgeKey(e.Cx, e.Cy, e.Side);
            if (used.Contains(key) || !Roll(density)) continue;

            bool tooClose = false;
            for (int i = 0; i < placed.Count; i++)
                if ((placed[i] - e.Surface).sqrMagnitude < minSqr) { tooClose = true; break; }
            if (tooClose) continue;

            build(e);
            placed.Add(e.Surface);
            used.Add(key);
            used.Add(EdgeKey(e.Cx + 1, e.Cy, e.Side));
            used.Add(EdgeKey(e.Cx - 1, e.Cy, e.Side));
            used.Add(EdgeKey(e.Cx, e.Cy + 1, e.Side));
            used.Add(EdgeKey(e.Cx, e.Cy - 1, e.Side));
        }
    }

    private int EdgeKey(int x, int y, int side) => ((x * _height) + y) * 4 + side;
    private Quaternion WallFacing(WallEdge e) => Quaternion.LookRotation(e.IntoRoom, Vector3.up);

    private void BuildTorch(WallEdge e)
    {
        var root = new GameObject("Torch");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = e.Surface + Vector3.up * 2.4f;
        root.transform.localRotation = WallFacing(e);

        CreatePart(PrimitiveType.Cube, root.transform, "Bracket", new Vector3(0, 0, 0.03f), Vector3.zero, new Vector3(0.2f, 0.36f, 0.06f), WoodMat());
        CreatePart(PrimitiveType.Cylinder, root.transform, "Handle", new Vector3(0, 0.16f, 0.16f), new Vector3(50, 0, 0), new Vector3(0.07f, 0.22f, 0.07f), WoodMat());
        Vector3 flame = new(0, 0.34f, 0.35f);
        CreatePart(PrimitiveType.Sphere, root.transform, "Flame", flame, Vector3.zero, new Vector3(0.18f, 0.26f, 0.18f), FlameMat());

        Vector3 world = root.transform.localPosition + WallFacing(e) * flame;
        SpawnLight(world + e.IntoRoom * 0.15f, PickFrom(GothicTorchColors), new Vector2(1.8f, 2.8f), 8.5f, flicker: true, heroEligible: true);
    }

    private void BuildWindow(WallEdge e)
    {
        var root = new GameObject("Window");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = e.Surface + Vector3.up * 2.3f;
        root.transform.localRotation = WallFacing(e);

        const float w = 1.2f, h = 1.6f;
        CreatePart(PrimitiveType.Cube, root.transform, "Pane", new Vector3(0, 0, -0.06f), Vector3.zero, new Vector3(w, h, 0.05f), GlassMat());
        CreatePart(PrimitiveType.Cube, root.transform, "FrameTop", new Vector3(0, h * 0.5f, 0), Vector3.zero, new Vector3(w + 0.24f, 0.14f, 0.18f), GraveStoneMat());
        CreatePart(PrimitiveType.Cube, root.transform, "FrameBottom", new Vector3(0, -h * 0.5f, 0), Vector3.zero, new Vector3(w + 0.24f, 0.14f, 0.18f), GraveStoneMat());
        CreatePart(PrimitiveType.Cube, root.transform, "FrameLeft", new Vector3(-w * 0.5f - 0.06f, 0, 0), Vector3.zero, new Vector3(0.14f, h + 0.2f, 0.18f), GraveStoneMat());
        CreatePart(PrimitiveType.Cube, root.transform, "FrameRight", new Vector3(w * 0.5f + 0.06f, 0, 0), Vector3.zero, new Vector3(0.14f, h + 0.2f, 0.18f), GraveStoneMat());
        CreatePart(PrimitiveType.Cube, root.transform, "BarV", new Vector3(0, 0, 0.02f), Vector3.zero, new Vector3(0.05f, h, 0.05f), MetalMat());
        CreatePart(PrimitiveType.Cube, root.transform, "BarH", new Vector3(0, 0, 0.02f), Vector3.zero, new Vector3(w, 0.05f, 0.05f), MetalMat());

        if (Roll(0.4f))
            SpawnLight(root.transform.localPosition + WallFacing(e) * new Vector3(0, 0, 0.4f), PickFrom(GraveyardColors), new Vector2(0.5f, 0.9f), 5f, false, false);
    }

    private void BuildCellBars(WallEdge e)
    {
        var root = new GameObject("CellBars");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = e.Surface;
        root.transform.localRotation = WallFacing(e);

        const float width = 1.7f, barH = 2.6f, z = 0.08f;
        int bars = 5;
        float step = width / (bars - 1);
        for (int i = 0; i < bars; i++)
            CreatePart(PrimitiveType.Cylinder, root.transform, "Bar", new Vector3(-width * 0.5f + step * i, barH * 0.5f, z), Vector3.zero, new Vector3(0.07f, barH * 0.5f, 0.07f), MetalMat());
        CreatePart(PrimitiveType.Cube, root.transform, "RailTop", new Vector3(0, barH - 0.1f, z), Vector3.zero, new Vector3(width + 0.2f, 0.1f, 0.1f), MetalMat());
        CreatePart(PrimitiveType.Cube, root.transform, "RailBottom", new Vector3(0, 0.15f, z), Vector3.zero, new Vector3(width + 0.2f, 0.1f, 0.1f), MetalMat());
    }

    private void BuildWallPanel(WallEdge e)
    {
        var root = new GameObject("WallPanel");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = e.Surface;
        root.transform.localRotation = WallFacing(e);

        float wh = WallHeightFor(e.Section);
        int variant = _rng.Next(0, 3);
        if (variant == 0)
            CreatePart(PrimitiveType.Cube, root.transform, "SupportBeam", new Vector3(0, (wh - 0.1f) * 0.5f, 0.08f), Vector3.zero, new Vector3(0.32f, wh - 0.1f, 0.16f), WoodMat());
        else if (variant == 1)
            CreatePart(PrimitiveType.Cube, root.transform, "StoneTrim", new Vector3(0, RandomRange(1f, wh - 1.2f), 0.05f), Vector3.zero, new Vector3(1.6f, 0.4f, 0.1f), GraveStoneMat());
        else
            CreatePart(PrimitiveType.Quad, root.transform, "Crack", new Vector3(RandomRange(-0.3f, 0.3f), RandomRange(1.2f, wh - 1.2f), 0.02f), new Vector3(0, 180, _rng.Next(0, 4) * 90), new Vector3(RandomRange(0.8f, 1.6f), RandomRange(0.8f, 1.6f), 1f), MetalMat());
    }

    // ---------------------------------------------------------------------
    // Graveyard decoration — graves, crosses, tombs, trees, cold light
    // ---------------------------------------------------------------------

    private void DecorateGraveyard()
    {
        foreach (SectionInfo s in _sections)
            if (s.Type == SectionType.Graveyard) DecorateGraveyardSection(s);
    }

    private void DecorateGraveyardSection(SectionInfo s)
    {
        RectInt b = s.Bounds;

        int target = Mathf.RoundToInt(b.width * b.height * 0.06f * GraveDensity);
        var placed = new List<Vector2Int>();

        for (int i = 0; i < target * 4 && placed.Count < target; i++)
        {
            int x = _rng.Next(b.xMin + 1, b.xMax - 1);
            int y = _rng.Next(b.yMin + 1, b.yMax - 1);
            if (!IsFloor(x, y) || _reserved[x, y]) continue;

            bool tooClose = false;
            foreach (Vector2Int p in placed)
                if (Mathf.Abs(p.x - x) <= 1 && Mathf.Abs(p.y - y) <= 1) { tooClose = true; break; }
            if (tooClose) continue;

            placed.Add(new Vector2Int(x, y));
            Vector3 pos = TileLocal(x, y);
            int kind = _rng.Next(0, 10);
            if (kind < 5) BuildGravestone(pos);
            else if (kind < 7) BuildCross(pos);
            else if (kind < 9) BuildTomb(pos);
            else BuildDeadTree(pos);
        }

        // Cold fill + rare warm lanterns.
        int lights = Mathf.RoundToInt(b.width * b.height * 0.02f * GraveyardLightDensity * 10f);
        for (int i = 0; i < lights; i++)
        {
            int x = _rng.Next(b.xMin + 1, b.xMax - 1);
            int y = _rng.Next(b.yMin + 1, b.yMax - 1);
            if (!IsFloor(x, y)) continue;
            bool lantern = Roll(0.25f);
            Color c = lantern ? GraveyardLanternColor : PickFrom(GraveyardColors);
            SpawnLight(TileLocal(x, y) + Vector3.up * (lantern ? 1.5f : 3f), c, new Vector2(0.5f, 1.1f), lantern ? 6f : 9f, flicker: lantern, heroEligible: false);
        }

        // A wide, soft cool fill so this open-sky section reads as moonlit night rather than black.
        if (ExteriorFillIntensity > 0f)
        {
            Vector2Int c = s.Center;
            float range = Mathf.Max(b.width, b.height);
            SpawnLight(TileLocal(c.x, c.y) + Vector3.up * (range * 0.6f),
                       PickFrom(GraveyardColors), new Vector2(ExteriorFillIntensity, ExteriorFillIntensity),
                       range, flicker: false, heroEligible: false);
        }
    }

    private void BuildGravestone(Vector3 pos)
    {
        var root = new GameObject("Gravestone");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = pos;
        root.transform.localRotation = Quaternion.Euler(RandomRange(-6f, 6f), _rng.Next(0, 4) * 90f, RandomRange(-4f, 4f));
        float h = RandomRange(0.9f, 1.4f);
        CreatePart(PrimitiveType.Cube, root.transform, "Slab", new Vector3(0, h * 0.5f, 0), Vector3.zero, new Vector3(0.7f, h, 0.18f), GraveStoneMat(), collider: true);
        CreatePart(PrimitiveType.Cube, root.transform, "Base", new Vector3(0, 0.08f, 0), Vector3.zero, new Vector3(0.9f, 0.16f, 0.4f), GraveStoneMat());
    }

    private void BuildCross(Vector3 pos)
    {
        var root = new GameObject("Cross");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = pos;
        root.transform.localRotation = Quaternion.Euler(0, _rng.Next(0, 4) * 90f, RandomRange(-5f, 5f));
        float h = RandomRange(1.2f, 1.8f);
        CreatePart(PrimitiveType.Cube, root.transform, "Post", new Vector3(0, h * 0.5f, 0), Vector3.zero, new Vector3(0.14f, h, 0.14f), GraveStoneMat(), collider: true);
        CreatePart(PrimitiveType.Cube, root.transform, "Arm", new Vector3(0, h * 0.72f, 0), Vector3.zero, new Vector3(0.6f, 0.14f, 0.14f), GraveStoneMat());
    }

    private void BuildTomb(Vector3 pos)
    {
        var root = new GameObject("Tomb");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = pos;
        root.transform.localRotation = Quaternion.Euler(0, _rng.Next(0, 4) * 90f, 0);
        CreatePart(PrimitiveType.Cube, root.transform, "Base", new Vector3(0, 0.35f, 0), Vector3.zero, new Vector3(1.6f, 0.7f, 1.0f), GraveStoneMat(), collider: true);
        CreatePart(PrimitiveType.Cube, root.transform, "Lid", new Vector3(0, 0.75f, 0), Vector3.zero, new Vector3(1.7f, 0.18f, 1.1f), GraveStoneMat());
    }

    private void BuildDeadTree(Vector3 pos)
    {
        var root = new GameObject("DeadTree");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = pos;
        root.transform.localRotation = Quaternion.Euler(0, _rng.Next(0, 360), 0);
        float h = RandomRange(2.5f, 4f);
        CreatePart(PrimitiveType.Cylinder, root.transform, "Trunk", new Vector3(0, h * 0.5f, 0), new Vector3(RandomRange(-4f, 4f), 0, RandomRange(-4f, 4f)), new Vector3(0.22f, h * 0.5f, 0.22f), WoodMat(), collider: true);
        int branches = _rng.Next(2, 5);
        for (int i = 0; i < branches; i++)
            CreatePart(PrimitiveType.Cylinder, root.transform, "Branch", new Vector3(0, RandomRange(h * 0.5f, h), 0), new Vector3(RandomRange(20f, 70f), _rng.Next(0, 360), 0), new Vector3(0.08f, RandomRange(0.4f, 0.9f), 0.08f), WoodMat());
    }

    // ---------------------------------------------------------------------
    // Hell decoration — pillars, lava cracks, red light
    // ---------------------------------------------------------------------

    private void DecorateHell()
    {
        foreach (SectionInfo s in _sections)
            if (s.Type == SectionType.HellPillars) DecorateHellSection(s);
    }

    private void DecorateHellSection(SectionInfo s)
    {
        RectInt b = s.Bounds;
        int spacing = Mathf.Max(2, PillarSpacing);

        for (int x = b.xMin + 1; x < b.xMax - 1; x += spacing)
        {
            for (int y = b.yMin + 1; y < b.yMax - 1; y += spacing)
            {
                int jx = x + _rng.Next(-1, 2);
                int jy = y + _rng.Next(-1, 2);
                if (!IsFloor(jx, jy) || _reserved[jx, jy]) continue;
                if (!Roll(HellPillarDensity)) continue;
                BuildPillar(TileLocal(jx, jy));
            }
        }

        // Glowing lava cracks on the floor.
        int cracks = Mathf.RoundToInt(b.width * b.height * LavaCrackDensity);
        for (int i = 0; i < cracks; i++)
        {
            int x = _rng.Next(b.xMin, b.xMax);
            int y = _rng.Next(b.yMin, b.yMax);
            if (!IsFloor(x, y)) continue;
            bool along = _rng.Next(0, 2) == 0;
            Vector3 scale = along ? new Vector3(RandomRange(1.5f, 3.5f), RandomRange(0.4f, 0.9f), 1f)
                                  : new Vector3(RandomRange(0.4f, 0.9f), RandomRange(1.5f, 3.5f), 1f);
            CreatePart(PrimitiveType.Quad, _decorRoot, "LavaCrack", TileLocal(x, y) + Vector3.up * 0.02f, new Vector3(90, 0, 0), scale, LavaMat());
        }

        // Red aggressive light.
        int lights = Mathf.RoundToInt(b.width * b.height * 0.02f * HellLightDensity * 10f);
        for (int i = 0; i < lights; i++)
        {
            int x = _rng.Next(b.xMin, b.xMax);
            int y = _rng.Next(b.yMin, b.yMax);
            if (!IsFloor(x, y)) continue;
            SpawnLight(TileLocal(x, y) + Vector3.up * RandomRange(0.3f, 2f), PickFrom(HellColors), new Vector2(1.5f, 3f), 9f, flicker: true, heroEligible: true);
        }
    }

    private void BuildPillar(Vector3 pos)
    {
        var root = new GameObject("Pillar");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = pos;
        float h = HellWallHeight;
        CreatePart(PrimitiveType.Cylinder, root.transform, "Shaft", new Vector3(0, h * 0.5f, 0), Vector3.zero, new Vector3(1.1f, h * 0.5f, 1.1f), HellPillarMat(), collider: true);
        CreatePart(PrimitiveType.Cube, root.transform, "Base", new Vector3(0, 0.25f, 0), Vector3.zero, new Vector3(1.5f, 0.5f, 1.5f), HellPillarMat());
        CreatePart(PrimitiveType.Cube, root.transform, "Capital", new Vector3(0, h - 0.25f, 0), Vector3.zero, new Vector3(1.5f, 0.5f, 1.5f), HellPillarMat());
        if (Roll(0.4f))
            SpawnLight(pos + Vector3.up * (h - 1f), PickFrom(HellColors), new Vector2(1f, 2f), 6f, flicker: true, heroEligible: false);
    }

    // ---------------------------------------------------------------------
    // Catacombs decoration — bone piles, skull niches, candle sconces (BSP rooms)
    // ---------------------------------------------------------------------

    private void DecorateCatacombs()
    {
        List<WallEdge> edges = CollectWallEdges(t => t == SectionType.Catacombs);
        Shuffle(edges);

        var used = new HashSet<int>();
        PlaceAlongEdges(edges, used, TorchDensity * 0.6f, 5f, BuildCandleSconce);
        PlaceAlongEdges(edges, used, CatacombBoneDensity, 3.5f, BuildBonePile);
        PlaceAlongEdges(edges, used, CatacombBoneDensity * 0.5f, 6f, BuildSkullNiche);
    }

    private void BuildCandleSconce(WallEdge e)
    {
        var root = new GameObject("CandleSconce");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = e.Surface + Vector3.up * 1.9f;
        root.transform.localRotation = WallFacing(e);

        CreatePart(PrimitiveType.Cube, root.transform, "Shelf", new Vector3(0, -0.08f, 0.1f), Vector3.zero, new Vector3(0.28f, 0.06f, 0.18f), GraveStoneMat());
        CreatePart(PrimitiveType.Cylinder, root.transform, "Candle", new Vector3(0, 0.05f, 0.12f), Vector3.zero, new Vector3(0.05f, 0.1f, 0.05f), BoneMat());
        Vector3 flame = new(0, 0.2f, 0.12f);
        CreatePart(PrimitiveType.Sphere, root.transform, "Flame", flame, Vector3.zero, new Vector3(0.07f, 0.11f, 0.07f), FlameMat());
        SpawnLight(root.transform.localPosition + WallFacing(e) * flame, new Color(1f, 0.66f, 0.32f), new Vector2(0.8f, 1.4f), 5.5f, flicker: true, heroEligible: false);
    }

    private void BuildBonePile(WallEdge e)
    {
        var root = new GameObject("BonePile");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = e.Surface;
        root.transform.localRotation = WallFacing(e);

        int bones = _rng.Next(4, 9);
        for (int i = 0; i < bones; i++)
            CreatePart(PrimitiveType.Cylinder, root.transform, "Bone",
                new Vector3(RandomRange(-0.5f, 0.5f), RandomRange(0.05f, 0.5f), RandomRange(0.05f, 0.35f)),
                new Vector3(90, RandomRange(-40f, 40f), RandomRange(-30f, 30f)),
                new Vector3(0.06f, RandomRange(0.18f, 0.34f), 0.06f), BoneMat());
        if (Roll(0.5f))
            CreatePart(PrimitiveType.Sphere, root.transform, "Skull", new Vector3(RandomRange(-0.3f, 0.3f), 0.18f, 0.2f), Vector3.zero, new Vector3(0.22f, 0.24f, 0.22f), BoneMat());
    }

    private void BuildSkullNiche(WallEdge e)
    {
        var root = new GameObject("SkullNiche");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = e.Surface + Vector3.up * 1.6f;
        root.transform.localRotation = WallFacing(e);

        CreatePart(PrimitiveType.Cube, root.transform, "Recess", new Vector3(0, 0, 0.05f), Vector3.zero, new Vector3(0.7f, 0.9f, 0.06f), CatacombWallMat());
        CreatePart(PrimitiveType.Sphere, root.transform, "Skull", new Vector3(0, 0, 0f), Vector3.zero, new Vector3(0.3f, 0.34f, 0.3f), BoneMat());
        if (Roll(0.4f))
            SpawnLight(root.transform.localPosition + WallFacing(e) * new Vector3(0, 0, 0.3f), new Color(1f, 0.7f, 0.4f), new Vector2(0.4f, 0.8f), 4.5f, flicker: true, heroEligible: false);
    }

    // ---------------------------------------------------------------------
    // Cavern decoration — rocks, stalagmites/stalactites, glowing crystals (eroded caves)
    // ---------------------------------------------------------------------

    private void DecorateCavern()
    {
        foreach (SectionInfo s in _sections)
            if (s.Type == SectionType.Cavern) DecorateCavernSection(s);
    }

    private void DecorateCavernSection(SectionInfo s)
    {
        RectInt b = s.Bounds;

        int target = Mathf.RoundToInt(b.width * b.height * 0.06f * CavernRockDensity);
        var placed = new List<Vector2Int>();

        for (int i = 0; i < target * 4 && placed.Count < target; i++)
        {
            int x = _rng.Next(b.xMin + 1, b.xMax - 1);
            int y = _rng.Next(b.yMin + 1, b.yMax - 1);
            if (!IsFloor(x, y) || _reserved[x, y]) continue;

            bool tooClose = false;
            foreach (Vector2Int p in placed)
                if (Mathf.Abs(p.x - x) <= 1 && Mathf.Abs(p.y - y) <= 1) { tooClose = true; break; }
            if (tooClose) continue;

            placed.Add(new Vector2Int(x, y));
            Vector3 pos = TileLocal(x, y);
            int kind = _rng.Next(0, 10);
            if (kind < 5) BuildRock(pos);
            else if (kind < 8) BuildStalagmite(pos);
            else BuildCrystalCluster(pos);
        }

        // Stalactites hanging from the cavern ceiling.
        if (CavernCeiling)
        {
            int stal = Mathf.RoundToInt(b.width * b.height * 0.02f * CavernRockDensity);
            for (int i = 0; i < stal; i++)
            {
                int x = _rng.Next(b.xMin, b.xMax);
                int y = _rng.Next(b.yMin, b.yMax);
                if (!IsFloor(x, y)) continue;
                BuildStalactite(TileLocal(x, y), CavernWallHeight);
            }
        }
    }

    private void BuildRock(Vector3 pos)
    {
        var root = new GameObject("Rock");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = pos;
        root.transform.localRotation = Quaternion.Euler(0, _rng.Next(0, 360), 0);
        int chunks = _rng.Next(1, 4);
        for (int i = 0; i < chunks; i++)
            CreatePart(PrimitiveType.Sphere, root.transform, "Chunk",
                new Vector3(RandomRange(-0.3f, 0.3f), RandomRange(0.1f, 0.4f), RandomRange(-0.3f, 0.3f)),
                new Vector3(RandomRange(-20f, 20f), _rng.Next(0, 360), RandomRange(-20f, 20f)),
                new Vector3(RandomRange(0.5f, 1.1f), RandomRange(0.35f, 0.7f), RandomRange(0.5f, 1.1f)), CavernRockMat(), collider: i == 0);
    }

    private void BuildStalagmite(Vector3 pos)
    {
        var root = new GameObject("Stalagmite");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = pos;
        float h = RandomRange(1.2f, 2.8f);
        const int seg = 4;
        for (int i = 0; i < seg; i++)
        {
            float t = i / (float)(seg - 1);
            float r = Mathf.Lerp(0.45f, 0.06f, t);
            CreatePart(PrimitiveType.Sphere, root.transform, "Seg",
                new Vector3(RandomRange(-0.05f, 0.05f), h * t, RandomRange(-0.05f, 0.05f)),
                Vector3.zero, new Vector3(r * 2f, Mathf.Lerp(0.5f, 0.3f, t), r * 2f), CavernRockMat(), collider: i == 0);
        }
    }

    private void BuildStalactite(Vector3 floorPos, float ceilingY)
    {
        var root = new GameObject("Stalactite");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = new Vector3(floorPos.x, ceilingY, floorPos.z);
        float h = RandomRange(0.8f, 2f);
        const int seg = 4;
        for (int i = 0; i < seg; i++)
        {
            float t = i / (float)(seg - 1);
            float r = Mathf.Lerp(0.4f, 0.05f, t);
            CreatePart(PrimitiveType.Sphere, root.transform, "Seg",
                new Vector3(RandomRange(-0.05f, 0.05f), -h * t, RandomRange(-0.05f, 0.05f)),
                Vector3.zero, new Vector3(r * 2f, Mathf.Lerp(0.5f, 0.3f, t), r * 2f), CavernRockMat());
        }
    }

    private void BuildCrystalCluster(Vector3 pos)
    {
        var root = new GameObject("CrystalCluster");
        root.transform.SetParent(_decorRoot, false);
        root.transform.localPosition = pos;
        root.transform.localRotation = Quaternion.Euler(0, _rng.Next(0, 360), 0);
        int n = _rng.Next(3, 6);
        for (int i = 0; i < n; i++)
            CreatePart(PrimitiveType.Cube, root.transform, "Crystal",
                new Vector3(RandomRange(-0.25f, 0.25f), RandomRange(0.2f, 0.6f), RandomRange(-0.25f, 0.25f)),
                new Vector3(RandomRange(-25f, 25f), _rng.Next(0, 360), RandomRange(-25f, 25f)),
                new Vector3(RandomRange(0.08f, 0.18f), RandomRange(0.4f, 0.9f), RandomRange(0.08f, 0.18f)), CrystalMat());
        if (Roll(0.7f))
            SpawnLight(pos + Vector3.up * 0.6f, PickFrom(CrystalColors), new Vector2(0.8f, 1.6f), 6f, flicker: false, heroEligible: false);
    }

    // ---------------------------------------------------------------------
    // Lighting helpers
    // ---------------------------------------------------------------------

    private Light SpawnLight(Vector3 localPos, Color color, Vector2 intensity, float range, bool flicker, bool heroEligible)
    {
        if (_spawnedLights >= MaxRealtimeLights) return null;

        var go = new GameObject("Light");
        go.transform.SetParent(_lightsRoot, false);
        go.transform.localPosition = localPos;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = RandomRange(intensity.x, intensity.y);
        light.range = range;

        if (AllowHeroShadows && heroEligible && _spawnedHeroShadows < HeroShadowLightCount)
        {
            light.shadows = LightShadows.Soft;
            _spawnedHeroShadows++;
        }
        else light.shadows = LightShadows.None;

        if (flicker) go.AddComponent<LightFlicker>();
        _spawnedLights++;
        return light;
    }

    private void ApplyFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogDensity = FogDensity;
    }

    // Drive scene ambient (so the dungeon is actually visible) and add a dim directional "moon"
    // for the exterior, open-sky sections. Applied at Generate so it overrides whatever the scene
    // had baked into its lighting settings.
    private void ApplyAmbientAndSky()
    {
        if (OverrideAmbient)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor * AmbientIntensity;
            RenderSettings.ambientIntensity = AmbientIntensity; // used when a skybox is the source
        }

        if (AddMoonlight)
        {
            var go = new GameObject("Moonlight");
            go.transform.SetParent(_lightsRoot, false);
            go.transform.localEulerAngles = MoonlightEuler;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = MoonlightColor;
            l.intensity = MoonlightIntensity;
            l.shadows = AllowHeroShadows ? LightShadows.Soft : LightShadows.None;
#if UNITY_EDITOR
            l.lightmapBakeType = LightmapBakeType.Mixed; // bakes outdoor shadows if the user bakes GI
#endif
        }
    }

    // Editor-only: flag generated geometry as static so occlusion culling and GI baking can use it.
    // (Frustum culling needs no setup — Unity always frustum-culls renderers per camera.)
    private void MarkStatic(GameObject go)
    {
#if UNITY_EDITOR
        if (!MarkStaticGeometry || Application.isPlaying) return;
        GameObjectUtility.SetStaticEditorFlags(go,
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);
#endif
    }

#if UNITY_EDITOR
    [ContextMenu("Bake Lighting + Occlusion (Editor)")]
    private void BakeLightingAndOcclusion()
    {
        ComputeOcclusion();
        BakeLighting();
    }

    private void ComputeOcclusion()
    {
        Debug.Log("[MultiSectionDungeonGenerator] Computing occlusion culling…", this);
        StaticOcclusionCulling.Compute();
    }

    private void BakeLighting()
    {
        // Procedurally built floor/ceiling meshes need a 2nd UV set to receive lightmaps. Skip
        // very large meshes — unwrapping them is prohibitively slow and rarely worth it here.
        foreach (Mesh m in _builtMeshes)
        {
            if (m == null || m.vertexCount > 60000) continue;
            if (m.uv2 != null && m.uv2.Length > 0) continue;
            try { Unwrapping.GenerateSecondaryUVSet(m); } catch { /* non-fatal */ }
        }
        Debug.Log("[MultiSectionDungeonGenerator] Starting async lightmap bake (torches stay realtime)…", this);
        Lightmapping.BakeAsync();
    }
#endif

    // ---------------------------------------------------------------------
    // Primitive + material helpers
    // ---------------------------------------------------------------------

    private GameObject CreatePart(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat, bool collider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediateSafe(col);
        }
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localEulerAngles = localEuler;
        go.transform.localScale = localScale;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mat != null) mr.sharedMaterial = mat;
        return go;
    }

    private Vector3 TileLocal(int x, int y) => new(x + 0.5f, 0f, y + 0.5f);

    private void EnsureMaterials()
    {
        _mGothicFloor ??= MakeMatte(new Color(0.22f, 0.21f, 0.23f));
        _mGothicWall ??= MakeMatte(new Color(0.27f, 0.26f, 0.30f));
        _mGothicCeiling ??= MakeMatte(new Color(0.15f, 0.14f, 0.17f));
        _mGraveGround ??= MakeMatte(new Color(0.17f, 0.20f, 0.16f));
        _mGraveStone ??= MakeMatte(new Color(0.42f, 0.44f, 0.43f));
        _mHellFloor ??= MakeMatte(new Color(0.13f, 0.07f, 0.07f));
        _mHellPillar ??= MakeMatte(new Color(0.22f, 0.14f, 0.13f));
        _mLava ??= MakeEmissive(new Color(0.4f, 0.1f, 0.02f), new Color(1f, 0.35f, 0.06f) * 5f);
        _mCatacombFloor ??= MakeMatte(new Color(0.20f, 0.19f, 0.17f));
        _mCatacombWall ??= MakeMatte(new Color(0.30f, 0.28f, 0.24f));
        _mBone ??= MakeMatte(new Color(0.82f, 0.78f, 0.66f));
        _mCavernFloor ??= MakeMatte(new Color(0.16f, 0.15f, 0.16f));
        _mCavernRock ??= MakeMatte(new Color(0.24f, 0.23f, 0.26f));
        _mCrystal ??= MakeEmissive(new Color(0.2f, 0.4f, 0.5f), new Color(0.3f, 0.7f, 1f) * 3.5f);
        _mWood ??= MakeMatte(new Color(0.18f, 0.12f, 0.08f));
        _mMetal ??= MakeMatte(new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.35f);
        _mFlame ??= MakeEmissive(new Color(0.6f, 0.2f, 0.05f), new Color(1f, 0.5f, 0.12f) * 4f);
    }

    private Material GothicFloorMat() => GothicFloorMaterial != null ? GothicFloorMaterial : _mGothicFloor;
    private Material GothicWallMat() => GothicWallMaterial != null ? GothicWallMaterial : _mGothicWall;
    private Material GothicCeilingMat() => GothicCeilingMaterial != null ? GothicCeilingMaterial : _mGothicCeiling;
    private Material GraveGroundMat() => GraveyardGroundMaterial != null ? GraveyardGroundMaterial : _mGraveGround;
    private Material GraveStoneMat() => GraveyardStoneMaterial != null ? GraveyardStoneMaterial : _mGraveStone;
    private Material HellFloorMat() => HellFloorMaterial != null ? HellFloorMaterial : _mHellFloor;
    private Material HellPillarMat() => HellPillarMaterial != null ? HellPillarMaterial : _mHellPillar;
    private Material LavaMat() => EmissiveLavaMaterial != null ? EmissiveLavaMaterial : _mLava;
    private Material CatacombFloorMat() => CatacombFloorMaterial != null ? CatacombFloorMaterial : _mCatacombFloor;
    private Material CatacombWallMat() => CatacombWallMaterial != null ? CatacombWallMaterial : _mCatacombWall;
    private Material BoneMat() => _mBone;
    private Material CavernFloorMat() => CavernFloorMaterial != null ? CavernFloorMaterial : _mCavernFloor;
    private Material CavernRockMat() => CavernRockMaterial != null ? CavernRockMaterial : _mCavernRock;
    private Material CrystalMat() => EmissiveCrystalMaterial != null ? EmissiveCrystalMaterial : _mCrystal;
    private Material WoodMat() => _mWood;
    private Material MetalMat() => _mMetal;
    private Material GlassMat() => _mGothicCeiling;
    private Material FlameMat() => _mFlame;

    private Material MakeMatte(Color albedo, float metallic = 0f, float smoothness = 0f)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { color = albedo };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", albedo);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
        if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
        return m;
    }

    private Material MakeEmissive(Color albedo, Color emissionHdr)
    {
        Material m = MakeMatte(albedo);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emissionHdr);
        return m;
    }

    // ---------------------------------------------------------------------
    // Small utilities
    // ---------------------------------------------------------------------

    private Color PickFrom(Color[] colors) => (colors == null || colors.Length == 0)
        ? Color.white : colors[_rng.Next(0, colors.Length)];

    private bool Roll(float chance) => _rng.NextDouble() <= Mathf.Clamp01(chance);

    private float RandomRange(float min, float max)
    {
        if (max < min) (min, max) = (max, min);
        return Mathf.Lerp(min, max, (float)_rng.NextDouble());
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static void DestroyImmediateSafe(UnityEngine.Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private void OnDrawGizmosSelected()
    {
        if (!DebugDrawSectionBounds || _sections == null) return;
        foreach (SectionInfo s in _sections)
        {
            Gizmos.color = s.Type switch
            {
                SectionType.GothicRuins => new Color(0.5f, 0.5f, 1f, 1f),
                SectionType.Graveyard => new Color(0.4f, 1f, 0.6f, 1f),
                SectionType.HellPillars => new Color(1f, 0.3f, 0.2f, 1f),
                SectionType.Catacombs => new Color(0.9f, 0.85f, 0.5f, 1f),
                SectionType.Cavern => new Color(0.4f, 0.8f, 1f, 1f),
                _ => Color.gray
            };
            Vector3 c = transform.TransformPoint(new Vector3(s.Bounds.center.x, 0.1f, s.Bounds.center.y));
            Vector3 size = new(s.Bounds.width, 0.2f, s.Bounds.height);
            Gizmos.DrawWireCube(c, size);
        }
    }
}
