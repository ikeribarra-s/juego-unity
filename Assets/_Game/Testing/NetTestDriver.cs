using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Dev tool for multi-peer tests with a built client (no human at the window). Command-line switches, read at startup:
///   -netlog    every second, log every player this peer sees: "[NetTest] t=12.0 you=P1 | P0@(x,y,z) crouch=.. | P1@(..)"
///   -netdrive  once the local player exists, walk it through a scripted route (forward, strafe, jump, crouch, turn) and log phases.
/// Created automatically by a RuntimeInitializeOnLoadMethod only when one of the switches is present, so it never exists in normal play.
/// </summary>
public class NetTestDriver : MonoBehaviour
{
    private bool _log, _drive;

    /// <summary>When set, a third-person PNG of the first remote player is saved here every <see cref="ShotInterval"/> s.</summary>
    public static string ShotDir;
    public static float ShotInterval = 1.5f;
    private Camera _shotCam; private RenderTexture _shotRt; private int _shotIndex; private float _nextShot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool log = false, drive = false;
        foreach (string a in System.Environment.GetCommandLineArgs()) { if (a == "-netlog") log = true; if (a == "-netdrive") drive = true; }
        if (!log && !drive) return;
        var go = new GameObject("NetTestDriver");
        DontDestroyOnLoad(go);
        var d = go.AddComponent<NetTestDriver>();
        d._log = log || drive; d._drive = drive;
    }

    private IEnumerator Start()
    {
        float next = 0f;
        if (_drive) StartCoroutine(Drive());
        while (true)
        {
            if (_log && Time.unscaledTime >= next)
            {
                next = Time.unscaledTime + 1f;
                var sb = new System.Text.StringBuilder($"[NetTest] t={Time.unscaledTime:F1} ");
                foreach (var p in NetworkPlayer.All)
                {
                    var c = p.Character;
                    sb.Append($"| P{p.OwnerClientId}{(p.IsOwner ? "(you)" : "")}@({p.transform.position.x:F2},{p.transform.position.y:F2},{p.transform.position.z:F2}) yaw={p.transform.eulerAngles.y:F0}");
                    if (!p.IsOwner && c != null && c.Movement != null)
                    {
                        var an = c.GetComponentInChildren<Animator>();
                        var m = c.Movement;
                        sb.Append($" [remote v={m.PlanarVelocity.magnitude:F2} vy={m.VerticalVelocity:F1} gnd={m.IsGrounded} crouch={m.IsCrouching} spr={m.IsSprinting} pitch={m.CameraPitch:F0}");
                        if (an != null) sb.Append($" | anim Speed={an.GetFloat("Speed"):F2} MoveZ={an.GetFloat("MoveZ"):F2} MoveX={an.GetFloat("MoveX"):F2} Grounded={an.GetBool("Grounded")} Crouch={an.GetBool("Crouch")} FreeFall={an.GetBool("FreeFall")}");
                        sb.Append(']');
                    }
                }
                if (NetworkPlayer.All.Count == 0) sb.Append("| no players");
                Debug.Log(sb.ToString());
            }
            if (!string.IsNullOrEmpty(ShotDir) && Time.unscaledTime >= _nextShot) { _nextShot = Time.unscaledTime + ShotInterval; ShotRemote(); }
            yield return null;
        }
    }

    private void ShotRemote()
    {
        NetworkPlayer target = null;
        foreach (var p in NetworkPlayer.All) if (!p.IsOwner) { target = p; break; }
        if (target == null) return;
        if (_shotCam == null)
        {
            var g = new GameObject("NetTestShotCam");
            DontDestroyOnLoad(g);
            _shotCam = g.AddComponent<Camera>(); _shotCam.fieldOfView = 40f; _shotCam.nearClipPlane = 0.1f; _shotCam.enabled = false;
            _shotRt = new RenderTexture(640, 480, 24); _shotCam.targetTexture = _shotRt;
            System.IO.Directory.CreateDirectory(ShotDir);
        }
        Transform t = target.transform;
        Vector3 c = t.position + Vector3.up * 1.2f;
        _shotCam.transform.position = c + t.forward * 4.2f + t.right * 1.4f + Vector3.up * 0.3f;
        _shotCam.transform.LookAt(c);
        _shotCam.Render();
        var prev = RenderTexture.active; RenderTexture.active = _shotRt;
        var tex = new Texture2D(_shotRt.width, _shotRt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, _shotRt.width, _shotRt.height), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(ShotDir, $"remote_{_shotIndex++:000}.png"), tex.EncodeToPNG());
        Destroy(tex);
    }

    private static void Call(object o, string method, params object[] args)
    {
        var m = typeof(CharacterMovement).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        m.Invoke(o, args);
    }

    private IEnumerator Drive()
    {
        while (NetworkPlayer.Local == null) yield return null;
        yield return new WaitForSecondsRealtime(3f);
        var mv = NetworkPlayer.Local.Character.Movement;
        Debug.Log("[NetTest] drive: forward");
        Call(mv, "OnMove", new Vector2(0, 1)); yield return new WaitForSecondsRealtime(2f);
        Debug.Log("[NetTest] drive: strafe right");
        Call(mv, "OnMove", new Vector2(1, 0)); yield return new WaitForSecondsRealtime(1f);
        Debug.Log("[NetTest] drive: stop");
        Call(mv, "OnMove", Vector2.zero); yield return new WaitForSecondsRealtime(1.5f);
        Debug.Log("[NetTest] drive: jump");
        Call(mv, "OnJump"); yield return new WaitForSecondsRealtime(1.5f);
        Debug.Log("[NetTest] drive: crouch");
        Call(mv, "OnCrouch", true); yield return new WaitForSecondsRealtime(1.5f);
        Call(mv, "OnCrouch", false); yield return new WaitForSecondsRealtime(1f);
        Debug.Log("[NetTest] drive: turn");
        Call(mv, "OnLook", new Vector2(40f, 0f)); yield return new WaitForSecondsRealtime(1f);
        Call(mv, "OnLook", Vector2.zero);
        Debug.Log("[NetTest] drive: forward again");
        Call(mv, "OnMove", new Vector2(0, 1)); yield return new WaitForSecondsRealtime(2f);
        Call(mv, "OnMove", Vector2.zero);
        Debug.Log("[NetTest] drive: done");
    }
}
