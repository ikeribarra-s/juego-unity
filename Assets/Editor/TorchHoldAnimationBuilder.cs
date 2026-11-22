using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu <b>Fluffterror > Animation > Build Torch Hold</b>. Builds (or rebuilds) everything the
/// "raised torch" left-arm pose needs, with no external tools:
///  • <c>PlayerAnimator.controller</c> — a copy of the Starter Assets controller (created once, never
///    overwritten, so hand edits survive). Starter Assets files are left untouched.
///  • <c>TorchHold_LeftArm.anim</c> — a looping humanoid muscle clip: left arm raised forward-left at
///    head height, forearm up, fist closed, plus a slow sway so it feels alive.
///  • <c>LeftArm_Torch.mask</c> — humanoid Avatar Mask with only the left arm + left fingers.
///  • Animator layer <c>LeftArm_Torch</c> (Override, mask above, weight 0) added to the controller.
///  • In the open scene(s): every Animator with an <see cref="AnimationHandler"/> is switched to the
///    new controller and gets a fresh <see cref="TorchArmAnimator"/> (re-added, so its grip calibration
///    defaults — tied to this pose — replace any stale serialized values).
///
/// Tweak the pose with the constants below and re-run the menu. Muscle values are normalized
/// (-1..1, Unity's Muscles &amp; Settings space); they were solved numerically against the
/// Starter Assets idle so that, looking level in first person, the lantern (~0.37 m up the shaft from the
/// fist) sits upper-left on screen: fist centre ~(-0.37, 2.00, 0.64) m from the character root (idle sample;
/// in play the head-look lean shifts it ~(0, -0.08, +0.1)), lantern ~(-0.33, 2.37, 0.59), shaft ~10° off vertical, elbow below the fist. The wrist is cocked so
/// the fist's grip axis (the torch shaft) points up. Muscles kept at or under ~0.77 to avoid limit pops.
/// </summary>
public static class TorchHoldAnimationBuilder
{
    private const string Folder         = "Assets/_Game/Characters/_Base/Animation";
    private const string SourceCtrl     = "Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
    private const string ControllerPath = Folder + "/PlayerAnimator.controller";
    private const string ClipPath       = Folder + "/TorchHold_LeftArm.anim";
    private const string MaskPath       = Folder + "/LeftArm_Torch.mask";
    public  const string LayerName      = "LeftArm_Torch";

    // ── Pose (normalized muscle values) ─────────────────────────────────────────
    private const float ShoulderDownUp     =  0.76f;
    private const float ShoulderFrontBack  = -0.75f;
    private const float ArmDownUp          =  0.35f;
    private const float ArmFrontBack       = -0.30f;
    private const float ArmTwist           =  0.34f;
    private const float ForearmStretch     =  0.78f;
    private const float ForearmTwist       =  0.76f;
    private const float HandDownUp         =  0.76f;
    private const float HandInOut          =  0.77f;

    // Fist (negative "Stretched" = curled).
    private const float FingerCurl         = -0.85f;
    private const float FingerSpread       = -0.2f;
    private const float ThumbStretch       = -0.45f;
    private const float ThumbSpread        =  0.35f;

    // ── Idle sway (loop) ────────────────────────────────────────────────────────
    private const float SwayPeriod         =  2.4f;   // seconds per loop
    private const float SwayArm            =  0.03f;  // Arm Down-Up amplitude
    private const float SwayForearm        =  0.04f;  // Forearm Stretch amplitude
    private const float SwayHand           =  0.06f;  // Hand Down-Up amplitude (flame bob)
    private const int   SwaySamples        = 12;

