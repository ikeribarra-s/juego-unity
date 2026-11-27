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
///
/// Also builds the <b>throw</b> and <b>pick-up</b> clips (same menu, or <b>Build Torch Throw + Pick-up</b>):
///  • <c>TorchThrow_LeftArm.anim</c> (0.75 s, calm): hold → small wind-up beside the head (held a beat) → release (arm
///    forward-up, fist opens) → soft follow-through (forward-down) → hanging arm. ClampedAuto tangents = eased keys.
///  • <c>TorchPickup_LeftArm.anim</c> (0.36 s): hanging arm → open-handed forward reach → fist closes (grab) → back to the
///    hold pose (= TorchHold at time 0, seamless exit transition), where the torch pops into the hand.
/// Key times come from <see cref="TorchActionTimings"/> (shared with the runtime grab / release). The arm poses were solved
/// the same way as the hold (HumanPoseHandler + coordinate descent on the UAL idle, hand / elbow / shaft targets in the
/// character frame): wind-up hand ~(-0.44, 1.95, 0.19) with the shaft tilted 30° back (held: 40°), release ~(-0.23, 1.85, 0.52),
/// follow-through ~(-0.15, 1.48, 0.49), reach ~(-0.25, 1.46, 0.52) m. Both are states on the <c>LeftArm_Torch</c> layer, played from code by
/// <see cref="TorchArmAnimator"/> (which also pulls the reach onto the torch handle and adds the torso), exit → TorchHold.
/// </summary>
public static class TorchHoldAnimationBuilder
{
    private const string Folder         = "Assets/_Game/Characters/_Base/Animation";
    private const string SourceCtrl     = "Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
    private const string ControllerPath = Folder + "/PlayerAnimator.controller";
    private const string ClipPath       = Folder + "/TorchHold_LeftArm.anim";
    private const string MaskPath       = Folder + "/LeftArm_Torch.mask";
    public  const string LayerName      = "LeftArm_Torch";
    private const string ThrowClipPath  = Folder + "/TorchThrow_LeftArm.anim";
    private const string PickupClipPath = Folder + "/TorchPickup_LeftArm.anim";

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

    // ── Throw / pick-up arm poses ───────────────────────────────────────────────
    // Order of every pose array (normalized muscles, left arm):
    private static readonly string[] ArmMuscles =
    {
        "Left Shoulder Down-Up", "Left Shoulder Front-Back", "Left Arm Down-Up", "Left Arm Front-Back", "Left Arm Twist In-Out",
        "Left Forearm Stretch", "Left Forearm Twist In-Out", "Left Hand Down-Up", "Left Hand In-Out",
    };
    private static readonly float[] PoseIdleArm   = { -0.336f,  0.85f,  -0.499f,  0.223f,  0.006f, 0.602f, -0.035f, -0.036f, -0.330f };   // UAL idle, arm hanging
    private static readonly float[] PoseWindUp    = {  0.012f,  0.265f,  0.533f, -0.139f,  0.600f, -0.228f, 0.844f,  0.565f,  0.850f };
    private static readonly float[] PoseWindUpMax = {  0.000f,  0.259f,  0.564f, -0.070f,  0.756f, -0.246f, 0.850f,  0.565f,  0.850f };   // held a beat
    private static readonly float[] PoseRelease   = {  0.431f,  0.165f,  0.426f, -0.664f,  0.106f,  0.523f, 0.850f,  0.521f,  0.855f };
    private static readonly float[] PoseFollow    = {  0.144f,  0.128f, -0.086f, -0.570f, -0.175f,  0.454f, 0.850f,  0.465f,  0.855f };
    private static readonly float[] PoseReach     = { -0.050f,  0.052f, -0.005f, -0.520f, -0.056f,  0.560f, 0.520f,  0.652f,  0.381f };

    // Hands: open (reach / let go) and relaxed (hanging) finger values; the fist is the hold's FingerCurl / ThumbXxx.
    private const float FingerOpen       =  0.35f;
    private const float FingerOpenSpread =  0.35f;
    private const float ThumbOpen        =  0.2f;
    private const float ThumbOpenSpread  =  0.3f;
    private const float FingerRelaxed    = -0.25f;
    private const float FingersOpenTime  =  0.08f;   // throw: seconds after the release until the fist is fully open

