using System.Collections.Generic;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private int Seed;
    [SerializeField] private int MapWidth  = 80;
    [SerializeField] private int MapHeight = 80;
    [SerializeField] private int MinRoomSize = 8;
    [SerializeField] private int MaxRoomSize = 20;
    [SerializeField] private int MaxDepth = 5;
    [SerializeField] private int CorridorWidth = 3;
    [Tooltip("A* cost penalty for changing direction — higher = straighter corridors (0 = free to wind).")]
    [SerializeField] private int _corridorTurnPenalty = 4;
    [Tooltip("A* step cost for reusing an already-carved floor tile vs 10 for fresh rock — lower = corridors merge more aggressively.")]
    [SerializeField] private int _corridorReuseCost = 2;
    [SerializeField] private Material FloorMaterial;
    [SerializeField] private Material WallMaterial;
    [SerializeField] private float WallHeight    = 3f;
    [SerializeField] private float WallThickness = 0.5f;

    // ── Decoration config (assign model prefabs here — no Resources.Load, no hardcoded paths) ──
    [Header("Decoration")]
    [SerializeField] private bool  _decorate = true;
    [Tooltip("Optional arch / doorway prefabs placed where corridors meet rooms.")]
    [SerializeField] private PropGroup _archways = new();
    [Tooltip("Large furniture for Storage rooms (crates, barrels, chests, vases...).")]
    [SerializeField] private PropGroup _storageFurniture = new();
    [Tooltip("Large furniture for Library/Study rooms (bookcases, desks, chairs, tables...).")]
    [SerializeField] private PropGroup _libraryFurniture = new();
    [Tooltip("Large furniture for Bedroom rooms (beds, nightstands, closets, sofas...).")]
    [SerializeField] private PropGroup _bedroomFurniture = new();
    [Tooltip("Large furniture for Hall rooms (columns, big tables, statues...).")]
    [SerializeField] private PropGroup _hallFurniture = new();
    [Tooltip("Ritual dressing (skulls, woodfire, candelabra, spikes, pedestals...).")]
    [SerializeField] private PropGroup _ritualProps = new();
    [Tooltip("Small clutter placed along walls and corners (barrels, pots, buckets...).")]
    [SerializeField] private PropGroup _wallClutter = new();
    [Tooltip("Cobwebs placed high in corners.")]
    [SerializeField] private PropGroup _cornerCobwebs = new();
    [Tooltip("Wall lights mounted on walls / near doors (torches, candles).")]
    [SerializeField] private PropGroup _wallLights = new();
    [Tooltip("Rare horror personality props placed sparsely (skull, spikes, trapdoor...).")]
    [SerializeField] private PropGroup _rareHorror = new();

    [Header("Density")]
    [SerializeField, Range(0f, 0.12f)] private float _featureDensity = 0.02f; // big furniture per room tile
    [SerializeField, Range(0f, 0.12f)] private float _clutterDensity = 0.03f; // wall clutter per room tile
    [SerializeField, Range(0f, 0.06f)] private float _cobwebDensity  = 0.012f;
    [SerializeField, Range(0f, 0.06f)] private float _lightDensity   = 0.012f;
    [SerializeField] private int _maxLightsPerRoom = 3;
    [SerializeField, Range(0f, 1f)] private float _rareHorrorChancePerRoom = 0.15f;
    [SerializeField] private bool _placeArches = true;
    [SerializeField] private bool _generatePropColliders = true;

    [Header("Light budget (keep PS2-cheap)")]
    [SerializeField] private int   _maxRealtimeLights = 24;
    [SerializeField] private int   _maxShadowLights   = 4;
    [SerializeField] private bool  _attachTorchLight  = true;
    [SerializeField] private Color _torchLightColor   = new(1f, 0.62f, 0.28f);
    [SerializeField] private float _torchLightIntensity = 3f;
    [SerializeField] private float _torchLightRange     = 6f;
    [SerializeField] private float _torchLightLocalHeight = 1.6f;

    [Header("Placement tuning")]
    [Tooltip("How far wall fixtures are pushed toward the wall, in tiles.")]
    [SerializeField, Range(0f, 0.5f)] private float _wallFixtureInset = 0.45f;
    [Tooltip("Height (world units) at which corner cobwebs are placed.")]
    [SerializeField] private float _cobwebHeight = 2.4f;
    [Tooltip("Minimum distance between placed arches — a wide opening gets one arch, not a stack.")]
    [SerializeField] private float _archSpacing = 2.88f;
    [Tooltip("How far the arch is pushed along the passage so it pops off the wall face.")]
    [SerializeField] private float _archForwardOffset = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool _debugDrawRooms = false;

    private const string RootName = "_dungeonRoot";
    private Transform _dungeonRoot;
    private Transform _propsRoot;
    private System.Random _rng;

    // Walkable tile grid — the single source of truth. Geometry is derived from it.
    private bool[,] _floor;
    private Material _runtimeFloorMat;
    private Material _runtimeWallMat;

    // Decoration state (rebuilt each Generate).
    private readonly List<RoomInfo> _rooms = new();
    private Cell[,] _cell;
    private int[,]  _roomIndex;
    private readonly List<Doorway> _doorways = new();
    private readonly HashSet<Vector2Int> _fixtureUsed = new();
    private int _lightsSpawned;
    private int _shadowLightsSpawned;

    // ── Data types ──────────────────────────────────────────────────────────────

    public enum RoomType { Empty, Storage, Bedroom, Library, Ritual, Extraction, Hall }

    [System.Serializable]
    public class PropEntry
    {
        public GameObject Prefab;
        [Min(0f)] public float Weight = 1f;
        [Tooltip("Local rotation correction for models whose forward axis is off.")]
        public Vector3 RotationOffset = Vector3.zero;
        public Vector2 ScaleRange = Vector2.one;
        public float YOffset = 0f;
    }

    [System.Serializable]
    public class PropGroup
    {
        public PropEntry[] Entries = System.Array.Empty<PropEntry>();

        public bool IsValid
        {
            get
            {
                if (Entries == null) return false;
                foreach (var e in Entries) if (e != null && e.Prefab != null) return true;
                return false;
            }
        }

        public PropEntry Pick(System.Random rng)
        {
            if (Entries == null || Entries.Length == 0) return null;
            float total = 0f;
            foreach (var e in Entries)
                if (e != null && e.Prefab != null) total += Mathf.Max(0f, e.Weight);
            if (total <= 0f) return null;

            double roll = rng.NextDouble() * total;
            foreach (var e in Entries)
            {
                if (e == null || e.Prefab == null) continue;
                roll -= Mathf.Max(0f, e.Weight);
                if (roll <= 0) return e;
            }
            for (int i = Entries.Length - 1; i >= 0; i--)
                if (Entries[i] != null && Entries[i].Prefab != null) return Entries[i];
            return null;
        }
    }

    private struct RectInt2D
    {
        public int X, Y, W, H;
        public RectInt2D(int x, int y, int w, int h) { X=x; Y=y; W=w; H=h; }
        public int CenterX => X + W / 2;
        public int CenterY => Y + H / 2;
        public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
    }

    private class RoomInfo
    {
        public RectInt2D Rect;
        public int CenterX, CenterY;
        public int Area;
        public RoomType Type = RoomType.Empty;
        public RoomInfo(RectInt2D r) { Rect = r; CenterX = r.CenterX; CenterY = r.CenterY; Area = r.W * r.H; }
    }

    private struct Doorway { public Vector2Int Tile; public Vector2Int Dir; public int Room; }

    // Per-tile occupancy used only by the dressing pass; never touches the floor grid.
    private enum Cell : byte { Wall, Corridor, RoomFree, Reserved, Filled }

    private class BSPNode
    {
        public RectInt2D Bounds;
        public BSPNode   Left, Right;
        public RectInt2D Room;
        public bool      HasRoom;
        public BSPNode(RectInt2D b) { Bounds = b; }
        public bool IsLeaf => Left == null && Right == null;
    }

    // ── Entry points ──────────────────────────────────────────────────────────

    [ContextMenu("Generate")]
    public void Generate()
    {
        Clear();

        int seed = Seed != 0 ? Seed : System.Environment.TickCount;
        _rng = new System.Random(seed);

        var rootGo = new GameObject(RootName);
        rootGo.transform.SetParent(transform, false);
        _dungeonRoot = rootGo.transform;

        var decorGo = new GameObject("Decor");
        decorGo.transform.SetParent(_dungeonRoot, false);
        _propsRoot = decorGo.transform;

        _floor = new bool[MapWidth, MapHeight];
        _rooms.Clear();

        var root = new BSPNode(new RectInt2D(0, 0, MapWidth, MapHeight));
        Split(root, 0);

        var leaves = new List<BSPNode>();
        CollectLeaves(root, leaves);
        foreach (var leaf in leaves)
        {
            CreateRoom(leaf);
            if (leaf.HasRoom)
            {
                CarveRoom(leaf.Room);
                _rooms.Add(new RoomInfo(leaf.Room)); // capture room metadata
            }
        }

        CarveCorridors(root);

        BuildFloorMeshes();
        BuildWallMeshes();

        if (_decorate) DecorateDungeon();
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        var existing = transform.Find(RootName);
        if (existing != null)
            DestroyImmediateSafe(existing.gameObject);
        _dungeonRoot     = null;
        _propsRoot       = null;
        _runtimeFloorMat = null;
        _runtimeWallMat  = null;
        _rooms.Clear();
        _doorways.Clear();
        _fixtureUsed.Clear();
    }

