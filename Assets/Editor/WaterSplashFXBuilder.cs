using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the pixel-art water splash VFX (Docs/Design/WaterSplashFX.md): sheet import settings, alpha-blended URP
/// Particles/Unlit materials, the four prefabs and the WaterSplashFX wiring on GameManager.
/// Re-runnable: it uses WaterSplash/WaterRipple/WaterDrop_Sheet.png when present, else the Atmo placeholder sheets
/// (AtmoSplash / AtmoMote, same grids), so re-run it once the final art lands.
/// Menu: Fluffterror > VFX > Build Water Splash / Wire Open Scene / Build + Wire SampleScene and TestScene / Add Water Test Pit.
/// </summary>
public static class WaterSplashFXBuilder
{
    private const string Dir = "Assets/_Game/Rendering/VFX/WaterSplash";
    private const string AtmoDir = "Assets/_Game/Rendering/VFX/Atmosphere";

    private static string Sheet(string finalName, string placeholder)
    {
        string f = $"{Dir}/{finalName}";
        return File.Exists(f) ? f : $"{AtmoDir}/{placeholder}";
    }

    [MenuItem("Fluffterror/VFX/Build Water Splash")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        string splashSheet = Sheet("WaterSplash_Sheet.png", "AtmoSplash_Sheet.png");
        string rippleSheet = Sheet("WaterRipple_Sheet.png", "AtmoSplash_Sheet.png");
        string dropSheet = Sheet("WaterDrop_Sheet.png", "AtmoMote_Sheet.png");
        bool placeholder = splashSheet.StartsWith(AtmoDir) || rippleSheet.StartsWith(AtmoDir) || dropSheet.StartsWith(AtmoDir);
        foreach (var s in new[] { splashSheet, rippleSheet, dropSheet }) ConfigureSheet(s);

        // real sheets carry their own palette; placeholders are tinted towards murky water
        Color tintReal = Color.white, tintPlace = new Color(0.55f, 0.72f, 0.75f, 0.95f);
        var splashMat = Mat("WaterSplash_Mat", splashSheet, splashSheet.StartsWith(AtmoDir) ? tintPlace : tintReal);
        var rippleMat = Mat("WaterRipple_Mat", rippleSheet, rippleSheet.StartsWith(AtmoDir) ? tintPlace : tintReal);
        var dropMat = Mat("WaterDrop_Mat", dropSheet, dropSheet.StartsWith(AtmoDir) ? tintPlace : tintReal);

        // Everything is emitted from script (WaterSplashFX): no emission, no shape, world space.
        Prefab("VFX_WaterStep", splashMat, false, ps =>
        {
            var m = ps.main; m.startLifetime = 0.45f; m.startSize = 0.28f; m.gravityModifier = 0.2f; m.maxParticles = 64;
            SheetAnim(ps);
        });
        Prefab("VFX_WaterEnter", splashMat, false, ps =>
        {
            var m = ps.main; m.startLifetime = 0.9f; m.startSize = 0.45f; m.gravityModifier = 0.3f; m.maxParticles = 96;
            SheetAnim(ps);
        });
        Prefab("VFX_WaterRipple", rippleMat, true, ps =>
        {
            var m = ps.main; m.startLifetime = 0.6f; m.startSize = 0.45f; m.gravityModifier = 0f; m.maxParticles = 12;
            SheetAnim(ps);
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        });
        Prefab("VFX_WaterDrips", dropMat, false, ps =>
        {
            var m = ps.main; m.startLifetime = 0.55f; m.startSize = 0.04f;
            m.gravityModifier = 1f; m.maxParticles = 96;   // x Physics.gravity = 9.81 m/s^2; entry droplets share this pool
            SheetAnim(ps);
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[WaterSplashFX] Built. sheets: splash={splashSheet}, ripple={rippleSheet}, drop={dropSheet}" +
                  (placeholder ? " (PLACEHOLDER art in use; re-run when the final sheets exist)" : " (final art)"));
    }

    private static void ConfigureSheet(string path)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter t) return;
        t.textureType = TextureImporterType.Default; t.filterMode = FilterMode.Point; t.mipmapEnabled = false;
        t.textureCompression = TextureImporterCompression.Uncompressed; t.wrapMode = TextureWrapMode.Clamp;
        t.alphaIsTransparency = false; t.npotScale = TextureImporterNPOTScale.None; t.isReadable = false;
        t.SaveAndReimport();
    }

    private static Material Mat(string name, string tex, Color tint)
    {
        string path = $"{Dir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3010;   // after transparent water planes (3000) so particles never sort behind them
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
        m.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void Prefab(string name, Material mat, bool flatMesh, System.Action<ParticleSystem> setup)
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
        if (flatMesh)
        {
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            r.alignment = ParticleSystemRenderSpace.World;
        }
        else { r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.View; }
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        setup(ps);
        PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/{name}.prefab");
        Object.DestroyImmediate(go);
    }

    private static void SheetAnim(ParticleSystem ps)
    {
        var t = ps.textureSheetAnimation; t.enabled = true; t.mode = ParticleSystemAnimationMode.Grid;
        t.numTilesX = 4; t.numTilesY = 4; t.animation = ParticleSystemAnimationType.WholeSheet;
        t.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        t.cycleCount = 1; t.startFrame = 0f;
    }

    // scene wiring

    [MenuItem("Fluffterror/VFX/Wire Water Splash In Open Scene")]
    public static void WireOpenScene()
    {
        var gm = GameObject.Find("GameManager");
        if (gm == null) { Debug.LogWarning("[WaterSplashFX] No GameManager in the open scene."); return; }
        if (!gm.TryGetComponent<WaterSplashFX>(out var fx)) fx = Undo.AddComponent<WaterSplashFX>(gm);
        var so = new SerializedObject(fx);
        void Set(string f, string prefab) =>
            so.FindProperty(f).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{prefab}.prefab")?.GetComponent<ParticleSystem>();
        Set("_stepPrefab", "VFX_WaterStep"); Set("_enterPrefab", "VFX_WaterEnter");
        Set("_ripplePrefab", "VFX_WaterRipple"); Set("_dripsPrefab", "VFX_WaterDrips");
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(gm.scene);
        EditorSceneManager.SaveScene(gm.scene);
    }

    [MenuItem("Fluffterror/VFX/Build + Wire SampleScene and TestScene")]
    public static void BuildAndWireBoth()
    {
        Build();
        foreach (var p in new[] { "Assets/Scenes/SampleScene.unity", "Assets/Scenes/TestScene.unity" })
        {
            EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
            WireOpenScene();
        }
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
    }

    /// <summary>
    /// Adds a 4x4 m shallow pool (0.25 m deep, WaterVolume + visible water plane) and a 2 m ledge to the OPEN scene,
    /// west of the player start. Idempotent (replaces "WaterTestPit"). Does not touch TestSceneBuilder.
    /// </summary>
    [MenuItem("Fluffterror/VFX/Add Water Test Pit")]
    public static void AddWaterTestPit()
    {
        var old = GameObject.Find("WaterTestPit");
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("WaterTestPit");
        Vector3 c = new Vector3(0f, 0f, 12f);
        var player = Object.FindAnyObjectByType<CharacterBase>();
        if (player != null) c = player.transform.position + player.transform.forward * 8f;
        c.y = 0f;
        root.transform.position = c;

        const float depth = 0.25f;
        var vol = new GameObject("VOL_Water");
        vol.transform.SetParent(root.transform, false);
        vol.transform.localPosition = new Vector3(0f, depth * 0.5f, 0f);
        var bc = vol.AddComponent<BoxCollider>(); bc.size = new Vector3(4f, depth, 4f); bc.isTrigger = true;
        vol.AddComponent<WaterVolume>();

        var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plane.name = "WaterSurface";
        Object.DestroyImmediate(plane.GetComponent<Collider>());
        plane.transform.SetParent(root.transform, false);
        plane.transform.localPosition = new Vector3(0f, depth, 0f);
        plane.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        plane.transform.localScale = new Vector3(4f, 4f, 1f);
        string matPath = $"{Dir}/WaterTestPit_Mat.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, matPath); }
        // transparent like the chunk water (M_ChunksWater): alpha blend, ZWrite off, queue 3000
        mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", 1f); mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000;
        mat.SetColor("_BaseColor", new Color(0.07f, 0.2f, 0.22f, 0.5f));
        EditorUtility.SetDirty(mat);
        plane.GetComponent<MeshRenderer>().sharedMaterial = mat;
        plane.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ledge.name = "Ledge_2m";
        ledge.transform.SetParent(root.transform, false);
        ledge.transform.localPosition = new Vector3(-4f, 1f, 0f);
        ledge.transform.localScale = new Vector3(2f, 2f, 3f);

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Debug.Log($"[WaterSplashFX] Water test pit added at {c} (surface y = {depth}).");
    }
}
