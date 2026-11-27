using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fluffterror > Death > Add Test Dummy To Open Scene (re-runnable; replaces an existing TestDummy / KillPad):
///  • wires a <see cref="SkeletonDisassembler"/> (with SD_BoneClack) on every character in the open scene,
///  • copies the local player as <c>TestDummy</c> a few metres ahead (input-less InputReader asset, camera disabled +
///    untagged, AudioListener / reverb / CameraEffects removed, NeckFollowCamera off, FirstPersonBodyCull remote,
///    TorchArmAnimator local view off, CartoonAnimator remote, owner name "Test Dummy"),
///  • adds a red <see cref="TrapKillVolume"/> pad next to it and <see cref="DeathDebugKeys"/> (K / F9 / F10) on GameManager.
/// Does not touch anything else (TestSceneBuilder is NOT re-run).
/// </summary>
public static class DeathTestSetup
{
    private const string DummyName   = "TestDummy";
    private const string PadName     = "KillPad";
    private const string InputPath   = "Assets/_Game/Testing/InputReader_Dummy.asset";
    private const string PadMatPath  = "Assets/_Game/Testing/M_Test_KillPad.mat";
    private const string BoneSdPath  = "Assets/_Game/Audio/SFX/Character/SD_BoneClack.asset";

    private static readonly Vector3 DummyPos = new(3f, 0.05f, 3.5f);
    private static readonly Vector3 PadPos   = new(5.5f, 0f, 2.5f);

    [MenuItem("Fluffterror/Death/Add Test Dummy To Open Scene")]
    public static void AddTestDummy()
    {
        CharacterBase player = FindPlayer();
        if (player == null) { Debug.LogError("[DeathTestSetup] No local player (CharacterBase with an enabled child Camera) in the open scene."); return; }

        // Replace previous run.
        foreach (var name in new[] { DummyName, PadName })
        {
            GameObject old = GameObject.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }

        var boneSd = AssetDatabase.LoadAssetAtPath<SoundDefinition>(BoneSdPath);
        WireDisassembler(player, boneSd, "");

        // ── Dummy ───────────────────────────────────────────────────────────
        GameObject dummy = Object.Instantiate(player.gameObject);
        dummy.name = DummyName;
        Undo.RegisterCreatedObjectUndo(dummy, "Add Test Dummy");
        dummy.transform.SetPositionAndRotation(DummyPos, Quaternion.Euler(0f, 200f, 0f));

        var cb = dummy.GetComponent<CharacterBase>();
        var so = new SerializedObject(cb);
        so.FindProperty("_input").objectReferenceValue = DummyInput();
        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (var cam in dummy.GetComponentsInChildren<Camera>(true))
        {
            cam.enabled = false;              // kept (InteractionSystem / PhysicsGrabber look one up in Start) but never renders
            cam.gameObject.tag = "Untagged";
            DestroyAll<EnvironmentReverb>(cam.gameObject);   // requires the reverb filter, which requires the listener
            DestroyAll<AudioReverbFilter>(cam.gameObject);
            DestroyAll<CameraEffects>(cam.gameObject);
            DestroyAll<AudioListener>(cam.gameObject);
        }
        foreach (var nfc in dummy.GetComponentsInChildren<NeckFollowCamera>(true)) nfc.enabled = false;
        foreach (var c in dummy.GetComponentsInChildren<FirstPersonBodyCull>(true)) SetBool(c, "_isLocalPlayer", false);
        foreach (var c in dummy.GetComponentsInChildren<TorchArmAnimator>(true)) SetBool(c, "_smoothLocalView", false);
        foreach (var c in dummy.GetComponentsInChildren<CartoonAnimator>(true))
        {
            var cso = new SerializedObject(c);
            var p = cso.FindProperty("_localMode");
            if (p != null) { p.enumValueIndex = 2; cso.ApplyModifiedPropertiesWithoutUndo(); }   // LocalMode.Remote
        }
        WireDisassembler(cb, boneSd, "Test Dummy");

        // ── Kill pad ────────────────────────────────────────────────────────
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = PadName;
        Undo.RegisterCreatedObjectUndo(pad, "Add Kill Pad");
        pad.transform.position = PadPos + Vector3.up * 0.03f;
        pad.transform.localScale = new Vector3(1.2f, 0.06f, 1.2f);
        Object.DestroyImmediate(pad.GetComponent<BoxCollider>());
        pad.GetComponent<MeshRenderer>().sharedMaterial = PadMaterial();
        var trigger = new GameObject("Trigger");
        trigger.transform.SetParent(pad.transform, false);
        var box = trigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 10f, 0f);       // in pad space (y scale 0.06): ~1.2 m tall volume above the plate
        box.size = new Vector3(1f, 20f, 1f);
        trigger.AddComponent<TrapKillVolume>();
        Transform testArea = GameObject.Find("TestArea") != null ? GameObject.Find("TestArea").transform : null;
        if (testArea != null) pad.transform.SetParent(testArea, true);