#if UNITY_EDITOR
    // One-shot editor setup: fills the prop groups with the Ultimate Modular Ruins models via
    // AssetDatabase (which resolves each FBX's correct root GameObject — hand-written fileIDs
    // are unreliable for models). Run it once from the component's context menu, then Generate.
    [ContextMenu("Assign Ruins Models (Editor Setup)")]
    private void AssignRuinsModels()
    {
        const string R = "Assets/3DModels/Ultimate Modular Ruins Pack - Aug 2021/FBX/";
        // Furniture & pots import lying on their back — stand them up with -90° on X.
        const float Up = -90f;
        _archways         = MakeGroup(R, 0f,  ("Wall_ArchGothic", 1f), ("Wall_ArchRound", 1f));
        _storageFurniture = MakeGroup(R, Up,  ("Crate", 1f), ("Barrel", 1f), ("Pot1", 0.7f), ("Pot2", 0.7f), ("Pot3", 0.7f));
        _libraryFurniture = MakeGroup(R, Up,  ("Bookcase_Full", 1f), ("Bookcase_Empty", 1f));
        _ritualProps      = MakeGroup(R, 0f,  ("Skull", 1f), ("Candles_1", 1f), ("Candles_2", 1f), ("Trapdoor", 0.5f));
        _wallClutter      = MakeGroup(R, Up,  ("Pot1", 1f), ("Pot2", 1f), ("Pot3", 1f),
                                              ("Pot1_Broken", 0.6f), ("Pot2_Broken", 0.6f), ("Pot3_Broken", 0.6f),
                                              ("Barrel", 0.8f), ("Crate", 0.8f));
        _wallLights       = MakeGroup(R, 0f,  ("Torch", 1f), ("Candles_1", 0.5f), ("Candles_2", 0.5f));
        _rareHorror       = MakeGroup(R, 0f,  ("Skull", 1f), ("Trapdoor", 1f));

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[DungeonGenerator] Assigned Ultimate Modular Ruins models to prop groups.");
    }

    private static PropGroup MakeGroup(string folder, float rotationX, params (string name, float weight)[] items)
    {
        var list = new List<PropEntry>();
        foreach (var (name, weight) in items)
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(folder + name + ".fbx");
            if (go == null) { Debug.LogWarning($"[DungeonGenerator] Missing model: {folder}{name}.fbx"); continue; }
            list.Add(new PropEntry
            {
                Prefab = go,
                Weight = weight,
                RotationOffset = new Vector3(rotationX, 0f, 0f),
                ScaleRange = Vector2.one,
            });
        }
        return new PropGroup { Entries = list.ToArray() };
    }
