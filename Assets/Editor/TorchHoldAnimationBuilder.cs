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
/// (-1..1, Unity's Muscles &amp; Settings space); the arm values were solved numerically (HumanPoseHandler +
/// coordinate descent, evaluating the real Animator incl. the head-look IK) on the 5-finger skeleton against the
/// Starter Assets idle so the <i>animated</i> pose itself frames the lantern for first person and reads right for
/// other players: fist centre ~(-0.18, 1.97, 0.61) m from the character root (lantern ~0.35 m above it), shaft ~3°
/// off vertical, elbow bent (~0.31 m forward, out to the left, below the fist), arm at ~85 % of its reach. <see cref="TorchArmAnimator"/> only adds a tiny view offset on top. The finger/thumb values were re-solved for the
/// 5-finger Mixamo skeleton (fingers loop around the hand's X axis, thumb closes over the index/middle).
/// Muscles kept at or under ~0.85 to avoid limit pops.
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
    private const float ShoulderDownUp     = 0.175f;
    private const float ShoulderFrontBack  =  0.24f;
    private const float ArmDownUp          = 0.395f;
    private const float ArmFrontBack       = -0.42f;
    private const float ArmTwist           =  0.15f;
    private const float ForearmStretch     =  0.21f;
    private const float ForearmTwist       = 0.845f;
    private const float HandDownUp         = 0.565f;
    private const float HandInOut          = 0.855f;

    // Fist (negative "Stretched" = curled). Solved on the 5-finger Mixamo skeleton (Assets/Art/Characters/Skeleton)
    // against the torch handle (radius ~0.03 m at the 0.3 hold scale): every finger joint/segment centre sits
    // 0.041–0.057 m from the shaft axis (= touching the handle, no intersection), the shaft running along the
    // hand's X axis through TorchArmAnimator._gripOffset. The thumb lies along the handle above the index
    // (thumb-up grip), its distal joint resting on the shaft.
    private const float FingerCurl         = -0.74f;  // .1 Stretched (proximal)
    private const float FingerCurlDistal   = -0.42f;  // .2 / .3 Stretched
    private const float FingerSpread       = -0.2f;
    private const float ThumbProximal      =  0.5f;   // Thumb.1 Stretched
    private const float ThumbDistal        = -0.34f;  // Thumb.2 / Thumb.3 Stretched
    private const float ThumbSpread        = -1.0f;   // negative = toward the palm front (opposition)

    // ── Idle sway (loop) ────────────────────────────────────────────────────────
    private const float SwayPeriod         =  2.4f;   // seconds per loop
    private const float SwayArm            =  0.02f;  // Arm Down-Up amplitude
    private const float SwayForearm        =  0.02f;  // Forearm Stretch amplitude
    private const float SwayHand           =  0.03f;  // Hand Down-Up amplitude (flame bob) — kept small so the
                                                       // close-up lantern stays on screen in first person
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
            Constant($"LeftHand.{f}.2 Stretched", FingerCurlDistal);
            Constant($"LeftHand.{f}.3 Stretched", FingerCurlDistal);
            Constant($"LeftHand.{f}.Spread",      FingerSpread);
        }
        Constant("LeftHand.Thumb.1 Stretched", ThumbProximal);
        Constant("LeftHand.Thumb.2 Stretched", ThumbDistal);
        Constant("LeftHand.Thumb.3 Stretched", ThumbDistal);
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
