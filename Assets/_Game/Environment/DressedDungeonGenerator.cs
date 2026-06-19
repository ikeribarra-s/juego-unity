using System;
using System.Collections.Generic;
using UnityEngine;

public class DressedDungeonGenerator : MonoBehaviour
{
    private enum RoomType
    {
        Empty,
        Storage,
        Bedroom,
        Library,
        Ritual,
        Extraction,
        Hall
    }

    [Serializable]
    private class PropGroup
    {
        public string Name;
        public GameObject[] Prefabs;
    }

    [Header("Layout")]
    [SerializeField] private int Seed;
    [SerializeField] private int MapWidth = 80;
    [SerializeField] private int MapHeight = 80;
    [SerializeField] private int MinRoomSize = 8;
    [SerializeField] private int MaxRoomSize = 20;
    [SerializeField] private int MaxDepth = 5;
    [SerializeField] private int CorridorWidth = 3;

    [Header("Geometry")]
    [SerializeField] private Material FloorMaterial;
    [SerializeField] private Material WallMaterial;
    [SerializeField] private Material CeilingMaterial;
    [SerializeField] private float WallHeight = 4.5f;
    [SerializeField] private float WallThickness = 0.5f;
    [SerializeField] private bool GenerateCeiling = true;
    [SerializeField] private bool CeilingCollider = true;
    [SerializeField] private float FloorCeilingWallOverlap = 0.03f;
    [SerializeField] private string WallLayerName = "Walls";

    [Header("Dressing")]
    [SerializeField, Range(0f, 1f)] private float PropDensity = 0.65f;
    [SerializeField, Range(0f, 1f)] private float WallPropDensity = 0.45f;
    [SerializeField] private int MaxPropsPerRoom = 10;
    [SerializeField] private int MaxRealtimeLights = 32;
    [SerializeField] private bool DisableExtraPropLightShadows = true;
    [SerializeField] private bool RandomizePropScale = true;
    [SerializeField] private Vector2 PropScaleRange = new Vector2(0.92f, 1.08f);

    [Header("Wall Lights")]
    [SerializeField] private float WallPropHeight = 1.7f;
    [SerializeField] private float WallLightHeight = 2.15f;
    [SerializeField] private float WallMountSurfaceOffset = 0.04f;
    [SerializeField] private Vector3 LightPropEulerOffset = new Vector3(-90f, 0f, 0f);
    [SerializeField] private bool AddPointLightToLightProps = true;
    [SerializeField] private Color[] PointLightColors =
    {
        new Color(1f, 0.58f, 0.25f),
        new Color(1f, 0.42f, 0.18f),
        new Color(0.65f, 0.9f, 1f),
        new Color(0.75f, 1f, 0.62f)
    };
    [SerializeField] private Vector2 PointLightIntensityRange = new Vector2(1.1f, 2.4f);
    [SerializeField] private Vector2 PointLightRangeRange = new Vector2(5f, 8f);
    [SerializeField] private Vector3 PointLightLocalOffset = new Vector3(0f, 0.00508f, 0f);
    [SerializeField] private bool AddFlickerToLightProps = true;

    [Header("Primitive Details (no external models)")]
    [SerializeField] private bool GenerateTorches = true;
    [SerializeField] private bool GenerateWindows = true;
    [SerializeField] private bool GenerateCellBars = true;
    [SerializeField] private bool GenerateWallDetails = true;
    [SerializeField] private bool GenerateFloorDetails = true;
    [SerializeField, Range(0f, 1f)] private float TorchDensity = 0.24f;
    [SerializeField, Range(0f, 1f)] private float WindowDensity = 0.08f;
    [SerializeField, Range(0f, 1f)] private float CellBarDensity = 0.06f;
    [SerializeField, Range(0f, 1f)] private float DetailDensity = 0.14f;
    [SerializeField] private float TorchMountHeight = 2.4f;
    [SerializeField] private float WindowCenterHeight = 2.3f;

    [Header("Generated Lighting")]
    [SerializeField] private int MaxShadowCastingLights = 4;
    [SerializeField, Range(0f, 1f)] private float ColdLightChance = 0.15f;
    [SerializeField] private Vector2 TorchLightIntensity = new Vector2(1.8f, 3f);
    [SerializeField] private float TorchLightRange = 8.5f;
    [SerializeField] private bool GenerateRoomFillLights = true;
    [SerializeField] private Vector2 RoomFillIntensity = new Vector2(0.5f, 1.1f);
    [SerializeField, Range(0f, 1f)] private float RoomFillHeightFactor = 0.85f;
    [SerializeField] private Color[] WarmTorchColors =
    {
        new Color(1f, 0.55f, 0.22f),
        new Color(1f, 0.42f, 0.15f),
        new Color(1f, 0.68f, 0.30f)
    };
    [SerializeField] private Color[] ColdHorrorColors =
    {
        new Color(0.42f, 0.65f, 1f),
        new Color(0.45f, 1f, 0.6f)
    };

    [Header("Prop Groups")]
    [SerializeField] private PropGroup StorageProps;
    [SerializeField] private PropGroup BedroomProps;
    [SerializeField] private PropGroup LibraryProps;
    [SerializeField] private PropGroup RitualProps;
    [SerializeField] private PropGroup HallProps;
    [SerializeField] private PropGroup GenericClutterProps;
    [SerializeField] private PropGroup WallProps;
    [SerializeField] private PropGroup LightProps;
    [SerializeField] private PropGroup ExtractionProps;

    private const string RootName = "_dressedDungeonRoot";
    private Transform _dungeonRoot;
    private Transform _propsRoot;
    private Transform _detailsRoot;
    private Transform _decorRoot;
    private Transform _lightsRoot;
    private System.Random _rng;

    private bool[,] _floor;
    private bool[,] _roomFloor;
    private bool[,] _occupied;
    private Material _runtimeFloorMat;
    private Material _runtimeWallMat;
    private Material _runtimeCeilingMat;
    private Material _torchWoodMat;
    private Material _flameMat;
    private Material _metalMat;
    private Material _glassMat;
    private Material _stoneTrimMat;
    private Material _stainMat;
    private int _spawnedRealtimeLights;
    private int _spawnedShadowLights;

