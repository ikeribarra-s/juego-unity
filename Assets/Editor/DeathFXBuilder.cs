using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;

/// <summary>
/// Builds the death FX (Docs/Design/DeathFX.md): sheet import, 3 materials, 3 prefabs, the 6 SoundDefinitions (+ clip
/// import settings), the shared <c>DeathFX_Profile.asset</c>, bone-clack cap on SD_BoneClack, and wires <see cref="DeathFX"/>
/// on GameManager. Re-runnable / idempotent. Menu: Fluffterror > VFX > Build Death FX / Wire Death FX In Open Scene /
/// Build + Wire SampleScene and TestScene.
/// </summary>
public static class DeathFXBuilder
{
    private const string VfxDir = "Assets/_Game/Rendering/VFX/Death";
    private const string ProfilePath = "Assets/_Game/Characters/_Base/Death/DeathFX_Profile.asset";
    private const string Sfx = "Assets/_Game/Audio/SFX";

    [MenuItem("Fluffterror/VFX/Build Death FX")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        string dust = $"{VfxDir}/BoneDust_Sheet.png", chip = $"{VfxDir}/BoneChip_Sheet.png", wisp = $"{VfxDir}/SkullWisp_Sheet.png";
        foreach (var s in new[] { dust, chip, wisp }) ConfigureSheet(s);

        var dustMat = Mat("BoneDust_Mat", dust, false, Color.white);
        var chipMat = Mat("BoneChip_Mat", chip, false, Color.white);
        var wispMat = Mat("SkullWisp_Mat", wisp, true, new Color(1.4f, 1.4f, 1.4f, 1f));

        Prefab("VFX_DeathDust", dustMat, false, ps =>
        {
            var m = ps.main; m.startLifetime = 0.75f; m.startSize = 0.3f; m.gravityModifier = 0.1f; m.maxParticles = 16;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 100f; lim.dampen = 0f; lim.drag = 2f;
            SheetAnim(ps);
        });
        Prefab("VFX_DeathChips", chipMat, false, ps =>
        {
            var m = ps.main; m.startLifetime = 0.65f; m.startSize = 0.045f; m.gravityModifier = 1f; m.maxParticles = 16;
            SheetAnim(ps);
        });
        Prefab("VFX_SkullWisp", wispMat, false, ps =>
        {
            var m = ps.main; m.startLifetime = 1.1f; m.startSize = 0.1f; m.gravityModifier = 0f; m.maxParticles = 12;
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            var n = ps.noise; n.enabled = true; n.strength = 0.15f; n.frequency = 0.8f;
            SheetAnim(ps);
        });

        // sounds
        var actions = AssetDatabase.LoadAssetAtPath<SoundDefinition>($"{Sfx}/Character/SD_BoneClack.asset");
        var ui = AssetDatabase.LoadAssetAtPath<SoundDefinition>($"{Sfx}/UI/SD_UI_Hover.asset");
        AudioMixerGroup gActions = actions != null ? new SerializedObject(actions).FindProperty("_mixerGroup").objectReferenceValue as AudioMixerGroup : null;
        AudioMixerGroup gSfx = ui != null ? new SerializedObject(ui).FindProperty("_mixerGroup").objectReferenceValue as AudioMixerGroup : null;

        var collapse = SD("Character", "SD_Death_Collapse", "SFX_Character_DeathCollapse_", gActions, 0.8f, 0.94f, 1.06f, 1f, 2f, 35f, 1, 0.5f, true);
        var sting    = SD("UI",        "SD_Death_Sting",    "SFX_UI_DeathSting_",            gSfx,     0.5f, 0.94f, 1.06f, 0f, 1f, 500f, 1, 2f, false);
        var roll     = SD("Character", "SD_Skull_Roll",     "SFX_Character_SkullRoll_",      gActions, 0.7f, 0.94f, 1.06f, 1f, 2f, 25f, 1, 0.5f, true);
        var yip      = SD("Character", "SD_Skull_Yip",      "SFX_Character_SkullYip_",       gActions, 0.5f, 0.94f, 1.06f, 1f, 2f, 20f, 1, 1f, true);
        var ready    = SD("UI",        "SD_Skull_Ready",    "SFX_UI_SkullReady_",            gSfx,     0.5f, 0.94f, 1.06f, 0f, 1f, 500f, 1, 1f, false);
        var pickup   = SD("Items",     "SD_Bone_Pickup",    "SFX_Items_BonePickup_",         gActions, 0.6f, 0.94f, 1.06f, 1f, 2f, 15f, 2, 0.1f, true);

        // the piece clack: capped (4 voices / 0.04 s), pitch 0.90-1.10
        if (actions != null)
        {
            var so = new SerializedObject(actions);
            so.FindProperty("_maxVoices").intValue = 4;
            so.FindProperty("_minInterval").floatValue = 0.04f;
            so.FindProperty("_pitchRange").vector2Value = new Vector2(0.9f, 1.1f);
            so.FindProperty("_volume").floatValue = 0.6f;
            so.ApplyModifiedProperties();
        }

        var profile = AssetDatabase.LoadAssetAtPath<DeathFXProfile>(ProfilePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<DeathFXProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
        profile.Collapse = collapse; profile.Sting = sting; profile.SkullRoll = roll; profile.SkullYip = yip;
        profile.SkullReady = ready; profile.BonePickup = pickup;
        // SkullHum stays empty and SkullHumEnabled false (decision 3: implemented, default off)
        EditorUtility.SetDirty(profile);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DeathFX] Built sheets/materials/prefabs/sounds/profile.");
    }

