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
                }
                if (NetworkPlayer.All.Count == 0) sb.Append("| no players");
                Debug.Log(sb.ToString());
            }
            yield return null;
        }
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