    private struct WallEdge
    {
        public Vector3 Surface;   // point on the inner wall face at floor level
        public Vector3 IntoRoom;  // unit normal pointing away from the wall into the room
        public int Cx, Cy, Side;  // owning floor tile + side (0=+z,1=-z,2=+x,3=-x)
    }

    private readonly List<RoomInfo> _rooms = new();

    private struct RectInt2D
    {
        public int X, Y, W, H;
        public RectInt2D(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public int CenterX => X + W / 2;
        public int CenterY => Y + H / 2;
        public int Area => W * H;
        public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
    }

    private class RoomInfo
    {
        public RectInt2D Rect;
        public RoomType Type;
        public readonly List<Vector2Int> Connections = new();

        public Vector2Int Center => new(Rect.CenterX, Rect.CenterY);
        public bool IsLarge => Rect.Area >= 140;
    }

    private class BSPNode
    {
        public RectInt2D Bounds;
        public BSPNode Left, Right;
        public RectInt2D Room;
        public bool HasRoom;
        public RoomInfo Info;
        public BSPNode(RectInt2D b) { Bounds = b; }
        public bool IsLeaf => Left == null && Right == null;
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        Clear();

        int seed = Seed != 0 ? Seed : System.Environment.TickCount;
        _rng = new System.Random(seed);
        _rooms.Clear();
        _spawnedRealtimeLights = 0;
        _spawnedShadowLights = 0;

        var rootGo = new GameObject(RootName);
        rootGo.transform.SetParent(transform, false);
        _dungeonRoot = rootGo.transform;

        var propsGo = new GameObject("Props");
        propsGo.transform.SetParent(_dungeonRoot, false);
        _propsRoot = propsGo.transform;

        _detailsRoot = NewChildRoot("Details");
        _decorRoot = NewChildRoot("Decor");
        _lightsRoot = NewChildRoot("Lights");

        _floor = new bool[MapWidth, MapHeight];
        _roomFloor = new bool[MapWidth, MapHeight];
        _occupied = new bool[MapWidth, MapHeight];

        var root = new BSPNode(new RectInt2D(0, 0, MapWidth, MapHeight));
        Split(root, 0);

        var leaves = new List<BSPNode>();
        CollectLeaves(root, leaves);
        foreach (var leaf in leaves)
        {
            CreateRoom(leaf);
            if (leaf.HasRoom)
                CarveRoom(leaf);
        }

        AssignRoomTypes();
        CarveCorridors(root);
        ReserveTraversalSpace();

        BuildFloorMeshes();
        BuildWallMeshes();
        if (GenerateCeiling)
            BuildCeilingMesh();
        DecorateDungeon();
        BuildPrimitiveDetails();
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        var existing = transform.Find(RootName);
        if (existing != null)
            DestroyImmediateSafe(existing.gameObject);

        _dungeonRoot = null;
        _propsRoot = null;
        _detailsRoot = null;
        _decorRoot = null;
        _lightsRoot = null;
        _runtimeFloorMat = null;
        _runtimeWallMat = null;
        _runtimeCeilingMat = null;
        _torchWoodMat = null;
        _flameMat = null;
        _metalMat = null;
        _glassMat = null;
        _stoneTrimMat = null;
        _stainMat = null;
        _rooms.Clear();
    }

