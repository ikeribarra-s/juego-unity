using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Generates <c>engineAnimatorController</c> (and its handle-spin AnimationClip) for the repairable
/// <see cref="PowerGenerator"/>, entirely from code — same "editor builds the asset" pattern as
/// <see cref="ChunkBuilder"/> / WallsLayerSetup. Re-running overwrites the asset.
///
/// <b>Design — one state, speed-driven (no snap-back):</b> a single looping <b>Spin</b> state whose
/// playback speed is multiplied by the <c>SpinSpeed</c> float parameter. <see cref="PowerGenerator"/>
/// eases that float toward a target (crank speed while repairing, run speed while running, 0 when
/// idle). At 0 the clip simply holds its current frame, so the handle <i>coasts to a stop in place</i>
/// instead of blending back to a start pose — which is what separate Idle/Crank/Run states did.
///
/// <b>Wiring (once, on your generator prefab):</b>
/// <list type="number">
///   <item>Make the spinning handle a child named "<c>Handle</c>" directly under the prefab root,
///         an identity-rotation pivot located on the axle (see <see cref="EngineAnimParams.HandlePath"/>).</item>
///   <item>Add an <see cref="Animator"/> to the prefab root and assign the generated controller.</item>
///   <item><see cref="PowerGenerator"/> auto-finds that Animator and drives <c>SpinSpeed</c> — no
///         other references needed.</item>
/// </list>
///
/// If the handle spins around the wrong axis, change <see cref="EngineAnimParams.SpinProperty"/>
/// (x/y/z) and rebuild.
/// </summary>
public static class EngineAnimatorBuilder
{
    private const string ControllerPath = "Assets/_Game/Machines/engineAnimatorController.controller";
    private const string ClipsFolder    = "Assets/_Game/Machines/EngineClips";

    // Seconds per revolution at SpinSpeed = 1. PowerGenerator's crank/run speeds scale this.
    private const float SpinPeriod = 1f;

    [MenuItem("Fluffterror/Animation/Build Engine Animator")]
    public static void Build()
    {
        EnsureFolder(ClipsFolder);

        AnimationClip spinClip = MakeSpinClip("EngineSpin", SpinPeriod);

        // Fresh controller every run so re-building is deterministic.
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter(EngineAnimParams.SpinSpeed, AnimatorControllerParameterType.Float);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        var spin = sm.AddState("Spin");
        spin.motion = spinClip;
        spin.speedParameterActive = true;          // multiply playback speed by the parameter…
        spin.speedParameter = EngineAnimParams.SpinSpeed;
        sm.defaultState = spin;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[EngineAnimatorBuilder] Built {ControllerPath}\n" +
                  $"Assign it to an Animator on your generator prefab and name the spinning child " +
                  $"'{EngineAnimParams.HandlePath}'. Handle spins around '{EngineAnimParams.SpinProperty}'.");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
    }

    // A looping clip that rotates the handle one full turn over <period> seconds.
    // All three euler channels are authored (Unity rebuilds the quaternion from the full triple):
    // the spin axis ramps 0→360, the other two stay pinned at 0. So the "Handle" child must be an
    // identity-rotation pivot — put any offset mesh as a child of it.
    private static AnimationClip MakeSpinClip(string name, float period)
    {
        var clip = new AnimationClip { frameRate = 30f };

        char axis = char.ToLowerInvariant(EngineAnimParams.SpinProperty[^1]);   // 'x' / 'y' / 'z'
        foreach (char c in "xyz")
        {
            // Linear 0→360 on the spin axis (constant angular velocity, loops seamlessly: 360 ≡ 0).
            AnimationCurve curve = c == axis
                ? AnimationCurve.Linear(0f, 0f, period, 360f)
                : AnimationCurve.Constant(0f, period, 0f);
            clip.SetCurve(EngineAnimParams.HandlePath, typeof(Transform), $"localEulerAnglesRaw.{c}", curve);
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string path = $"{ClipsFolder}/{name}.anim";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf   = System.IO.Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
