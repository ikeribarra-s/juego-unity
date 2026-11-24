using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu: Fluffterror > Scenes > Build Test Scene
///
/// Builds Assets/Scenes/TestScene.unity: a flat, well-lit sandbox with one labelled station per
/// feature. It starts as a COPY of SampleScene so the player, HUD and managers keep all their
/// wiring, then removes the dungeon and lays out the test area. Re-run to rebuild from scratch
/// (anything hand-placed in TestScene is overwritten).
///
/// Layout (player spawns at the origin looking +Z):
///   West  (-X) : movement — jump boxes, stairs, ramps, crouch tunnel
///   East  (+X) : interaction — evidence table, torches, generators
///   North (+Z) : extraction zone
///   South (-Z) : audio — small reverb room and long hall
/// </summary>
public static class TestSceneBuilder
{
    private const string SourceScene = "Assets/Scenes/SampleScene.unity";
    private const string TestScene   = "Assets/Scenes/TestScene.unity";
    private const string TestingDir  = "Assets/_Game/Testing";
    private const string GeneratorPrefab = "Assets/_Game/Machines/Generator.prefab";
    private const string TorchPrefab     = "Assets/_Game/Environment/Torches/Torch.prefab";

    // Roots from SampleScene that only make sense in the dungeon.
    private static readonly string[] RootsToRemove = { "DungeonGenerator", "Plane" };

    private static int _wallsLayer;
    private static Material _floorMat, _blockMat, _accentMat, _wallMat;

