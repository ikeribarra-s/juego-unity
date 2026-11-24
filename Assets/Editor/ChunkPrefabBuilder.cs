using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Builds one prefab per authored map chunk (CH_*.fbx exported by
// ArtSource/Environment/Chunks/build_chunks.py) into Assets/Art/Environment/Chunks/.
//
//  - COL_Wall_* / COL_Block_*  -> BoxCollider on the "Walls" layer (EnvironmentReverb, creature sight)
//  - COL_Floor_* / COL_Ramp_* / COL_Rail_* / COL_Gate_* -> BoxCollider, Default layer
//  - VOL_Water -> trigger BoxCollider
//  - SOCKET_* empties are kept as child transforms (+Z = facing)
//  - SM_* meshes get M_Chunks (water: M_ChunksWater); ceilings cast two-sided shadows
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

    [MenuItem("Fluffterror/Chunks/Build Authored Chunk Prefabs")]
    public static void BuildAll()
    {
        ConfigureTexture();
        var mat = EnsureMaterial();
        var water = EnsureWaterMaterial();
        int wallsLayer = LayerMask.NameToLayer("Walls");
        if (wallsLayer < 0) Debug.LogWarning("[ChunkPrefabBuilder] No 'Walls' layer — run Fluffterror > Setup > Add Walls Layer.");

        var guids = AssetDatabase.FindAssets("t:Model", new[] { Models });
        int built = 0;
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            string name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("CH_")) continue;
            ConfigureModel(path, mat, water);
            BuildPrefab(path, name, mat, water, wallsLayer);
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

    private static Material EnsureMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, MatPath);
        }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath));
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.08f);
        m.SetFloat("_Metallic", 0f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material EnsureWaterMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(WaterMatPath);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, WaterMatPath);
        }
        // URP Lit, transparent alpha blend, dark teal, glossy (catches torch highlights)
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetShaderPassEnabled("DepthOnly", false);
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.SetColor("_BaseColor", new Color(0.05f, 0.15f, 0.17f, 0.78f));
        m.SetFloat("_Smoothness", 0.92f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void ConfigureModel(string path, Material mat, Material water)
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
        var k1 = new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Chunks");
        var k2 = new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_ChunksWater");
        if (!map.TryGetValue(k1, out var o1) || o1 != mat) { mi.AddRemap(k1, mat); dirty = true; }
        if (!map.TryGetValue(k2, out var o2) || o2 != water) { mi.AddRemap(k2, water); dirty = true; }
        if (dirty) mi.SaveAndReimport();
    }

    private static void BuildPrefab(string modelPath, string name, Material mat, Material water, int wallsLayer)
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

        // Blender keeps object names unique per .blend, so repeated socket/collider names come
        // through as "SOCKET_Torch_01.003" — strip the numeric suffix.
        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            t.name = System.Text.RegularExpressions.Regex.Replace(t.name, @"\.\d+$", "");

        Transform gate = null;
        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            if (t.name.EndsWith("_Gate") && t.name.StartsWith("SM_")) gate = t;

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
                if (n.StartsWith("COL_Gate") && gate != null) t.SetParent(gate, true);
                else GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic
                                                           | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                continue;
            }
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && n.StartsWith("SM_"))
            {
                bool isWater = n.EndsWith("_Water");
                bool moving = n.EndsWith("_Bell") || n.EndsWith("_Gate");
                mr.sharedMaterial = isWater ? water : mat;
                mr.shadowCastingMode = n.EndsWith("_Ceiling") ? ShadowCastingMode.TwoSided
                                     : isWater ? ShadowCastingMode.Off : ShadowCastingMode.On;
                mr.receiveShadows = true;
                if (!moving)
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic
                                                               | (isWater ? (StaticEditorFlags)0 : StaticEditorFlags.OccluderStatic));
            }
        }

        string prefabPath = $"{Root}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
        Object.DestroyImmediate(inst);
    }
}
