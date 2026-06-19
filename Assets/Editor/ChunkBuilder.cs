using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Editor tool that assembles reusable "chunk" prefabs out of the Updated Modular Dungeon kit.
//
// Each chunk is an empty root GameObject with three child groups — Floor / Walls / Props — full
// of nested model-prefab instances. Chunks are square (CHUNK_TILES²) and every open side gets a
// centered opening + arch so neighbouring chunks line up when a generator places them on a grid.
//
// Piece sizes are MEASURED from the floor/wall models at build time (renderer bounds), so the
// floor tiles seamlessly and walls/props sit on the floor surface regardless of the kit's scale.
// Placement is intentionally simple — this produces a solid starting point that you then tweak by
// hand (rotation/scale/material) until it looks good. Re-running overwrites the prefabs.
//
// Menu: Fluffterror ▸ Chunks ▸ Build Dungeon Chunks
public class ChunkBuilder
{
    private const string Kit = "Assets/3DModels/Updated Modular Dungeon - May 2019/FBX/";
    private const string OutFolder = "Assets/_Game/Environment/Chunks";
    private const int CHUNK_TILES = 4; // chunk footprint, in floor tiles (square)
    private const int OPEN_WIDTH  = 2; // width of a side opening, in tiles (centered)

    // Kit model file names (without folder / extension).
    private const string Floor  = "Floor_Modular";
    private const string Wall   = "Wall_Modular";
    private const string Arch   = "Arch";

    [Flags]
    private enum Side { None = 0, N = 1, E = 2, S = 4, W = 8 }
    private enum Dressing { None, Pillars, Storage, Ritual, Cozy }

    [MenuItem("Fluffterror/Chunks/Build Dungeon Chunks")]
    private static void BuildAllMenu() => new ChunkBuilder().Run();

    // ── cached, measured kit pieces ───────────────────────────────────────────
    private readonly Dictionary<string, (GameObject prefab, Bounds bounds)> _cache = new();
    private float _tileX, _tileZ, _floorTop, _wallH;

    private void Run()
    {
        if (!TryGet(Floor, out var floor))
        {
            Debug.LogError($"[ChunkBuilder] Floor model not found at {Kit}{Floor}.fbx — aborting.");
            return;
        }

        // Establish the tile grid from the real floor footprint.
        _tileX    = floor.bounds.size.x;
        _tileZ    = floor.bounds.size.z;
        _floorTop = floor.bounds.size.y;
        _wallH    = TryGet(Wall, out var wall) ? wall.bounds.size.y : 3f;

        EnsureOutFolder();

        var chunks = new (string name, Side open, Dressing dress)[]
        {
            ("Chunk_Cross",        Side.N | Side.E | Side.S | Side.W, Dressing.Pillars),
            ("Chunk_T_N",          Side.N | Side.E | Side.W,          Dressing.Pillars),
            ("Chunk_Corner_NE",    Side.N | Side.E,                   Dressing.Cozy),
            ("Chunk_Straight_NS",  Side.N | Side.S,                   Dressing.None),
            ("Chunk_DeadEnd_S",    Side.S,                            Dressing.Cozy),
            ("Chunk_Room_Storage", Side.S,                            Dressing.Storage),
            ("Chunk_Room_Ritual",  Side.N | Side.S,                   Dressing.Ritual),
            ("Chunk_Closed",       Side.None,                         Dressing.Pillars),
        };

        int built = 0;
        try
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                var c = chunks[i];
                EditorUtility.DisplayProgressBar("Building chunks", c.name, (float)i / chunks.Length);
                BuildChunk(c.name, c.open, c.dress);
                built++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ChunkBuilder] Built {built} chunk prefabs in {OutFolder}. Tile = {_tileX:0.##} x {_tileZ:0.##}.");
    }

    // ── chunk assembly ──────────────────────────────────────────────────────────