    private Transform NewChildRoot(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_dungeonRoot, false);
        return go.transform;
    }

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
            node.Left = new BSPNode(new RectInt2D(node.Bounds.X, node.Bounds.Y, node.Bounds.W, s));
            node.Right = new BSPNode(new RectInt2D(node.Bounds.X, node.Bounds.Y + s, node.Bounds.W, node.Bounds.H - s));
        }
        else
        {
            int s = _rng.Next(MinRoomSize, node.Bounds.W - MinRoomSize + 1);
            node.Left = new BSPNode(new RectInt2D(node.Bounds.X, node.Bounds.Y, s, node.Bounds.H));
            node.Right = new BSPNode(new RectInt2D(node.Bounds.X + s, node.Bounds.Y, node.Bounds.W - s, node.Bounds.H));
        }

        Split(node.Left, depth + 1);
        Split(node.Right, depth + 1);
    }

    private void CollectLeaves(BSPNode node, List<BSPNode> leaves)
    {
        if (node == null) return;
        if (node.IsLeaf) { leaves.Add(node); return; }
        CollectLeaves(node.Left, leaves);
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

        leaf.Room = new RectInt2D(x, y, w, h);
        leaf.HasRoom = true;

        var info = new RoomInfo { Rect = leaf.Room };
        leaf.Info = info;
        _rooms.Add(info);
    }

    private void AssignRoomTypes()
    {
        if (_rooms.Count == 0) return;

        int extractionIndex = _rng.Next(_rooms.Count);
        for (int i = 0; i < _rooms.Count; i++)
        {
            RoomInfo room = _rooms[i];
            if (i == extractionIndex)
            {
                room.Type = RoomType.Extraction;
                continue;
            }

            if (!room.IsLarge && Roll(0.25f))
            {
                room.Type = RoomType.Empty;
                continue;
            }

            int pick = _rng.Next(0, 100);
            room.Type = pick switch
            {
                < 22 => RoomType.Storage,
                < 42 => RoomType.Library,
                < 58 => RoomType.Bedroom,
                < 72 => RoomType.Ritual,
                < 88 => RoomType.Hall,
                _ => RoomType.Empty
            };
        }
    }

    private void CarveRoom(BSPNode leaf)
    {
        RectInt2D r = leaf.Room;
        for (int x = r.X; x < r.X + r.W; x++)
        {
            for (int y = r.Y; y < r.Y + r.H; y++)
            {
                SetFloor(x, y);
                _roomFloor[x, y] = true;
            }
        }
    }

    private void CarveCorridors(BSPNode node)
    {
        if (node == null || node.IsLeaf) return;
        CarveCorridors(node.Left);
        CarveCorridors(node.Right);

        if (TryGetRoomInfo(node.Left, out RoomInfo a) &&
            TryGetRoomInfo(node.Right, out RoomInfo b))
        {
            Vector2Int ac = a.Center;
            Vector2Int bc = b.Center;
            a.Connections.Add(ac);
            b.Connections.Add(bc);

            if (_rng.Next(0, 2) == 0)
            {
                CarveHorizontal(ac.x, bc.x, ac.y);
                CarveVertical(ac.y, bc.y, bc.x);
            }
            else
            {
                CarveVertical(ac.y, bc.y, ac.x);
                CarveHorizontal(ac.x, bc.x, bc.y);
            }
        }
    }

    private bool TryGetRoomInfo(BSPNode node, out RoomInfo room)
    {
        room = null;
        if (node == null) return false;

        var list = new List<BSPNode>();
        CollectLeaves(node, list);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].HasRoom && list[i].Info != null)
            {
                room = list[i].Info;
                return true;
            }
        }

        return false;
    }

    private void CarveHorizontal(int xa, int xb, int y)
    {
        int lo = Mathf.Min(xa, xb);
        int hi = Mathf.Max(xa, xb);
        for (int x = lo; x <= hi; x++)
            CarveBand(x, y, true);
    }

    private void CarveVertical(int ya, int yb, int x)
    {
        int lo = Mathf.Min(ya, yb);
        int hi = Mathf.Max(ya, yb);
        for (int y = lo; y <= hi; y++)
            CarveBand(x, y, false);
    }

    private void CarveBand(int x, int y, bool horizontal)
    {
        int lo = -(CorridorWidth - 1) / 2;
        int hi = CorridorWidth / 2;
        for (int o = lo; o <= hi; o++)
            SetFloor(horizontal ? x : x + o, horizontal ? y + o : y);
    }

    private void SetFloor(int x, int y)
    {
        if (!InBounds(x, y)) return;
        _floor[x, y] = true;
    }

    private bool IsFloor(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        return _floor[x, y];
    }

    private bool InBounds(int x, int y) => x >= 0 && x < MapWidth && y >= 0 && y < MapHeight;

    private void ReserveTraversalSpace()
    {
        foreach (RoomInfo room in _rooms)
        {
            Vector2Int center = room.Center;
            MarkOccupied(center.x, center.y, 2);

            for (int x = room.Rect.X; x < room.Rect.X + room.Rect.W; x++)
                MarkOccupied(x, center.y, 1);

            for (int y = room.Rect.Y; y < room.Rect.Y + room.Rect.H; y++)
                MarkOccupied(center.x, y, 1);

            foreach (Vector2Int connection in room.Connections)
                MarkOccupied(connection.x, connection.y, 2);
        }
    }

    private void DecorateDungeon()
    {
        foreach (RoomInfo room in _rooms)
        {
            PlaceRoomFeature(room);
            PlaceWallDressing(room);
            PlaceClutter(room);
        }
    }

    private void PlaceRoomFeature(RoomInfo room)
    {
        PropGroup group = GetFeatureGroup(room.Type);
        if (!HasPrefabs(group)) return;

        int count = room.Type == RoomType.Empty ? 0 : _rng.Next(1, room.IsLarge ? 4 : 3);
        if (room.Type == RoomType.Extraction)
            count = 1;

        for (int i = 0; i < count; i++)
        {
            if (!Roll(PropDensity)) continue;
            if (TryFindInteriorSpot(room, out Vector2Int tile, 2))
                SpawnProp(PickPrefab(group), TileToWorld(tile), RandomCardinalRotation(), group == LightProps);
        }
    }

    private void PlaceWallDressing(RoomInfo room)
    {
        int attempts = Mathf.Clamp(Mathf.RoundToInt((room.Rect.W + room.Rect.H) * WallPropDensity), 1, 8);

        for (int i = 0; i < attempts; i++)
        {
            PropGroup group = Roll(0.35f) && HasPrefabs(LightProps) ? LightProps : WallProps;
            if (!HasPrefabs(group)) continue;
            if (!Roll(WallPropDensity)) continue;
            if (group == LightProps && _spawnedRealtimeLights >= MaxRealtimeLights) continue;

            if (TryFindWallMountSpot(room, group == LightProps, out Vector3 position, out Quaternion rotation))
            {
                SpawnProp(PickPrefab(group), position, rotation, group == LightProps);
            }
        }
    }

    private void PlaceClutter(RoomInfo room)
    {
        if (!HasPrefabs(GenericClutterProps)) return;

        int budget = Mathf.Clamp(Mathf.RoundToInt(room.Rect.Area * 0.035f * PropDensity), 0, MaxPropsPerRoom);
        for (int i = 0; i < budget; i++)
        {
            if (!Roll(PropDensity)) continue;
            if (TryFindCornerOrWallSpot(room, out Vector2Int tile, out Quaternion rotation))
                SpawnProp(PickPrefab(GenericClutterProps), TileToWorld(tile), rotation, false);
        }
    }

    private PropGroup GetFeatureGroup(RoomType type)
    {
        return type switch
        {
            RoomType.Storage => StorageProps,
            RoomType.Bedroom => BedroomProps,
            RoomType.Library => LibraryProps,
            RoomType.Ritual => RitualProps,
            RoomType.Extraction => ExtractionProps,
            RoomType.Hall => HallProps,
            _ => GenericClutterProps
        };
    }

    private bool TryFindInteriorSpot(RoomInfo room, out Vector2Int tile, int padding)
    {
        for (int i = 0; i < 24; i++)
        {
            int x = _rng.Next(room.Rect.X + padding, room.Rect.X + room.Rect.W - padding);
            int y = _rng.Next(room.Rect.Y + padding, room.Rect.Y + room.Rect.H - padding);
            if (CanOccupy(x, y, 1))
            {
                tile = new Vector2Int(x, y);
                MarkOccupied(x, y, 1);
                return true;
            }
        }

        tile = default;
        return false;
    }

    private bool TryFindWallSpot(RoomInfo room, out Vector2Int tile, out Quaternion rotation)
    {
        for (int i = 0; i < 32; i++)
        {
            int side = _rng.Next(0, 4);
            int x = _rng.Next(room.Rect.X + 1, room.Rect.X + room.Rect.W - 1);
            int y = _rng.Next(room.Rect.Y + 1, room.Rect.Y + room.Rect.H - 1);

            if (side == 0) y = room.Rect.Y + 1;
            else if (side == 1) y = room.Rect.Y + room.Rect.H - 2;
            else if (side == 2) x = room.Rect.X + 1;
            else x = room.Rect.X + room.Rect.W - 2;

            if (!CanOccupy(x, y, 0)) continue;

            tile = new Vector2Int(x, y);
            rotation = side switch
            {
                0 => Quaternion.Euler(0f, 0f, 0f),
                1 => Quaternion.Euler(0f, 180f, 0f),
                2 => Quaternion.Euler(0f, 90f, 0f),
                _ => Quaternion.Euler(0f, -90f, 0f)
            };
            MarkOccupied(x, y, 0);
            return true;
        }

        tile = default;
        rotation = Quaternion.identity;
        return false;
    }

    private bool TryFindWallMountSpot(RoomInfo room, bool isLight, out Vector3 position, out Quaternion rotation)
    {
        for (int i = 0; i < 32; i++)
        {
            int side = _rng.Next(0, 4);
            int x = _rng.Next(room.Rect.X + 1, room.Rect.X + room.Rect.W - 1);
            int y = _rng.Next(room.Rect.Y + 1, room.Rect.Y + room.Rect.H - 1);

            if (side == 0) y = room.Rect.Y + 1;
            else if (side == 1) y = room.Rect.Y + room.Rect.H - 2;
            else if (side == 2) x = room.Rect.X + 1;
            else x = room.Rect.X + room.Rect.W - 2;

            if (!CanOccupy(x, y, 0)) continue;

            float height = isLight ? WallLightHeight : WallPropHeight;
            float inset = WallThickness * 0.5f + WallMountSurfaceOffset;
            position = side switch
            {
                0 => new Vector3(x + 0.5f, height, room.Rect.Y + inset),
                1 => new Vector3(x + 0.5f, height, room.Rect.Y + room.Rect.H - inset),
                2 => new Vector3(room.Rect.X + inset, height, y + 0.5f),
                _ => new Vector3(room.Rect.X + room.Rect.W - inset, height, y + 0.5f)
            };

            Quaternion wallRotation = side switch
            {
                0 => Quaternion.Euler(0f, 0f, 0f),
                1 => Quaternion.Euler(0f, 180f, 0f),
                2 => Quaternion.Euler(0f, 90f, 0f),
                _ => Quaternion.Euler(0f, -90f, 0f)
            };

            rotation = isLight
                ? wallRotation * Quaternion.Euler(LightPropEulerOffset)
                : wallRotation;

            MarkOccupied(x, y, 0);
            return true;
        }

        position = default;
        rotation = Quaternion.identity;
        return false;
    }

    private bool TryFindCornerOrWallSpot(RoomInfo room, out Vector2Int tile, out Quaternion rotation)
    {
        if (Roll(0.45f))
            return TryFindWallSpot(room, out tile, out rotation);

        Vector2Int[] corners =
        {
            new(room.Rect.X + 1, room.Rect.Y + 1),
            new(room.Rect.X + room.Rect.W - 2, room.Rect.Y + 1),
            new(room.Rect.X + 1, room.Rect.Y + room.Rect.H - 2),
            new(room.Rect.X + room.Rect.W - 2, room.Rect.Y + room.Rect.H - 2)
        };

        for (int i = 0; i < corners.Length; i++)
        {
            int index = _rng.Next(0, corners.Length);
            Vector2Int candidate = corners[index];
            if (!CanOccupy(candidate.x, candidate.y, 0)) continue;

            tile = candidate;
            rotation = RandomCardinalRotation();
            MarkOccupied(candidate.x, candidate.y, 0);
            return true;
        }

        return TryFindWallSpot(room, out tile, out rotation);
    }

    private bool CanOccupy(int x, int y, int radius)
    {
        for (int ix = x - radius; ix <= x + radius; ix++)
        {
            for (int iy = y - radius; iy <= y + radius; iy++)
            {
                if (!InBounds(ix, iy) || !_roomFloor[ix, iy] || _occupied[ix, iy])
                    return false;
            }
        }

        return true;
    }

    private void MarkOccupied(int x, int y, int radius)
    {
        for (int ix = x - radius; ix <= x + radius; ix++)
        {
            for (int iy = y - radius; iy <= y + radius; iy++)
            {
                if (InBounds(ix, iy))
                    _occupied[ix, iy] = true;
            }
        }
    }

    private void SpawnProp(GameObject prefab, Vector3 position, Quaternion rotation, bool countLights)
    {
        if (prefab == null || _propsRoot == null) return;

        GameObject go = Instantiate(prefab, position, rotation, _propsRoot);
        go.name = prefab.name;

        if (RandomizePropScale)
        {
            float scale = RandomRange(PropScaleRange.x, PropScaleRange.y);
            go.transform.localScale *= scale;
        }

        if (countLights && AddPointLightToLightProps && go.GetComponentInChildren<Light>(includeInactive: true) == null)
            AddTorchPointLight(go);

        Light[] lights = go.GetComponentsInChildren<Light>(includeInactive: true);
        if (lights.Length == 0) return;

        for (int i = 0; i < lights.Length; i++)
        {
            if (countLights)
            {
                if (_spawnedRealtimeLights >= MaxRealtimeLights)
                {
                    lights[i].enabled = false;
                    continue;
                }

                ConfigureTorchLight(lights[i]);
                _spawnedRealtimeLights++;
            }

            if (DisableExtraPropLightShadows)
                lights[i].shadows = LightShadows.None;
        }

        if (countLights && AddFlickerToLightProps && go.GetComponent<LightFlicker>() == null)
            go.AddComponent<LightFlicker>();
    }

    private void AddTorchPointLight(GameObject owner)
    {
        var lightGo = new GameObject("Generated Point Light");
        lightGo.transform.SetParent(owner.transform, false);
        lightGo.transform.localPosition = PointLightLocalOffset;

        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
    }

    private void ConfigureTorchLight(Light light)
    {
        light.type = LightType.Point;
        light.color = PickPointLightColor();
        light.intensity = RandomRange(PointLightIntensityRange.x, PointLightIntensityRange.y);
        light.range = RandomRange(PointLightRangeRange.x, PointLightRangeRange.y);
        light.transform.localPosition += PointLightLocalOffset;

        if (DisableExtraPropLightShadows)
            light.shadows = LightShadows.None;
    }

    private Color PickPointLightColor()
    {
        if (PointLightColors == null || PointLightColors.Length == 0)
            return new Color(1f, 0.58f, 0.25f);

        return PointLightColors[_rng.Next(0, PointLightColors.Length)];
    }

    private GameObject PickPrefab(PropGroup group)
    {
        if (!HasPrefabs(group)) return null;
        return group.Prefabs[_rng.Next(0, group.Prefabs.Length)];
    }

    private bool HasPrefabs(PropGroup group) => group != null && group.Prefabs != null && group.Prefabs.Length > 0;

    private bool Roll(float chance) => _rng.NextDouble() <= Mathf.Clamp01(chance);

    private float RandomRange(float min, float max)
    {
        if (max < min) (min, max) = (max, min);
        return Mathf.Lerp(min, max, (float)_rng.NextDouble());
    }

    private Quaternion RandomCardinalRotation()
    {
        return Quaternion.Euler(0f, _rng.Next(0, 4) * 90f, 0f);
    }

    private Vector3 TileToWorld(Vector2Int tile)
    {
        return new Vector3(tile.x + 0.5f, 0f, tile.y + 0.5f);
    }

    // ---------------------------------------------------------------------
    // Primitive details — torches, windows, cell bars, wall/floor dressing.
    // Everything below is generated from cubes/cylinders/spheres/quads only;
    // no external models, no Resources.Load. Deterministic from Seed.
    // ---------------------------------------------------------------------

    private void BuildPrimitiveDetails()
    {
        EnsureDetailMaterials();

        // Soft ambient fill per room first so every room reads even before
        // torches consume the remaining light budget.
        if (GenerateRoomFillLights)
            BuildRoomFillLights();

        List<WallEdge> edges = CollectWallEdges();
        Shuffle(edges);

        // Reused as a "this edge tile is taken" guard so different detail types
        // don't stack on the same wall cell.
        var usedEdges = new HashSet<int>();

        if (GenerateTorches)
            PlaceAlongEdges(edges, usedEdges, TorchDensity, 4.5f, BuildTorch);
        if (GenerateWindows)
            PlaceAlongEdges(edges, usedEdges, WindowDensity, 6f, BuildWindow);
        if (GenerateCellBars)
            PlaceAlongEdges(edges, usedEdges, CellBarDensity, 8f, BuildCellBars);
        if (GenerateWallDetails)
            PlaceAlongEdges(edges, usedEdges, DetailDensity, 3f, BuildWallPanel);

        if (GenerateFloorDetails)
        {
            BuildFloorPatches();
            BuildCeilingBeams();
        }
    }

    private List<WallEdge> CollectWallEdges()
    {
        var edges = new List<WallEdge>();
        float inset = WallThickness * 0.5f;

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                if (!_floor[x, y]) continue;

                if (!IsFloor(x, y + 1))
                    edges.Add(new WallEdge
                    {
                        Surface = new Vector3(x + 0.5f, 0f, y + 1 - inset),
                        IntoRoom = new Vector3(0f, 0f, -1f),
                        Cx = x, Cy = y, Side = 0
                    });
                if (!IsFloor(x, y - 1))
                    edges.Add(new WallEdge
                    {
                        Surface = new Vector3(x + 0.5f, 0f, y + inset),
                        IntoRoom = new Vector3(0f, 0f, 1f),
                        Cx = x, Cy = y, Side = 1
                    });
                if (!IsFloor(x + 1, y))
                    edges.Add(new WallEdge
                    {
                        Surface = new Vector3(x + 1 - inset, 0f, y + 0.5f),
                        IntoRoom = new Vector3(-1f, 0f, 0f),
                        Cx = x, Cy = y, Side = 2
                    });
                if (!IsFloor(x - 1, y))
                    edges.Add(new WallEdge
                    {
                        Surface = new Vector3(x + inset, 0f, y + 0.5f),
                        IntoRoom = new Vector3(1f, 0f, 0f),
                        Cx = x, Cy = y, Side = 3
                    });
            }
        }

        return edges;
    }

    private void PlaceAlongEdges(List<WallEdge> edges, HashSet<int> usedEdges,
        float density, float minSpacing, Action<WallEdge> build)
    {
        if (density <= 0f) return;

        var placed = new List<Vector3>();
        float minSqr = minSpacing * minSpacing;

        foreach (WallEdge edge in edges)
        {
            int key = EdgeKey(edge.Cx, edge.Cy, edge.Side);
            if (usedEdges.Contains(key)) continue;
            if (!Roll(density)) continue;

            bool tooClose = false;
            for (int i = 0; i < placed.Count; i++)
            {
                if ((placed[i] - edge.Surface).sqrMagnitude < minSqr) { tooClose = true; break; }
            }
            if (tooClose) continue;

            build(edge);
            placed.Add(edge.Surface);

            // Reserve this cell and its immediate neighbours on the same side.
            usedEdges.Add(key);
            usedEdges.Add(EdgeKey(edge.Cx + 1, edge.Cy, edge.Side));
            usedEdges.Add(EdgeKey(edge.Cx - 1, edge.Cy, edge.Side));
            usedEdges.Add(EdgeKey(edge.Cx, edge.Cy + 1, edge.Side));
            usedEdges.Add(EdgeKey(edge.Cx, edge.Cy - 1, edge.Side));
        }
    }

    private int EdgeKey(int x, int y, int side) => ((x * MapHeight) + y) * 4 + side;

    private Quaternion WallFacing(WallEdge edge) => Quaternion.LookRotation(edge.IntoRoom, Vector3.up);

    // --- Torch -----------------------------------------------------------

    private void BuildTorch(WallEdge edge)
    {
        var root = new GameObject("Torch");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = edge.Surface + Vector3.up * TorchMountHeight;
        root.transform.localRotation = WallFacing(edge); // local +Z points into the room

        // Mount plate flush against the wall.
        CreatePart(PrimitiveType.Cube, root.transform, "Bracket",
            new Vector3(0f, 0f, 0.03f), Vector3.zero, new Vector3(0.2f, 0.36f, 0.06f), _torchWoodMat);

        // Handle: a stick leaning up and out from the wall.
        CreatePart(PrimitiveType.Cylinder, root.transform, "Handle",
            new Vector3(0f, 0.16f, 0.16f), new Vector3(50f, 0f, 0f), new Vector3(0.07f, 0.22f, 0.07f), _torchWoodMat);

        // Flame cluster at the tip of the handle (emissive).
        Vector3 flameLocal = new Vector3(0f, 0.34f, 0.35f);
        CreatePart(PrimitiveType.Sphere, root.transform, "Flame",
            flameLocal, Vector3.zero, new Vector3(0.18f, 0.26f, 0.18f), _flameMat);
        CreatePart(PrimitiveType.Sphere, root.transform, "FlameCore",
            flameLocal + new Vector3(0f, 0.06f, 0f), Vector3.zero, new Vector3(0.1f, 0.16f, 0.1f), _flameMat);

        bool cold = Roll(ColdLightChance);
        Vector3 flameWorld = root.transform.TransformPoint(flameLocal);
        SpawnGeneratedLight(flameWorld + edge.IntoRoom * 0.15f, cold, "TorchLight");
    }

    // --- Window ----------------------------------------------------------

    private void BuildWindow(WallEdge edge)
    {
        var root = new GameObject("Window");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = edge.Surface + Vector3.up * WindowCenterHeight;
        root.transform.localRotation = WallFacing(edge);

        const float w = 1.2f, h = 1.6f;

        // Dark inset pane recessed slightly into the wall.
        CreatePart(PrimitiveType.Cube, root.transform, "Pane",
            new Vector3(0f, 0f, -0.06f), Vector3.zero, new Vector3(w, h, 0.05f), _glassMat);

        // Stone frame.
        CreatePart(PrimitiveType.Cube, root.transform, "FrameTop",
            new Vector3(0f, h * 0.5f, 0f), Vector3.zero, new Vector3(w + 0.24f, 0.14f, 0.18f), _stoneTrimMat);
        CreatePart(PrimitiveType.Cube, root.transform, "FrameBottom",
            new Vector3(0f, -h * 0.5f, 0f), Vector3.zero, new Vector3(w + 0.24f, 0.14f, 0.18f), _stoneTrimMat);
        CreatePart(PrimitiveType.Cube, root.transform, "FrameLeft",
            new Vector3(-w * 0.5f - 0.06f, 0f, 0f), Vector3.zero, new Vector3(0.14f, h + 0.2f, 0.18f), _stoneTrimMat);
        CreatePart(PrimitiveType.Cube, root.transform, "FrameRight",
            new Vector3(w * 0.5f + 0.06f, 0f, 0f), Vector3.zero, new Vector3(0.14f, h + 0.2f, 0.18f), _stoneTrimMat);

        // Crossbars (dark metal).
        CreatePart(PrimitiveType.Cube, root.transform, "BarV",
            new Vector3(0f, 0f, 0.02f), Vector3.zero, new Vector3(0.05f, h, 0.05f), _metalMat);
        CreatePart(PrimitiveType.Cube, root.transform, "BarH",
            new Vector3(0f, 0f, 0.02f), Vector3.zero, new Vector3(w, 0.05f, 0.05f), _metalMat);

        // Occasional faint cold light bleeding through the glass.
        if (Roll(0.4f))
        {
            Vector3 worldPos = root.transform.TransformPoint(new Vector3(0f, 0f, 0.4f));
            SpawnGeneratedLight(worldPos, true, "WindowLight", 0.55f);
        }
    }

    // --- Cell bars -------------------------------------------------------

    private void BuildCellBars(WallEdge edge)
    {
        var root = new GameObject("CellBars");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = edge.Surface;
        root.transform.localRotation = WallFacing(edge);

        const float width = 1.7f, barHeight = 2.6f, z = 0.08f;
        int bars = 5;
        float step = width / (bars - 1);

        for (int i = 0; i < bars; i++)
        {
            float x = -width * 0.5f + step * i;
            CreatePart(PrimitiveType.Cylinder, root.transform, "Bar",
                new Vector3(x, barHeight * 0.5f, z), Vector3.zero, new Vector3(0.07f, barHeight * 0.5f, 0.07f), _metalMat);
        }

        // Top and bottom rails tying the bars together.
        CreatePart(PrimitiveType.Cube, root.transform, "RailTop",
            new Vector3(0f, barHeight - 0.1f, z), Vector3.zero, new Vector3(width + 0.2f, 0.1f, 0.1f), _metalMat);
        CreatePart(PrimitiveType.Cube, root.transform, "RailBottom",
            new Vector3(0f, 0.15f, z), Vector3.zero, new Vector3(width + 0.2f, 0.1f, 0.1f), _metalMat);
    }

    // --- Wall panels / beams / cracks ------------------------------------

    private void BuildWallPanel(WallEdge edge)
    {
        var root = new GameObject("WallPanel");
        root.transform.SetParent(_detailsRoot, false);
        root.transform.localPosition = edge.Surface;
        root.transform.localRotation = WallFacing(edge);

        int variant = _rng.Next(0, 3);
        if (variant == 0)
        {
            // Vertical support beam running floor-to-ceiling.
            float h = WallHeight - 0.1f;
            CreatePart(PrimitiveType.Cube, root.transform, "SupportBeam",
                new Vector3(0f, h * 0.5f, 0.08f), Vector3.zero, new Vector3(0.32f, h, 0.16f), _torchWoodMat);
        }
        else if (variant == 1)
        {
            // Stone block band / trim at mid height.
            float y = RandomRange(1.0f, WallHeight - 1.2f);
            CreatePart(PrimitiveType.Cube, root.transform, "StoneTrim",
                new Vector3(0f, y, 0.05f), Vector3.zero, new Vector3(1.6f, 0.4f, 0.1f), _stoneTrimMat);
        }
        else
        {
            // Flat panel / stain proud of the wall.
            float y = RandomRange(1.2f, WallHeight - 1.2f);
            CreatePart(PrimitiveType.Quad, root.transform, "Stain",
                new Vector3(RandomRange(-0.3f, 0.3f), y, 0.02f),
                new Vector3(0f, 180f, _rng.Next(0, 4) * 90f),
                new Vector3(RandomRange(0.8f, 1.6f), RandomRange(0.8f, 1.6f), 1f), _stainMat);
        }
    }

    // --- Floor & ceiling dressing ----------------------------------------

    private void BuildFloorPatches()
    {
        int target = Mathf.RoundToInt(_rooms.Count * 6 * DetailDensity);
        for (int i = 0; i < target; i++)
        {
            int x = _rng.Next(0, MapWidth);
            int y = _rng.Next(0, MapHeight);
            if (!IsFloor(x, y)) continue;

            CreatePart(PrimitiveType.Quad, _decorRoot, "FloorStain",
                new Vector3(x + 0.5f, 0.012f, y + 0.5f),
                new Vector3(90f, _rng.Next(0, 4) * 90f, 0f),
                new Vector3(RandomRange(0.8f, 1.8f), RandomRange(0.8f, 1.8f), 1f), _stainMat);
        }
    }

    private void BuildCeilingBeams()
    {
        float beamY = WallHeight - 0.18f;
        foreach (RoomInfo room in _rooms)
        {
            RectInt2D r = room.Rect;
            for (int x = r.X + 2; x < r.X + r.W - 1; x += 3)
            {
                CreatePart(PrimitiveType.Cube, _decorRoot, "CeilingBeam",
                    new Vector3(x + 0.5f, beamY, r.CenterY + 0.5f), Vector3.zero,
                    new Vector3(0.3f, 0.3f, r.H - 0.5f), _torchWoodMat);
            }
        }
    }

    // --- Lighting --------------------------------------------------------

    private void BuildRoomFillLights()
    {
        float y = WallHeight * Mathf.Clamp01(RoomFillHeightFactor);

        foreach (RoomInfo room in _rooms)
        {
            if (_spawnedRealtimeLights >= MaxRealtimeLights) break;

            Vector2Int c = room.Center;
            var go = new GameObject("RoomFillLight");
            go.transform.SetParent(_lightsRoot, false);
            go.transform.localPosition = new Vector3(c.x + 0.5f, y, c.y + 0.5f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            bool cold = room.Type == RoomType.Ritual || Roll(ColdLightChance);
            light.color = cold ? PickFrom(ColdHorrorColors) : PickFrom(WarmTorchColors);
            light.intensity = RandomRange(RoomFillIntensity.x, RoomFillIntensity.y);
            light.range = Mathf.Max(room.Rect.W, room.Rect.H) * 0.9f;
            light.shadows = LightShadows.None;
            _spawnedRealtimeLights++;
        }
    }

    private void SpawnGeneratedLight(Vector3 worldPos, bool cold, string name, float intensityScale = 1f)
    {
        if (_spawnedRealtimeLights >= MaxRealtimeLights) return;

        var go = new GameObject(name);
        go.transform.SetParent(_lightsRoot, false);
        go.transform.position = worldPos;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = cold ? PickFrom(ColdHorrorColors) : PickFrom(WarmTorchColors);
        light.intensity = RandomRange(TorchLightIntensity.x, TorchLightIntensity.y) * intensityScale;
        light.range = TorchLightRange;

        if (_spawnedShadowLights < MaxShadowCastingLights)
        {
            light.shadows = LightShadows.Soft;
            _spawnedShadowLights++;
        }
        else
        {
            light.shadows = LightShadows.None;
        }

        go.AddComponent<LightFlicker>();
        _spawnedRealtimeLights++;
    }

    private Color PickFrom(Color[] colors)
    {
        if (colors == null || colors.Length == 0) return new Color(1f, 0.55f, 0.22f);
        return colors[_rng.Next(0, colors.Length)];
    }

    // --- Primitive helpers -----------------------------------------------

    private GameObject CreatePart(PrimitiveType type, Transform parent, string name,
        Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material material, bool collider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;

        if (!collider)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediateSafe(col);
        }

        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localEulerAngles = localEuler;
        go.transform.localScale = localScale;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && material != null)
            mr.sharedMaterial = material;

        return go;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void BuildFloorMeshes()
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var uvs = new List<Vector2>();

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
                if (_floor[x, y])
                    AddHorizontalTile(verts, tris, uvs, x, y, 0f, true);
        }

        if (verts.Count == 0) return;

        var mesh = new Mesh { name = "DungeonFloor" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Floor");
        go.transform.SetParent(_dungeonRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = ResolveFloorMat();
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private void AddHorizontalTile(List<Vector3> verts, List<int> tris, List<Vector2> uvs, int x, int y, float height, bool faceUp)
    {
        int start = verts.Count;
        float overlap = Mathf.Max(0f, FloorCeilingWallOverlap);
        float x0 = x - (!IsFloor(x - 1, y) ? overlap : 0f);
        float x1 = x + 1 + (!IsFloor(x + 1, y) ? overlap : 0f);
        float z0 = y - (!IsFloor(x, y - 1) ? overlap : 0f);
        float z1 = y + 1 + (!IsFloor(x, y + 1) ? overlap : 0f);

        if (faceUp)
        {
            verts.Add(new Vector3(x0, height, z0));
            verts.Add(new Vector3(x0, height, z1));
            verts.Add(new Vector3(x1, height, z1));
            verts.Add(new Vector3(x1, height, z0));
            tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        else
        {
            verts.Add(new Vector3(x0, height, z0));
            verts.Add(new Vector3(x1, height, z0));
            verts.Add(new Vector3(x1, height, z1));
            verts.Add(new Vector3(x0, height, z1));
            tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        uvs.Add(new Vector2(x0, z0));
        uvs.Add(faceUp ? new Vector2(x0, z1) : new Vector2(x1, z0));
        uvs.Add(new Vector2(x1, z1));
        uvs.Add(faceUp ? new Vector2(x1, z0) : new Vector2(x0, z1));
    }

    private void BuildCeilingMesh()
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var uvs = new List<Vector2>();

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
                if (_floor[x, y])
                    AddHorizontalTile(verts, tris, uvs, x, y, WallHeight, false);
        }

        if (verts.Count == 0) return;

        var mesh = new Mesh { name = "DungeonCeiling" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Ceiling");
        go.transform.SetParent(_dungeonRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = ResolveCeilingMat();

        if (CeilingCollider)
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private void BuildWallMeshes()
    {
        float wallY = WallHeight * 0.5f;

        for (int y = 0; y < MapHeight; y++)
        {
            BuildEdgeRunsAlongX(y, +1, wallY);
            BuildEdgeRunsAlongX(y, -1, wallY);
        }

        for (int x = 0; x < MapWidth; x++)
        {
            BuildEdgeRunsAlongZ(x, +1, wallY);
            BuildEdgeRunsAlongZ(x, -1, wallY);
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
            float len = x - xs;
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
            float len = y - ys;
            float xEdge = dx > 0 ? x + 1 : x;
            CreateWall(
                new Vector3(xEdge, wallY, (ys + y) * 0.5f),
                new Vector3(WallThickness, WallHeight, len + WallThickness));
        }
    }

    private void CreateWall(Vector3 center, Vector3 size)
    {
        var go = new GameObject("Wall");
        go.transform.SetParent(_dungeonRoot, false);
        go.transform.localPosition = center;
        ApplyWallLayer(go);
        var mesh = BuildBoxMesh(size);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = ResolveWallMat();
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private void ApplyWallLayer(GameObject go)
    {
        if (string.IsNullOrEmpty(WallLayerName)) return;

        int layer = LayerMask.NameToLayer(WallLayerName);
        if (layer < 0)
        {
            Debug.LogWarning($"[DressedDungeonGenerator] Layer '{WallLayerName}' not found. " +
                             "Run Fluffterror > Setup > Add Walls Layer, then regenerate.", this);
            return;
        }

        go.layer = layer;
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

    private Material ResolveCeilingMat()
    {
        if (CeilingMaterial != null) return CeilingMaterial;
        return _runtimeCeilingMat ??= DefaultMaterial();
    }

    private Mesh BuildBoxMesh(Vector3 size)
    {
        float hx = size.x * 0.5f;
        float hy = size.y * 0.5f;
        float hz = size.z * 0.5f;
        Vector3[] c =
        {
            new(-hx, -hy, -hz), new(hx, -hy, -hz), new(hx, -hy, hz), new(-hx, -hy, hz),
            new(-hx, hy, -hz), new(hx, hy, -hz), new(hx, hy, hz), new(-hx, hy, hz)
        };
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var uvs = new List<Vector2>();

        void Face(int a, int b, int d, int e, float uvWidth, float uvHeight)
        {
            int s = verts.Count;
            verts.AddRange(new[] { c[a], c[b], c[d], c[e] });
            uvs.AddRange(new[]
            {
                new Vector2(0f, 0f), new Vector2(uvWidth, 0f),
                new Vector2(uvWidth, uvHeight), new Vector2(0f, uvHeight)
            });
            tris.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
        }

        Face(0, 1, 5, 4, size.x, size.y);
        Face(2, 3, 7, 6, size.x, size.y);
        Face(3, 0, 4, 7, size.z, size.y);
        Face(1, 2, 6, 5, size.z, size.y);
        Face(4, 5, 6, 7, size.x, size.z);
        Face(3, 2, 1, 0, size.x, size.z);

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void EnsureDetailMaterials()
    {
        _torchWoodMat ??= MakeLit(new Color(0.18f, 0.12f, 0.08f));
        _metalMat ??= MakeLit(new Color(0.08f, 0.08f, 0.09f), metallic: 0.6f, smoothness: 0.35f);
        _stoneTrimMat ??= MakeLit(new Color(0.32f, 0.31f, 0.33f));
        _stainMat ??= MakeLit(new Color(0.06f, 0.05f, 0.05f));
        _glassMat ??= MakeLit(new Color(0.04f, 0.05f, 0.07f));
        _flameMat ??= MakeEmissive(new Color(0.6f, 0.2f, 0.05f), new Color(1f, 0.5f, 0.12f) * 4f);
    }

    private Material MakeLit(Color albedo, float metallic = 0f, float smoothness = 0f)
    {
        Material material = DefaultMaterial();
        material.color = albedo;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", albedo);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);

        return material;
    }

    private Material MakeEmissive(Color albedo, Color emissionHdr)
    {
        Material material = MakeLit(albedo);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", emissionHdr);

        return material;
    }

    private Material DefaultMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader)
        {
            color = new Color(0.28f, 0.28f, 0.3f)
        };

        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0f);

        if (material.HasProperty("_EnvironmentReflections"))
            material.SetFloat("_EnvironmentReflections", 0f);

        if (material.HasProperty("_SpecularHighlights"))
            material.SetFloat("_SpecularHighlights", 0f);

        return material;
    }

    private static void DestroyImmediateSafe(UnityEngine.Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}