    private static void ConfigureSheet(string path)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter t) return;
        t.textureType = TextureImporterType.Default; t.filterMode = FilterMode.Point; t.mipmapEnabled = false;
        t.textureCompression = TextureImporterCompression.Uncompressed; t.wrapMode = TextureWrapMode.Clamp;
        t.alphaIsTransparency = false; t.npotScale = TextureImporterNPOTScale.None; t.isReadable = false;
        t.SaveAndReimport();
    }

    private static Material Mat(string name, string tex, bool additive, Color tint)
    {
        string path = $"{VfxDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", 1f);
        m.SetFloat("_DstBlendAlpha", additive ? 1f : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = additive ? 3010 : 3000;   // dust/chips: alpha queue; wisp after the water particles
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
        m.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void Prefab(string name, Material mat, bool flat, System.Action<ParticleSystem> setup)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.playOnAwake = false; m.loop = false; m.duration = 1f; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m.simulationSpace = ParticleSystemSimulationSpace.World; m.startSpeed = 0f; m.startRotation = 0f;
        var e = ps.emission; e.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.collision; col.enabled = false;
        var lights = ps.lights; lights.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.View;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        setup(ps);
        PrefabUtility.SaveAsPrefabAsset(go, $"{VfxDir}/{name}.prefab");
        Object.DestroyImmediate(go);
    }

    private static void SheetAnim(ParticleSystem ps)
    {
        var t = ps.textureSheetAnimation; t.enabled = true; t.mode = ParticleSystemAnimationMode.Grid;
        t.numTilesX = 4; t.numTilesY = 4; t.animation = ParticleSystemAnimationType.WholeSheet;
        t.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        t.cycleCount = 1; t.startFrame = 0f;
    }

    private static SoundDefinition SD(string cat, string name, string clipPrefix, AudioMixerGroup group, float vol, float pMin, float pMax,
                                      float blend, float minD, float maxD, int voices, float interval, bool mono)
    {
        var clips = new System.Collections.Generic.List<AudioClip>();
        foreach (string g in AssetDatabase.FindAssets("t:AudioClip", new[] { $"{Sfx}/{cat}" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!Path.GetFileName(p).StartsWith(clipPrefix)) continue;
            if (AssetImporter.GetAtPath(p) is AudioImporter ai)
            {
                ai.forceToMono = mono; ai.loadInBackground = false;
                var s = ai.defaultSampleSettings;
                s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.ADPCM; s.preloadAudioData = true;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport();
            }
            clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(p));
        }
        clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        string path = $"{Sfx}/{cat}/{name}.asset";
        var sd = AssetDatabase.LoadAssetAtPath<SoundDefinition>(path);
        if (sd == null) { sd = ScriptableObject.CreateInstance<SoundDefinition>(); AssetDatabase.CreateAsset(sd, path); }
        var so = new SerializedObject(sd);
        var arr = so.FindProperty("_clips"); arr.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        so.FindProperty("_volume").floatValue = vol;
        so.FindProperty("_pitchRange").vector2Value = new Vector2(pMin, pMax);
        so.FindProperty("_spatialBlend").floatValue = blend;
        so.FindProperty("_mixerGroup").objectReferenceValue = group;
        so.FindProperty("_minDistance").floatValue = minD;
        so.FindProperty("_maxDistance").floatValue = maxD;
        so.FindProperty("_maxVoices").intValue = voices;
        so.FindProperty("_minInterval").floatValue = interval;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(sd);
        return sd;
    }

    // scene wiring

    [MenuItem("Fluffterror/VFX/Wire Death FX In Open Scene")]
    public static void WireOpenScene()
    {
        var gm = GameObject.Find("GameManager");
        if (gm == null) { Debug.LogWarning("[DeathFX] No GameManager in the open scene."); return; }
        if (!gm.TryGetComponent<DeathFX>(out var fx)) fx = Undo.AddComponent<DeathFX>(gm);
        var so = new SerializedObject(fx);
        so.FindProperty("_profile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DeathFXProfile>(ProfilePath);
        void Set(string f, string prefab) =>
            so.FindProperty(f).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{VfxDir}/{prefab}.prefab")?.GetComponent<ParticleSystem>();
        Set("_dustPrefab", "VFX_DeathDust"); Set("_chipsPrefab", "VFX_DeathChips"); Set("_wispPrefab", "VFX_SkullWisp");
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(gm.scene);
        EditorSceneManager.SaveScene(gm.scene);
    }

    [MenuItem("Fluffterror/VFX/Build + Wire SampleScene and TestScene (Death FX)")]
    public static void BuildAndWireBoth()
    {
        Build();
        foreach (var p in new[] { "Assets/Scenes/SampleScene.unity", "Assets/Scenes/TestScene.unity" })
        {
            EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
            WireOpenScene();
        }
        EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity", OpenSceneMode.Single);
    }
}