        // ── Debug keys ──────────────────────────────────────────────────────
        GameObject gm = GameObject.Find("GameManager");
        if (gm == null) gm = new GameObject("GameManager");
        var keys = gm.GetComponent<DeathDebugKeys>();
        if (keys == null) keys = Undo.AddComponent<DeathDebugKeys>(gm);
        var kso = new SerializedObject(keys);
        kso.FindProperty("_player").objectReferenceValue = player;
        kso.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log($"[DeathTestSetup] TestDummy at {DummyPos}, kill pad at {PadPos}, DeathDebugKeys on '{gm.name}'. Save the scene.");
    }

    [MenuItem("Fluffterror/Death/Wire Skeleton Disassembler In Open Scene")]
    public static void WireOpenScene()
    {
        var boneSd = AssetDatabase.LoadAssetAtPath<SoundDefinition>(BoneSdPath);
        int n = 0;
        foreach (var cb in Object.FindObjectsByType<CharacterBase>(FindObjectsInactive.Include)) { WireDisassembler(cb, boneSd, null); n++; }
        if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[DeathTestSetup] SkeletonDisassembler wired on {n} character(s).");
    }

    private static CharacterBase FindPlayer()
    {
        foreach (var cb in Object.FindObjectsByType<CharacterBase>(FindObjectsInactive.Exclude))
        {
            if (cb.name == DummyName) continue;
            Camera cam = cb.GetComponentInChildren<Camera>();
            if (cam != null && cam.enabled) return cb;
        }
        return null;
    }

    private static void WireDisassembler(CharacterBase cb, SoundDefinition sd, string ownerName)
    {
        var d = cb.GetComponent<SkeletonDisassembler>();
        if (d == null) d = Undo.AddComponent<SkeletonDisassembler>(cb.gameObject);
        var so = new SerializedObject(d);
        if (sd != null) so.FindProperty("_boneSound").objectReferenceValue = sd;
        if (ownerName != null) so.FindProperty("_ownerNameOverride").stringValue = ownerName;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static InputReader DummyInput()
    {
        var reader = AssetDatabase.LoadAssetAtPath<InputReader>(InputPath);
        if (reader != null) return reader;
        reader = ScriptableObject.CreateInstance<InputReader>();   // no InputActionAsset → never fires events
        AssetDatabase.CreateAsset(reader, InputPath);
        AssetDatabase.SaveAssets();
        return reader;
    }

    private static Material PadMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(PadMatPath);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", new Color(0.75f, 0.06f, 0.05f));
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(1.2f, 0.08f, 0.05f));
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        AssetDatabase.CreateAsset(mat, PadMatPath);
        return mat;
    }

    private static void SetBool(Object c, string prop, bool value)
    {
        var so = new SerializedObject(c);
        var p = so.FindProperty(prop);
        if (p == null) { Debug.LogWarning($"[DeathTestSetup] {c.GetType().Name}.{prop} not found."); return; }
        p.boolValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void DestroyAll<T>(GameObject go) where T : Component
    {
        foreach (var c in go.GetComponents<T>()) Object.DestroyImmediate(c);
    }
}
