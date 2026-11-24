using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Dev tool (Play mode): drives the REAL player CharacterController (radius / step offset / slope limit as
/// configured) along a route — NavMesh path corners when there is one, otherwise straight waypoints — with gravity,
/// and reports whether it arrives. Used to walk authored chunks (stairs, crawl gaps, planks, balconies).
/// </summary>
public class CharacterWalkTest : MonoBehaviour
{
    public struct Test { public string Name; public Vector3[] Points; public bool UseNavMesh; public bool Crouch; }

    private readonly List<Test> _tests = new();
    public bool Running { get; private set; }
    public string Log { get; private set; } = "";

    public void Add(string name, bool useNavMesh, bool crouch, params Vector3[] points) =>
        _tests.Add(new Test { Name = name, Points = points, UseNavMesh = useNavMesh, Crouch = crouch });

    public void Run() { if (!Running) StartCoroutine(RunAll()); }

    private IEnumerator RunAll()
    {
        Running = true;
        var player = FindAnyObjectByType<CharacterBase>();
        var cc = player.GetComponent<CharacterController>();
        var move = player.GetComponent<CharacterMovement>();
        foreach (var t in _tests)
        {
            if (move != null) move.enabled = false;
            float h0 = cc.height; Vector3 c0 = cc.center;
            if (t.Crouch) { cc.height = 1.2f; cc.center = new Vector3(0f, 0.6f, 0f); }

            cc.enabled = false; player.transform.position = t.Points[0] + Vector3.up * 0.05f; cc.enabled = true;
            yield return null;

            var route = new List<Vector3>();
            for (int i = 1; i < t.Points.Length; i++)
            {
                if (t.UseNavMesh)
                {
                    var path = new NavMeshPath();
                    NavMesh.SamplePosition(t.Points[i - 1], out var a, 2f, NavMesh.AllAreas);
                    NavMesh.SamplePosition(t.Points[i], out var b, 2f, NavMesh.AllAreas);
                    if (NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.corners.Length > 0)
                    { for (int k = 1; k < path.corners.Length; k++) route.Add(path.corners[k]); }
                    route.Add(t.Points[i]);
                }
                else route.Add(t.Points[i]);
            }

            float vy = 0f, timer = 0f, maxY = player.transform.position.y; int wp = 0;
            while (wp < route.Count && timer < 30f)
            {
                Vector3 to = route[wp] - player.transform.position; to.y = 0f;
                if (to.magnitude < 0.35f) { wp++; continue; }
                vy = cc.isGrounded ? -1f : vy - 20f * Time.deltaTime;
                Vector3 step = to.normalized * 3.5f * Time.deltaTime + Vector3.up * vy * Time.deltaTime;
                cc.Move(step);
                maxY = Mathf.Max(maxY, player.transform.position.y);
                timer += Time.deltaTime;
                yield return null;
            }
            Vector3 end = t.Points[t.Points.Length - 1];
            Vector3 d = end - player.transform.position;
            bool ok = new Vector2(d.x, d.z).magnitude < 0.8f && Mathf.Abs(d.y) < 1.0f;
            Log += $"{t.Name}: {(ok ? "REACHED" : "FAILED")} in {timer:F1}s, final {player.transform.position:F2} target {end:F2}, max y {maxY:F2}, waypoint {wp}/{route.Count}\n";

            if (t.Crouch) { cc.height = h0; cc.center = c0; }
            if (move != null) move.enabled = true;
        }
        _tests.Clear();
        Running = false;
        Debug.Log("[WalkTest]\n" + Log, this);
    }
}
