using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Dev tool: measures the UAL locomotion clips on the open scene's player rig (facing, ground speed from stance feet,
/// hip / head / skull-top heights). Call <c>LocomotionMeasure.Report()</c> (execute_code) — nothing is saved.</summary>
public static class LocomotionMeasure
{
    public static string Report(string filter = "")
    {
        var sync = Object.FindObjectsByType<AnimationAudioSync>(FindObjectsInactive.Include);
        var anim = sync[0].GetComponent<Animator>();
        var root = anim.transform;
        var feet = new[] { anim.GetBoneTransform(HumanBodyBones.LeftFoot), anim.GetBoneTransform(HumanBodyBones.RightFoot) };
        var toe = anim.GetBoneTransform(HumanBodyBones.LeftToes);
        var hipT = anim.GetBoneTransform(HumanBodyBones.Hips);
        var head = anim.GetBoneTransform(HumanBodyBones.Head);
        var smrs = anim.GetComponentsInChildren<SkinnedMeshRenderer>();
        var sb = new StringBuilder();
        foreach (var p in System.IO.Directory.GetFiles(LocomotionClipImporter.Dir, "*.fbx"))
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(p.Replace('\\', '/')).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            if (filter != "" && !clip.name.Contains(filter)) continue;
            int n = Mathf.Max(2, Mathf.RoundToInt(clip.length * 60) + 1); float dt = clip.length / (n - 1);
            var pos = new Vector3[2, n]; var hh = new float[2, n];
            float face = 0, topMax = 0, headMin = 9, headMax = 0, hipMin = 9, lx = 0;
            using (var probe = new AnimProbe(anim, clip))
            {
                for (int i = 0; i < n; i++)
                {
                    probe.Pose(clip.length * i / (n - 1));
                    for (int k = 0; k < 2; k++) { pos[k, i] = root.InverseTransformPoint(feet[k].position); hh[k, i] = pos[k, i].y; }
                    face += root.InverseTransformPoint(toe.position).z - pos[0, i].z; lx += pos[0, i].x;
                    var hy = root.InverseTransformPoint(head.position).y; headMin = Mathf.Min(headMin, hy); headMax = Mathf.Max(headMax, hy);
                    hipMin = Mathf.Min(hipMin, root.InverseTransformPoint(hipT.position).y);
                    if (i % 12 == 0)
                    {
                        var m = new Mesh();
                        foreach (var s in smrs) { s.BakeMesh(m); foreach (var v in m.vertices) topMax = Mathf.Max(topMax, root.InverseTransformPoint(s.transform.TransformPoint(v)).y); }
                        Object.DestroyImmediate(m);
                    }
                }
            }
            Vector3 st = Vector3.zero; int c = 0;
            for (int k = 0; k < 2; k++)
            {
                float mn = float.MaxValue; for (int i = 0; i < n; i++) mn = Mathf.Min(mn, hh[k, i]);
                for (int i = 1; i < n; i++) if (hh[k, i] < mn + 0.03f && hh[k, i - 1] < mn + 0.03f) { var v = (pos[k, i] - pos[k, i - 1]) / dt; v.y = 0; st += v; c++; }
            }
            st /= Mathf.Max(1, c);
            sb.AppendLine($"{clip.name} len={clip.length:0.00} toeFwd={face / n:0.00} Lx={lx / n:0.00} bodyV=({-st.x:0.00},{-st.z:0.00}) |v|={st.magnitude:0.00} hipMin={hipMin:0.00} head {headMin:0.00}-{headMax:0.00} skullTop={topMax:0.00}");
        }
        return sb.ToString();
    }
}