    [MenuItem("Fluffterror/Animation/Build Torch Hold")]
    public static void Build()
    {
        EnsureFolder();
        AnimatorController controller = EnsureController();
        AnimationClip clip = BuildClip();
        AnimationClip throwClip  = BuildThrowClip();
        AnimationClip pickupClip = BuildPickupClip();
        AvatarMask mask = BuildMask();
        EnsureLayer(controller, clip, mask, throwClip, pickupClip);
        AssetDatabase.SaveAssets();
        int wired = WireOpenScenes(controller);
        Debug.Log($"[TorchHoldAnimationBuilder] Built {ControllerPath} (layer '{LayerName}': {TorchActionTimings.HoldState}, " +
                  $"{TorchActionTimings.ThrowState}, {TorchActionTimings.PickupState}), {ClipPath}, {ThrowClipPath}, {PickupClipPath}, " +
                  $"{MaskPath}. Wired {wired} character model(s) in the open scene(s).");
    }

    /// <summary>Same as Build Torch Hold (one builder owns the whole layer, so re-running either keeps everything wired).</summary>
    [MenuItem("Fluffterror/Animation/Build Torch Throw + Pick-up")]
    public static void BuildActions() => Build();

    // Hold clip value at time 0 (the sway phase the pick-up must end on, so the exit into TorchHold is seamless).
    private static float HoldAt0(string muscle) => muscle switch
    {
        "Left Arm Down-Up"     => ArmDownUp      + SwayArm     * Mathf.Sin(0f),
        "Left Forearm Stretch" => ForearmStretch + SwayForearm * Mathf.Sin(0.6f / SwayPeriod * Mathf.PI * 2f),
        "Left Hand Down-Up"    => HandDownUp     + SwayHand    * Mathf.Sin(1.2f / SwayPeriod * Mathf.PI * 2f),
        _                      => HoldConst(muscle),
    };

    private static float HoldConst(string muscle) => muscle switch
    {
        "Left Shoulder Down-Up"     => ShoulderDownUp,
        "Left Shoulder Front-Back"  => ShoulderFrontBack,
        "Left Arm Down-Up"          => ArmDownUp,
        "Left Arm Front-Back"       => ArmFrontBack,
        "Left Arm Twist In-Out"     => ArmTwist,
        "Left Forearm Stretch"      => ForearmStretch,
        "Left Forearm Twist In-Out" => ForearmTwist,
        "Left Hand Down-Up"         => HandDownUp,
        "Left Hand In-Out"          => HandInOut,
        _ => 0f,
    };

    private static float[] HoldPose()
    {
        var p = new float[ArmMuscles.Length];
        for (int i = 0; i < p.Length; i++) p[i] = HoldAt0(ArmMuscles[i]);
        return p;
    }