#endif

    // ── BSP split ─────────────────────────────────────────────────────────────

    private void Split(BSPNode node, int depth)
    {
        if (depth >= MaxDepth) return;

        bool canH = node.Bounds.H >= MinRoomSize * 2;
        bool canV = node.Bounds.W >= MinRoomSize * 2;
        if (!canH && !canV) return;

        bool horizontal;
        if (canH && canV)
        {
            float r = (float)node.Bounds.W / node.Bounds.H;
            horizontal = r >= 1.25f ? false : r <= 0.8f ? true : _rng.Next(0, 2) == 0;
        }
        else horizontal = canH;

        if (horizontal)
        {
            int s = _rng.Next(MinRoomSize, node.Bounds.H - MinRoomSize + 1);
            node.Left  = new BSPNode(new RectInt2D(node.Bounds.X, node.Bounds.Y,     node.Bounds.W, s));
            node.Right = new BSPNode(new RectInt2D(node.Bounds.X, node.Bounds.Y + s, node.Bounds.W, node.Bounds.H - s));
        }
        else
        {
            int s = _rng.Next(MinRoomSize, node.Bounds.W - MinRoomSize + 1);
            node.Left  = new BSPNode(new RectInt2D(node.Bounds.X,     node.Bounds.Y, s,                 node.Bounds.H));
            node.Right = new BSPNode(new RectInt2D(node.Bounds.X + s, node.Bounds.Y, node.Bounds.W - s, node.Bounds.H));
        }

        Split(node.Left,  depth + 1);
        Split(node.Right, depth + 1);
    }

    private void CollectLeaves(BSPNode node, List<BSPNode> leaves)
    {
        if (node == null) return;
        if (node.IsLeaf) { leaves.Add(node); return; }
        CollectLeaves(node.Left,  leaves);
        CollectLeaves(node.Right, leaves);
    }

    private void CreateRoom(BSPNode leaf)
    {
        int maxW = Mathf.Min(MaxRoomSize, leaf.Bounds.W - 2);
        int maxH = Mathf.Min(MaxRoomSize, leaf.Bounds.H - 2);
        if (maxW < MinRoomSize || maxH < MinRoomSize) return;

        int w = _rng.Next(MinRoomSize, maxW + 1);
        int h = _rng.Next(MinRoomSize, maxH + 1);
        int x = leaf.Bounds.X + _rng.Next(1, leaf.Bounds.W - w);
        int y = leaf.Bounds.Y + _rng.Next(1, leaf.Bounds.H - h);

        leaf.Room    = new RectInt2D(x, y, w, h);
        leaf.HasRoom = true;
    }

    // ── Grid carving ──────────────────────────────────────────────────────────

    private void CarveRoom(RectInt2D r)
    {
        for (int x = r.X; x < r.X + r.W; x++)
            for (int y = r.Y; y < r.Y + r.H; y++)
                SetFloor(x, y);
    }

    private void CarveCorridors(BSPNode node)
    {
        if (node == null || node.IsLeaf) return;
        CarveCorridors(node.Left);
        CarveCorridors(node.Right);

        if (TryGetRoomCenter(node.Left,  out int ax, out int ay) &&
            TryGetRoomCenter(node.Right, out int bx, out int by))
        {
            // A* through the grid: reusing already-carved floor and going straight are cheap,
            // so corridors merge into existing space and avoid redundant parallel halls instead
            // of always cutting a fresh right-angle. Crossing a room just merges tiles; walls are
            // derived afterwards, so openings still appear automatically.
            var path = FindCorridorPath(ax, ay, bx, by);
            if (path != null)
            {
                foreach (var t in path) CarveCorridorTile(t.x, t.y);
            }
            // Fallback: plain L-shape if the search somehow fails (shouldn't on an open grid).
            else if (_rng.Next(0, 2) == 0)
            {
                CarveHorizontal(ax, bx, ay);
                CarveVertical(ay, by, bx);
            }
            else
            {
                CarveVertical(ay, by, ax);
                CarveHorizontal(ax, bx, by);
            }
        }
    }

    // Deterministic A* between two room centers over the walkable grid. No RNG, so output stays
    // seed-reproducible. Cost model: fresh rock = CorridorStepFresh, already-floor = _corridorReuseCost
    // (encourages merging), plus _corridorTurnPenalty whenever the path changes direction (keeps it
    // from zig-zagging). The heuristic uses the cheapest possible step cost so it stays admissible.
    private const int CorridorStepFresh = 10;

    private List<Vector2Int> FindCorridorPath(int sx, int sy, int gx, int gy)
    {
        int w = MapWidth, h = MapHeight, n = w * h;
        int Index(int x, int y) => x * h + y;

        var g      = new int[n];
        var came   = new int[n];
        var dir    = new int[n];   // direction code (0..3) used to reach the tile; -1 = none
        var closed = new bool[n];
        for (int i = 0; i < n; i++) { g[i] = int.MaxValue; came[i] = -1; dir[i] = -1; }

        int hUnit = Mathf.Min(_corridorReuseCost, CorridorStepFresh);
        int Heur(int x, int y) => (Mathf.Abs(x - gx) + Mathf.Abs(y - gy)) * hUnit;

        int start = Index(sx, sy), goal = Index(gx, gy);
        g[start] = 0;

        var open = new MinHeap(n / 4 + 16);
        open.Push(start, Heur(sx, sy));

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

            int cx = cur / h, cy = cur % h;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d], ny = cy + dy[d];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                int ni = Index(nx, ny);
                if (closed[ni]) continue;

                int step = IsFloor(nx, ny) ? _corridorReuseCost : CorridorStepFresh;
                if (dir[cur] != -1 && dir[cur] != d) step += _corridorTurnPenalty;

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
            path.Add(new Vector2Int(c / h, c % h));
        return path;
    }

    // Carve a CorridorWidth × CorridorWidth block centred on a path tile, so corridors keep their
    // width through turns. Already-floor tiles are harmlessly re-set.
    private void CarveCorridorTile(int x, int y)
    {
        int lo = -(CorridorWidth - 1) / 2;
        int hi = CorridorWidth / 2;
        for (int ox = lo; ox <= hi; ox++)
            for (int oy = lo; oy <= hi; oy++)
                SetFloor(x + ox, y + oy);
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

    private bool TryGetRoomCenter(BSPNode node, out int cx, out int cy)
    {
        cx = cy = 0;
        if (node == null) return false;
        var list = new List<BSPNode>();
        CollectLeaves(node, list);
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].HasRoom) { cx = list[i].Room.CenterX; cy = list[i].Room.CenterY; return true; }
        return false;
    }

    private void CarveHorizontal(int xa, int xb, int y)
    {
        int lo = Mathf.Min(xa, xb), hi = Mathf.Max(xa, xb);
        for (int x = lo; x <= hi; x++)
            CarveBand(x, y, true);
    }

    private void CarveVertical(int ya, int yb, int x)
    {
        int lo = Mathf.Min(ya, yb), hi = Mathf.Max(ya, yb);
        for (int y = lo; y <= hi; y++)
            CarveBand(x, y, false);
    }

    // Carve CorridorWidth tiles perpendicular to the corridor direction, centred on (x,y).
    private void CarveBand(int x, int y, bool horizontal)
    {
        int lo = -(CorridorWidth - 1) / 2;
        int hi = CorridorWidth / 2;
        for (int o = lo; o <= hi; o++)
            SetFloor(horizontal ? x : x + o, horizontal ? y + o : y);
    }

    private void SetFloor(int x, int y)
    {
        if (x < 0 || x >= MapWidth || y < 0 || y >= MapHeight) return;
        _floor[x, y] = true;
    }

    private bool IsFloor(int x, int y)
    {
        if (x < 0 || x >= MapWidth || y < 0 || y >= MapHeight) return false;
        return _floor[x, y];
    }

    private bool InBounds(int x, int y) => x >= 0 && x < MapWidth && y >= 0 && y < MapHeight;

    // ── Geometry from grid ──────────────────────────────────────────────────────

    // Greedy horizontal merge: one floor quad per run of contiguous floor tiles in a row.
    private void BuildFloorMeshes()
    {
        for (int y = 0; y < MapHeight; y++)
        {
            int x = 0;
            while (x < MapWidth)
            {
                if (!_floor[x, y]) { x++; continue; }
                int xs = x;
                while (x < MapWidth && _floor[x, y]) x++;
                float len = x - xs;
                CreateFloorQuad(
                    new Vector3((xs + x) * 0.5f, 0f, y + 0.5f),
                    new Vector2(len, 1f),
                    "Floor");
            }
        }
    }

    // A wall edge exists between a floor tile and a non-floor neighbour. Collinear edges
    // are merged into runs so each wall is one box instead of one box per tile.
    private void BuildWallMeshes()
    {
        float wallY = WallHeight * 0.5f;

        // North (+Z) and South (-Z) edges run along X — merge per row.
        for (int y = 0; y < MapHeight; y++)
        {
            BuildEdgeRunsAlongX(y, +1, wallY); // north edge at z = y+1
            BuildEdgeRunsAlongX(y, -1, wallY); // south edge at z = y
        }

        // East (+X) and West (-X) edges run along Z — merge per column.
        for (int x = 0; x < MapWidth; x++)
        {
            BuildEdgeRunsAlongZ(x, +1, wallY); // east edge at x = x+1
            BuildEdgeRunsAlongZ(x, -1, wallY); // west edge at x = x
        }
    }

    private void BuildEdgeRunsAlongX(int y, int dz, float wallY)
    {
        int x = 0;
        while (x < MapWidth)
        {
            if (!(_floor[x, y] && !IsFloor(x, y + dz))) { x++; continue; }
            int xs = x;
            while (x < MapWidth && _floor[x, y] && !IsFloor(x, y + dz)) x++;
            float len   = x - xs;
            float zEdge = dz > 0 ? y + 1 : y;
            CreateWall(
                new Vector3((xs + x) * 0.5f, wallY, zEdge),
                new Vector3(len + WallThickness, WallHeight, WallThickness));
        }
    }

    private void BuildEdgeRunsAlongZ(int x, int dx, float wallY)
    {
        int y = 0;
        while (y < MapHeight)
        {
            if (!(_floor[x, y] && !IsFloor(x + dx, y))) { y++; continue; }
            int ys = y;
            while (y < MapHeight && _floor[x, y] && !IsFloor(x + dx, y)) y++;
            float len   = y - ys;
            float xEdge = dx > 0 ? x + 1 : x;
            CreateWall(
                new Vector3(xEdge, wallY, (ys + y) * 0.5f),
                new Vector3(WallThickness, WallHeight, len + WallThickness));
        }
    }

    // ── Decoration ───────────────────────────────────────────────────────────────

    private void DecorateDungeon()
    {
        if (_rooms.Count == 0) return;

        _fixtureUsed.Clear();
        _lightsSpawned = 0;
        _shadowLightsSpawned = 0;

        ClassifyTiles();
        AssignRoomTypes();
        ReserveSkeleton();

        if (_placeArches) PlaceArches();

        for (int i = 0; i < _rooms.Count; i++)
            DecorateRoom(i, _rooms[i]);
    }

    // Build the occupancy grid + room ownership + doorways from the floor grid & room rects.
    private void ClassifyTiles()
    {
        _cell = new Cell[MapWidth, MapHeight];
        _roomIndex = new int[MapWidth, MapHeight];

        for (int x = 0; x < MapWidth; x++)
            for (int y = 0; y < MapHeight; y++)
            {
                _roomIndex[x, y] = -1;
                _cell[x, y] = _floor[x, y] ? Cell.Corridor : Cell.Wall;
            }

        for (int i = 0; i < _rooms.Count; i++)
        {
            var r = _rooms[i].Rect;
            for (int x = r.X; x < r.X + r.W; x++)
                for (int y = r.Y; y < r.Y + r.H; y++)
                    if (IsFloor(x, y))
                    {
                        _cell[x, y] = Cell.RoomFree;
                        _roomIndex[x, y] = i;
                    }
        }

        // Doorways: a corridor tile adjacent to a room tile. Record the room-side tile.
        _doorways.Clear();
        var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        for (int x = 0; x < MapWidth; x++)
            for (int y = 0; y < MapHeight; y++)
            {
                if (_cell[x, y] != Cell.Corridor) continue;
                foreach (var d in dirs)
                {
                    int nx = x + d.x, ny = y + d.y;
                    if (!InBounds(nx, ny) || _cell[nx, ny] != Cell.RoomFree) continue;
                    _doorways.Add(new Doorway
                    {
                        Tile = new Vector2Int(nx, ny),
                        Dir  = new Vector2Int(x - nx, y - ny), // room tile -> corridor
                        Room = _roomIndex[nx, ny]
                    });
                }
            }
    }

    private void AssignRoomTypes()
    {
        // Largest room becomes the Extraction room.
        int biggest = 0;
        for (int i = 1; i < _rooms.Count; i++)
            if (_rooms[i].Area > _rooms[biggest].Area) biggest = i;

        for (int i = 0; i < _rooms.Count; i++)
        {
            if (i == biggest) { _rooms[i].Type = RoomType.Extraction; continue; }
            _rooms[i].Type = PickRoomType(_rooms[i].Area);
        }
    }

    private RoomType PickRoomType(int area)
    {
        // Size-biased: big rooms feel like halls/libraries/rituals; small rooms are storage/empty.
        RoomType[] pool = area >= 220 ? new[] { RoomType.Hall, RoomType.Library, RoomType.Ritual }
                        : area >= 120 ? new[] { RoomType.Bedroom, RoomType.Library, RoomType.Storage }
                                      : new[] { RoomType.Storage, RoomType.Empty };
        return pool[_rng.Next(pool.Length)];
    }

    // Reserve a walkable skeleton so dressing can never seal the player in: a clearance at each
    // room centre plus a straight route from every doorway to that centre. Reserved tiles stay
    // floor (walkable) but are off-limits to floor props.
    private void ReserveSkeleton()
    {
        for (int i = 0; i < _rooms.Count; i++)
        {
            var room = _rooms[i];
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    ReserveTile(room.CenterX + dx, room.CenterY + dy);
        }

        foreach (var door in _doorways)
        {
            var room = _rooms[door.Room];
            ReserveRoute(door.Tile.x, door.Tile.y, room.CenterX, room.CenterY);
        }
    }

    private void ReserveTile(int x, int y)
    {
        if (!InBounds(x, y)) return;
        if (_cell[x, y] == Cell.RoomFree) _cell[x, y] = Cell.Reserved;
    }

    private void ReserveRoute(int fromX, int fromY, int toX, int toY)
    {
        int x = fromX, y = fromY;
        ReserveTile(x, y);
        while (x != toX) { x += x < toX ? 1 : -1; ReserveTile(x, y); }
        while (y != toY) { y += y < toY ? 1 : -1; ReserveTile(x, y); }
    }

    private void PlaceArches()
    {
        if (!_archways.IsValid) return;
        var done = new HashSet<Vector2Int>();
        var placed = new List<Vector2>(); // XZ of placed arches, for spacing
        foreach (var door in _doorways)
        {
            if (!done.Add(door.Tile)) continue;

            Vector2 dir = new(door.Dir.x, door.Dir.y);
            // Sit at the room/corridor threshold, then push forward so the arch pops off the wall.
            float px = door.Tile.x + 0.5f + dir.x * (0.5f + _archForwardOffset);
            float pz = door.Tile.y + 0.5f + dir.y * (0.5f + _archForwardOffset);
            var flat = new Vector2(px, pz);

            bool tooClose = false;
            foreach (var p in placed)
                if ((p - flat).sqrMagnitude < _archSpacing * _archSpacing) { tooClose = true; break; }
            if (tooClose) continue;

            var e = _archways.Pick(_rng);
            if (e == null) continue;

            SpawnPropAt(e, new Vector3(px, e.YOffset, pz), YawFromDir(dir), addCollider: false);
            placed.Add(flat);
            _fixtureUsed.Add(door.Tile);
        }
    }

    private void DecorateRoom(int index, RoomInfo room)
    {
        // Gather candidate tiles owned by this room.
        var free = new List<Vector2Int>();
        var wallTiles = new List<Vector2Int>();
        var cornerTiles = new List<Vector2Int>();
        var r = room.Rect;
        for (int x = r.X; x < r.X + r.W; x++)
            for (int y = r.Y; y < r.Y + r.H; y++)
            {
                if (!IsFloor(x, y) || _roomIndex[x, y] != index) continue;
                var t = new Vector2Int(x, y);
                if (_cell[x, y] == Cell.RoomFree) free.Add(t);
                if (IsWallAdjacent(x, y)) wallTiles.Add(t);
                if (IsCorner(x, y))       cornerTiles.Add(t);
            }

        Shuffle(free); Shuffle(wallTiles); Shuffle(cornerTiles);

        // Furniture is biased toward walls: list wall-adjacent free tiles first.
        var furnitureOrder = new List<Vector2Int>();
        foreach (var t in free) if (IsWallAdjacent(t.x, t.y)) furnitureOrder.Add(t);
        foreach (var t in free) if (!IsWallAdjacent(t.x, t.y)) furnitureOrder.Add(t);

        int area = room.Area;

        // 1) Feature furniture by room type (rooms only — never corridors).
        var feature = FeatureGroupFor(room.Type);
        if (feature != null)
            PlaceFurniture(furnitureOrder, feature, Mathf.RoundToInt(area * _featureDensity), addCollider: true);

        // 2) Clutter along walls / corners.
        if (room.Type != RoomType.Extraction)
            PlaceFurniture(furnitureOrder, _wallClutter, Mathf.RoundToInt(area * _clutterDensity), addCollider: true);

        // 3) Wall lights (budget-capped, mostly shadowless).
        int lights = Mathf.Clamp(Mathf.RoundToInt(area * _lightDensity), 0, _maxLightsPerRoom);
        PlaceWallLights(wallTiles, lights);

        // 4) Corner cobwebs, placed high.
        PlaceWallFixtures(cornerTiles, _cornerCobwebs, Mathf.RoundToInt(area * _cobwebDensity), _cobwebHeight);

        // 5) Rare horror personality.
        if (_rareHorror.IsValid && _rng.NextDouble() < _rareHorrorChancePerRoom)
            PlaceFurniture(furnitureOrder, _rareHorror, 1, addCollider: false);
    }

    private PropGroup FeatureGroupFor(RoomType type) => type switch
    {
        RoomType.Storage => _storageFurniture.IsValid ? _storageFurniture : null,
        RoomType.Library => _libraryFurniture.IsValid ? _libraryFurniture : null,
        RoomType.Bedroom => _bedroomFurniture.IsValid ? _bedroomFurniture : null,
        RoomType.Hall    => _hallFurniture.IsValid    ? _hallFurniture    : null,
        RoomType.Ritual  => _ritualProps.IsValid      ? _ritualProps      : null,
        _ => null, // Empty & Extraction stay clear
    };

    // ── Placement primitives ──────────────────────────────────────────────────────

    private void PlaceFurniture(List<Vector2Int> order, PropGroup group, int count, bool addCollider)
    {
        if (group == null || !group.IsValid || count <= 0) return;
        foreach (var t in order)
        {
            if (count <= 0) break;
            if (_cell[t.x, t.y] != Cell.RoomFree) continue; // skip reserved/corridor/filled
            var e = group.Pick(_rng);
            if (e == null) continue;

            float yaw = TryWallInward(t.x, t.y, out var inward) ? YawFromDir(inward) : (float)(_rng.NextDouble() * 360.0);
            Vector3 pos = new(t.x + 0.5f, e.YOffset, t.y + 0.5f);
            SpawnPropAt(e, pos, yaw, addCollider);
            _cell[t.x, t.y] = Cell.Filled;
            count--;
        }
    }

    private void PlaceWallLights(List<Vector2Int> wallTiles, int count)
    {
        if (!_wallLights.IsValid || count <= 0) return;
        foreach (var t in wallTiles)
        {
            if (count <= 0) break;
            if (_fixtureUsed.Contains(t) || _cell[t.x, t.y] == Cell.Filled) continue;
            if (!TryWallInward(t.x, t.y, out var inward)) continue;
            var e = _wallLights.Pick(_rng);
            if (e == null) continue;

            Vector2 outward = -inward;
            Vector3 pos = new(t.x + 0.5f + outward.x * _wallFixtureInset, e.YOffset, t.y + 0.5f + outward.y * _wallFixtureInset);
            var go = SpawnPropAt(e, pos, YawFromDir(inward), addCollider: false);
            ConfigureLights(go);
            _fixtureUsed.Add(t);
            count--;
        }
    }

    private void PlaceWallFixtures(List<Vector2Int> tiles, PropGroup group, int count, float extraHeight)
    {
        if (group == null || !group.IsValid || count <= 0) return;
        foreach (var t in tiles)
        {
            if (count <= 0) break;
            if (_fixtureUsed.Contains(t) || _cell[t.x, t.y] == Cell.Filled) continue;
            if (!TryWallInward(t.x, t.y, out var inward)) continue;
            var e = group.Pick(_rng);
            if (e == null) continue;

            Vector2 outward = -inward;
            Vector3 pos = new(t.x + 0.5f + outward.x * _wallFixtureInset, e.YOffset + extraHeight, t.y + 0.5f + outward.y * _wallFixtureInset);
            SpawnPropAt(e, pos, YawFromDir(inward), addCollider: false);
            _fixtureUsed.Add(t);
            count--;
        }
    }

    private GameObject SpawnPropAt(PropEntry e, Vector3 pos, float yaw, bool addCollider)
    {
        if (e == null || e.Prefab == null) return null;
        var go = InstantiatePrefab(e.Prefab, _propsRoot);
        if (go == null) return null;

        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(e.RotationOffset));
        float s = Mathf.Lerp(e.ScaleRange.x, e.ScaleRange.y, (float)_rng.NextDouble());
        if (s > 0f && !Mathf.Approximately(s, 1f)) go.transform.localScale *= s;

        if (addCollider && _generatePropColliders) AddBoundsCollider(go);
        return go;
    }

    // Caps real-time lights to the budget and keeps shadows scarce (PS2-cheap).
    private void ConfigureLights(GameObject go)
    {
        if (go == null) return;
        var lights = go.GetComponentsInChildren<Light>(true);

        if (lights.Length == 0 && _attachTorchLight)
        {
            var lg = new GameObject("TorchLight");
            lg.transform.SetParent(go.transform, false);
            lg.transform.localPosition = Vector3.up * _torchLightLocalHeight;
            var l = lg.AddComponent<Light>();
            l.type      = LightType.Point;
            l.color     = _torchLightColor;
            l.intensity = _torchLightIntensity;
            l.range     = _torchLightRange;
            lights = new[] { l };
        }

        foreach (var l in lights)
        {
            if (_lightsSpawned >= _maxRealtimeLights) { l.enabled = false; continue; }
            if (_shadowLightsSpawned < _maxShadowLights) { l.shadows = LightShadows.Soft; _shadowLightsSpawned++; }
            else                                          { l.shadows = LightShadows.None; }
            _lightsSpawned++;
        }
    }

    private GameObject InstantiatePrefab(GameObject prefab, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var inst = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (inst != null) return inst; // keeps the prefab/model link for authored editing
        }
