using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using M = PropPalette.Mount;

/// <summary>
/// Living Map Milestone 1 — builds every data asset the runtime systems use and wires the open scene.
/// Menu: <b>Fluffterror › Living Map › Build Assets + Wire Scene</b> (or the single steps).
///  • PropPalettes per theme + kit swaps from the three kits under Assets/3DModels (editor-only paths here —
///    runtime code only ever sees the Inspector-assigned assets);
///  • landmark prefabs (kit pieces nested as prefab instances, like ChunkBuilder);
///  • SoundDefinitions for SFX/Ambient + import settings (one-shots ADPCM/preload, beds Vorbis/streaming);
///  • director events + profile, soundscape profile, the three run modifiers;
///  • Torch prefab: TorchStateController + its sounds;
///  • open scene: TensionDirector + RunSetup on GameManager, SoundscapeController on SoundManager, generator palettes.
/// Re-running overwrites the assets in place (same GUIDs) so scene references survive.
/// </summary>
public static class LivingMapAssetBuilder
{
    private const string Ruins = "Assets/3DModels/Ultimate Modular Ruins Pack - Aug 2021/FBX/";
    private const string Dungeon = "Assets/3DModels/Updated Modular Dungeon - May 2019/FBX/";
    private const string Furniture = "Assets/3DModels/Furniture Pack - March 2019/FBX/";

    private const string DataDir = "Assets/_Game/Environment/Data";
    private const string PaletteDir = DataDir + "/Palettes";
    private const string DirectorDir = DataDir + "/Director";
    private const string LandmarkDir = "Assets/_Game/Environment/Landmarks";
    private const string ModifierDir = "Assets/_Game/Mission/Data/RunModifiers";
    private const string AmbientDir = "Assets/_Game/Audio/SFX/Ambient";
    private const string TorchPrefab = "Assets/_Game/Environment/Torches/Torch.prefab";

    [MenuItem("Fluffterror/Living Map/Build Assets + Wire Scene")]
    public static void BuildAll()
    {
        BuildSounds();
        BuildLandmarks();
        BuildPalettes();
        BuildDirectorAndModifiers();
        BuildChunkLibrary();
        SetupTorchPrefab();
        WireOpenScene();
        AssetDatabase.SaveAssets();
        Debug.Log("[LivingMap] Assets built and scene wired. Regenerate the dungeon (DungeonGenerator › Generate).");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureDir(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        EnsureDir(Path.GetDirectoryName(path).Replace('\\', '/'));
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }

    private static GameObject Model(string kit, string name)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(kit + name + ".fbx");
        if (go == null) Debug.LogWarning($"[LivingMap] Missing kit model {kit}{name}.fbx");
        return go;
    }

    private static PropPalette.Entry E(string kit, string name, M mount, float weight = 1f, bool solid = true,
                                       float sMin = 1f, float sMax = 1f, float y = 0f, bool shadows = true,
                                       Vector3? rot = null, string display = "", bool randomYaw = true) => new()
    {
        Prefab = Model(kit, name), Weight = weight, Placement = mount, Solid = solid,
        ScaleRange = new Vector2(sMin, sMax), YOffset = y,
        CastShadows = shadows && mount != M.Wall && mount != M.Ceiling,   // wall decals: no shadow pass (perf)
        RotationOffset = rot ?? Vector3.zero, DisplayName = display, RandomYaw = randomYaw,
    };

    private static PropPalette Palette(string name, params PropPalette.Entry[] entries)
    {
        var p = LoadOrCreate<PropPalette>($"{PaletteDir}/{name}.asset");
        var list = new List<PropPalette.Entry>();
        foreach (var e in entries) if (e.Prefab != null) list.Add(e);
        p.EditorSetEntries(list.ToArray());
        EditorUtility.SetDirty(p);
        return p;
    }

    // ── palettes ─────────────────────────────────────────────────────────────

