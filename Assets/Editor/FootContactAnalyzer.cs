using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Fluffterror › Animation › Analyse Foot Contacts
///
/// Counts the frames of the player's locomotion / jump / land clips and finds the foot-contact frames by
/// sampling each clip on the real humanoid rig (AnimationMode, nothing is saved), then writes the
/// normalized contact tables into every <see cref="AnimationAudioSync"/> in the open scene and prints a table.
///
///   • foot contact (loops)  — per foot: height of its lowest point (foot or toe bone, model-root space);
///                             contact = downward crossing of (10th-percentile height + 1.5 cm).
///   • take-off (JumpStart)  — the frame where the lowest foot reaches its lowest point and starts rising.
///   • land contact          — first downward crossing of (final standing height + 2 cm) by either foot.
/// </summary>
public static class FootContactAnalyzer
{
    private const float SampleRate = 240f;   // sub-frame resolution

    [MenuItem("Fluffterror/Animation/Analyse Foot Contacts")]
    public static void Run()
    {
        var syncs = Object.FindObjectsByType<AnimationAudioSync>(FindObjectsInactive.Include);
        if (syncs.Length == 0) { Debug.LogWarning("[FootContacts] No AnimationAudioSync in the open scene."); return; }

        var sync = syncs[0];
        var anim = sync.GetComponent<Animator>();
        var ctrl = anim.runtimeAnimatorController as AnimatorController;
        if (ctrl == null || !anim.isHuman) { Debug.LogWarning("[FootContacts] Needs a humanoid Animator with an AnimatorController."); return; }

        var so = new SerializedObject(sync);
        string loco = so.FindProperty("_locoStateName").stringValue;
        string crouchSt = so.FindProperty("_crouchStateName").stringValue;
        string jump = so.FindProperty("_jumpStartState").stringValue;
        string land = so.FindProperty("_landState").stringValue;

        var report = new StringBuilder("[FootContacts] clip | length | fps | frames | contact frames (normalized)\n");
        var foot = new List<AnimationAudioSync.ClipContacts>();
        var take = new List<AnimationAudioSync.ClipContacts>();
        var lnd  = new List<AnimationAudioSync.ClipContacts>();

        {
            foreach (var clip in ClipsOf(ctrl, loco).Concat(ClipsOf(ctrl, crouchSt)).Distinct())
            {
                var (l, r) = SampleFeet(anim, clip);
                // Two-step gait: take one reliable contact (left foot, else right) and mirror it half a cycle later. The UAL gaits
                // plant one foot much longer than the other, so per-foot crossings can land only 4-5 frames apart and CharacterAudio's
                // 0.18 s step floor would swallow one of them.
                var lc = Contacts(l).ToArray(); var rc = Contacts(r).ToArray();
                float[] phases = lc.Length > 0 ? new[] { lc[0], (lc[0] + 0.5f) % 1f } : rc.Length > 0 ? new[] { rc[0], (rc[0] + 0.5f) % 1f } : new float[0];
                // Tables are in STATE time: a blend-tree child with a cycleOffset plays clip time = state time + offset.
                float offset = CycleOffset(ctrl, loco, clip);
                phases = phases.Select(t => Mathf.Repeat(t - offset, 1f)).OrderBy(t => t).ToArray();
                if (phases.Length == 0) { Line(report, clip, "no contacts (idle)", null); continue; }
                foot.Add(new AnimationAudioSync.ClipContacts(clip.name, phases));
                Line(report, clip, "foot", phases);
            }
            foreach (var clip in ClipsOf(ctrl, jump))
            {
                var (l, r) = SampleFeet(anim, clip);
                var low = l.Zip(r, Mathf.Min).ToArray();
                int i = System.Array.IndexOf(low, low.Min());
                var phases = new[] { i / (float)(low.Length - 1) };
                take.Add(new AnimationAudioSync.ClipContacts(clip.name, phases));
                Line(report, clip, "take-off", phases);
            }
            foreach (var clip in ClipsOf(ctrl, land))
            {
                var (l, r) = SampleFeet(anim, clip);
                var low = l.Zip(r, Mathf.Min).ToArray();
                float thr = low[low.Length - 1] + 0.02f;
                float t = 0f;
                if (low[0] > thr)
                    for (int i = 1; i < low.Length; i++)
                        if (low[i] <= thr) { t = Cross(low, i, thr) / (low.Length - 1); break; }
                var phases = new[] { t };
                lnd.Add(new AnimationAudioSync.ClipContacts(clip.name, phases));
                Line(report, clip, "land", phases);
            }
        }

        foreach (var s in syncs) s.SetTables(foot.ToArray(), take.ToArray(), lnd.ToArray());
        Debug.Log(report.ToString());
    }

