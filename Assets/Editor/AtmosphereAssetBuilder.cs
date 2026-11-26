using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Atmosphere pass: pixel particle prefabs (dust motes, torch smoke, drip splash, ceiling dust, generator sparks),
/// the place-bed / stinger SoundDefinitions + SSP_Default entries, and the AtmosphereFX wiring in the open scene.
/// Menu: Fluffterror > Atmosphere > Build Assets / Wire Open Scene / Build + Wire SampleScene and TestScene.
/// Re-running overwrites in place (same GUIDs). Sheets come from Tools/ComfyUI/make_atmo_sheets.py.
/// </summary>
public static class AtmosphereAssetBuilder
{
    private const string Dir = "Assets/_Game/Rendering/VFX/Atmosphere";
    private const string AmbientDir = "Assets/_Game/Audio/SFX/Ambient";
    private const string ProfilePath = "Assets/_Game/Environment/Data/Director/SSP_Default.asset";
    private const string EmbersSheet = "Assets/_Game/Rendering/VFX/TorchEmbers/TorchEmbers_Sheet.png";

    [MenuItem("Fluffterror/Atmosphere/Build Assets")]
    public static void BuildAssets()
    {
        AssetDatabase.Refresh();
        foreach (var n in new[] { "AtmoMote", "AtmoSmoke", "AtmoSplash" }) ConfigureSheet($"{Dir}/{n}_Sheet.png");

        var mote = Mat("AtmoMote_Mat", $"{Dir}/AtmoMote_Sheet.png", true, new Color(1.0f, 0.82f, 0.55f, 0.55f));
        var smoke = Mat("AtmoSmoke_Mat", $"{Dir}/AtmoSmoke_Sheet.png", false, new Color(0.55f, 0.55f, 0.6f, 0.4f));
        var splash = Mat("AtmoSplash_Mat", $"{Dir}/AtmoSplash_Sheet.png", false, new Color(0.8f, 0.9f, 1f, 0.75f));
        var dust = Mat("AtmoCeilingDust_Mat", $"{Dir}/AtmoMote_Sheet.png", false, new Color(0.62f, 0.58f, 0.5f, 0.8f));
        var sparkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Rendering/VFX/TorchEmbers/TorchEmbers_Mat.mat");

        // motes: slow drifting specks in torch light
        Prefab("VFX_DustMotes", mote, ps =>
        {
            var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.loop = true;
            m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f); m.startSpeed = 0.03f; m.maxParticles = 14;
            m.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.075f);
            m.startColor = new Color(1f, 0.85f, 0.6f, 0.8f); m.gravityModifier = 0f;
            var e = ps.emission; e.rateOverTime = 2.2f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.6f; sh.scale = new Vector3(1f, 0.6f, 1f);
            var n = ps.noise; n.enabled = true; n.strength = 0.12f; n.frequency = 0.25f; n.scrollSpeed = 0.1f;
            FadeInOut(ps, 0.15f, 0.25f);
            Sheet(ps, 2f);
        });
        Prefab("VFX_TorchSmoke", smoke, ps =>
        {
            var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.loop = true;
            m.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.2f); m.startSpeed = 0f; m.maxParticles = 10;
            m.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.15f); m.startRotation = 0f;
            m.startColor = new Color(1f, 1f, 1f, 0.5f);
            var e = ps.emission; e.rateOverTime = 3f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.03f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f); v.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            v.y = new ParticleSystem.MinMaxCurve(0.28f, 0.4f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            FadeInOut(ps, 0.1f, 0.8f);
            Sheet(ps, 1f);
        });
        Prefab("VFX_DripSplash", splash, ps =>
        {
            var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.loop = false; m.playOnAwake = false;
            m.duration = 1f; m.startLifetime = 0.5f; m.startSpeed = 0f; m.maxParticles = 4; m.startSize = 0.2f;
            var e = ps.emission; e.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            Sheet(ps, 1f);
        });
        Prefab("VFX_CeilingDust", dust, ps =>
        {
            var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.loop = false; m.playOnAwake = false;
            m.duration = 3f; m.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f); m.startSpeed = 0.15f; m.maxParticles = 120;
            m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f); m.gravityModifier = 0.04f;
            m.startColor = new Color(1f, 1f, 1f, 0.9f);
            var e = ps.emission; e.rateOverTime = 28f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(2.6f, 0.1f, 2.6f); sh.rotation = new Vector3(90f, 0f, 0f);
            var n = ps.noise; n.enabled = true; n.strength = 0.1f; n.frequency = 0.5f;
            FadeInOut(ps, 0.1f, 0.5f);
            Sheet(ps, 3f);
        });
        Prefab("VFX_SparkEmbers", sparkMat, ps =>
        {
            var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.loop = true;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            m.maxParticles = 20; m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f); m.gravityModifier = 0.8f;
            var e = ps.emission; e.rateOverTime = 7f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.12f; sh.rotation = new Vector3(-90f, 0f, 0f);
            Sheet(ps, 1f);
        });

        BuildSounds();
        AssetDatabase.SaveAssets();
        Debug.Log("[Atmosphere] Assets built.");
    }

    // sheets / materials / prefabs

    private static void ConfigureSheet(string path)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter t) return;
        t.textureType = TextureImporterType.Default; t.filterMode = FilterMode.Point; t.mipmapEnabled = false;
        t.textureCompression = TextureImporterCompression.Uncompressed; t.wrapMode = TextureWrapMode.Clamp;
        t.alphaIsTransparency = false; t.npotScale = TextureImporterNPOTScale.None;
        t.SaveAndReimport();
    }

    private static Material Mat(string name, string tex, bool additive, Color tint)
    {
        string path = $"{Dir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", additive ? 1f : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3000;
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
        m.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void Prefab(string name, Material mat, System.Action<ParticleSystem> setup)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.playOnAwake = true; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        var col = ps.collision; col.enabled = false;
        var lights = ps.lights; lights.enabled = false;
        setup(ps);
        PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/{name}.prefab");
        Object.DestroyImmediate(go);
    }

    private static void Sheet(ParticleSystem ps, float cycles)
    {
        var t = ps.textureSheetAnimation; t.enabled = true; t.mode = ParticleSystemAnimationMode.Grid;
        t.numTilesX = 4; t.numTilesY = 4; t.animation = ParticleSystemAnimationType.WholeSheet;
        t.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        t.cycleCount = Mathf.Max(1, Mathf.RoundToInt(cycles)); t.startFrame = 0f;
    }

    private static void FadeInOut(ParticleSystem ps, float inT, float outFrom)
    {
        var c = ps.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inT), new GradientAlphaKey(1f, outFrom), new GradientAlphaKey(0f, 1f) });
        c.color = g;
    }

    // sounds / profile

    private static void BuildSounds()
    {
        var bedRef = AssetDatabase.LoadAssetAtPath<SoundDefinition>($"{AmbientDir}/SD_BedCatacomb.asset");
        var stingRef = AssetDatabase.LoadAssetAtPath<SoundDefinition>($"{AmbientDir}/SD_Amb_Sting.asset");
        foreach (var n in new[] { "BedCorridor", "BedDen", "BedSewer", "BedTower", "BedHall", "BedRitual" })
            Sd($"SD_{n}", $"SFX_Ambient_{n}_01.wav", true, bedRef, 1f, 0, 0f);
        Sd("SD_StingRitual", "SFX_Ambient_StingRitual_01.wav", false, stingRef, 0.8f, 1, 3f);
        Sd("SD_StingDen", "SFX_Ambient_StingDen_01.wav", false, stingRef, 0.8f, 1, 3f);
        AssetDatabase.SaveAssets();

        var ssp = AssetDatabase.LoadAssetAtPath<SoundscapeProfile>(ProfilePath);
        if (ssp == null) { Debug.LogWarning("[Atmosphere] SSP_Default missing."); return; }
        SoundDefinition L(string n) => AssetDatabase.LoadAssetAtPath<SoundDefinition>($"{AmbientDir}/{n}.asset");
        var acoustic = new System.Collections.Generic.List<SoundscapeProfile.AcousticBed>(ssp.AcousticBeds ?? System.Array.Empty<SoundscapeProfile.AcousticBed>());
        void AddA(Acoustic t, string sd, bool onlyTransition = false)
        {
            if (acoustic.Exists(x => x != null && x.Type == t)) return;
            acoustic.Add(new SoundscapeProfile.AcousticBed { Type = t, Loop = L(sd), OnlyInTransition = onlyTransition });
        }
        AddA(Acoustic.Crypt, "SD_BedCatacomb"); AddA(Acoustic.Cave, "SD_BedDen"); AddA(Acoustic.Sewer, "SD_BedSewer");
        AddA(Acoustic.Tower, "SD_BedTower"); AddA(Acoustic.Hall, "SD_BedHall"); AddA(Acoustic.Small, "SD_BedCorridor", true);
        ssp.AcousticBeds = acoustic.ToArray();
        var named = new System.Collections.Generic.List<SoundscapeProfile.NamedRoomBed>(ssp.NamedBeds ?? System.Array.Empty<SoundscapeProfile.NamedRoomBed>());
        void AddN(string key, string loop, string sting)
        {
            if (named.Exists(x => x != null && x.NameContains == key)) return;
            named.Add(new SoundscapeProfile.NamedRoomBed { NameContains = key, Loop = L(loop), Sting = L(sting) });
        }
        AddN("CH_Room_Ritual", "SD_BedRitual", "SD_StingRitual"); AddN("ChuchoDen", "SD_BedDen", "SD_StingDen");
        EditorUtility.SetDirty(ssp);
    }

    private static void Sd(string name, string wav, bool bed, SoundDefinition reference, float volume, int maxVoices, float minInterval)
    {
        string wavPath = $"{AmbientDir}/{wav}";
        if (AssetImporter.GetAtPath(wavPath) is AudioImporter imp)
        {
            var s = imp.defaultSampleSettings;
            if (bed) { s.loadType = AudioClipLoadType.Streaming; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.5f; s.preloadAudioData = false; }
            else { s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.ADPCM; s.preloadAudioData = true; }
            imp.defaultSampleSettings = s; imp.forceToMono = true; imp.SaveAndReimport();
        }
        string path = $"{AmbientDir}/{name}.asset";
        var sd = AssetDatabase.LoadAssetAtPath<SoundDefinition>(path);
        if (sd != null) return;   // never overwrite hand-tuned volumes / caps
        sd = ScriptableObject.CreateInstance<SoundDefinition>(); AssetDatabase.CreateAsset(sd, path);
        var so = new SerializedObject(sd);
        var clips = so.FindProperty("_clips"); clips.arraySize = 1;
        clips.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(wavPath);
        so.FindProperty("_volume").floatValue = volume;
        so.FindProperty("_pitchRange").vector2Value = Vector2.one;
        so.FindProperty("_spatialBlend").floatValue = 0f;
        so.FindProperty("_mixerGroup").objectReferenceValue = new SerializedObject(reference).FindProperty("_mixerGroup").objectReferenceValue;
        so.FindProperty("_minDistance").floatValue = 1f; so.FindProperty("_maxDistance").floatValue = 500f;
        so.FindProperty("_maxVoices").intValue = maxVoices; so.FindProperty("_minInterval").floatValue = minInterval;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sd);
    }

    // scene wiring

    [MenuItem("Fluffterror/Atmosphere/Wire Open Scene")]
    public static void WireOpenScene()
    {
        var sm = GameObject.Find("SoundManager");
        if (sm == null) { Debug.LogWarning("[Atmosphere] No SoundManager in the open scene."); return; }
        if (!sm.TryGetComponent<AtmosphereFX>(out var fx)) fx = Undo.AddComponent<AtmosphereFX>(sm);
        var so = new SerializedObject(fx);
        void Set(string f, string prefab) =>
            so.FindProperty(f).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{prefab}.prefab")?.GetComponent<ParticleSystem>();
        Set("_motesPrefab", "VFX_DustMotes"); Set("_smokePrefab", "VFX_TorchSmoke"); Set("_splashPrefab", "VFX_DripSplash");
        Set("_ceilingDustPrefab", "VFX_CeilingDust"); Set("_sparksPrefab", "VFX_SparkEmbers");
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(sm.scene);
        EditorSceneManager.SaveScene(sm.scene);
    }

    [MenuItem("Fluffterror/Atmosphere/Build + Wire SampleScene and TestScene")]
    public static void BuildAndWireBoth()
    {
        BuildAssets();
        foreach (var p in new[] { "Assets/Scenes/SampleScene.unity", "Assets/Scenes/TestScene.unity" })
        {
            EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
            WireOpenScene();
        }
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
    }
}
