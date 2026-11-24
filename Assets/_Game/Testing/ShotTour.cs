using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Dev tool (Play mode): walks the player through a list of look-at targets — teleport onto the NavMesh
/// <c>dist</c> m from the target with a clear line, wait for the light budget / moods to settle, then render the
/// player camera (real post FX) to a PNG. Used to review generated maps from first person.
/// </summary>
public class ShotTour : MonoBehaviour
{
    public struct Stop { public Vector3 Target; public float Dist; public string File; public float Pitch; }

    private readonly List<Stop> _stops = new();
    public bool Running { get; private set; }
    public string Log { get; private set; } = "";

    public void Add(Vector3 target, float dist, string file, float pitch = 0f) =>
        _stops.Add(new Stop { Target = target, Dist = dist, File = file, Pitch = pitch });

    public void Run(int settleFrames = 45) { if (!Running) StartCoroutine(Tour(settleFrames)); }

    private IEnumerator Tour(int settle)
    {
        Running = true;
        var player = FindAnyObjectByType<CharacterBase>();
        foreach (var s in _stops)
        {
            bool moved = Place(player, s.Target, s.Dist);
            for (int i = 0; i < settle; i++) yield return null;
            var cam = Camera.main;
            Quaternion r0 = cam.transform.rotation;
            Vector3 look = s.Target + Vector3.up * (1.2f + s.Pitch) - cam.transform.position;
            if (look.sqrMagnitude > 0.01f) cam.transform.rotation = Quaternion.LookRotation(look);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; cam.transform.rotation = r0;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(s.File, tex.EncodeToPNG());
            rt.Release(); Destroy(tex);
            Log += $"{Path.GetFileName(s.File)} {(moved ? "ok" : "NO SPOT")} {cam.transform.position:F1}\n";
        }
        _stops.Clear();
        Running = false;
    }

    private static bool Place(CharacterBase player, Vector3 target, float dist)
    {
        int walls = LayerMask.GetMask("Walls");
        foreach (float d in new[] { dist, dist + 1.5f, dist - 1.5f, dist + 3f, dist * 0.5f })
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                Vector3 p = target + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - target.y) > 1.5f) continue;
                if (Physics.Linecast(hit.position + Vector3.up * 1.6f, target + Vector3.up * 1.2f, walls)) continue;
                var cc = player.GetComponent<CharacterController>();
                cc.enabled = false;
                player.transform.position = hit.position + Vector3.up * 0.1f;
                Vector3 look = target - hit.position; look.y = 0f;
                if (look.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(look);
                cc.enabled = true;
                return true;
            }
        return false;
    }
}
