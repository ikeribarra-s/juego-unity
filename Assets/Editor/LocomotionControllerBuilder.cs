using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Fluffterror › Animation › Build Locomotion Controller. Rebuilds the BASE layer of PlayerAnimator.controller from the
/// Quaternius UAL clips (the LeftArm_Torch layer is not touched). Backup of the original Starter Assets controller:
/// PlayerAnimator_StarterBackup.controller (created once).
///
/// Params : MoveX / MoveZ (local planar velocity m/s), VelocityY, Speed (planar |v|), MotionSpeed, Grounded, FreeFall, Crouch (bool), Jump (trigger).
/// States : Locomotion (2D freeform: idle + walk + jog + sprint ring) · Crouch (2D freeform) · JumpStart · InAir · JumpLand.
/// Clip speeds: see the constants below (measured on the retargeted rig, see LocomotionMeasure)
/// so feet match the ground.
/// </summary>
public static class LocomotionControllerBuilder
{
    const string Dir  = "Assets/_Game/Characters/_Base/Animation";
    const string Ctrl = Dir + "/PlayerAnimator.controller";
    const string Backup = Dir + "/PlayerAnimator_StarterBackup.controller";

    // Native ground speeds of the clips on our rig (m/s): median stance-toe speed, left and right feet agree within ~5 %.
    const float NatWalk = 1.05f, NatJog = 7f, NatCrouch = 1.0f;
    // Every child sits at the speed its feet really cover (native x timeScale), so any blend stays close to the body speed.
    // The game's 3 and 5.4 m/s fall between a brisk walk and the jog: cadence stays ~0.65-0.7 s per cycle and the stride grows.
    const float BriskScale = 2.1f, JogScale = 1.2f, CrouchSpeed = 1.5f;
    // Blended strides come out shorter than the children's speeds suggest: pull the positions in so the faster clips weigh more.
    const float BlendCalibration = 0.75f;

    // Jump_Start opens with a wind-up crouch and the feet leave the floor at 0.125-0.15, but the body rises on the frame Space is
    // pressed (no input delay): enter the clip at push-off so pose and movement leave the ground together.
    const float JumpPushOff = 0.125f;

    static AnimationClip Clip(string name)
    {
        var path = $"{LocomotionClipImporter.Dir}/Anim_{name}.fbx";
        var c = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__"));
        if (c == null) Debug.LogError("[Loco] clip missing: " + path);
        return c;
    }