    [MenuItem("Fluffterror/Scenes/Build Test Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (File.Exists(TestScene) &&
            !EditorUtility.DisplayDialog("Rebuild Test Scene",
                "TestScene.unity already exists. Rebuild it from SampleScene? Hand-made changes in it will be lost.",
                "Rebuild", "Cancel"))
            return;

        AssetDatabase.DeleteAsset(TestScene);
        if (!AssetDatabase.CopyAsset(SourceScene, TestScene))
        {
            Debug.LogError($"[TestSceneBuilder] Could not copy {SourceScene}.");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(TestScene, OpenSceneMode.Single);
        _wallsLayer = LayerMask.NameToLayer("Walls");
        if (_wallsLayer < 0) _wallsLayer = 0;
        CreateMaterials();

        StripDungeon(scene);
        SetupLighting();

        var area = new GameObject("TestArea").transform;
        BuildFloorAndBounds(area);
        BuildMovementStations(area);
        BuildInteractionStations(area, scene);
        BuildAudioStations(area);
        PlaceSceneObjects(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[TestSceneBuilder] TestScene built: " + TestScene);
    }

    // ───────────────────────────────── scene cleanup ─────────────────────────────────

    private static void StripDungeon(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (System.Array.IndexOf(RootsToRemove, root.name) >= 0)
            {
                Object.DestroyImmediate(root);
                continue;
            }

            // Dungeon-only fog/ambient — the test area should be readable.
            foreach (var c in root.GetComponentsInChildren<DungeonLightingController>(true))
                Object.DestroyImmediate(c);

            // Enemy off by default (it kills you while you test other things). Tick it to test the creature.
            foreach (var s in root.GetComponentsInChildren<EnemySpawner>(true))
                s.enabled = false;
        }
    }

    private static void SetupLighting()
    {
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.52f);

        // Exactly ONE directional light: URP only shadows the main one, every extra directional is an
        // unshadowed additional light that lights roofed interiors from nowhere.
        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>())
        {
            if (l.type != LightType.Directional) continue;
            if (sun == null) sun = l;
            else Object.DestroyImmediate(l.gameObject);
        }
        if (sun != null)
        {
            RenderSettings.sun = sun;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.shadows = LightShadows.Soft;
        }
    }

    // ───────────────────────────────── geometry ─────────────────────────────────

    private static void BuildFloorAndBounds(Transform area)
    {
        const float size = 80f;
        var floor = Box("Floor", area, new Vector3(0, -0.5f, 0), new Vector3(size, 1, size), _floorMat);
        _floorMat.SetTextureScale("_BaseMap", new Vector2(size, size));
        floor.isStatic = true;

        var bounds = Group("Bounds", area);
        const float h = 4f, half = size * 0.5f;
        Box("Wall_N", bounds, new Vector3(0, h / 2, half), new Vector3(size, h, 0.5f), _wallMat, _wallsLayer);
        Box("Wall_S", bounds, new Vector3(0, h / 2, -half), new Vector3(size, h, 0.5f), _wallMat, _wallsLayer);
        Box("Wall_E", bounds, new Vector3(half, h / 2, 0), new Vector3(0.5f, h, size), _wallMat, _wallsLayer);
        Box("Wall_W", bounds, new Vector3(-half, h / 2, 0), new Vector3(0.5f, h, size), _wallMat, _wallsLayer);

        Label("SPAWN", area, new Vector3(0, 0.05f, -1.5f), 0.6f, flat: true);
    }

    private static void BuildMovementStations(Transform area)
    {
        // Jump boxes: 0.5 / 1.0 / 1.5 m (1.5 should be just out of reach).
        var jump = Group("Station_Jump", area);
        float[] heights = { 0.5f, 1.0f, 1.5f };
        for (int i = 0; i < heights.Length; i++)
        {
            float hh = heights[i];
            Box($"Box_{hh:0.0}m", jump, new Vector3(-8, hh / 2, 6 + i * 4), new Vector3(2, hh, 2), _blockMat);
            Label($"{hh:0.0} m", jump, new Vector3(-8, hh + 0.4f, 6 + i * 4), 0.35f);
        }
        Label("JUMP", jump, new Vector3(-8, 2.6f, 4), 0.7f);

        // Stairs: 10 steps of 0.2 m rise.
        var stairs = Group("Station_Stairs", area);
        for (int i = 0; i < 10; i++)
        {
            float rise = 0.2f * (i + 1);
            Box($"Step_{i + 1}", stairs, new Vector3(-14, rise / 2, 6 + i * 0.4f), new Vector3(2.5f, rise, 0.4f), _blockMat);
        }
        Box("Landing", stairs, new Vector3(-14, 1f, 11.5f), new Vector3(2.5f, 2f, 2f), _blockMat);
        Label("STAIRS (0.2 m steps)", stairs, new Vector3(-14, 2.8f, 6), 0.45f);

        // Ramps at 20°, 35°, 50° (the last should exceed the CharacterController slope limit).
        var ramps = Group("Station_Ramps", area);
        float[] angles = { 20f, 35f, 50f };
        for (int i = 0; i < angles.Length; i++)
        {
            float a = angles[i];
            const float len = 6f;
            float x = -20 - i * 4f;
            float rise = Mathf.Sin(a * Mathf.Deg2Rad) * len;
            float run = Mathf.Cos(a * Mathf.Deg2Rad) * len;
            var ramp = Box($"Ramp_{a}deg", ramps, new Vector3(x, rise / 2, 6 + run / 2), new Vector3(3, 0.3f, len), _blockMat);
            ramp.transform.rotation = Quaternion.Euler(-a, 0, 0);
            Label($"{a}°", ramps, new Vector3(x, 0.6f, 4.5f), 0.4f);
        }
        Label("RAMPS", ramps, new Vector3(-24, 3f, 4), 0.7f);

        // Crouch tunnel: 1.3 m clearance (standing needs 1.8).
        var tunnel = Group("Station_Crouch", area);
        const float clear = 1.3f, len2 = 8f, width = 2f, tz = 22f, tx = -10f;
        Box("Ceiling", tunnel, new Vector3(tx, clear + 0.25f, tz), new Vector3(width + 1, 0.5f, len2), _accentMat, _wallsLayer);
        Box("Side_L", tunnel, new Vector3(tx - width / 2 - 0.25f, clear / 2, tz), new Vector3(0.5f, clear, len2), _wallMat, _wallsLayer);
        Box("Side_R", tunnel, new Vector3(tx + width / 2 + 0.25f, clear / 2, tz), new Vector3(0.5f, clear, len2), _wallMat, _wallsLayer);
        Label("CROUCH TUNNEL (1.3 m) — try standing up inside", tunnel, new Vector3(tx, 2.4f, tz - len2 / 2 - 0.5f), 0.35f);

        // Long straight for sprint / Trotín.
        var track = Group("Station_SprintTrack", area);
        for (int i = 0; i <= 6; i++)
            Label($"{i * 5} m", track, new Vector3(-32, 0.05f, -4 + i * 5), 0.4f, flat: true);
        Label("SPRINT TRACK (Trotín: Q)", track, new Vector3(-32, 2.5f, -6), 0.5f);
    }

    private static void BuildInteractionStations(Transform area, Scene scene)
    {
        // Evidence table (evidence objects from SampleScene are moved onto it in PlaceSceneObjects).
        var evidence = Group("Station_Evidence", area);
        Box("Table", evidence, new Vector3(8, 0.4f, 8), new Vector3(5, 0.8f, 1.6f), _blockMat);
        Label("EVIDENCE — hold E to grab, G to throw", evidence, new Vector3(8, 2.2f, 8), 0.4f);
        Box("ThrowTarget", evidence, new Vector3(8, 1.5f, 18), new Vector3(3, 3, 0.3f), _accentMat, _wallsLayer);
        Label("throw target (fragile items break > 7 m/s)", evidence, new Vector3(8, 3.4f, 18), 0.3f);

        // Torches.
        var torches = Group("Station_Torches", area);
        var torchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TorchPrefab);
        for (int i = 0; i < 2; i++)
        {
            var pos = new Vector3(14 + i * 2.5f, 0, 2);
            Box($"Pedestal_{i + 1}", torches, pos + Vector3.up * 0.5f, new Vector3(0.6f, 1f, 0.6f), _blockMat);
            if (torchPrefab != null)
            {
                var torch = Spawn(torchPrefab, torches, pos + Vector3.up * 1.0f, torchPrefab.GetComponent<Torch>().UprightRotation); // FBX is Z-up: stand it upright
                Torch.ApplySpawnScale(torch);
                WallMount.PlaceInFrontOf(torch.transform, pos + Vector3.up * 1.0f, Vector3.up, 0f);              // pivot = lantern centre: rest its base on the pedestal
            }
        }
        Label(torchPrefab != null ? "TORCHES — E to take" : "TORCHES (prefab missing)", torches, new Vector3(15.25f, 2.4f, 2), 0.4f);

        // Generators — the mission needs all of them running to unlock extraction.
        var gens = Group("Station_Generators", area);
        var genPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GeneratorPrefab);
        if (genPrefab != null)
            for (int i = 0; i < 3; i++)
                Spawn(genPrefab, gens, new Vector3(20, 0, 8 + i * 5), Quaternion.Euler(0, -90, 0));
        Label(genPrefab != null ? "GENERATORS — hold E to repair (skill checks)" : "GENERATORS (prefab missing)",
              gens, new Vector3(20, 3f, 6), 0.4f);
    }

    private static void BuildAudioStations(Transform area)
    {
        // Small closed room (tight reverb) with a doorway.
        var room = Group("Station_ReverbRoom", area);
        Vector3 c = new(-10, 0, -14);
        const float s = 6f, h = 3f;
        Box("Wall_N", room, c + new Vector3(0, h / 2, s / 2), new Vector3(s, h, 0.3f), _wallMat, _wallsLayer);
        Box("Wall_S", room, c + new Vector3(0, h / 2, -s / 2), new Vector3(s, h, 0.3f), _wallMat, _wallsLayer);
        Box("Wall_W", room, c + new Vector3(-s / 2, h / 2, 0), new Vector3(0.3f, h, s), _wallMat, _wallsLayer);
        Box("Wall_E_a", room, c + new Vector3(s / 2, h / 2, 1.75f), new Vector3(0.3f, h, 2.5f), _wallMat, _wallsLayer);
        Box("Wall_E_b", room, c + new Vector3(s / 2, h / 2, -1.75f), new Vector3(0.3f, h, 2.5f), _wallMat, _wallsLayer);
        Box("Roof", room, c + new Vector3(0, h + 0.15f, 0), new Vector3(s, 0.3f, s), _wallMat, _wallsLayer);
        Label("SMALL ROOM (reverb)", room, c + new Vector3(s / 2 + 0.3f, h + 0.6f, 0), 0.4f);

        // Long open hall (big reverb).
        var hall = Group("Station_ReverbHall", area);
        Vector3 hc = new(10, 0, -18);
        const float hw = 5f, hl = 20f, hh = 6f;
        Box("Wall_W", hall, hc + new Vector3(-hw / 2, hh / 2, 0), new Vector3(0.3f, hh, hl), _wallMat, _wallsLayer);
        Box("Wall_E", hall, hc + new Vector3(hw / 2, hh / 2, 0), new Vector3(0.3f, hh, hl), _wallMat, _wallsLayer);
        Box("Wall_S", hall, hc + new Vector3(0, hh / 2, -hl / 2), new Vector3(hw, hh, 0.3f), _wallMat, _wallsLayer);
        Label("LONG HALL (reverb)", hall, hc + new Vector3(0, hh + 0.6f, hl / 2), 0.5f);
    }

    // ───────────────────────────── existing scene objects ─────────────────────────────

    private static void PlaceSceneObjects(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            switch (root.name)
            {
                case "Character":
                    root.transform.SetPositionAndRotation(new Vector3(0, 0.05f, 0), Quaternion.identity);
                    break;
                case "Trotin":
                    // Second character: parked and disabled. Swap which one is active to test Trotín.
                    root.transform.SetPositionAndRotation(new Vector3(3, 0.05f, 0), Quaternion.identity);
                    root.SetActive(false);
                    break;
                case "Evidence":
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    int i = 0;
                    foreach (Transform item in root.transform)
                    {
                        item.position = new Vector3(6.5f + i * 1.5f, 1.2f, 8);
                        i++;
                    }
                    break;
                case "Extraction":
                    root.transform.SetPositionAndRotation(new Vector3(0, 0, 30), Quaternion.identity);
                    Label("EXTRACTION — needs all generators + evidence dropped inside",
                          GameObject.Find("TestArea").transform, new Vector3(0, 4f, 26), 0.45f);
                    break;
            }
        }
    }

    // ───────────────────────────────── helpers ─────────────────────────────────

    private static void CreateMaterials()
    {
        if (!AssetDatabase.IsValidFolder(TestingDir))
            AssetDatabase.CreateFolder("Assets/_Game", "Testing");

        Texture2D grid = GridTexture();
        _floorMat  = Mat("M_Test_Floor",  new Color(0.55f, 0.57f, 0.6f), grid);
        _blockMat  = Mat("M_Test_Block",  new Color(0.85f, 0.6f, 0.3f), grid);
        _accentMat = Mat("M_Test_Accent", new Color(0.35f, 0.65f, 0.85f), null);
        _wallMat   = Mat("M_Test_Wall",   new Color(0.42f, 0.44f, 0.5f), null);
    }

    private static Texture2D GridTexture()
    {
        string path = $"{TestingDir}/T_TestGrid.png";
        if (!File.Exists(path))
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool edge = x < 3 || y < 3;
                bool mid = x == n / 2 || y == n / 2;
                float v = edge ? 0.55f : mid ? 0.85f : 1f;
                tex.SetPixel(x, y, new Color(v, v, v, 1));
            }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material Mat(string name, Color color, Texture2D tex)
    {
        string path = $"{TestingDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Smoothness", 0.1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, int layer = 0)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        return go;
    }

    private static GameObject Spawn(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(pos, rot);
        return go;
    }

    private static void Label(string text, Transform parent, Vector3 pos, float size, bool flat = false)
    {
        var go = new GameObject("Label_" + text.Split(' ')[0]);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        // TextMesh faces -Z by default; flip so labels read from the spawn side (player looks +Z).
        go.transform.rotation = flat ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 64;
        tm.characterSize = size * 0.15f;
        tm.color = new Color(1f, 0.95f, 0.8f);
    }
}
