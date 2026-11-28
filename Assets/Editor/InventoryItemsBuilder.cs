using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires the inventory items: bone icons into <c>DeathFX_Profile.asset</c> (<see cref="DeathFXProfile.BoneIcons"/>), the
/// <see cref="ItemPickup"/> component (name, icon, sounds) onto the Map / SaltBag prefabs, and drops test copies into the open scene.
/// Re-runnable. Menu: Fluffterror > Items.
/// </summary>
public static class InventoryItemsBuilder
{
    private const string IconDir     = "Assets/_Game/UI/Icons/Inventory/";
    private const string ProfilePath = "Assets/_Game/Characters/_Base/Death/DeathFX_Profile.asset";
    private const string MapPrefab   = "Assets/Art/Props/Map/Map.prefab";
    private const string SaltPrefab  = "Assets/Art/Props/SaltBag/SaltBag.prefab";
    private const string PickupSd    = "Assets/_Game/Audio/SFX/Items/SD_Pickup.asset";
    private const string DropSd      = "Assets/_Game/Audio/SFX/Items/SD_Drop.asset";
    private const string SaltMat     = "Assets/Art/Props/SaltBag/M_SaltLine.mat";

    private static Sprite Icon(string file)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + file);
        if (s == null) Debug.LogWarning($"[InventoryItems] icon not found or not a Sprite: {IconDir}{file}");
        return s;
    }

    private static string BoneIconFile(BoneGroup g) => g switch
    {
        BoneGroup.Ribcage       => "Icon_Bone_Ribcage.png",
        BoneGroup.Pelvis        => "Icon_Bone_Pelvis.png",
        BoneGroup.LeftUpperArm  => "Icon_Bone_Humerus_L.png",
        BoneGroup.RightUpperArm => "Icon_Bone_Humerus_R.png",
        BoneGroup.LeftForearm   => "Icon_Bone_Forearm_L.png",
        BoneGroup.RightForearm  => "Icon_Bone_Forearm_R.png",
        BoneGroup.LeftHand      => "Icon_Bone_Hand_L.png",
        BoneGroup.RightHand     => "Icon_Bone_Hand_R.png",
        BoneGroup.LeftThigh     => "Icon_Bone_Femur_L.png",
        BoneGroup.RightThigh    => "Icon_Bone_Femur_R.png",
        BoneGroup.LeftShin      => "Icon_Bone_Shin_L.png",
        BoneGroup.RightShin     => "Icon_Bone_Shin_R.png",
        BoneGroup.LeftFoot      => "Icon_Bone_Foot_L.png",
        BoneGroup.RightFoot     => "Icon_Bone_Foot_R.png",
        _ => null,
    };

    [MenuItem("Fluffterror/Items/Setup Inventory Items")]
    public static void Setup()
    {
        // 1. Bone icons -> profile
        var profile = AssetDatabase.LoadAssetAtPath<DeathFXProfile>(ProfilePath);
        if (profile != null)
        {
            Undo.RecordObject(profile, "Bone icons");
            profile.BoneIcons = new Sprite[BoneGroups.Count];
            int n = 0;
            for (int i = 0; i < BoneGroups.Count; i++)
            {
                string f = BoneIconFile((BoneGroup)i);
                if (f == null) continue;
                profile.BoneIcons[i] = Icon(f);
                if (profile.BoneIcons[i] != null) n++;
            }
            EditorUtility.SetDirty(profile);
            Debug.Log($"[InventoryItems] {n} bone icons assigned to DeathFX_Profile.");
        }
        else Debug.LogWarning("[InventoryItems] DeathFX_Profile.asset not found: " + ProfilePath);

        // 2. ItemPickup on the prefabs
        var pickup = AssetDatabase.LoadAssetAtPath<SoundDefinition>(PickupSd);
        var drop   = AssetDatabase.LoadAssetAtPath<SoundDefinition>(DropSd);
        SetupPrefab(MapPrefab,  "Map",      "",      Icon("Icon_Map.png"),     pickup, drop, 0.30f);
        SetupPrefab(SaltPrefab, "Salt Bag", "",      Icon("Icon_SaltBag.png"), pickup, drop, 0.30f);
        SetupSaltUse(drop);
        AssetDatabase.SaveAssets();
    }

    // ── Usable salt bag: pixel-art FX assets, sounds, profile, held model, SaltBagUse on the prefab ──────────────
    private const string SaltVfxDir  = "Assets/_Game/Rendering/VFX/Salt";
    private const string SaltSfxDir  = "Assets/_Game/Audio/SFX/Items";
    private const string SaltProfile = "Assets/_Game/Items/SaltFX_Profile.asset";
    private const string SaltHeld    = "Assets/Art/Props/SaltBag/SaltBag_Held.prefab";

    private static void SetupSaltUse(SoundDefinition ignored)
    {
        AssetDatabase.Refresh();
        foreach (var sheet in new[] { "SaltPile_Sheet.png", "SaltGrain_Sheet.png", "SaltPuff_Sheet.png" })
            ConfigureSheet($"{SaltVfxDir}/{sheet}");

        var pile  = SaltMaterial("SaltPile_Mat",  "SaltPile_Sheet.png",  lit: true);
        var grain = SaltMaterial("SaltGrain_Mat", "SaltGrain_Sheet.png", lit: false);
        var puff  = SaltMaterial("SaltPuff_Mat",  "SaltPuff_Sheet.png",  lit: false);

        var actions = AssetDatabase.LoadAssetAtPath<SoundDefinition>(PickupSd);
        var group = actions != null ? new SerializedObject(actions).FindProperty("_mixerGroup").objectReferenceValue : null;
        var pour  = SaltSound("SD_Salt_Pour",  "SFX_Items_SaltPour_",  group, 0.8f, 1f,    1f,    1.5f, 25f, 1, 0f);
        var shake = SaltSound("SD_Salt_Shake", "SFX_Items_SaltShake_", group, 0.7f, 0.94f, 1.06f, 1.5f, 20f, 2, 0.3f);
        var land  = SaltSound("SD_Salt_Land",  "SFX_Items_SaltLand_",  group, 0.55f, 0.85f, 1.15f, 1f,  14f, 3, 0.07f);

        var profile = AssetDatabase.LoadAssetAtPath<SaltFXProfile>(SaltProfile);
        if (profile == null) { profile = ScriptableObject.CreateInstance<SaltFXProfile>(); AssetDatabase.CreateAsset(profile, SaltProfile); }
        profile.Pile = pile; profile.Grain = grain; profile.Puff = puff;
        profile.PourLoop = pour; profile.Shake = shake; profile.Land = land;
        EditorUtility.SetDirty(profile);

        var held = BuildHeldPrefab();

        var root = PrefabUtility.LoadPrefabContents(SaltPrefab);
        try
        {
            var use = root.GetComponent<SaltBagUse>();
            if (use == null) use = root.AddComponent<SaltBagUse>();
            use.Setup(profile, held);
            PrefabUtility.SaveAsPrefabAsset(root, SaltPrefab);
            Debug.Log("[InventoryItems] SaltBag.prefab: SaltBagUse + SaltFX_Profile + held model configured.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }

    private static void ConfigureSheet(string path)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter t) { Debug.LogWarning("[InventoryItems] missing sheet " + path); return; }
        t.textureType = TextureImporterType.Default; t.filterMode = FilterMode.Point; t.mipmapEnabled = false;
        t.textureCompression = TextureImporterCompression.Uncompressed; t.wrapMode = TextureWrapMode.Clamp;
        t.alphaIsTransparency = false; t.npotScale = TextureImporterNPOTScale.None; t.isReadable = false;
        t.SaveAndReimport();
    }

    private static Material SaltMaterial(string name, string sheet, bool lit)
    {
        string path = $"{SaltVfxDir}/{name}.mat";
        string shaderName = lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Particles/Unlit";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shaderName)); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find(shaderName);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SaltVfxDir}/{sheet}");
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        if (lit)
        {
            // floor piles: alpha-CLIPPED (hard pixel edges, writes depth, no sorting), matte, lit by the dungeon lights
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Smoothness", 0f); m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Cull", 2f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.SetFloat("_ReceiveShadows", 1f);
        }
        else
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3010;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    private static SoundDefinition SaltSound(string name, string clipPrefix, Object mixerGroup, float vol, float pMin, float pMax,
                                             float minD, float maxD, int voices, float interval)
    {
        var clips = new System.Collections.Generic.List<AudioClip>();
        foreach (string g in AssetDatabase.FindAssets("t:AudioClip", new[] { SaltSfxDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!System.IO.Path.GetFileName(p).StartsWith(clipPrefix)) continue;
            if (AssetImporter.GetAtPath(p) is AudioImporter ai)
            {
                ai.forceToMono = true; ai.loadInBackground = false;
                var st = ai.defaultSampleSettings;
                st.loadType = AudioClipLoadType.DecompressOnLoad; st.compressionFormat = AudioCompressionFormat.ADPCM; st.preloadAudioData = true;
                ai.defaultSampleSettings = st;
                ai.SaveAndReimport();
            }
            clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(p));
        }
        string path = $"{SaltSfxDir}/{name}.asset";
        var sd = AssetDatabase.LoadAssetAtPath<SoundDefinition>(path);
        if (sd == null) { sd = ScriptableObject.CreateInstance<SoundDefinition>(); AssetDatabase.CreateAsset(sd, path); }
        var so = new SerializedObject(sd);
        var arr = so.FindProperty("_clips"); arr.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        so.FindProperty("_volume").floatValue = vol;
        so.FindProperty("_pitchRange").vector2Value = new Vector2(pMin, pMax);
        so.FindProperty("_spatialBlend").floatValue = 1f;
        so.FindProperty("_mixerGroup").objectReferenceValue = mixerGroup;
        so.FindProperty("_minDistance").floatValue = minD;
        so.FindProperty("_maxDistance").floatValue = maxD;
        so.FindProperty("_maxVoices").intValue = voices;
        so.FindProperty("_minInterval").floatValue = interval;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sd);
        return sd;
    }

    /// <summary>The model of the bag only (mesh + material, no physics / scripts): what the hand holds.</summary>
    private static GameObject BuildHeldPrefab()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(SaltPrefab);
        var inst = (GameObject)Object.Instantiate(src);
        inst.name = "SaltBag_Held";
        foreach (var c in inst.GetComponents<Component>())
            if (c is SaltBagUse) Object.DestroyImmediate(c);
        foreach (var c in inst.GetComponents<Component>())
            if (c is ItemPickup) Object.DestroyImmediate(c);
        foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var c in inst.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(c);
        var prefab = PrefabUtility.SaveAsPrefabAsset(inst, SaltHeld);
        Object.DestroyImmediate(inst);
        return prefab;
    }

    private static void SetupPrefab(string path, string displayName, string subtitle, Sprite icon,
                                    SoundDefinition pickup, SoundDefinition drop, float assistRadius)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var item = root.GetComponent<ItemPickup>();
            if (item == null) item = root.AddComponent<ItemPickup>();
            item.Setup(displayName, subtitle, icon, pickup, drop);
            var so = new SerializedObject(item);
            so.FindProperty("_assistRadius").floatValue = assistRadius;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[InventoryItems] {path}: ItemPickup \"{displayName}\" configured.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Fluffterror/Items/Add Test Items To Open Scene")]
    public static void AddTestItems()
    {
        var player = Object.FindAnyObjectByType<CharacterBase>();
        if (player == null) { Debug.LogWarning("[InventoryItems] no CharacterBase in the open scene."); return; }

        var map  = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefab);
        var salt = AssetDatabase.LoadAssetAtPath<GameObject>(SaltPrefab);
        if (map == null || salt == null) { Debug.LogWarning("[InventoryItems] Map / SaltBag prefab missing."); return; }

        // replace a previous batch
        var old = GameObject.Find("TestItems");
        if (old != null) Undo.DestroyObjectImmediate(old);

        var parent = new GameObject("TestItems");
        Undo.RegisterCreatedObjectUndo(parent, "Test items");

        Vector3 fwd = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 center = player.transform.position + fwd * 3.2f;

        // two rows of two so a full inventory (3 slots) can be tested
        var layout = new (GameObject prefab, string name, float side, float depth)[]
        {
            (map,  "TestItem_Map_1",  -0.9f, 0f),
            (salt, "TestItem_Salt_1", -0.3f, 0f),
            (map,  "TestItem_Map_2",   0.3f, 0f),
            (salt, "TestItem_Salt_2",  0.9f, 0f),
        };
        foreach (var (prefab, name, side, depth) in layout)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            Vector3 p = center + right * side + fwd * depth;
            // rest on whatever floor is below
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(player.transform))
                p.y = hit.point.y + 0.02f;
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Test item");
        }

        EditorSceneManager.MarkSceneDirty(parent.scene);
        Debug.Log($"[InventoryItems] 4 test items placed at {center} in {parent.scene.name} (save the scene to keep them).");
    }
}