    [MenuItem("Fluffterror/Animation/Build Locomotion Controller")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(Backup) == null)
            AssetDatabase.CopyAsset(Ctrl, Backup);

        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctrl);

        // ---- parameters ----
        foreach (var n in new[] { "Jump", "MoveX", "MoveZ", "VelocityY", "Crouch", "Landing" })
            for (int i = c.parameters.Length - 1; i >= 0; i--) if (c.parameters[i].name == n) c.RemoveParameter(i);
        Ensure(c, "Speed", AnimatorControllerParameterType.Float);
        Ensure(c, "MotionSpeed", AnimatorControllerParameterType.Float);
        Ensure(c, "Grounded", AnimatorControllerParameterType.Bool);
        Ensure(c, "FreeFall", AnimatorControllerParameterType.Bool);
        c.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        c.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
        c.AddParameter("VelocityY", AnimatorControllerParameterType.Float);
        c.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
        c.AddParameter("Landing", AnimatorControllerParameterType.Bool);   // falling, touchdown within AnimationHandler._landLead
        c.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

        // ---- clean the old base layer (and its blend trees) ----
        var layers = c.layers;
        var sm = layers[0].stateMachine;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Ctrl).OfType<BlendTree>().ToArray()) Object.DestroyImmediate(o, true);
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);

        // ---- states ----
        var loco = sm.AddState("Locomotion", new Vector3(260, 0));
        loco.motion = LocoTree(c);
        var crouch = sm.AddState("Crouch", new Vector3(260, 120));
        crouch.motion = CrouchTree(c);
        var start = sm.AddState("JumpStart", new Vector3(560, 0));
        start.motion = Clip("Jump_Start");
        var air = sm.AddState("InAir", new Vector3(560, 120));
        air.motion = Clip("Jump_InAir");
        var land = sm.AddState("JumpLand", new Vector3(260, 240));
        land.motion = Clip("Jump_Land"); land.speed = 1.25f;   // slower than the fall so the knees bend after contact, not before
        sm.defaultState = loco;
        foreach (var s in new[] { loco, crouch }) { s.writeDefaultValues = true; }

        // ---- transitions ----
        T(loco, crouch, 0.2f, "Crouch", true);
        T(crouch, loco, 0.2f, "Crouch", false);
        foreach (var g in new[] { loco, crouch, land })
        {
            var t = T(g, start, 0.04f); t.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            t.offset = JumpPushOff;
        }
        foreach (var g in new[] { loco, crouch, land })
        {
            var t = T(g, air, 0.15f); t.AddCondition(AnimatorConditionMode.If, 0, "FreeFall");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Landing");
            t.interruptionSource = TransitionInterruptionSource.Destination;   // a short fall may land while still blending into InAir
        }
        var sa = T(start, air, 0.2f, exit: JumpPushOff + 0.25f);
        sa.interruptionSource = TransitionInterruptionSource.Destination;
        var sl = T(start, land, 0.08f); sl.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        var al = T(air, land, 0.05f); al.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        // Predicted touchdown: start landing just before contact (the stepped Animator + fade would otherwise arrive late).
        var slp = T(start, land, 0.08f); slp.AddCondition(AnimatorConditionMode.If, 0, "Landing");
        var alp = T(air, land, 0.06f);   alp.AddCondition(AnimatorConditionMode.If, 0, "Landing");
        // landing -> locomotion / crouch: early when moving, otherwise after the recovery
        foreach (var dst in new[] { crouch, loco })
        {
            bool isCrouch = dst == crouch;
            var tm = T(land, dst, 0.15f, exit: 0.25f);
            tm.AddCondition(isCrouch ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Crouch");
            tm.AddCondition(AnimatorConditionMode.Greater, 1.5f, "Speed");
            var ts = T(land, dst, 0.15f, exit: 0.5f);
            ts.AddCondition(isCrouch ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Crouch");
        }

        layers[0].stateMachine = sm;
        c.layers = layers;
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        Debug.Log("[Loco] controller rebuilt: " + Ctrl);
    }

    static void Ensure(AnimatorController c, string n, AnimatorControllerParameterType t)
    {
        if (!c.parameters.Any(p => p.name == n)) c.AddParameter(n, t);
    }

    static AnimatorStateTransition T(AnimatorState from, AnimatorState to, float dur, string param = null, bool value = true, float exit = -1f)
    {
        var t = from.AddTransition(to);
        t.duration = dur; t.hasFixedDuration = true; t.offset = 0f;
        t.hasExitTime = exit >= 0f; t.exitTime = exit < 0f ? 0f : exit;
        t.interruptionSource = TransitionInterruptionSource.None;
        if (param != null) t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
        return t;
    }

    static BlendTree NewTree(AnimatorController c, string name)
    {
        var bt = new BlendTree { name = name, blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(bt, c);
        return bt;
    }

    static void Add(BlendTree bt, AnimationClip clip, Vector2 pos, float timeScale, float cycleOffset = 0f)
    {
        bt.AddChild(clip, pos);
        var ch = bt.children;
        ch[ch.Length - 1].timeScale = timeScale;
        ch[ch.Length - 1].cycleOffset = cycleOffset;
        bt.children = ch;
    }

    static BlendTree LocoTree(AnimatorController c)
    {
        var bt = NewTree(c, "Locomotion2D");
        Add(bt, Clip("Idle"), Vector2.zero, 1f);
        foreach (var (dir, v) in new[] { ("F", Vector2.up), ("B", Vector2.down), ("L", Vector2.left), ("R", Vector2.right) })
        {
            Add(bt, Clip("Walk_" + dir), v * NatWalk, 1f);
            Add(bt, Clip("Walk_" + dir), v * NatWalk * BriskScale * BlendCalibration, BriskScale);
            // cycleOffset: best correlation of the jog's left-right foot separation with the walk's, so strides add up instead of smearing
            Add(bt, Clip("Jog_" + dir),  v * NatJog * JogScale * BlendCalibration, JogScale,
                dir == "B" ? 0.16f : 0.84f);
        }
        return bt;
    }

    static BlendTree CrouchTree(AnimatorController c)
    {
        var bt = NewTree(c, "Crouch2D");
        Add(bt, Clip("CrouchIdle"), Vector2.zero, 1f);
        float ts = CrouchSpeed / NatCrouch;
        Add(bt, Clip("CrouchWalk_F"), new Vector2(0, CrouchSpeed),  ts);
        Add(bt, Clip("CrouchWalk_B"), new Vector2(0, -CrouchSpeed), ts);
        Add(bt, Clip("CrouchWalk_L"), new Vector2(-CrouchSpeed, 0), ts);
        Add(bt, Clip("CrouchWalk_R"), new Vector2(CrouchSpeed, 0),  ts);
        return bt;
    }
}