    [MenuItem("Fluffterror/Animation/Build Torch Hold")]
    public static void Build()
    {
        EnsureFolder();
        AnimatorController controller = EnsureController();
        AnimationClip clip = BuildClip();
        AvatarMask mask = BuildMask();
        EnsureLayer(controller, clip, mask);
        AssetDatabase.SaveAssets();
        int wired = WireOpenScenes(controller);
        Debug.Log($"[TorchHoldAnimationBuilder] Built {ControllerPath} (layer '{LayerName}'), {ClipPath}, {MaskPath}. " +
                  $"Wired {wired} character model(s) in the open scene(s).");
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(Folder)) return;
        AssetDatabase.CreateFolder("Assets/_Game/Characters/_Base", "Animation");
    }

    private static AnimatorController EnsureController()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null) return existing;
        if (!AssetDatabase.CopyAsset(SourceCtrl, ControllerPath))
            Debug.LogError($"[TorchHoldAnimationBuilder] Could not copy {SourceCtrl}.");
        return AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
    }

    private static AnimationClip BuildClip()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = "TorchHold_LeftArm" };
            AssetDatabase.CreateAsset(clip, ClipPath);
        }
        clip.ClearCurves();
        clip.frameRate = 30f;

        Sway("Left Arm Down-Up",       ArmDownUp,      SwayArm,     0f);
        Sway("Left Forearm Stretch",   ForearmStretch, SwayForearm, 0.6f);
        Sway("Left Hand Down-Up",      HandDownUp,     SwayHand,    1.2f);
        Constant("Left Shoulder Down-Up",     ShoulderDownUp);
        Constant("Left Shoulder Front-Back",  ShoulderFrontBack);
        Constant("Left Arm Front-Back",       ArmFrontBack);
        Constant("Left Arm Twist In-Out",     ArmTwist);
        Constant("Left Forearm Twist In-Out", ForearmTwist);
        Constant("Left Hand In-Out",          HandInOut);

        // Finger curve attributes use the "LeftHand.<Finger>.<n> Stretched" naming (not HumanTrait.MuscleName).
        foreach (string f in new[] { "Index", "Middle", "Ring", "Little" })
        {
            Constant($"LeftHand.{f}.1 Stretched", FingerCurl);
            Constant($"LeftHand.{f}.2 Stretched", FingerCurl);
            Constant($"LeftHand.{f}.3 Stretched", FingerCurl);
            Constant($"LeftHand.{f}.Spread",      FingerSpread);
        }
        Constant("LeftHand.Thumb.1 Stretched", ThumbStretch);
        Constant("LeftHand.Thumb.2 Stretched", ThumbStretch);
        Constant("LeftHand.Thumb.3 Stretched", ThumbStretch);
        Constant("LeftHand.Thumb.Spread",      ThumbSpread);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;

        void Constant(string attr, float v) =>
            Set(attr, new AnimationCurve(new Keyframe(0f, v), new Keyframe(SwayPeriod, v)));

        void Sway(string attr, float center, float amp, float phase)
        {
            var keys = new Keyframe[SwaySamples + 1];
            for (int i = 0; i <= SwaySamples; i++)
            {
                float t = SwayPeriod * i / SwaySamples;
                keys[i] = new Keyframe(t, center + amp * Mathf.Sin((t / SwayPeriod + phase / SwayPeriod) * Mathf.PI * 2f));
            }
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            Set(attr, curve);
        }

        void Set(string attr, AnimationCurve curve) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), attr), curve);
    }

    private static AvatarMask BuildMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask { name = "LeftArm_Torch" };
            AssetDatabase.CreateAsset(mask, MaskPath);
        }
        for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            mask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.LeftFingers);
        mask.transformCount = 0;
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static void EnsureLayer(AnimatorController controller, AnimationClip clip, AvatarMask mask)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        int index = System.Array.FindIndex(layers, l => l.name == LayerName);
        if (index < 0)
        {
            controller.AddLayer(LayerName);
            layers = controller.layers;
            index = layers.Length - 1;
        }

        AnimatorControllerLayer layer = layers[index];
        layer.avatarMask    = mask;
        layer.blendingMode  = AnimatorLayerBlendingMode.Override;
        layer.defaultWeight = 0f;   // TorchArmAnimator blends it in while a torch is carried
        layer.iKPass        = false;

        AnimatorStateMachine sm = layer.stateMachine;
        AnimatorState state = null;
        foreach (ChildAnimatorState s in sm.states)
            if (s.state.name == "TorchHold") state = s.state;
        if (state == null) state = sm.AddState("TorchHold");
        state.motion = clip;
        state.writeDefaultValues = true;
        sm.defaultState = state;

        layers[index] = layer;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
    }

    private static int WireOpenScenes(RuntimeAnimatorController controller)
    {
        int wired = 0;
        var dirty = new HashSet<UnityEngine.SceneManagement.Scene>();
        foreach (AnimationHandler handler in Object.FindObjectsByType<AnimationHandler>(FindObjectsInactive.Include))
        {
            var animator = handler.GetComponent<Animator>();
            if (animator == null) continue;
            Undo.RecordObject(animator, "Assign PlayerAnimator");
            animator.runtimeAnimatorController = controller;
            // Re-add so the grip/framing calibration (code defaults, matched to the pose constants above)
            // always wins over stale values serialized in the scene.
            TorchArmAnimator old = handler.GetComponent<TorchArmAnimator>();
            if (old != null) Undo.DestroyObjectImmediate(old);
            Undo.AddComponent<TorchArmAnimator>(handler.gameObject);
            dirty.Add(handler.gameObject.scene);
            wired++;
        }
        foreach (var scene in dirty) EditorSceneManager.MarkSceneDirty(scene);
        return wired;
    }
}