#endif
        return Instantiate(prefab, parent);
    }

    // Box collider sized to the combined mesh bounds (in the prop's local space) so authored
    // furniture blocks the player without per-mesh collision cost.
    private void AddBoundsCollider(GameObject go)
    {
        var filters = go.GetComponentsInChildren<MeshFilter>();
        if (filters.Length == 0) return;

        bool has = false;
        Bounds local = default;
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            Matrix4x4 m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector3[] corners =
            {
                new(mb.min.x, mb.min.y, mb.min.z), new(mb.max.x, mb.min.y, mb.min.z),
                new(mb.min.x, mb.max.y, mb.min.z), new(mb.min.x, mb.min.y, mb.max.z),
                new(mb.max.x, mb.max.y, mb.min.z), new(mb.max.x, mb.min.y, mb.max.z),
                new(mb.min.x, mb.max.y, mb.max.z), new(mb.max.x, mb.max.y, mb.max.z),
            };
            foreach (var c in corners)
            {
                var p = m.MultiplyPoint3x4(c);
                if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                else local.Encapsulate(p);
            }
        }
        if (!has) return;

        var bc = go.AddComponent<BoxCollider>();
        bc.center = local.center;
        bc.size   = local.size;
    }

    // ── Decoration helpers ────────────────────────────────────────────────────────

    // Inward = direction from this tile toward open floor (away from adjacent walls).
    private bool TryWallInward(int x, int y, out Vector2 inward)
    {
        int ix = 0, iy = 0;
        if (!IsFloor(x + 1, y)) ix -= 1;
        if (!IsFloor(x - 1, y)) ix += 1;
        if (!IsFloor(x, y + 1)) iy -= 1;
        if (!IsFloor(x, y - 1)) iy += 1;
        inward = new Vector2(ix, iy);
        if (inward == Vector2.zero) return false;
        inward.Normalize();
        return true;
    }

    private bool IsWallAdjacent(int x, int y) =>
        !IsFloor(x + 1, y) || !IsFloor(x - 1, y) || !IsFloor(x, y + 1) || !IsFloor(x, y - 1);

    private bool IsCorner(int x, int y)
    {
        bool e = !IsFloor(x + 1, y), w = !IsFloor(x - 1, y);
        bool n = !IsFloor(x, y + 1), s = !IsFloor(x, y - 1);
        return (e || w) && (n || s);
    }

    private static float YawFromDir(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

    private void Shuffle(List<Vector2Int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ── Mesh helpers ──────────────────────────────────────────────────────────

    private void CreateFloorQuad(Vector3 center, Vector2 size, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_dungeonRoot, false);
        go.transform.position = center;
        var mesh = BuildQuadMesh(size);
        go.AddComponent<MeshFilter>().sharedMesh       = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = ResolveFloorMat();
        go.AddComponent<MeshCollider>().sharedMesh     = mesh;
    }

    private void CreateWall(Vector3 center, Vector3 size)
    {
        var go = new GameObject("Wall");
        go.transform.SetParent(_dungeonRoot, false);
        go.transform.position = center;
        var mesh = BuildBoxMesh(size);
        go.AddComponent<MeshFilter>().sharedMesh       = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = ResolveWallMat();
        go.AddComponent<MeshCollider>().sharedMesh     = mesh;
    }

    private Material ResolveFloorMat()
    {
        if (FloorMaterial != null) return FloorMaterial;
        return _runtimeFloorMat ??= DefaultMaterial();
    }

    private Material ResolveWallMat()
    {
        if (WallMaterial != null) return WallMaterial;
        return _runtimeWallMat ??= DefaultMaterial();
    }

    private Mesh BuildQuadMesh(Vector2 size)
    {
        float hx = size.x * 0.5f, hz = size.y * 0.5f;
        var mesh = new Mesh
        {
            vertices  = new[] { new Vector3(-hx,0,-hz), new Vector3(-hx,0,hz),
                                 new Vector3( hx,0, hz), new Vector3( hx,0,-hz) },
            uv        = new[] { new Vector2(0,0), new Vector2(0,1),
                                 new Vector2(1,1), new Vector2(1,0) },
            triangles = new[] { 0,1,2, 0,2,3 }
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildBoxMesh(Vector3 size)
    {
        float hx = size.x*0.5f, hy = size.y*0.5f, hz = size.z*0.5f;
        Vector3[] c = {
            new(-hx,-hy,-hz), new(hx,-hy,-hz), new(hx,-hy,hz), new(-hx,-hy,hz),
            new(-hx, hy,-hz), new(hx, hy,-hz), new(hx, hy,hz), new(-hx, hy,hz)
        };
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();

        void Face(int a, int b, int d, int e)
        {
            int s = verts.Count;
            verts.AddRange(new[] { c[a], c[b], c[d], c[e] });
            uvs.AddRange(new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) });
            tris.AddRange(new[] { s, s+2, s+1,  s, s+3, s+2 });
        }

        Face(0,1,5,4); Face(2,3,7,6);
        Face(3,0,4,7); Face(1,2,6,5);
        Face(4,5,6,7); Face(3,2,1,0);

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Material DefaultMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        return new Material(shader);
    }

    private static void DestroyImmediateSafe(Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    // ── Debug ───────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!_debugDrawRooms || _rooms == null) return;
        foreach (var room in _rooms)
        {
            Gizmos.color = room.Type switch
            {
                RoomType.Storage    => Color.yellow,
                RoomType.Bedroom    => Color.cyan,
                RoomType.Library    => Color.green,
                RoomType.Ritual     => Color.red,
                RoomType.Extraction => Color.magenta,
                RoomType.Hall       => new Color(1f, 0.6f, 0.1f),
                _                   => Color.gray,
            };
            Vector3 c = transform.TransformPoint(new Vector3(room.Rect.CenterX + 0.5f, 1f, room.Rect.CenterY + 0.5f));
            Gizmos.DrawWireCube(c, new Vector3(room.Rect.W, 2f, room.Rect.H));
        }
    }
}
