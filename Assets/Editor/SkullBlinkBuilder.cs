using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Skull blink setup (Docs/Design/SkullBlink.md section 6). Re-runnable; never touches TestSceneBuilder.</summary>
public static class SkullBlinkBuilder
{
    const string Src  = "ArtSource/Characters/Animations/Blink/";
    const string Dest = "Assets/Art/Characters/Skeleton/";
    const string Lids = Dest + "Skeleton_Lids.fbx";

    [MenuItem("Fluffterror/Characters/Skull Blink/Setup Assets")]
    public static void SetupAssets()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[SkullBlink] Not in Play mode."); return; }
        if (!File.Exists(Lids))
        {
            File.Copy(Src + "Skeleton_Lids.fbx", Lids);
            AssetDatabase.ImportAsset(Lids, ImportAssetOptions.ForceUpdate);
        }
        var imp = (ModelImporter)AssetImporter.GetAtPath(Lids);
        imp.globalScale = 1.201179f;
        imp.useFileScale = true;
        imp.importBlendShapes = true;
        imp.importBlendShapeNormals = ModelImporterNormals.Calculate;
        imp.isReadable = true;
        imp.animationType = ModelImporterAnimationType.None;
        imp.importAnimation = false;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Dest + "M_Skeleton.mat");
        if (mat != null)
        {
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Local);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Lids))
                if (o is Material m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(m), mat);
        }
        imp.SaveAndReimport();

        var go = AssetDatabase.LoadAssetAtPath<GameObject>(Lids);
        var mf = go.GetComponentInChildren<MeshFilter>();
        var mr = go.GetComponentInChildren<MeshRenderer>();
        if (mf == null || mf.sharedMesh == null) { Debug.LogError("[SkullBlink] Lids FBX has no mesh."); return; }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < mf.sharedMesh.blendShapeCount; i++) sb.Append(mf.sharedMesh.GetBlendShapeName(i)).Append(' ');
        Debug.Log($"[SkullBlink] Lids imported (Rig None = MeshFilter, converted to a SkinnedMeshRenderer when wired): verts {mf.sharedMesh.vertexCount}, blend shapes: {sb}, material {(mr && mr.sharedMaterial ? mr.sharedMaterial.name : "none")}, readable {mf.sharedMesh.isReadable}");
        CheckTable("Skeleton_Lids@Blink.fbx", SkullBlink.BlinkKeys, 8);
        CheckTable("Skeleton_Lids@BlinkDouble.fbx", SkullBlink.BlinkDoubleKeys, 13);
        SetupSound();
    }

    const string SfxDir = "Assets/_Game/Audio/SFX/Character/";
    const string SoundPath = SfxDir + "SD_Skull_Blink.asset";

    /// <summary>Import settings for SFX_Character_SkullBlink_01..03 + the SD_Skull_Blink SoundDefinition (quiet, 3D, close range).</summary>
    public static SoundDefinition SetupSound()
    {
        var clips = new System.Collections.Generic.List<AudioClip>();
        for (int i = 1; i <= 3; i++)
        {
            string p = $"{SfxDir}SFX_Character_SkullBlink_{i:00}.wav";
            var imp = AssetImporter.GetAtPath(p) as AudioImporter;
            if (imp == null) { Debug.LogError("[SkullBlink] Missing " + p); continue; }
            var d = imp.defaultSampleSettings;
            d.loadType = AudioClipLoadType.DecompressOnLoad;
            d.compressionFormat = AudioCompressionFormat.ADPCM;
            d.preloadAudioData = true;
            imp.defaultSampleSettings = d;
            imp.forceToMono = true;
            imp.loadInBackground = false;
            imp.SaveAndReimport();
            clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(p));
        }
        var sd = AssetDatabase.LoadAssetAtPath<SoundDefinition>(SoundPath);
        if (sd == null)
        {
            sd = ScriptableObject.CreateInstance<SoundDefinition>();
            AssetDatabase.CreateAsset(sd, SoundPath);
        }
        var so = new SerializedObject(sd);
        var arr = so.FindProperty("_clips");
        arr.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        so.FindProperty("_volume").floatValue = 0.5f;
        so.FindProperty("_pitchRange").vector2Value = new Vector2(0.92f, 1.08f);
        so.FindProperty("_spatialBlend").floatValue = 1f;
        so.FindProperty("_minDistance").floatValue = 0.5f;
        so.FindProperty("_maxDistance").floatValue = 6f;
        so.FindProperty("_maxVoices").intValue = 2;
        so.FindProperty("_minInterval").floatValue = 0.08f;
        var yip = AssetDatabase.LoadAssetAtPath<SoundDefinition>(SfxDir + "SD_Skull_Yip.asset");   // same Actions mixer group
        if (yip != null) so.FindProperty("_mixerGroup").objectReferenceValue = new SerializedObject(yip).FindProperty("_mixerGroup").objectReferenceValue;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sd);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SkullBlink] SD_Skull_Blink ready ({clips.Count} clips).");
        return sd;
    }

    // Verify the code tables against the Blender clips (temporary import into a scratch folder, then deleted).
    static void CheckTable(string file, float[] table, int frames)
    {
        string tmpDir = "Assets/_SkullBlinkTmp";
        AssetDatabase.CreateFolder("Assets", "_SkullBlinkTmp");
        string p = tmpDir + "/" + file;
        File.Copy(Src + file, p, true);
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        AnimationClip clip = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p)) if (o is AnimationClip c && !c.name.StartsWith("__preview")) clip = c;
        if (clip == null) Debug.LogWarning($"[SkullBlink] No clip in {file}.");
        else
        {
            float maxErr = 0f;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (!b.propertyName.StartsWith("blendShape.")) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                for (int f = 0; f < frames; f++) maxErr = Mathf.Max(maxErr, Mathf.Abs(curve.Evaluate(f / 30f) - table[f]));
            }
            Debug.Log($"[SkullBlink] {file}: max |clip - table| = {maxErr:F2} (len {clip.length:F3}s) {(maxErr <= 1f ? "OK" : "MISMATCH - update the code tables")}");
        }
        AssetDatabase.DeleteAsset(tmpDir);
    }

    [MenuItem("Fluffterror/Characters/Skull Blink/Wire Open Scene")]
    public static void WireOpenScene()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[SkullBlink] Not in Play mode."); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Lids);
        if (prefab == null) { Debug.LogError("[SkullBlink] Run Setup Assets first."); return; }
        int n = 0;
        var sound = SetupSound();
        foreach (var anim in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include))
        {
            Transform head = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null) continue;
            Transform jaw = head.Find("JawPivot");
            if (jaw == null) { Debug.Log($"[SkullBlink] '{anim.name}' has no JawPivot - skipped."); continue; }
            var existing = head.Find("LidPivot");
            if (existing != null)
            {
                var sb0 = existing.GetComponentInChildren<SkullBlink>(true);
                if (sb0 != null) { AssignSound(sb0, sound); n++; }
                Debug.Log($"[SkullBlink] '{anim.name}' already wired (sound assigned).");
                continue;
            }

            string mode = PrefabUtility.IsPartOfPrefabInstance(anim.gameObject) ? "scene instance (override)" : "plain scene object";
            var pivot = new GameObject("LidPivot");
            Undo.RegisterCreatedObjectUndo(pivot, "Skull blink");
            pivot.transform.SetParent(head, false);
            pivot.transform.localPosition = jaw.localPosition;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;

            // Rig None imports a static MeshRenderer, which cannot show blend shapes: build the SkinnedMeshRenderer by hand
            // (same mesh + materials, FBX root rotation/scale kept; the link to the FBX is the mesh asset itself).
            var srcMf = prefab.GetComponentInChildren<MeshFilter>();
            var srcMr = prefab.GetComponentInChildren<MeshRenderer>();
            var inst = new GameObject("Skeleton_Lids");
            inst.transform.SetParent(pivot.transform, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = srcMf.transform.localRotation;
            inst.transform.localScale = srcMf.transform.localScale;
            var smr = inst.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = srcMf.sharedMesh;
            smr.sharedMaterials = srcMr.sharedMaterials;
            smr.rootBone = inst.transform;   // own space: the mesh bounds are in FBX (Z-up) space
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.receiveShadows = true;
            smr.updateWhenOffscreen = true;
            var blink = smr.gameObject.GetComponent<SkullBlink>();
            if (blink == null) blink = smr.gameObject.AddComponent<SkullBlink>();
            AssignSound(blink, sound);
            EditorUtility.SetDirty(smr);
            n++;
            Debug.Log($"[SkullBlink] Wired '{anim.name}' ({mode}).");
        }
        if (n > 0) EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[SkullBlink] {n} model(s) wired. Save the scene.");
    }

    static void AssignSound(SkullBlink b, SoundDefinition sd)
    {
        var so = new SerializedObject(b);
        so.FindProperty("_blinkSound").objectReferenceValue = sd;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(b);
    }
}