    private static AnimationClip LoadOrCreateClip(string path, string name)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = name };
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = 30f;
        return clip;
    }

    // One curve per arm muscle through the given (time, pose) keys; ClampedAuto tangents (no overshoot past the keys).
    private static void ArmCurves(AnimationClip clip, float[] times, float[][] poses)
    {
        for (int m = 0; m < ArmMuscles.Length; m++)
        {
            var values = new float[times.Length];
            for (int k = 0; k < times.Length; k++) values[k] = poses[k][m];
            SetCurve(clip, ArmMuscles[m], times, values);
        }
    }

    private static void SetCurve(AnimationClip clip, string attr, float[] times, float[] values)
    {
        var curve = new AnimationCurve();
        for (int k = 0; k < times.Length; k++) curve.AddKey(new Keyframe(times[k], values[k]));
        for (int k = 0; k < curve.length; k++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), attr), curve);
    }

    // Finger keys per time: (curl .1, curl .2/.3, spread, thumb .1, thumb .2/.3, thumb spread).
    private static void FingerCurves(AnimationClip clip, float[] times, float[][] hands)
    {
        float[] Col(int c) { var v = new float[times.Length]; for (int k = 0; k < v.Length; k++) v[k] = hands[k][c]; return v; }
        foreach (string f in new[] { "Index", "Middle", "Ring", "Little" })
        {
            SetCurve(clip, $"LeftHand.{f}.1 Stretched", times, Col(0));
            SetCurve(clip, $"LeftHand.{f}.2 Stretched", times, Col(1));
            SetCurve(clip, $"LeftHand.{f}.3 Stretched", times, Col(1));
            SetCurve(clip, $"LeftHand.{f}.Spread",      times, Col(2));
        }
        SetCurve(clip, "LeftHand.Thumb.1 Stretched", times, Col(3));
        SetCurve(clip, "LeftHand.Thumb.2 Stretched", times, Col(4));
        SetCurve(clip, "LeftHand.Thumb.3 Stretched", times, Col(4));
        SetCurve(clip, "LeftHand.Thumb.Spread",      times, Col(5));
    }

    private static float[] Fist    => new[] { FingerCurl, FingerCurlDistal, FingerSpread, ThumbProximal, ThumbDistal, ThumbSpread };
    private static float[] Open    => new[] { FingerOpen, FingerOpen, FingerOpenSpread, ThumbOpen, ThumbOpen, ThumbOpenSpread };
    private static float[] Relaxed => new[] { FingerRelaxed, FingerRelaxed, 0f, 0f, 0f, 0f };

    private static AnimationClip BuildThrowClip()
    {
        AnimationClip clip = LoadOrCreateClip(ThrowClipPath, "TorchThrow_LeftArm");
        ArmCurves(clip,
            new[] { 0f, TorchActionTimings.ThrowWindUp, TorchActionTimings.ThrowWindUpHold, TorchActionTimings.ThrowReleasePose,
                    TorchActionTimings.ThrowFollow, TorchActionTimings.ThrowDuration },
            new[] { HoldPose(), PoseWindUp, PoseWindUpMax, PoseRelease, PoseFollow, PoseIdleArm });
        float rel = TorchActionTimings.ThrowRelease;
        FingerCurves(clip,
            new[] { 0f, rel, rel + FingersOpenTime, TorchActionTimings.ThrowDuration },
            new[] { Fist, Fist, Open, Relaxed });
        FinishClip(clip, loop: false);
        return clip;
    }

    private static AnimationClip BuildPickupClip()
    {
        AnimationClip clip = LoadOrCreateClip(PickupClipPath, "TorchPickup_LeftArm");
        float[] hold = HoldPose();
        ArmCurves(clip,
            new[] { 0f, TorchActionTimings.PickupReach, TorchActionTimings.PickupGrab, TorchActionTimings.PickupDuration },
            new[] { PoseIdleArm, PoseReach, PoseReach, hold });
        float grab = TorchActionTimings.PickupGrab;
        FingerCurves(clip,
            new[] { 0f, TorchActionTimings.PickupReach * 0.6f, grab, grab + 0.05f, TorchActionTimings.PickupDuration },
            new[] { Relaxed, Open, Open, Fist, Fist });
        FinishClip(clip, loop: false);
        return clip;
    }

    private static void FinishClip(AnimationClip clip, bool loop)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
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

    private static void EnsureLayer(AnimatorController controller, AnimationClip clip, AvatarMask mask,
                                    AnimationClip throwClip, AnimationClip pickupClip)
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
        layer.iKPass        = false;  // the pick-up reach IK is TorchArmAnimator's own (post-animation, on step frames)

        AnimatorStateMachine sm = layer.stateMachine;
        AnimatorState state = EnsureState(sm, TorchActionTimings.HoldState, clip, new Vector3(300f, 100f));
        sm.defaultState = state;
        // Played from code (TorchArmAnimator, Animator.Play); when they end they fall back into the hold.
        foreach (var (name, motion, pos) in new[]
                 { (TorchActionTimings.ThrowState, throwClip, new Vector3(300f, 200f)),
                   (TorchActionTimings.PickupState, pickupClip, new Vector3(300f, 0f)) })
        {
            AnimatorState s = EnsureState(sm, name, motion, pos);
            foreach (AnimatorStateTransition old in s.transitions) s.RemoveTransition(old);
            AnimatorStateTransition exit = s.AddTransition(state);
            exit.hasExitTime         = true;
            exit.exitTime            = 1f;
            exit.hasFixedDuration    = true;
            exit.duration            = 0f;
            exit.interruptionSource  = TransitionInterruptionSource.None;
        }

        layers[index] = layer;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
    }

    private static AnimatorState EnsureState(AnimatorStateMachine sm, string name, Motion motion, Vector3 pos)
    {
        AnimatorState state = null;
        foreach (ChildAnimatorState s in sm.states)
            if (s.state.name == name) state = s.state;
        if (state == null) state = sm.AddState(name, pos);
        state.motion = motion;
        state.writeDefaultValues = true;
        return state;
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
