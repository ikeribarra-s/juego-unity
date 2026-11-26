using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Dev helper (Play mode, paused, deterministic): drives the real CharacterMovement through reflection and steps frames
/// at a fixed 60 Hz (Time.captureFramerate) so locomotion can be measured from MCP execute_code. Not shipped.</summary>
public static class LocoPlayTest
{
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;
    public static CharacterMovement CM => Object.FindAnyObjectByType<CharacterMovement>();
    public static Animator Anim => CM.GetComponentInChildren<Animator>();

    // Every mutator throws outside Play mode: in edit mode they would move the scene's Character.
    static void Guard() { if (!Application.isPlaying) throw new System.InvalidOperationException("LocoPlayTest: not in Play mode"); }

    public static void Pause() { Guard(); Time.captureFramerate = 60; EditorApplication.isPaused = true; }
    public static void Step(int n) { Guard(); for (int i = 0; i < n; i++) EditorApplication.Step(); }

    public static void Teleport(Vector3 p, float yaw = 0f)
    {
        Guard();
        var cc = CM.GetComponent<CharacterController>(); cc.enabled = false;
        CM.transform.position = p; CM.transform.rotation = Quaternion.Euler(0, yaw, 0); cc.enabled = true;
    }

    public static void Input(Vector2 move, bool sprint = false, bool crouch = false)
    {
        Guard();
        var t = typeof(CharacterMovement);
        t.GetField("_moveInput", BF).SetValue(CM, move);
        t.GetField("_isSprinting", BF).SetValue(CM, sprint);
        t.GetField("_crouchHeld", BF).SetValue(CM, crouch);
        if (crouch) t.GetField("_isCrouching", BF).SetValue(CM, true);
    }

    public static void Jump() { Guard(); typeof(CharacterMovement).GetField("_jumpRequested", BF).SetValue(CM, true); }

    public static string State()
    {
        var a = Anim; var sb = new StringBuilder();
        var cur = a.GetCurrentAnimatorStateInfo(0);
        string name = new[] { "Locomotion", "Crouch", "JumpStart", "InAir", "JumpLand" }.FirstOrDefault(n => cur.IsName(n)) ?? "?";
        var next = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : default;
        string nn = a.IsInTransition(0) ? new[] { "Locomotion", "Crouch", "JumpStart", "InAir", "JumpLand" }.FirstOrDefault(n => next.IsName(n)) ?? "?" : "-";
        sb.Append($"state={name}->{nn} y={CM.transform.position.y:0.00} v={CM.PlanarVelocity.magnitude:0.00} vy={a.GetFloat("VelocityY"):0.0} MoveX={a.GetFloat("MoveX"):0.0} MoveZ={a.GetFloat("MoveZ"):0.0} gr={a.GetBool("Grounded")} ff={a.GetBool("FreeFall")} cr={a.GetBool("Crouch")} scaleY={a.transform.localScale.y:0.00}");
        return sb.ToString();
    }

    /// <summary>Highest skull vertex, neck bone and camera heights (world, above the character root).</summary>
    public static string Heights()
    {
        var a = Anim; float root = CM.transform.position.y; float top = 0;
        foreach (var s in a.GetComponentsInChildren<SkinnedMeshRenderer>()) { var m = new Mesh(); s.BakeMesh(m); foreach (var v in m.vertices) top = Mathf.Max(top, s.transform.TransformPoint(v).y); Object.DestroyImmediate(m); }
        var cam = Camera.main;
        return $"skullTop={top - root:0.00} neck={a.GetBoneTransform(HumanBodyBones.Neck).position.y - root:0.00} head={a.GetBoneTransform(HumanBodyBones.Head).position.y - root:0.00} camera={(cam != null ? cam.transform.position.y - root : -1):0.00} capsuleH={CM.GetComponent<CharacterController>().height:0.00}";
    }
}