    private void BuildChunk(string name, Side open, Dressing dress)
    {
        var root   = new GameObject(name);
        var floors = NewGroup("Floor", root.transform);
        var walls  = NewGroup("Walls", root.transform);
        var props  = NewGroup("Props", root.transform);

        // Floor grid.
        var fb = _cache[Floor].bounds;
        for (int gz = 0; gz < CHUNK_TILES; gz++)
            for (int gx = 0; gx < CHUNK_TILES; gx++)
            {
                float cx = (gx + 0.5f) * _tileX;
                float cz = (gz + 0.5f) * _tileZ;
                var go = Instantiate(Floor, floors);
                if (go != null)
                    go.transform.position = new Vector3(cx - fb.center.x, -fb.min.y, cz - fb.center.z);
            }

        float chunkW = CHUNK_TILES * _tileX;
        float chunkD = CHUNK_TILES * _tileZ;

        // Walls + arched openings, per side.
        BuildSide(walls, alongX: true,  fixedCoord: 0f,      span: chunkW, open: open.HasFlag(Side.S), yaw: 0f);
        BuildSide(walls, alongX: true,  fixedCoord: chunkD,  span: chunkW, open: open.HasFlag(Side.N), yaw: 0f);
        BuildSide(walls, alongX: false, fixedCoord: 0f,      span: chunkD, open: open.HasFlag(Side.W), yaw: 90f);
        BuildSide(walls, alongX: false, fixedCoord: chunkW,  span: chunkD, open: open.HasFlag(Side.E), yaw: 90f);

        Dress(props, dress, open, chunkW, chunkD);

        SaveChunk(root, name);
        UnityEngine.Object.DestroyImmediate(root);
    }

    // Lays one side's wall tiles (skipping the centered opening) and an arch in the opening.
    private void BuildSide(Transform walls, bool alongX, float fixedCoord, float span, bool open, float yaw)
    {
        int openStart = (CHUNK_TILES - OPEN_WIDTH) / 2;
        float tile = alongX ? _tileX : _tileZ;

        for (int i = 0; i < CHUNK_TILES; i++)
        {
            if (open && i >= openStart && i < openStart + OPEN_WIDTH) continue;
            float c = (i + 0.5f) * tile;
            var (x, z) = alongX ? (c, fixedCoord) : (fixedCoord, c);
            PlaceOnFloor(Wall, x, z, yaw, walls);
        }

        if (open)
        {
            float mid = span * 0.5f;
            var (x, z) = alongX ? (mid, fixedCoord) : (fixedCoord, mid);
            PlaceOnFloor(Arch, x, z, yaw, walls);
        }
    }

    // ── dressing ────────────────────────────────────────────────────────────────

    private void Dress(Transform props, Dressing dress, Side open, float chunkW, float chunkD)
    {
        switch (dress)
        {
            case Dressing.Pillars:
                PlaceAtCell(props, "Column", 0, 0);
                PlaceAtCell(props, "Column", CHUNK_TILES - 1, 0);
                PlaceAtCell(props, "Column", 0, CHUNK_TILES - 1);
                PlaceAtCell(props, "Column", CHUNK_TILES - 1, CHUNK_TILES - 1);
                WallTorches(props, open, chunkW, chunkD);
                break;

            case Dressing.Storage:
                PlaceAtCell(props, "Crate",   1, 1);
                PlaceAtCell(props, "Barrel",  2, 1);
                PlaceAtCell(props, "Chest",   1, 2);
                PlaceAtCell(props, "Barrel2", 2, 2);
                PlaceAtCell(props, "Vase",    0, CHUNK_TILES - 1);
                WallTorches(props, open, chunkW, chunkD);
                break;

            case Dressing.Ritual:
                PlaceOnFloor("Pedestal", chunkW * 0.5f, chunkD * 0.5f, 0f, props);
                if (TryGet("Pedestal", out var ped))
                    PlaceOnFloor("Skull", chunkW * 0.5f, chunkD * 0.5f, 0f, props, _floorTop + ped.bounds.size.y);
                PlaceAtCell(props, "Woodfire", 1, 1);
                PlaceAtCell(props, "Skull",    CHUNK_TILES - 1, CHUNK_TILES - 1);
                WallTorches(props, open, chunkW, chunkD);
                break;

            case Dressing.Cozy:
                PlaceAtCell(props, "Barrel",     1, 1);
                PlaceAtCell(props, "Table_Small", 2, 2);
                PlaceCornerCobweb(props, chunkW, chunkD);
                WallTorches(props, open, chunkW, chunkD);
                break;

            case Dressing.None:
            default:
                break;
        }
    }

