using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Builds one prefab per authored map chunk (CH_*.fbx exported by
// ArtSource/Environment/Chunks/build_chunks.py) into Assets/Art/Environment/Chunks/.
//
//  Meshes (one shared atlas material M_Chunks; water M_ChunksWater; light shaft M_ChunksShaft):
//   SM_<C>            structure  - LOD0+LOD1, casts shadows (one-sided), occluder + occludee
//   SM_<C>_Detail     detail     - LOD0 only, no shadows, occludee only
//   SM_<C>_FloorLOD1  flat floor - LOD1 only, no shadows
//   SM_<C>_Ceiling    ceiling    - both LODs, two-sided shadows (blocks the moon, generator §9)
//   _Water/_Shaft (no shadows), _Bell/_Gate/_Lid_NN (movable, not static), _Collar/_Roof (inactive,
//   AuthoredChunk.FitToTheme turns them on)
//  Colliders: COL_Wall_* / COL_Block_* -> BoxCollider on "Walls"; COL_Floor/Ramp/Rail/Gate/Lid -> Default;
//   VOL_* -> trigger. COL_Gate/COL_Lid_NN are parented to their moving mesh.
//  LODGroup on the root (LOD0 > LOD0_HEIGHT, LOD1 > CULL_HEIGHT, culled below).
//  SOCKET_* empties stay as child transforms (+Z = facing). AuthoredChunk on the root.
//
// The FBX is unpacked into the prefab, so re-run this after re-exporting from Blender.
// Menu: Fluffterror > Chunks > Build Authored Chunk Prefabs
public static class ChunkPrefabBuilder
{
    private const string Root = "Assets/Art/Environment/Chunks";
    private const string Models = Root + "/Models";
    private const string TexPath = Root + "/T_Chunks_D.png";
    private const string MatPath = Root + "/M_Chunks.mat";
    private const string WaterMatPath = Root + "/M_ChunksWater.mat";
    private const string ShaftMatPath = Root + "/M_ChunksShaft.mat";
    private const string PS2Shader = "Fluffterror/PS2Lit";   // Assets/Materials/Shaders/PS2Lit.shader

    public const float LOD0_HEIGHT = 0.30f;   // LOD0 -> LOD1 (lodBias 2, fov 60: ~47 m for an 8 m chunk, ~93 m for 16 m)
    public const float CULL_HEIGHT = 0.05f;   // culled below (~280 m / 560 m; fog hides it long before)

    private const StaticEditorFlags Occluder = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
    private const StaticEditorFlags Occludee = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;

    [MenuItem("Fluffterror/Chunks/Build Authored Chunk Prefabs")]
    public static void BuildAll()
    {
        ConfigureTexture();
        var mat = EnsureMaterial();
        var water = EnsureWaterMaterial();
        var shaft = EnsureShaftMaterial();
        int wallsLayer = LayerMask.NameToLayer("Walls");
        if (wallsLayer < 0) Debug.LogWarning("[ChunkPrefabBuilder] No 'Walls' layer — run Fluffterror > Setup > Add Walls Layer.");

        int built = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { Models }))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            string name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("CH_")) continue;
            ConfigureModel(path, mat, water, shaft);
            BuildPrefab(path, name, mat, water, shaft, wallsLayer);
            built++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ChunkPrefabBuilder] Built {built} chunk prefabs in {Root}.");
    }

    private static void ConfigureTexture()
    {
        var ti = AssetImporter.GetAtPath(TexPath) as TextureImporter;
        if (ti == null) { Debug.LogError("[ChunkPrefabBuilder] Missing " + TexPath); return; }
        ti.textureType = TextureImporterType.Default;
        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.sRGBTexture = true;
        ti.alphaIsTransparency = false;
        ti.SaveAndReimport();
    }

    private static Material LoadOrCreate(string path, string shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    private static Material EnsureMaterial()
    {
        // PS2 pass: point-sampled 512 atlas of tileable 64 px cells x baked vertex-colour AO/grime.
        var m = LoadOrCreate(MatPath, PS2Shader);
        var ps2 = Shader.Find(PS2Shader);
        if (ps2 != null && m.shader != ps2) m.shader = ps2;
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath));
        m.SetColor("_BaseColor", Color.white);
        m.enableInstancing = true;     // SRP Batcher handles the merged meshes; instancing helps any repeated detail
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void MakeTransparent(Material m, BlendMode src, BlendMode dst)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)src);
        m.SetFloat("_DstBlend", (float)dst);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetShaderPassEnabled("DepthOnly", false);
        m.SetShaderPassEnabled("ShadowCaster", false);
    }

    private static Material EnsureWaterMaterial()
    {
        var m = LoadOrCreate(WaterMatPath, "Universal Render Pipeline/Lit");
        m.SetFloat("_Blend", 0f);
        MakeTransparent(m, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
        m.SetColor("_BaseColor", new Color(0.05f, 0.15f, 0.17f, 0.78f));
        m.SetFloat("_Smoothness", 0.92f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material EnsureShaftMaterial()
    {
        // moonlight shaft through a broken window: unlit, additive, faint
        var m = LoadOrCreate(ShaftMatPath, "Universal Render Pipeline/Unlit");
        m.SetFloat("_Blend", 2f);
        MakeTransparent(m, BlendMode.One, BlendMode.One);
        m.SetFloat("_Cull", 0f);
        m.SetColor("_BaseColor", new Color(0.10f, 0.12f, 0.16f, 1f));
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void ConfigureModel(string path, Material mat, Material water, Material shaft)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        bool dirty = false;
        void Set<T>(System.Func<T> get, System.Action<T> set, T v)
        {
            if (!Equals(get(), v)) { set(v); dirty = true; }
        }
        Set(() => mi.globalScale, v => mi.globalScale = v, 1f);
        Set(() => mi.useFileScale, v => mi.useFileScale = v, true);
        Set(() => mi.importAnimation, v => mi.importAnimation = v, false);
        Set(() => mi.importCameras, v => mi.importCameras = v, false);
        Set(() => mi.importLights, v => mi.importLights = v, false);
        Set(() => mi.addCollider, v => mi.addCollider = v, false);
        Set(() => mi.isReadable, v => mi.isReadable = v, false);
        Set(() => mi.animationType, v => mi.animationType = v, ModelImporterAnimationType.None);
        Set(() => mi.materialImportMode, v => mi.materialImportMode = v, ModelImporterMaterialImportMode.ImportViaMaterialDescription);
        var map = mi.GetExternalObjectMap();
        foreach (var (key, m) in new[] { ("M_Chunks", mat), ("M_ChunksWater", water), ("M_ChunksShaft", shaft) })
        {
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), key);
            if (!map.TryGetValue(id, out var o) || o != m) { mi.AddRemap(id, m); dirty = true; }
        }
        if (dirty) mi.SaveAndReimport();
    }

    private static void BuildPrefab(string modelPath, string name, Material mat, Material water, Material shaft, int wallsLayer)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = name;
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Flatten the Blender root empty (CH_<Name>) if the importer kept it as a child.
        var inner = inst.transform.Find(name);
        if (inner != null && inner != inst.transform)
        {
            while (inner.childCount > 0) inner.GetChild(0).SetParent(inst.transform, true);
            Object.DestroyImmediate(inner.gameObject);
        }
        // Blender keeps object names unique per .blend: strip ".003"-style suffixes.
        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            t.name = System.Text.RegularExpressions.Regex.Replace(t.name, @"\.\d+$", "");

        string sm = "SM_" + name.Substring(3);
        var byName = new Dictionary<string, Transform>(System.StringComparer.Ordinal);
        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            if (!byName.ContainsKey(t.name)) byName[t.name] = t;
        Transform Find(string n) => byName.TryGetValue(n, out var t) ? t : null;

        var lod0 = new List<Renderer>();
        var lod1 = new List<Renderer>();
        foreach (var t in inst.GetComponentsInChildren<Transform>(true).ToArray())
        {
            if (t == null || t == inst.transform) continue;
            string n = t.name;
            var go = t.gameObject;
            if (n.StartsWith("COL_") || n.StartsWith("VOL_"))
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(mf);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = b.center;
                bc.size = b.size;
                bool sightBlocker = n.StartsWith("COL_Wall") || n.StartsWith("COL_Block");
                go.layer = sightBlocker && wallsLayer >= 0 ? wallsLayer : 0;
                if (n.StartsWith("VOL_")) bc.isTrigger = true;
                if (n.StartsWith("VOL_Water")) go.AddComponent<WaterVolume>();
                Transform mover = null;
                if (n.StartsWith("COL_Gate")) mover = Find(sm + "_Gate");
                else if (n.StartsWith("COL_Lid_")) mover = Find(sm + "_Lid_" + n.Substring("COL_Lid_".Length));
                if (mover != null) t.SetParent(mover, true);
                else GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                continue;
            }
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null || !n.StartsWith("SM_")) continue;
            string suffix = n.Length > sm.Length ? n.Substring(sm.Length) : "";
            mr.sharedMaterial = suffix == "_Water" ? water : suffix == "_Shaft" ? shaft : mat;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            bool moving = suffix == "_Bell" || suffix == "_Gate" || suffix.StartsWith("_Lid_");
            switch (suffix)
            {
                case "":            mr.shadowCastingMode = ShadowCastingMode.On; Flags(go, Occluder); lod0.Add(mr); lod1.Add(mr); break;
                case "_Detail":     mr.shadowCastingMode = ShadowCastingMode.Off; Flags(go, Occludee); lod0.Add(mr); break;
                case "_FloorLOD1":  mr.shadowCastingMode = ShadowCastingMode.Off; Flags(go, Occludee); lod1.Add(mr); break;
                case "_Ceiling":    mr.shadowCastingMode = ShadowCastingMode.TwoSided; Flags(go, Occludee); lod0.Add(mr); lod1.Add(mr); break;
                case "_Water":
                case "_Shaft":      mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = suffix == "_Water"; Flags(go, Occludee); lod0.Add(mr); lod1.Add(mr); break;
                case "_Collar":     mr.shadowCastingMode = ShadowCastingMode.On; Flags(go, Occludee); go.SetActive(false); lod0.Add(mr); lod1.Add(mr); break;
                case "_Roof":       mr.shadowCastingMode = ShadowCastingMode.On; Flags(go, Occludee); go.SetActive(false); lod0.Add(mr); lod1.Add(mr); break;
                default:            mr.shadowCastingMode = moving ? ShadowCastingMode.On : ShadowCastingMode.Off; lod0.Add(mr); lod1.Add(mr); break;
            }
        }

        NestHeroProps(inst, lod0, lod1);

        var lg = inst.AddComponent<LODGroup>();
        lg.fadeMode = LODFadeMode.None;
        lg.SetLODs(new[] { new LOD(LOD0_HEIGHT, lod0.ToArray()), new LOD(CULL_HEIGHT, lod1.ToArray()) });
        lg.RecalculateBounds();

        var roof = Find(sm + "_Roof");
        var collar = Find(sm + "_Collar");
        Vector2Int fp = new Vector2Int(8, 8);
        if (roof != null)
        {
            var b = roof.GetComponent<MeshFilter>().sharedMesh.bounds;
            fp = new Vector2Int(Mathf.RoundToInt(b.size.x), Mathf.RoundToInt(b.size.z));
        }
        float wallH = collar != null ? collar.localPosition.y : 4f;
        var ac = inst.AddComponent<AuthoredChunk>();
        ac.Configure(fp, wallH, collar ? collar.gameObject : null, roof ? roof.gameObject : null);

        // Acoustic zone (no collider: tested mathematically, so it can't touch physics/NavMesh/interaction)
        var zone = inst.AddComponent<AcousticZone>();
        zone.Configure(AcousticFor(name, fp), new Vector3(0f, wallH * 0.5f + 0.5f, 0f), new Vector3(fp.x, wallH + 1f, fp.y));

        PrefabUtility.SaveAsPrefabAsset(inst, $"{Root}/{name}.prefab");
        Object.DestroyImmediate(inst);
    }

    private static Acoustic AcousticFor(string name, Vector2Int fp)
    {
        if (name.StartsWith("CH_Corridor_")) return Acoustic.Small;
        switch (name)
        {
            case "CH_Room_PillarHall": case "CH_Room_Library": case "CH_Room_Chapel":
            case "CH_Room_SpiralHall": case "CH_Room_Ritual": case "CH_SP_WellRoom":
                return Acoustic.Hall;
            case "CH_Room_Crypt": case "CH_Room_Mausoleum": case "CH_SP_Ossuary": case "CH_SP_FloodedCrypt":
            case "CH_Room_Larder": case "CH_Room_PrisonBlock":
                return Acoustic.Crypt;
            case "CH_SP_CaveTransition": case "CH_SP_ChuchoDen": return Acoustic.Cave;
            case "CH_SP_SewerChannel": return Acoustic.Sewer;
            case "CH_SP_BellTower": return Acoustic.Tower;
        }
        return fp.x * fp.y >= 144 ? Acoustic.Hall : Acoustic.Small;
    }

    private static void Flags(GameObject go, StaticEditorFlags f) => GameObjectUtility.SetStaticEditorFlags(go, f);

    private const string HeroPropDir = "Assets/Art/Environment/Props/Meshy";

    /// <summary>
    /// Meshy hero props: every HERO_&lt;Name&gt;[_NN] empty exported by build_chunks.py gets a nested instance of
    /// PROP_Meshy&lt;Name&gt;.prefab at local zero (the empty carries position, facing and scale; prop +Z = front).
    /// The props' own BoxColliders are removed: the chunk's COL_* boxes already cover each feature, so the
    /// collider/socket layout (and the ChunkLibrary contract) is unchanged. Props under a moving part
    /// (SM_*_Bell, SM_*_Lid_NN) stay non-static and move with it.
    /// </summary>
    private static void NestHeroProps(GameObject inst, List<Renderer> lod0, List<Renderer> lod1)
    {
        foreach (var t in inst.GetComponentsInChildren<Transform>(true).ToArray())
        {
            if (!t.name.StartsWith("HERO_")) continue;
            // HERO_<Prop>[_NN][__<Part>]: <Part> = moving object suffix (Bell, Lid_03) to ride along with
            string body = t.name.Substring(5), part = null;
            int sep = body.IndexOf("__", System.StringComparison.Ordinal);
            if (sep >= 0) { part = body.Substring(sep + 2); body = body.Substring(0, sep); }
            string prop = System.Text.RegularExpressions.Regex.Replace(body, @"_\d\d$", "");
            if (part != null)
            {
                var mover = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "SM_" + inst.name.Substring(3) + "_" + part);
                if (mover != null) t.SetParent(mover, true);
                else Debug.LogWarning($"[ChunkPrefabBuilder] {inst.name}: {t.name} part '{part}' not found");
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{HeroPropDir}/PROP_Meshy{prop}.prefab");
            if (prefab == null) { Debug.LogWarning($"[ChunkPrefabBuilder] {inst.name}: no PROP_Meshy{prop}.prefab for {t.name}"); continue; }
            var p = (GameObject)PrefabUtility.InstantiatePrefab(prefab, t);
            p.transform.localPosition = Vector3.zero;
            p.transform.localRotation = Quaternion.identity;
            p.transform.localScale = Vector3.one;
            foreach (var c in p.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            bool moving = false;
            for (var a = t; a != null && a != inst.transform; a = a.parent)
                if (a.name.EndsWith("_Bell") || a.name.Contains("_Lid_") || a.name.EndsWith("_Gate")) moving = true;
            foreach (var r in p.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (!moving) Flags(r.gameObject, Occludee);
                lod0.Add(r); lod1.Add(r);
            }
        }
    }
}