    private static float CycleOffset(AnimatorController ctrl, string stateName, AnimationClip clip)
    {
        foreach (var cs in ctrl.layers[0].stateMachine.states)
            if (cs.state.name == stateName && cs.state.motion is BlendTree bt)
                foreach (var c in bt.children) if (c.motion == clip) return c.cycleOffset;
        return 0f;
    }

    private static IEnumerable<AnimationClip> ClipsOf(AnimatorController ctrl, string stateName)
    {
        foreach (var cs in ctrl.layers[0].stateMachine.states)
        {
            if (cs.state.name != stateName) continue;
            if (cs.state.motion is BlendTree bt)
                foreach (var c in bt.children) { if (c.motion is AnimationClip ac && ac.name != "Idle" && ac.name != "CrouchIdle") yield return ac; }
            else if (cs.state.motion is AnimationClip clip) yield return clip;
        }
    }

    /// <summary>Lowest point (foot or toes) of each foot above the model root, sampled at SampleRate.</summary>
    private static (float[] left, float[] right) SampleFeet(Animator anim, AnimationClip clip)
    {
        Transform root = anim.transform;
        Transform lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot),  lt = anim.GetBoneTransform(HumanBodyBones.LeftToes);
        Transform rf = anim.GetBoneTransform(HumanBodyBones.RightFoot), rt = anim.GetBoneTransform(HumanBodyBones.RightToes);
        int n = Mathf.Max(2, Mathf.RoundToInt(clip.length * SampleRate) + 1);
        var l = new float[n]; var r = new float[n];
        using var probe = new AnimProbe(anim, clip);
        for (int i = 0; i < n; i++)
        {
            probe.Pose(clip.length * i / (n - 1));
            l[i] = Low(root, lf, lt);
            r[i] = Low(root, rf, rt);
        }
        return (l, r);
    }

    private static float Low(Transform root, Transform foot, Transform toes)
    {
        float y = root.InverseTransformPoint(foot.position).y;
        return toes != null ? Mathf.Min(y, root.InverseTransformPoint(toes.position).y) : y;
    }

    /// <summary>Normalized times where a looping foot curve comes down onto its stance level.</summary>
    private static IEnumerable<float> Contacts(float[] h)
    {
        var sorted = h.OrderBy(v => v).ToArray();
        float stance = sorted[Mathf.FloorToInt(sorted.Length * 0.1f)];
        float swing  = sorted[sorted.Length - 1];
        if (swing - stance < 0.05f) yield break;   // foot never lifts: idle
        float thr = stance + 0.05f;   // 5 cm: the UAL gaits plant the heel/toe slowly, 1.5 cm made both feet cross together
        int n = h.Length - 1;                       // last sample == first (loop)
        for (int i = 1; i <= n; i++)
            if (h[i - 1] > thr && h[i] <= thr)
                yield return (Cross(h, i, thr) / n) % 1f;
    }

    private static float Cross(float[] h, int i, float thr) =>
        i - 1 + (h[i - 1] - thr) / Mathf.Max(1e-6f, h[i - 1] - h[i]);

    private static void Line(StringBuilder sb, AnimationClip c, string kind, float[] phases)
    {
        int frames = Mathf.RoundToInt(c.length * c.frameRate);
        string list = phases == null ? "-" :
            string.Join(", ", phases.Select(p => $"f{p * c.length * c.frameRate:0.0} ({p:0.000})"));
        sb.AppendLine($"  {c.name} | {c.length:0.000}s | {c.frameRate} | {frames} | {kind}: {list}");
    }
}