    // A torch on the inner face of each closed side, raised to mid-wall and facing inward.
    private void WallTorches(Transform props, Side open, float chunkW, float chunkD)
    {
        float inset = 0.35f * _tileX;
        float y = _floorTop + _wallH * 0.5f;
        if (!open.HasFlag(Side.S)) PlaceRaised(props, "Torch", chunkW * 0.5f, inset,          0f,   y);
        if (!open.HasFlag(Side.N)) PlaceRaised(props, "Torch", chunkW * 0.5f, chunkD - inset,  180f, y);
        if (!open.HasFlag(Side.W)) PlaceRaised(props, "Torch", inset,          chunkD * 0.5f,  90f,  y);
        if (!open.HasFlag(Side.E)) PlaceRaised(props, "Torch", chunkW - inset,  chunkD * 0.5f, 270f, y);
    }

    private void PlaceCornerCobweb(Transform props, float chunkW, float chunkD)
    {
        if (!TryGet("Cobweb", out _)) return;
        PlaceRaised(props, "Cobweb", 0.3f * _tileX, chunkD - 0.3f * _tileZ, 0f, _floorTop + _wallH * 0.85f);
    }

    // ── placement primitives ──────────────────────────────────────────────────

    private void PlaceAtCell(Transform parent, string model, int gx, int gz)
        => PlaceOnFloor(model, (gx + 0.5f) * _tileX, (gz + 0.5f) * _tileZ, 0f, parent);

    private void PlaceRaised(Transform parent, string model, float x, float z, float yaw, float baseY)
        => PlaceOnFloor(model, x, z, yaw, parent, baseY);

    // Instantiates a model with its base resting on the floor (or a custom baseY) at (x, z).
    private GameObject PlaceOnFloor(string model, float x, float z, float yaw, Transform parent, float? baseY = null)
    {
        var go = Instantiate(model, parent);
        if (go == null) return null;
        var b = _cache[model].bounds;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f); // Y-only yaw keeps min.y valid
        go.transform.position = new Vector3(x, (baseY ?? _floorTop) - b.min.y, z);
        return go;
    }

    private GameObject Instantiate(string model, Transform parent)
    {
        if (!TryGet(model, out var entry)) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab);
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one;
        return go;
    }

    // ── model cache / measuring ──────────────────────────────────────────────

    private bool TryGet(string model, out (GameObject prefab, Bounds bounds) entry)
    {
        if (_cache.TryGetValue(model, out entry)) return entry.prefab != null;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}{model}.fbx");
        Bounds bounds = new(Vector3.zero, Vector3.one);
        if (prefab == null)
            Debug.LogWarning($"[ChunkBuilder] Missing model (skipped): {Kit}{model}.fbx");
        else
            bounds = MeasureBounds(prefab);

        entry = (prefab, bounds);
        _cache[model] = entry;
        return prefab != null;
    }

    private static Bounds MeasureBounds(GameObject prefab)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        inst.transform.localScale = Vector3.one;

        var renderers = inst.GetComponentsInChildren<Renderer>();
        Bounds b = new(Vector3.zero, Vector3.zero);
        bool has = false;
        foreach (var r in renderers)
        {
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }

        UnityEngine.Object.DestroyImmediate(inst);
        return has ? b : new Bounds(Vector3.zero, Vector3.one);
    }

    // ── prefab IO ──────────────────────────────────────────────────────────────

    private static Transform NewGroup(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void EnsureOutFolder()
    {
        if (AssetDatabase.IsValidFolder(OutFolder)) return;
        const string parent = "Assets/_Game/Environment";
        if (!AssetDatabase.IsValidFolder(parent))
            AssetDatabase.CreateFolder("Assets/_Game", "Environment");
        AssetDatabase.CreateFolder(parent, "Chunks");
    }

    private static void SaveChunk(GameObject root, string name)
    {
        string path = $"{OutFolder}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
    }
}
