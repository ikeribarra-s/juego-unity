using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Editor helper: pose a humanoid Animator from a clip through a PlayableGraph (honours avatar retargeting,
/// bake-into-pose and rotation offset, unlike AnimationMode.SampleAnimationClip).</summary>
public sealed class AnimProbe : System.IDisposable
{
    PlayableGraph _g;
    AnimationClipPlayable _p;
    readonly Animator _a;
    readonly bool _wasEnabled;
    readonly Transform[] _ts; readonly Vector3[] _pos; readonly Quaternion[] _rot; readonly Vector3[] _scl;

    public AnimProbe(Animator anim, AnimationClip clip)
    {
        _a = anim; _wasEnabled = anim.enabled;
        // Posing writes the bones; remember the scene pose so Dispose can put it back (never save a posed rig into a scene).
        _ts = anim.GetComponentsInChildren<Transform>(true);
        _pos = new Vector3[_ts.Length]; _rot = new Quaternion[_ts.Length]; _scl = new Vector3[_ts.Length];
        for (int i = 0; i < _ts.Length; i++) { _pos[i] = _ts[i].localPosition; _rot[i] = _ts[i].localRotation; _scl[i] = _ts[i].localScale; }
        anim.enabled = true;
        _g = PlayableGraph.Create("probe");
        _g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var o = AnimationPlayableOutput.Create(_g, "o", anim);
        _p = AnimationClipPlayable.Create(_g, clip);
        o.SetSourcePlayable(_p);
        _p.SetApplyFootIK(false);
    }

    public void Pose(float t)
    {
        _p.SetTime(t);
        _g.Evaluate(0f);
    }

    public void Dispose()
    {
        if (_g.IsValid()) _g.Destroy();
        for (int i = 0; i < _ts.Length; i++) if (_ts[i] != null) { _ts[i].localPosition = _pos[i]; _ts[i].localRotation = _rot[i]; _ts[i].localScale = _scl[i]; }
        _a.enabled = _wasEnabled;
    }
}