    [MenuItem("Fluffterror/Living Map/Steps/Build Palettes")]
    public static void BuildPalettes()
    {
        EnsureDir(PaletteDir);
        // Floor/against-wall furniture. Small clutter = not solid, no shadows (cheap, walk-through).
        Palette("PP_Gothic_Floor",
            E(Ruins, "Bookcase_Full", M.AgainstWall, 2f),
            E(Furniture, "Bookcase_Books", M.AgainstWall, 1.5f),
            E(Ruins, "Bookcase_Empty", M.AgainstWall, 0.8f),
            E(Furniture, "Closet", M.AgainstWall, 0.6f),
            E(Dungeon, "Barrel", M.AgainstWall, 1f),
            E(Dungeon, "Crate", M.AgainstWall, 1f),
            E(Dungeon, "Table_Small", M.Floor, 1.2f),
            E(Dungeon, "Table_Big", M.Floor, 0.6f),
            E(Dungeon, "Chair", M.Floor, 1f, solid: false, shadows: false),
            E(Ruins, "Chest", M.Floor, 0.6f),
            E(Ruins, "Pot1", M.Floor, 0.8f),
            E(Ruins, "Pot2_Broken", M.Floor, 0.8f, solid: false, shadows: false),
            E(Ruins, "Bricks", M.Floor, 1f, solid: false, shadows: false),
            E(Dungeon, "Bucket", M.Floor, 0.5f, solid: false, shadows: false),
            E(Ruins, "Cart", M.Floor, 0.3f, sMin: 0.6f, sMax: 0.7f));
        Palette("PP_Gothic_Wall",
            E(Dungeon, "Banner_wall", M.Wall, 1.2f, solid: false, y: 1.2f),
            E(Ruins, "Flag_Wall", M.Wall, 1f, solid: false, y: 1.4f),
            E(Dungeon, "Sword_WallMount", M.Wall, 0.6f, solid: false, y: 1.6f, shadows: false),
            E(Dungeon, "Cobweb", M.Ceiling, 1.2f, solid: false, shadows: false),
            E(Dungeon, "Cobweb2", M.Ceiling, 1f, solid: false, shadows: false));
        Palette("PP_Transition_Floor",
            E(Dungeon, "Barrel2", M.AgainstWall, 1f),
            E(Dungeon, "Crate", M.AgainstWall, 1f, sMin: 0.7f, sMax: 0.9f),
            E(Ruins, "Pot3", M.AgainstWall, 0.6f),
            E(Ruins, "Bricks", M.Floor, 1f, solid: false, shadows: false),
            E(Dungeon, "Skull", M.Floor, 0.6f, solid: false, shadows: false));
        Palette("PP_Transition_Wall",
            E(Dungeon, "Cobweb", M.Ceiling, 1.5f, solid: false, shadows: false),
            E(Dungeon, "Cobweb2", M.Ceiling, 1f, solid: false, shadows: false),
            E(Dungeon, "Banner_wall", M.Wall, 0.5f, solid: false, y: 1.2f));
        Palette("PP_Graveyard_Floor",
            E(Ruins, "Column_Round_Short", M.Floor, 1.2f),
            E(Ruins, "Wall_Broken", M.Floor, 0.6f),
            E(Ruins, "Wall_Double_Broken", M.Floor, 0.6f),
            E(Ruins, "Rail_Straight", M.Floor, 0.8f),
            E(Ruins, "Bricks", M.Floor, 1f, solid: false, shadows: false),
            E(Ruins, "Pot1_Broken", M.Floor, 0.6f, solid: false, shadows: false),
            E(Ruins, "Skull", M.Floor, 0.5f, solid: false, shadows: false),
            E(Ruins, "Grass", M.Floor, 1.2f, solid: false, shadows: false, sMin: 0.8f, sMax: 1.3f),
            E(Ruins, "Bush_Round", M.Floor, 0.5f, solid: false, sMin: 0.7f, sMax: 1f));
        Palette("PP_Hell_Floor",
            E(Dungeon, "Spikes", M.Floor, 1f),
            E(Dungeon, "Trap_spikes", M.Floor, 0.6f),
            E(Ruins, "BearTrap_Open", M.Floor, 0.8f, solid: false, shadows: false),
            E(Dungeon, "Woodfire", M.Floor, 0.6f, solid: false),
            E(Ruins, "Bricks", M.Floor, 1f, solid: false, shadows: false),
            E(Dungeon, "Skull", M.Floor, 1f, solid: false, shadows: false));
        Palette("PP_Hell_Wall",
            E(Dungeon, "Banner_wall", M.Wall, 1f, solid: false, y: 1.6f),
            E(Ruins, "Flag_Wall2", M.Wall, 0.8f, solid: false, y: 1.6f),
            E(Dungeon, "Sword_WallMount", M.Wall, 0.6f, solid: false, y: 1.8f, shadows: false));
        Palette("PP_Catacomb_Floor",
            E(Dungeon, "Pedestal", M.AgainstWall, 0.5f, sMin: 0.6f, sMax: 0.7f),
            E(Ruins, "Chest", M.AgainstWall, 0.6f),
            E(Ruins, "Pot1", M.AgainstWall, 1f),
            E(Ruins, "Pot3", M.AgainstWall, 1f),
            E(Ruins, "Pot2", M.Floor, 0.8f, solid: false, shadows: false),
            E(Ruins, "Pot3_Broken", M.Floor, 0.8f, solid: false, shadows: false),
            E(Ruins, "Skull", M.Floor, 1.2f, solid: false, shadows: false),
            E(Ruins, "Candles_2", M.Floor, 0.8f, solid: false, shadows: false, sMin: 0.6f, sMax: 0.8f),
            E(Dungeon, "Coin_Pile", M.Floor, 0.4f, solid: false, shadows: false, sMin: 2f, sMax: 3f),
            E(Dungeon, "Vase", M.Floor, 0.6f, solid: false, shadows: false, sMin: 1.4f, sMax: 1.8f));
        Palette("PP_Catacomb_Wall",
            E(Dungeon, "Cobweb", M.Ceiling, 1.4f, solid: false, shadows: false),
            E(Dungeon, "Cobweb2", M.Ceiling, 1f, solid: false, shadows: false));
        Palette("PP_Cavern_Floor",
            E(Ruins, "Cart", M.Floor, 0.4f, sMin: 0.6f, sMax: 0.7f),
            E(Dungeon, "Barrel", M.Floor, 0.8f),
            E(Dungeon, "Crate", M.Floor, 0.8f),
            E(Dungeon, "Bucket", M.Floor, 0.6f, solid: false, shadows: false),
            E(Dungeon, "Woodfire", M.Floor, 0.4f, solid: false),
            E(Dungeon, "Floor_BricksSeparate2", M.Floor, 1f, solid: false, shadows: false),
            E(Ruins, "Bricks", M.Floor, 1f, solid: false, shadows: false),
            E(Dungeon, "Bag_Standing", M.Floor, 0.5f, solid: false, shadows: false, sMin: 1.5f, sMax: 2f),
            E(Dungeon, "Skull", M.Floor, 0.6f, solid: false, shadows: false));
        Palette("PP_Cavern_Wall",
            E(Dungeon, "Cobweb2", M.Ceiling, 1f, solid: false, shadows: false, sMin: 1.4f, sMax: 1.8f));

        // Kit swaps for the primitive builders.
        Palette("PP_Kit_HellPillars",
            E(Dungeon, "Column", M.Floor, 1f, sMin: 1.45f, sMax: 1.5f),
            E(Dungeon, "Column2", M.Floor, 1f, sMin: 1.45f, sMax: 1.5f),
            E(Ruins, "Column_Square", M.Floor, 0.6f, sMin: 1.5f, sMax: 1.55f));
        Palette("PP_Kit_DeadTrees",
            E(Ruins, "DeadTree_1", M.Floor, 1f, sMin: 1.3f, sMax: 1.7f),
            E(Ruins, "DeadTree_2", M.Floor, 1f, sMin: 1.3f, sMax: 1.6f),
            E(Ruins, "DeadTree_3", M.Floor, 1f, sMin: 1.1f, sMax: 1.4f));
        Palette("PP_Kit_Windows",
            E(Ruins, "Window_Bars", M.Wall, 1f, solid: false, y: 1.4f),
            E(Ruins, "Window_Bars_Overgrown", M.Wall, 0.5f, solid: false, y: 1.4f),
            E(Ruins, "Window_Open", M.Wall, 0.6f, solid: false, y: 1.4f));
        Palette("PP_Kit_CellBars",
            E(Dungeon, "Arch_bars", M.Wall, 1f, solid: false, y: 0f, sMin: 0.8f, sMax: 0.8f));
        Palette("PP_Kit_WallPanels",
            E(Dungeon, "Banner_wall", M.Wall, 1f, solid: false, y: 1.3f),
            E(Ruins, "Flag_Wall", M.Wall, 1f, solid: false, y: 1.5f),
            E(Dungeon, "Sword_WallMount", M.Wall, 0.6f, solid: false, y: 1.7f, shadows: false),
            E(Dungeon, "Decorative_Wall", M.Wall, 0.8f, solid: false, y: 1.2f));
        Palette("PP_Kit_BonePiles",
            E(Ruins, "Skull", M.Floor, 1.2f, solid: false, shadows: false),
            E(Dungeon, "Skull", M.Floor, 1f, solid: false, shadows: false),
            E(Ruins, "Pot2_Broken", M.Floor, 0.6f, solid: false, shadows: false),
            E(Ruins, "Bricks", M.Floor, 0.6f, solid: false, shadows: false),
            E(Ruins, "Candles_1", M.Floor, 0.6f, solid: false, shadows: false, sMin: 0.6f, sMax: 0.7f));
        Palette("PP_Kit_Candles",
            E(Ruins, "Candles_1", M.Floor, 1f, solid: false, shadows: false, sMin: 0.28f, sMax: 0.32f),
            E(Ruins, "Candles_2", M.Floor, 1f, solid: false, shadows: false, sMin: 0.28f, sMax: 0.32f));

        // Landmarks per theme (prefabs from BuildLandmarks).
        PropPalette.Entry L(string prefab, string display, float w = 1f)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{LandmarkDir}/{prefab}.prefab");
            return new PropPalette.Entry { Prefab = go, Weight = w, Placement = M.Floor, Solid = true, DisplayName = display, ScaleRange = Vector2.one, CastShadows = true };
        }
        Palette("PP_Landmarks_Gothic", L("LM_BellArch", "the Bell"), L("LM_FoxStatue", "the Fox Statue"), L("LM_Shrine", "the Shrine"), L("LM_BrokenArch", "the Broken Arch"));
        Palette("PP_Landmarks_Graveyard", L("LM_DeadTree", "the Hanging Tree"), L("LM_StagStatue", "the Stag"), L("LM_BellArch", "the Bell"), L("LM_Well", "the Well"));
        Palette("PP_Landmarks_Hell", L("LM_Bonfire", "the Bonfire"), L("LM_SpikePit", "the Spike Pit"), L("LM_HorseStatue", "the Horse"), L("LM_BrokenArch", "the Broken Arch"));
        Palette("PP_Landmarks_Catacombs", L("LM_Shrine", "the Shrine"), L("LM_FoxStatue", "the Fox Statue"), L("LM_Well", "the Well"), L("LM_BrokenArch", "the Broken Arch"));
        Palette("PP_Landmarks_Cavern", L("LM_Wagon", "the Wagon"), L("LM_Bonfire", "the Bonfire"), L("LM_HorseStatue", "the Horse"), L("LM_DeadTree", "the Hanging Tree"));
        AssetDatabase.SaveAssets();
    }

    // ── landmark prefabs ─────────────────────────────────────────────────────

    private static GameObject Piece(Transform parent, string kit, string name, Vector3 pos, float yaw = 0f, float scale = 1f)
    {
        var src = Model(kit, name);
        if (src == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * src.transform.localRotation;
        go.transform.localScale = src.transform.localScale * scale;
        // Rest the piece on y = pos.y with its bounds centred on pos.xz.
        Bounds b = default; bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        Vector3 world = parent.TransformPoint(pos);
        if (any) go.transform.position += new Vector3(world.x - b.center.x, world.y - b.min.y, world.z - b.center.z);
        return go;
    }

    private static GameObject Prim(Transform parent, PrimitiveType t, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    private static void SaveLandmark(GameObject root, string name)
    {
        EnsureDir(LandmarkDir);
        PrefabUtility.SaveAsPrefabAsset(root, $"{LandmarkDir}/{name}.prefab");
        Object.DestroyImmediate(root);
    }

    [MenuItem("Fluffterror/Living Map/Steps/Build Landmark Prefabs")]
    public static void BuildLandmarks()
    {
        EnsureDir(LandmarkDir);
        var bronze = LoadOrCreateMaterial($"{LandmarkDir}/M_LandmarkBronze.mat", new Color(0.62f, 0.46f, 0.2f), 0.6f, 0.45f);
        var rope = LoadOrCreateMaterial($"{LandmarkDir}/M_LandmarkRope.mat", new Color(0.25f, 0.2f, 0.14f), 0f, 0.1f);

        { // The Bell: a ruined arch with a big bronze bell (primitive bell, kit frame).
            var r = new GameObject("LM_BellArch").transform;
            var beam = LoadOrCreateMaterial($"{LandmarkDir}/M_LandmarkBeam.mat", new Color(0.2f, 0.14f, 0.09f), 0f, 0.1f);
            Piece(r, Ruins, "Column_Round", new Vector3(-1.5f, 0, 0));
            Piece(r, Ruins, "Column_Round", new Vector3(1.5f, 0, 0));
            Prim(r, PrimitiveType.Cube, "Beam", new Vector3(0, 4.12f, 0), Vector3.zero, new Vector3(3.9f, 0.32f, 0.36f), beam);
            Prim(r, PrimitiveType.Cylinder, "Rope", new Vector3(0, 3.72f, 0), Vector3.zero, new Vector3(0.06f, 0.26f, 0.06f), rope);
            Prim(r, PrimitiveType.Sphere, "BellTop", new Vector3(0, 3.3f, 0), Vector3.zero, new Vector3(0.8f, 0.62f, 0.8f), bronze);
            Prim(r, PrimitiveType.Cylinder, "BellBody", new Vector3(0, 2.95f, 0), Vector3.zero, new Vector3(0.9f, 0.38f, 0.9f), bronze);
            Prim(r, PrimitiveType.Cylinder, "BellLip", new Vector3(0, 2.6f, 0), Vector3.zero, new Vector3(1.12f, 0.05f, 1.12f), bronze);
            Prim(r, PrimitiveType.Sphere, "Clapper", new Vector3(0, 2.45f, 0), Vector3.zero, new Vector3(0.2f, 0.2f, 0.2f), bronze);
            Piece(r, Ruins, "Bricks", new Vector3(1.4f, 0, 1.1f), 30f);
            Piece(r, Ruins, "Brick", new Vector3(-1.1f, 0, -1.0f), 70f);
            SaveLandmark(r.gameObject, "LM_BellArch");
        }
        { // Fox statue on a stone base with candles.
            var r = new GameObject("LM_FoxStatue").transform;
            Piece(r, Ruins, "Floor_SquareLarge", Vector3.zero, 0f, 1.2f);
            Piece(r, Ruins, "Statue_Fox", new Vector3(0, 0.07f, 0));
            Piece(r, Ruins, "Candles_1", new Vector3(1.05f, 0.07f, 0.9f), 0f, 0.5f);
            Piece(r, Ruins, "Candles_2", new Vector3(-1.0f, 0.07f, -0.9f), 0f, 0.5f);
            SaveLandmark(r.gameObject, "LM_FoxStatue");
        }
        { // Stag statue in rubble.
            var r = new GameObject("LM_StagStatue").transform;
            Piece(r, Ruins, "Statue_Stag", Vector3.zero, 0f, 1.1f);
            Piece(r, Ruins, "Bricks", new Vector3(1.4f, 0, -0.6f), 70f);
            Piece(r, Ruins, "Brick", new Vector3(-1.2f, 0, 0.7f), 20f);
            SaveLandmark(r.gameObject, "LM_StagStatue");
        }
        { // Horse statue.
            var r = new GameObject("LM_HorseStatue").transform;
            Piece(r, Dungeon, "Statue_Horse", Vector3.zero, 0f, 0.85f);
            Piece(r, Dungeon, "Skull", new Vector3(1.0f, 0, 1.1f));
            SaveLandmark(r.gameObject, "LM_HorseStatue");
        }
        { // A big dead tree fenced with rails, skulls at its feet.
            var r = new GameObject("LM_DeadTree").transform;
            Piece(r, Ruins, "DeadTree_3", Vector3.zero, 0f, 2.2f);
            Piece(r, Ruins, "Rail_Straight", new Vector3(2.4f, 0, 0), 0f);
            Piece(r, Ruins, "Rail_Straight", new Vector3(-2.4f, 0, 0), 0f);
            Piece(r, Ruins, "Rail_Straight", new Vector3(0, 0, 2.4f), 90f);
            Piece(r, Ruins, "Skull", new Vector3(0.9f, 0, -1.2f), 140f);
            Piece(r, Ruins, "Skull", new Vector3(-1.1f, 0, -0.6f), 220f);
            SaveLandmark(r.gameObject, "LM_DeadTree");
        }
        { // Bonfire ringed with skulls — brings its own warm flickering light.
            var r = new GameObject("LM_Bonfire").transform;
            Piece(r, Dungeon, "Woodfire", Vector3.zero, 0f, 1.8f);
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f;
                Piece(r, Ruins, "Skull", new Vector3(Mathf.Cos(a) * 1.5f, 0, Mathf.Sin(a) * 1.5f), -a * Mathf.Rad2Deg + 90f, 0.8f);
            }
            var lg = new GameObject("FireLight"); lg.transform.SetParent(r, false); lg.transform.localPosition = new Vector3(0, 1.1f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.5f, 0.18f); l.intensity = 2.6f; l.range = 9f; l.shadows = LightShadows.None;
            lg.AddComponent<LightFlicker>();
            SaveLandmark(r.gameObject, "LM_Bonfire");
        }
        { // Shrine: pedestal, golden chest, candles, two standing banners.
            var r = new GameObject("LM_Shrine").transform;
            Piece(r, Dungeon, "Pedestal2", Vector3.zero, 0f, 0.55f);
            Piece(r, Ruins, "Chest_Gold", new Vector3(0, 1.27f, 0), 0f, 0.8f);
            Piece(r, Dungeon, "Banner", new Vector3(1.7f, 0, 0), 0f, 0.8f);
            Piece(r, Dungeon, "Banner", new Vector3(-1.7f, 0, 0), 0f, 0.8f);
            Piece(r, Ruins, "Candles_1", new Vector3(0.9f, 0, 1.0f), 0f, 0.5f);
            Piece(r, Ruins, "Candles_2", new Vector3(-0.9f, 0, 1.0f), 0f, 0.5f);
            SaveLandmark(r.gameObject, "LM_Shrine");
        }
        { // Spike pit: a square of spike traps fenced by broken low walls.
            var r = new GameObject("LM_SpikePit").transform;
            Piece(r, Dungeon, "Trap_spikes", new Vector3(-1f, 0, -1f));
            Piece(r, Dungeon, "Trap_spikes", new Vector3(1f, 0, -1f));
            Piece(r, Dungeon, "Trap_spikes", new Vector3(-1f, 0, 1f));
            Piece(r, Dungeon, "Trap_spikes", new Vector3(1f, 0, 1f));
            Piece(r, Ruins, "Wall_Double_Broken", new Vector3(0, 0, 2.4f));
            Piece(r, Ruins, "Wall_Double_Broken", new Vector3(2.4f, 0, 0), 90f);
            Piece(r, Dungeon, "Skull", new Vector3(-0.4f, 1.17f, 0.3f));
            SaveLandmark(r.gameObject, "LM_SpikePit");
        }
        { // Abandoned wagon with cargo.
            var r = new GameObject("LM_Wagon").transform;
            Piece(r, Ruins, "Cart", Vector3.zero, 20f, 0.8f);
            Piece(r, Dungeon, "Barrel", new Vector3(2.0f, 0, -1.2f));
            Piece(r, Dungeon, "Barrel2", new Vector3(2.4f, 0, -0.1f));
            Piece(r, Dungeon, "Crate", new Vector3(-2.0f, 0, 1.3f), 15f);
            Piece(r, Dungeon, "Bag_Coins", new Vector3(-1.4f, 0, -1.4f), 0f, 2f);
            SaveLandmark(r.gameObject, "LM_Wagon");
        }
        { // Well: ring of short columns + a low wall ring + a bucket on a rope.
            var r = new GameObject("LM_Well").transform;
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                Piece(r, Ruins, "Rail_Divider", new Vector3(Mathf.Cos(a) * 1.0f, 0, Mathf.Sin(a) * 1.0f), -a * Mathf.Rad2Deg, 1.6f);
            }
            var wellMat = LoadOrCreateMaterial($"{LandmarkDir}/M_LandmarkWater.mat", new Color(0.02f, 0.03f, 0.04f), 0f, 0.9f);
            Prim(r, PrimitiveType.Cylinder, "Water", new Vector3(0, 0.3f, 0), Vector3.zero, new Vector3(1.8f, 0.02f, 1.8f), wellMat);
            Piece(r, Ruins, "Column_Round_Short", new Vector3(1.15f, 0, 0), 0f, 1.2f);
            Piece(r, Ruins, "Column_Round_Short", new Vector3(-1.15f, 0, 0), 0f, 1.2f);
            Prim(r, PrimitiveType.Cylinder, "Beam", new Vector3(0, 2.25f, 0), new Vector3(0, 0, 90), new Vector3(0.12f, 1.25f, 0.12f), rope);
            Piece(r, Dungeon, "Bucket", new Vector3(0, 1.3f, 0));
            Prim(r, PrimitiveType.Cylinder, "Rope", new Vector3(0, 1.95f, 0), Vector3.zero, new Vector3(0.03f, 0.3f, 0.03f), rope);
            SaveLandmark(r.gameObject, "LM_Well");
        }
        { // A lone broken arch with rubble.
            var r = new GameObject("LM_BrokenArch").transform;
            Piece(r, Ruins, "Wall_ArchRound_Broken", Vector3.zero);
            Piece(r, Ruins, "Bricks", new Vector3(1.4f, 0, 1.0f), 45f);
            Piece(r, Ruins, "Bricks", new Vector3(-1.6f, 0, -0.9f), 200f);
            Piece(r, Ruins, "Column_Round_Short", new Vector3(2.6f, 0, -1.2f));
            SaveLandmark(r.gameObject, "LM_BrokenArch");
        }
        AssetDatabase.SaveAssets();
    }

    private static Material LoadOrCreateMaterial(string path, Color c, float metallic, float smooth)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ── sounds ───────────────────────────────────────────────────────────────

    [MenuItem("Fluffterror/Living Map/Steps/Build Sound Definitions")]
    public static void BuildSounds()
    {
        var sfxGroup = FirstGroup("Assets/_Game/Audio/SFX.mixer");
        var actGroup = FirstGroup("Assets/_Game/Audio/Actions.mixer");
        SD("SD_Amb_Drip", "Drip", 1f, new Vector2(0.9f, 1.12f), 1f, actGroup, 2f, false);
        SD("SD_Amb_Distant", "Distant", 1f, new Vector2(0.9f, 1.05f), 1f, actGroup, 10f, false);
        SD("SD_Amb_Bell", "Bell", 0.85f, new Vector2(0.94f, 1.06f), 1f, actGroup, 10f, false);
        SD("SD_Torch_Gutter", "TorchGutter", 0.9f, new Vector2(0.92f, 1.08f), 1f, actGroup, 3f, false);
        SD("SD_Torch_Out", "TorchOut", 0.9f, new Vector2(0.92f, 1.08f), 1f, actGroup, 3f, false);
        SD("SD_Torch_Relight", "TorchRelight", 0.9f, new Vector2(0.94f, 1.06f), 1f, actGroup, 3f, false);
        SD("SD_Amb_Sting", "Sting", 0.8f, new Vector2(0.97f, 1.03f), 0f, sfxGroup, 1f, false);
        foreach (var bed in new[] { "BedGothic", "BedCatacomb", "BedCavern", "BedGraveyard", "BedHell" })
            SD("SD_" + bed, bed, 1f, Vector2.one, 0f, sfxGroup, 1f, true);
        AssetDatabase.SaveAssets();
    }

    private static AudioMixerGroup FirstGroup(string mixerPath)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(mixerPath))
            if (o is AudioMixerGroup g) return g;
        return null;
    }

    private static void SD(string name, string clipGroup, float vol, Vector2 pitch, float spatial, AudioMixerGroup group, float minDist, bool bed)
    {
        var clips = new List<AudioClip>();
        foreach (var guid in AssetDatabase.FindAssets($"SFX_Ambient_{clipGroup}_ t:AudioClip", new[] { AmbientDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileName(p).StartsWith($"SFX_Ambient_{clipGroup}_")) continue;
            ConfigureImport(p, bed);
            clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(p));
        }
        clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        var sd = LoadOrCreate<SoundDefinition>($"{AmbientDir}/{name}.asset");
        var so = new SerializedObject(sd);
        var arr = so.FindProperty("_clips");
        arr.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        so.FindProperty("_volume").floatValue = vol;
        so.FindProperty("_pitchRange").vector2Value = pitch;
        so.FindProperty("_spatialBlend").floatValue = spatial;
        so.FindProperty("_mixerGroup").objectReferenceValue = group;
        so.FindProperty("_minDistance").floatValue = minDist;
        so.FindProperty("_maxDistance").floatValue = 500f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureImport(string path, bool bed)
    {
        if (AssetImporter.GetAtPath(path) is not AudioImporter imp) return;
        var s = imp.defaultSampleSettings;
        if (bed)
        {
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            s.preloadAudioData = false;
        }
        else
        {
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.ADPCM;
            s.preloadAudioData = true;
        }
        imp.defaultSampleSettings = s;
        imp.forceToMono = true;
        imp.SaveAndReimport();
    }

    private static SoundDefinition LoadSD(string name) => AssetDatabase.LoadAssetAtPath<SoundDefinition>($"{AmbientDir}/{name}.asset");

    // ── director, soundscape, modifiers ──────────────────────────────────────

    [MenuItem("Fluffterror/Living Map/Steps/Build Director + Modifiers")]
    public static void BuildDirectorAndModifiers()
    {
        EnsureDir(DirectorDir);
        AmbientEventDefinition Ev(string name, AmbientEventDefinition.EventKind kind, AmbientEventDefinition.PhaseMask phases,
                                  float weight, float cooldown, Vector2 dist, Vector2 dur, float tension, SoundDefinition sound = null)
        {
            var e = LoadOrCreate<AmbientEventDefinition>($"{DirectorDir}/{name}.asset");
            e.Kind = kind; e.Phases = phases; e.Weight = weight; e.Cooldown = cooldown; e.Distance = dist;
            e.Duration = dur; e.TensionAdd = tension; e.Sound = sound; e.PreferOccluded = true;
            EditorUtility.SetDirty(e);
            return e;
        }
        var P = AmbientEventDefinition.PhaseMask.Calm; var B = AmbientEventDefinition.PhaseMask.BuildUp;
        var R = AmbientEventDefinition.PhaseMask.Relief;
        var gutter = Ev("AE_TorchGutter", AmbientEventDefinition.EventKind.TorchGutter, P | B | R, 2f, 14f, new Vector2(4f, 16f), new Vector2(1.5f, 3.5f), 0.02f);
        var die = Ev("AE_TorchDie", AmbientEventDefinition.EventKind.TorchDie, B, 1.6f, 22f, new Vector2(7f, 22f), new Vector2(1.2f, 2.5f), 0.05f);
        die.RelightAfter = new Vector2(30f, 70f); EditorUtility.SetDirty(die);
        var stutter = Ev("AE_LightStutter", AmbientEventDefinition.EventKind.LightStutter, B, 0.8f, 40f, new Vector2(0f, 12f), new Vector2(0.8f, 2f), 0.05f);
        stutter.Radius = 12f; EditorUtility.SetDirty(stutter);
        var distant = Ev("AE_DistantSound", AmbientEventDefinition.EventKind.DistantSound, P | B | R, 2.5f, 10f, new Vector2(15f, 35f), Vector2.one, 0.015f, LoadSD("SD_Amb_Distant"));
        var bell = Ev("AE_FakeBell", AmbientEventDefinition.EventKind.CreatureCue, B, 1f, 35f, new Vector2(14f, 28f), Vector2.one, 0.05f, LoadSD("SD_Amb_Bell"));
        bell.Cue = CreatureCue.BellJingle; EditorUtility.SetDirty(bell);
        var sting = Ev("AE_SpikeSting", AmbientEventDefinition.EventKind.Sting, AmbientEventDefinition.PhaseMask.Spike, 0f, 5f, Vector2.zero, Vector2.one, 0f, LoadSD("SD_Amb_Sting"));

        var dp = LoadOrCreate<DirectorProfile>($"{DirectorDir}/DP_Default.asset");
        dp.Events = new[] { gutter, die, stutter, distant, bell };
        dp.SpikeEntryEvent = sting;
        // Pacing: first build-up ≈ 50 s, first spike ≈ 1:45, then a cycle every ~2–2.5 min
        // (a 10-min session gets 3–4 spikes and plenty of smaller moments).
        dp.CalmRise = 0.005f; dp.BuildUpRise = 0.0065f; dp.RiseJitter = 0.003f;
        dp.BuildUpAt = 0.35f; dp.SpikeAt = 0.8f;
        dp.SpikeDuration = new Vector2(8f, 14f); dp.ReliefDuration = new Vector2(20f, 35f);
        dp.EventsPerMinute = new Vector4(2f, 5f, 0f, 1f); dp.MinEventGap = 6f; dp.StartGrace = 12f;
        EditorUtility.SetDirty(dp);

        var ssp = LoadOrCreate<SoundscapeProfile>($"{DirectorDir}/SSP_Default.asset");
        ssp.Beds = new[]
        {
            new SoundscapeProfile.Bed { Section = MultiSectionDungeonGenerator.SectionType.GothicRuins, Loop = LoadSD("SD_BedGothic") },
            new SoundscapeProfile.Bed { Section = MultiSectionDungeonGenerator.SectionType.Catacombs, Loop = LoadSD("SD_BedCatacomb") },
            new SoundscapeProfile.Bed { Section = MultiSectionDungeonGenerator.SectionType.Cavern, Loop = LoadSD("SD_BedCavern") },
            new SoundscapeProfile.Bed { Section = MultiSectionDungeonGenerator.SectionType.Graveyard, Loop = LoadSD("SD_BedGraveyard") },
            new SoundscapeProfile.Bed { Section = MultiSectionDungeonGenerator.SectionType.HellPillars, Loop = LoadSD("SD_BedHell") },
        };
        ssp.DefaultBed = LoadSD("SD_BedCatacomb");
        ssp.Drip = LoadSD("SD_Amb_Drip");
        ssp.BedVolume = 0.5f;   // beds mastered at -30 LUFS → ≈ -36 in game: level with the music bed, far under footsteps (-20)
        EditorUtility.SetDirty(ssp);

        EnsureDir(ModifierDir);
        var blackout = LoadOrCreate<BlackoutModifier>($"{ModifierDir}/RM_Blackout.asset");
        SetModifierText(blackout, "Blackout", "half the torches are dead", new Color(0.65f, 0.74f, 0.95f));
        var fog = LoadOrCreate<FogModifier>($"{ModifierDir}/RM_Fog.asset");
        SetModifierText(fog, "Fog", "you can't see far, neither can it", new Color(0.78f, 0.82f, 0.8f));
        var extra = LoadOrCreate<ExtraCreatureModifier>($"{ModifierDir}/RM_ExtraCreature.asset");
        SetModifierText(extra, "Extra creature", "something else is down here", new Color(0.85f, 0.35f, 0.33f));
        AssetDatabase.SaveAssets();
    }

    private static void SetModifierText(RunModifier m, string display, string desc, Color c)
    {
        var so = new SerializedObject(m);
        so.FindProperty("_displayName").stringValue = display;
        so.FindProperty("_description").stringValue = desc;
        so.FindProperty("_cardColor").colorValue = c;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── torch prefab ─────────────────────────────────────────────────────────

    [MenuItem("Fluffterror/Living Map/Steps/Setup Torch Prefab")]
    public static void SetupTorchPrefab()
    {
        var root = PrefabUtility.LoadPrefabContents(TorchPrefab);
        var tsc = root.GetComponent<TorchStateController>();
        if (tsc == null) tsc = root.AddComponent<TorchStateController>();
        var so = new SerializedObject(tsc);
        so.FindProperty("_gutterSound").objectReferenceValue = LoadSD("SD_Torch_Gutter");
        so.FindProperty("_outSound").objectReferenceValue = LoadSD("SD_Torch_Out");
        so.FindProperty("_relightSound").objectReferenceValue = LoadSD("SD_Torch_Relight");
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, TorchPrefab);
        PrefabUtility.UnloadPrefabContents(root);
    }


    // ── authored chunks ──────────────────────────────────────────────────────

    private const string ChunkDir = "Assets/Art/Environment/Chunks";
    private const string ChunkLibraryPath = DataDir + "/ChunkLibrary.asset";

    [System.Serializable] private class JManifest { public JChunk[] chunks; }
    [System.Serializable] private class JChunk { public string name; public string role; public int[] size_m; public float wall_height; public JTags tags; public JDoor[] doors; public string[] mask_north_up; public JSocket[] sockets; }
    [System.Serializable] private class JTags { public string[] themes; public string role; public float weight; public int min; public int max; public int torches_suggested; public string near; }
    [System.Serializable] private class JDoor { public string label; public string side; public float offset; }
    [System.Serializable] private class JSocket { public string name; public float[] pos; public float[] forward; }

    // Call-out names for chunks that can stand in as a section landmark (they have a SOCKET_Landmark).
    private static readonly Dictionary<string, string> ChunkDisplayNames = new()
    {
        { "CH_Room_PillarHall", "the Pillar Hall" }, { "CH_Room_Ritual", "the Ritual Altar" }, { "CH_SP_BellTower", "the Bell Tower" },
        { "CH_SP_ChuchoDen", "the Den" }, { "CH_SP_Ossuary", "the Ossuary" }, { "CH_Room_Chapel", "the Chapel" },
        { "CH_Room_Library", "the Library" }, { "CH_Room_SpiralHall", "the Spiral Hall" }, { "CH_Room_Mausoleum", "the Mausoleum" },
        { "CH_Extraction_Gate", "the Gate" }, { "CH_SP_WellRoom", "the Well Room" }, { "CH_SP_PitBridge", "the Pit Bridge" },
        { "CH_SP_FloodedCrypt", "the Flooded Crypt" }, { "CH_Room_PrisonBlock", "the Prison" }, { "CH_Room_Crypt", "the Crypt" },
        { "CH_SP_SewerChannel", "the Sewer" }, { "CH_SP_CaveTransition", "the Crystal Cave" }, { "CH_Room_Larder", "the Larder" },
    };

    [MenuItem("Fluffterror/Living Map/Steps/Build Chunk Library")]
    public static void BuildChunkLibrary()
    {
        string json = File.ReadAllText(ChunkDir + "/chunks_manifest.json");
        var m = JsonUtility.FromJson<JManifest>(json);
        var entries = new List<ChunkLibrary.Entry>();
        foreach (var c in m.chunks)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ChunkDir}/{c.name}.prefab");
            if (prefab == null) { Debug.LogWarning($"[LivingMap] Chunk prefab missing: {c.name}"); continue; }
            var doors = new List<ChunkLibrary.Door>();
            foreach (var d in c.doors)
            {
                JSocket sock = null;
                foreach (var so in c.sockets) if (so.name == "SOCKET_Door_" + d.label) sock = so;
                if (sock == null) continue;
                doors.Add(new ChunkLibrary.Door
                {
                    Label = d.label, Side = d.side[0],
                    LocalPos = new Vector3(sock.pos[0], sock.pos[1], sock.pos[2]),
                    LocalForward = new Vector3(sock.forward[0], sock.forward[1], sock.forward[2]),
                });
            }
            var t = c.tags ?? new JTags { themes = new[] { "Any" }, weight = 1, max = 1 };
            entries.Add(new ChunkLibrary.Entry
            {
                Name = c.name, Prefab = prefab, Role = string.IsNullOrEmpty(t.role) ? c.role : t.role,
                Size = new Vector2Int(c.size_m[0], c.size_m[1]), WallHeight = c.wall_height,
                Themes = t.themes ?? new[] { "Any" }, Weight = t.weight, Min = t.min, Max = t.max,
                TorchesSuggested = t.torches_suggested, Near = t.near,
                DisplayName = ChunkDisplayNames.TryGetValue(c.name, out var dn) ? dn : null,
                Mask = c.mask_north_up, Doors = doors.ToArray(),
            });
        }
        var lib = LoadOrCreate<ChunkLibrary>(ChunkLibraryPath);
        lib.EditorSetEntries(entries.ToArray());
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LivingMap] Chunk library: {entries.Count} chunks.");
    }

    // ── scene wiring ─────────────────────────────────────────────────────────

    [MenuItem("Fluffterror/Living Map/Steps/Wire Open Scene")]
    public static void WireOpenScene()
    {
        var gm = GameObject.Find("GameManager");
        var sm = GameObject.Find("SoundManager");
        var gen = Object.FindAnyObjectByType<MultiSectionDungeonGenerator>();
        if (gm == null || sm == null || gen == null) { Debug.LogWarning("[LivingMap] Scene needs GameManager, SoundManager and a MultiSectionDungeonGenerator."); return; }

        var director = gm.GetComponent<TensionDirector>();
        if (director == null) director = Undo.AddComponent<TensionDirector>(gm);
        var dso = new SerializedObject(director);
        dso.FindProperty("_profile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DirectorProfile>($"{DirectorDir}/DP_Default.asset");
        dso.FindProperty("_moods").objectReferenceValue = gm.GetComponent<LightingMoodController>();
        dso.ApplyModifiedProperties();

        var setup = gm.GetComponent<RunSetup>();
        if (setup == null) setup = Undo.AddComponent<RunSetup>(gm);
        var rso = new SerializedObject(setup);
        var pool = rso.FindProperty("_pool");
        string[] mods = { "RM_Blackout", "RM_Fog", "RM_ExtraCreature" };
        pool.arraySize = mods.Length;
        for (int i = 0; i < mods.Length; i++)
            pool.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<RunModifier>($"{ModifierDir}/{mods[i]}.asset");
        rso.FindProperty("_messages").objectReferenceValue = Object.FindAnyObjectByType<MissionMessageUI>();
        rso.ApplyModifiedProperties();

        if (gm.GetComponent<ChunkDistanceCuller>() == null) Undo.AddComponent<ChunkDistanceCuller>(gm);   // chunk-grid maps: fog-range culling

        var scape = sm.GetComponent<SoundscapeController>();
        if (scape == null) scape = Undo.AddComponent<SoundscapeController>(sm);
        var sso = new SerializedObject(scape);
        sso.FindProperty("_profile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SoundscapeProfile>($"{DirectorDir}/SSP_Default.asset");
        sso.ApplyModifiedProperties();

        // Generator palettes.
        PropPalette Pal(string n) => AssetDatabase.LoadAssetAtPath<PropPalette>($"{PaletteDir}/{n}.asset");
        var g = new SerializedObject(gen);
        var dressing = g.FindProperty("Dressing");
        var themes = new (MultiSectionDungeonGenerator.SectionType t, string floor, string wall, string lm, float fd, float wd, Color light)[]
        {
            (MultiSectionDungeonGenerator.SectionType.GothicRuins, "PP_Gothic_Floor", "PP_Gothic_Wall", "PP_Landmarks_Gothic", 2.2f, 4f, new Color(0.55f, 0.7f, 1f)),
            (MultiSectionDungeonGenerator.SectionType.Graveyard, "PP_Graveyard_Floor", null, "PP_Landmarks_Graveyard", 1.6f, 0f, new Color(0.5f, 1f, 0.7f)),
            (MultiSectionDungeonGenerator.SectionType.HellPillars, "PP_Hell_Floor", "PP_Hell_Wall", "PP_Landmarks_Hell", 1.2f, 3f, new Color(1f, 0.35f, 0.15f)),
            (MultiSectionDungeonGenerator.SectionType.Catacombs, "PP_Catacomb_Floor", "PP_Catacomb_Wall", "PP_Landmarks_Catacombs", 2.5f, 5f, new Color(1f, 0.75f, 0.4f)),
            (MultiSectionDungeonGenerator.SectionType.Cavern, "PP_Cavern_Floor", "PP_Cavern_Wall", "PP_Landmarks_Cavern", 1.2f, 2f, new Color(0.45f, 0.9f, 1f)),
            (MultiSectionDungeonGenerator.SectionType.Transition, "PP_Transition_Floor", "PP_Transition_Wall", null, 0.8f, 3f, Color.white),
        };
        dressing.arraySize = themes.Length;
        for (int i = 0; i < themes.Length; i++)
        {
            var el = dressing.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("Section").enumValueIndex = (int)themes[i].t;
            el.FindPropertyRelative("FloorProps").objectReferenceValue = Pal(themes[i].floor);
            el.FindPropertyRelative("WallProps").objectReferenceValue = themes[i].wall != null ? Pal(themes[i].wall) : null;
            el.FindPropertyRelative("Landmarks").objectReferenceValue = themes[i].lm != null ? Pal(themes[i].lm) : null;
            el.FindPropertyRelative("FloorDensity").floatValue = themes[i].fd;
            el.FindPropertyRelative("WallDensity").floatValue = themes[i].wd;
            el.FindPropertyRelative("LandmarkLight").colorValue = themes[i].light;
        }
        g.FindProperty("KitHellPillars").objectReferenceValue = Pal("PP_Kit_HellPillars");
        g.FindProperty("KitDeadTrees").objectReferenceValue = Pal("PP_Kit_DeadTrees");
        g.FindProperty("KitWindows").objectReferenceValue = Pal("PP_Kit_Windows");
        g.FindProperty("KitCellBars").objectReferenceValue = Pal("PP_Kit_CellBars");
        g.FindProperty("KitWallPanels").objectReferenceValue = Pal("PP_Kit_WallPanels");
        g.FindProperty("KitBonePiles").objectReferenceValue = Pal("PP_Kit_BonePiles");
        g.FindProperty("KitCandles").objectReferenceValue = Pal("PP_Kit_Candles");
        g.FindProperty("Chunks").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ChunkLibrary>(ChunkLibraryPath);
        g.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(gm.scene);
    }
}
