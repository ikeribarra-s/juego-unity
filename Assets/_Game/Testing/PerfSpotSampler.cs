using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Dev tool: teleports the player through fixed world spots in Play mode, waits for the frame to settle, and logs
/// median CPU main / render thread and GPU time (FrameTimingManager) plus draw calls / set-pass / triangles /
/// shadow casters (editor stats) and active lights per spot. Used to compare map changes at the same camera spots.
/// Add it to any object, or call <see cref="Run"/> from code; results are logged with the tag [PerfSpots].
/// </summary>
public class PerfSpotSampler : MonoBehaviour
{
    [SerializeField] private Vector3[] _spots = { new(107, 2, 42), new(116, 2, 108), new(60, 2, 160), new(170, 2, 60), new(180, 2, 180) };
    [SerializeField] private float[] _yaws = { -90, 0, 90, 180, 45 };
    [SerializeField] private int _settleFrames = 40;
    [SerializeField] private int _sampleFrames = 60;
    [SerializeField] private bool _runOnStart;

    public string LastReport { get; private set; } = "";
    public bool Running { get; private set; }

    private void Start() { if (_runOnStart) Run(); }

    [ContextMenu("Run")]
    public void Run()
    {
        if (!Running) StartCoroutine(Sample());
    }

    private IEnumerator Sample()
    {
        Running = true;
        var sb = new StringBuilder("[PerfSpots]\n");
        var player = FindAnyObjectByType<CharacterBase>();
        for (int i = 0; i < _spots.Length; i++)
        {
            if (player != null && NavMesh.SamplePosition(_spots[i], out NavMeshHit hit, 15f, NavMesh.AllAreas))
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = hit.position + Vector3.up * 0.1f;
                player.transform.rotation = Quaternion.Euler(0f, i < _yaws.Length ? _yaws[i] : 0f, 0f);
                if (cc != null) cc.enabled = true;
            }
            for (int f = 0; f < _settleFrames; f++) yield return null;

            var main = new List<double>(); var render = new List<double>(); var gpu = new List<double>();
            long draws = 0, setpass = 0, tris = 0, casters = 0; int n = 0;
            var timings = new FrameTiming[1];
            for (int f = 0; f < _sampleFrames; f++)
            {
                yield return null;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    main.Add(timings[0].cpuMainThreadFrameTime);
                    render.Add(timings[0].cpuRenderThreadFrameTime);
                    if (timings[0].gpuFrameTime > 0) gpu.Add(timings[0].gpuFrameTime);
                }
#if UNITY_EDITOR
                draws += UnityEditor.UnityStats.drawCalls; setpass += UnityEditor.UnityStats.setPassCalls;
                tris += UnityEditor.UnityStats.triangles; casters += UnityEditor.UnityStats.shadowCasters; n++;
#endif
            }
            int lights = 0, shadowed = 0;
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.enabled && l.type != LightType.Directional) { lights++; if (l.shadows != LightShadows.None) shadowed++; }
            n = Mathf.Max(1, n);
            sb.AppendLine($"spot {i} {_spots[i]}: main {Med(main):F2} render {Med(render):F2} gpu {Med(gpu):F2} ms | draws {draws / n} setpass {setpass / n} " +
                          $"tris {tris / n / 1e6:F2}M casters {casters / n} | lights {lights} shadowed {shadowed}");
        }
        LastReport = sb.ToString();
        Debug.Log(LastReport, this);
        Running = false;
    }

    private static double Med(List<double> l)
    {
        if (l.Count == 0) return 0;
        l.Sort();
        return l[l.Count / 2];
    }
}
