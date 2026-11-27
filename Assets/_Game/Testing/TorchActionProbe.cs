using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Dev probe (Play mode, add at runtime to the player root): scripts the animated torch pick-up / throw and measures them.
/// <see cref="Run"/> picks up a pedestal torch, a wall torch (spawned on a temporary wall) and floor torches (clones lying on
/// the floor), and throws standing, walking, sprinting, crouched and looking up / down. Game time is fixed at 60 fps
/// (<c>Time.captureDeltaTime</c>) so the run is deterministic and screenshots don't skew it.
/// Reports per action: pop (torch into the hand) / release time, how far the reserved torch moved before the pop (must be 0),
/// launch direction vs camera forward, min on-screen camera depth of the torch geometry (TorchClipProbe method), the largest
/// on-screen hand / elbow jump in camera space on the body's step frames vs. between them (stepped actions must only move on
/// step frames), and the hand↔torch offset range while held. <see cref="ShotDir"/> set = first-person + external third-person PNGs at
/// fixed action times and on the grab / release frame. <see cref="RemoteLook"/> = the plain stepped animation (as other
/// players see it) with the full body, third-person shots only.
/// </summary>
[DefaultExecutionOrder(200)] // after TorchArmAnimator (50) and TorchCarrier (100)
public class TorchActionProbe : MonoBehaviour
{
    public string Report = "";
    public bool   Running;
    public string ShotDir = "";
    public bool   RemoteLook;
    public int    ShotWidth = 960, ShotHeight = 540;
    public Vector3 Cam3Offset = new(-3.0f, 1.9f, 1.0f);   // third-person camera in the character's axes (left side, a bit forward)
    public string TracePhase = "";   // phase name to log per frame into Trace
    public string Trace = "";
    private readonly StringBuilder _trace = new();

    private CharacterBase _ch;
    private CharacterMovement _move;
    private TorchCarrier _carrier;
    private TorchArmAnimator _arm;
    private FirstPersonBodyCull _cull;
    private Camera _cam, _cam3;
    private Transform _hand, _elbow, _shoulder;
    private RenderTexture _rt;
    private Texture2D _tex;
    private readonly StringBuilder _sb = new();

    private static readonly BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo PitchF   = typeof(CharacterMovement).GetField("_cameraPitch", NP);
    private static readonly FieldInfo CrouchF  = typeof(CharacterMovement).GetField("_crouchHeld", NP);
    private static readonly FieldInfo MoveF    = typeof(CharacterMovement).GetField("_moveInput", NP);
    private static readonly FieldInfo SprintF  = typeof(CharacterMovement).GetField("_isSprinting", NP);
    private static readonly FieldInfo SmoothF  = typeof(TorchArmAnimator).GetField("_smoothLocalView", NP);
    private static readonly FieldInfo GripF    = typeof(TorchArmAnimator).GetField("_gripOffset", NP);
    private static readonly MethodInfo DropM   = typeof(TorchCarrier).GetMethod("OnDropItem", NP);

    // Per-phase tracking.
    private string  _phase = "";
    private float   _phaseStart;
    private float   _minZ;
    private float   _maxElbow2, _maxHand2;
    private float   _jumpStep, _jumpOff;                 // max visible hand/elbow first difference on step / non-step frames
    private RetroAnimationStepper _stepper;
    private Torch   _pendingTracked; private Vector3 _pendingStart; private float _pendingMoved;
    private Vector3 _e1, _e2, _h1, _h2; private int _hist;
    private bool    _rigid; private Vector3 _offMin, _offMax; private int _offN;
    private Torch   _tracked; private readonly List<(Transform t, Vector3[] v)> _meshes = new();
    private Torch   _afterRelease; private float _afterReleaseUntil;
    private float   _eventTime = -1f; private int _eventFrame; private string _eventInfo = "";
    private float[] _marks; private int _markIdx; private bool _shotOnEvent; private int _shotN;

    public void Run() { if (!Running) StartCoroutine(Suite()); }
    /// <summary>Short run: floor pick-up + standing throw at a free spot (for tracing / tuning).</summary>
    public void RunQuick(float throwPitch = 0f) { if (!Running) StartCoroutine(Quick(throwPitch)); }

    private IEnumerator Quick(float throwPitch)
    {
        Running = true; Setup(); _sb.Clear(); Time.captureDeltaTime = 1f / 60f;
        bool smoothWas = (bool)SmoothF.GetValue(_arm);
        if (RemoteLook) { SmoothF.SetValue(_arm, false); if (_cull != null) _cull.Apply(false); }
        Torch proto = null;
        foreach (var t in FindObjectsByType<Torch>(FindObjectsInactive.Exclude))
            if (t.transform.parent != null && t.transform.parent.name.Contains("Gallery")) { proto = t; break; }
        Vector3 s = new(0f, 0.05f, 4f);
        Teleport(s, 180f);
        yield return Wait(0.3f);
        Torch ft = FloorTorch(proto, 1.0f);
        yield return Wait(0.2f);
        SetPitch(PitchTo(_carrier.GripPointWorld(ft)));
        yield return Wait(0.6f);
        yield return Pickup("quick pickup", ft);
        SetPitch(0f);
        Teleport(s, 0f);
        yield return Held("quick held", 0.5f);
        yield return Throw("quick throw", throwPitch, Vector2.zero, false, false);
        yield return Wait(1f);
        if (ft != null) Destroy(ft.gameObject);
        if (RemoteLook) { SmoothF.SetValue(_arm, smoothWas); if (_cull != null) _cull.Apply(true); }
        Time.captureDeltaTime = 0f;
        Report = _sb.ToString(); Running = false;
    }

    private void Setup()
    {
        _ch = GetComponent<CharacterBase>();
        _move = GetComponent<CharacterMovement>();
        _carrier = GetComponent<TorchCarrier>();
        _arm = GetComponentInChildren<TorchArmAnimator>();
        _cull = GetComponentInChildren<FirstPersonBodyCull>();
        _cam = GetComponentInChildren<Camera>();
        Animator an = _arm.GetComponent<Animator>();
        _hand = an.GetBoneTransform(HumanBodyBones.LeftHand);
        _elbow = an.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        _shoulder = an.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _stepper = an.GetComponent<RetroAnimationStepper>();
        _carrier.TorchPickedUp += OnGrab;
        _carrier.TorchThrown   += OnThrown;
        if (_cam3 == null)
        {
            var go = new GameObject("ProbeCam3P");
            _cam3 = go.AddComponent<Camera>();
            _cam3.enabled = false; _cam3.fieldOfView = 50f; _cam3.nearClipPlane = 0.05f;
        }
    }

    private Vector3 Fist => _hand.position + _hand.rotation * (Vector3)GripF.GetValue(_arm);

    private void OnGrab(Torch t)
    {
        _eventTime = Time.time - _phaseStart; _eventFrame = Time.frameCount;
        _eventInfo = $"pop at {_eventTime:0.000} s, torch moved before pop {_pendingMoved * 1000f:0.0} mm";
        _shotOnEvent = true;
    }

    private void OnThrown(Torch t)
    {
        _eventTime = Time.time - _phaseStart; _eventFrame = Time.frameCount;
        Vector3 v = t.TryGetComponent(out Rigidbody b) ? b.linearVelocity : Vector3.zero;
        Vector3 want = _cam.transform.forward * 6f + Vector3.up * 1.5f;
        Vector3 vp = _cam.transform.InverseTransformPoint(t.transform.position);
        _eventInfo = $"release at {_eventTime:0.000} s, launch err {Vector3.Angle(v, want):0.0} deg, |v| {v.magnitude:0.00}, " +
                     $"torch at view ({vp.x:0.00}, {vp.y:0.00}, {vp.z:0.00})";
        _afterRelease = t; _afterReleaseUntil = Time.time + 0.15f;
        _shotOnEvent = true;
    }

    private void BeginPhase(string name, float[] marks = null)
    {
        _phase = name; _phaseStart = Time.time; _minZ = 99f; _maxElbow2 = _maxHand2 = 0f; _hist = 0;
        _jumpStep = _jumpOff = 0f; _pendingTracked = null; _pendingMoved = 0f;
        _rigid = false; _offN = 0; _offMin = Vector3.one * 99f; _offMax = -Vector3.one * 99f;
        _eventTime = -1f; _eventInfo = ""; _marks = marks; _markIdx = 0; _shotOnEvent = false;
    }

    private void EndPhase()
    {
        string rig = _offN > 0 ? $", hand->torch offset range {(_offMax - _offMin).magnitude * 1000f:0.00} mm over {_offN} frames" : "";
        _sb.AppendLine($"{_phase,-28}: minZ {(_minZ > 90f ? "-" : _minZ.ToString("0.000"))} m, arm jump on steps {_jumpStep * 1000f:0} mm / " +
                       $"between steps {_jumpOff * 1000f:0.0} mm, 2nd diff hand {_maxHand2 * 1000f:0} mm{rig}{(_eventInfo != "" ? " | " + _eventInfo : "")}");
        if (_trace.Length > 0) { Trace = _trace.ToString(); _trace.Clear(); }
        _phase = "";
    }

    private void LateUpdate()
    {
        if (_phase == "" || _cam == null) return;
        // Camera-space 2nd differences (snaps show as spikes).
        Vector3 e = _cam.transform.InverseTransformPoint(_elbow.position), h = _cam.transform.InverseTransformPoint(_hand.position);
        if (_hist >= 2)
        {
            // Only snaps you can see: the joint inside a slightly widened view frustum (off-screen jumps don't matter).
            float tv = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f, th = tv * _cam.aspect;
            bool Vis(Vector3 q) => q.z > 0.02f && Mathf.Abs(q.x) <= q.z * th && Mathf.Abs(q.y) <= q.z * tv;
            if (Vis(e) || Vis(_e1)) _maxElbow2 = Mathf.Max(_maxElbow2, (e - 2f * _e1 + _e2).magnitude);
            if (Vis(h) || Vis(_h1)) _maxHand2  = Mathf.Max(_maxHand2,  (h - 2f * _h1 + _h2).magnitude);
            float jump = Mathf.Max(Vis(h) || Vis(_h1) ? (h - _h1).magnitude : 0f, Vis(e) || Vis(_e1) ? (e - _e1).magnitude : 0f);
            bool step = _stepper != null && _stepper.LastStepFrame == Time.frameCount;
            if (step) _jumpStep = Mathf.Max(_jumpStep, jump); else _jumpOff = Mathf.Max(_jumpOff, jump);
        }
        if (_phase == TracePhase && _hist >= 2)
            _trace.AppendLine($"{(_stepper != null && _stepper.LastStepFrame == Time.frameCount ? "S" : " ")} {Time.time - _phaseStart:0.000} w {_arm.GetComponent<Animator>().GetLayerWeight(1):0.00} act {_carrier.Action} " +
                              $"hand ({h.x:0.000},{h.y:0.000},{h.z:0.000}) d1 {(h - _h1).magnitude * 1000f:0} d2 {(h - 2f * _h1 + _h2).magnitude * 1000f:0} " +
                              $"elb ({e.x:0.000},{e.y:0.000},{e.z:0.000}) d2 {(e - 2f * _e1 + _e2).magnitude * 1000f:0}");
        _e2 = _e1; _e1 = e; _h2 = _h1; _h1 = h; _hist++;

        Torch pend = _carrier.PendingTorch;
        if (pend != null)
        {
            if (pend != _pendingTracked) { _pendingTracked = pend; _pendingStart = pend.transform.position; }
            _pendingMoved = Mathf.Max(_pendingMoved, Vector3.Distance(pend.transform.position, _pendingStart));
        }
        Torch t = _carrier.Carried != null ? _carrier.Carried : (Time.time < _afterReleaseUntil ? _afterRelease : null);
        if (t != null)
        {
            CacheMeshes(t);
            _minZ = Mathf.Min(_minZ, SampleMinZ());
            if (_rigid && _carrier.Carried == t)
            {
                Vector3 o = _hand.InverseTransformPoint(t.transform.position);
                _offMin = Vector3.Min(_offMin, o); _offMax = Vector3.Max(_offMax, o); _offN++;
            }
        }

        if (!string.IsNullOrEmpty(ShotDir))
        {
            float at = Time.time - _phaseStart;
            if (_shotOnEvent) { _shotOnEvent = false; Shot(_eventInfo.StartsWith("pop") ? "POP" : "RELEASE"); }
            else if (_marks != null && _markIdx < _marks.Length && at >= _marks[_markIdx] - 1e-4f) { Shot($"t{_marks[_markIdx]:0.00}"); _markIdx++; }
        }
    }

    private void CacheMeshes(Torch t)
    {
        if (t == _tracked) return;
        _tracked = t; _meshes.Clear();
        foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
        {
            var r = mf.GetComponent<Renderer>();
            if (mf.sharedMesh == null || r == null || !r.enabled) continue;
            if (r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Flipbook")) continue;
            _meshes.Add((mf.transform, mf.sharedMesh.isReadable ? mf.sharedMesh.vertices : Corners(mf.sharedMesh.bounds)));
        }
    }

    private static Vector3[] Corners(Bounds b)
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        return c;
    }

    // Same as TorchClipProbe: nearest on-screen (or sliceable) vertex depth.
    private float SampleMinZ()
    {
        float m = 99f;
        var w2c = _cam.worldToCameraMatrix;
        float tanV = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * _cam.aspect, n = _cam.nearClipPlane;
        foreach (var (tr, v) in _meshes)
        {
            var l2w = tr.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = w2c.MultiplyPoint3x4(l2w.MultiplyPoint3x4(v[i]));
                float z = -p.z;
                bool onScreen = z > 0f && Mathf.Abs(p.x) <= z * tanH && Mathf.Abs(p.y) <= z * tanV;
                bool slice = z <= n && Mathf.Abs(p.x) <= Mathf.Max(z, n) * tanH && Mathf.Abs(p.y) <= Mathf.Max(z, n) * tanV;
                if ((onScreen || slice) && z < m) m = z;
            }
        }
        return m;
    }

    // ── Screenshots ──────────────────────────────────────────────────────────
    private void Shot(string tag)
    {
        if (_rt == null) { _rt = new RenderTexture(ShotWidth, ShotHeight, 24); _tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false); }
        Directory.CreateDirectory(ShotDir);
        string baseName = $"{Sanitize(_phase)}_{_shotN++:00}_{tag}";
        if (!RemoteLook) Save(_cam, Path.Combine(ShotDir, baseName + "_fp.png"));
        Transform root = transform;
        _cam3.transform.position = root.position + root.rotation * Cam3Offset;
        _cam3.transform.LookAt(root.position + Vector3.up * 1.5f + root.forward * 0.5f);
        if (!RemoteLook && _cull != null) _cull.Apply(false);
        Save(_cam3, Path.Combine(ShotDir, baseName + "_3p.png"));
        if (!RemoteLook && _cull != null) _cull.Apply(true);
    }

    private void Save(Camera c, string path)
    {
        var req = new RenderPipeline.StandardRequest { destination = _rt };
        if (RenderPipeline.SupportsRenderRequest(c, req)) RenderPipeline.SubmitRenderRequest(c, req);
        else { var old = c.targetTexture; c.targetTexture = _rt; c.Render(); c.targetTexture = old; }
        var prev = RenderTexture.active; RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0); _tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, _tex.EncodeToPNG());
    }

    private static string Sanitize(string s)
    {
        var b = new StringBuilder();
        foreach (char ch in s) b.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return b.ToString();
    }

    // ── Scripted run ────────────────────────────────────────────────────────
    private void Teleport(Vector3 pos, float yaw)
    {
        var cc = _ch.Controller;
        cc.enabled = false;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        cc.enabled = true;
    }

    private void SetPitch(float p)
    {
        PitchF.SetValue(_move, p);
        if (_ch.CameraRoot != null) _ch.CameraRoot.localRotation = Quaternion.Euler(p, 0f, 0f);
    }

    private float PitchTo(Vector3 target)
    {
        Vector3 c = _cam.transform.position, d = target - c;
        return Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
    }

    private void SetMove(Vector2 input, bool sprint, bool crouch)
    {
        MoveF.SetValue(_move, input);
        SprintF.SetValue(_move, sprint);
        CrouchF.SetValue(_move, crouch);
    }

    private IEnumerator Wait(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

    private static readonly float[] PickupMarks = { 0f, 0.06f, 0.12f, 0.19f, 0.25f, 0.31f };
    private static readonly float[] ThrowMarks  = { 0f, 0.10f, 0.20f, 0.28f, 0.34f, 0.45f, 0.52f, 0.62f, 0.72f };

    private IEnumerator Pickup(string label, Torch torch)
    {
        BeginPhase(label, PickupMarks);
        torch.Interact(_ch);
        float start = Time.time;
        while ((_carrier.IsBusy || _carrier.Carried == null) && Time.time - start < 1.5f) yield return null;
        if (_carrier.Carried != torch) _eventInfo += " FAILED (not carried)";
        EndPhase();
    }

    private IEnumerator Held(string label, float seconds)
    {
        yield return Wait(0.35f);   // past any grab glide / pick-up recovery / pitch change
        BeginPhase(label);
        _rigid = true;
        yield return Wait(seconds);
        EndPhase();
    }

    private IEnumerator Throw(string label, float pitch, Vector2 move, bool sprint, bool crouch)
    {
        SetMove(move, sprint, crouch);
        SetPitch(pitch);
        yield return Wait(crouch ? 1.0f : 0.8f);
        BeginPhase(label, ThrowMarks);
        DropM.Invoke(_carrier, null);   // the G handler (PhysicsGrabber priority included)
        bool second = _carrier.Throw(); // must be refused: an action is playing
        float start = Time.time;
        while ((_carrier.IsBusy || _carrier.Carried != null) && Time.time - start < 1.5f) yield return null;
        if (second) _eventInfo += " SECOND THROW ACCEPTED";
        if (_carrier.Carried != null) _eventInfo += " FAILED (still carried)";
        yield return Wait(0.1f);
        EndPhase();
        SetMove(Vector2.zero, false, false);
        SetPitch(0f);
        yield return Wait(crouch ? 0.8f : 0.3f);
    }

    private Torch Clone(Torch proto, Vector3 pos, Quaternion rot)
    {
        var go = Instantiate(proto.gameObject, pos, rot);
        go.name = "ProbeTorch";
        return go.GetComponent<Torch>();
    }

    private Torch FloorTorch(Torch proto, float distance)
    {
        // Lying on its side in front of the player, like a thrown torch.
        Vector3 f = transform.forward;
        Quaternion lying = Quaternion.LookRotation(f) * Quaternion.Euler(0f, 0f, 90f) * proto.UprightRotation;
        return Clone(proto, transform.position + f * distance + Vector3.up * 0.13f, lying);
    }

    private IEnumerator Suite()
    {
        Running = true;
        Setup();
        _sb.Clear();
        Time.captureDeltaTime = 1f / 60f;
        bool smoothWas = (bool)SmoothF.GetValue(_arm);
        if (RemoteLook) { SmoothF.SetValue(_arm, false); if (_cull != null) _cull.Apply(false); }
        _sb.AppendLine($"near clip {_cam.nearClipPlane:0.000}, aspect {_cam.aspect:0.00}, animated actions {_carrier.AnimatedActions}, remote look {RemoteLook}");

        Torch proto = null, pedestal = null;
        foreach (var t in FindObjectsByType<Torch>(FindObjectsInactive.Exclude))
        {
            string parent = t.transform.parent != null ? t.transform.parent.name : "";
            if (parent == "Station_Torches" && pedestal == null) pedestal = t;
            else if (parent.Contains("Gallery") && proto == null) proto = t;
        }
        var temp = new List<GameObject>();

        // 1. Pedestal torch (1.9 m torch on a 1 m pedestal).
        Vector3 ped = pedestal.transform.position;
        Teleport(new Vector3(ped.x, 0.05f, ped.z - 1.15f), 0f);
        yield return Wait(0.3f);
        SetPitch(PitchTo(_carrier.GripPointWorld(pedestal)));
        yield return Wait(0.6f);
        yield return Pickup("pickup pedestal", pedestal);
        SetPitch(0f);
        yield return Held("held idle", 1.0f);
        Teleport(new Vector3(ped.x, 0.05f, ped.z - 1.15f), 180f);
        yield return Throw("throw standing", 0f, Vector2.zero, false, false);

        // 2. Wall torch (flame ~2.2 m) on a temporary wall at the origin area.
        Vector3 spot = new(0f, 0.05f, -2f);
        Teleport(spot, 0f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ProbeWall"; temp.Add(wall);
        wall.transform.position = spot + new Vector3(0f, 1.5f, 1.45f);
        wall.transform.localScale = new Vector3(3f, 3f, 0.2f);
        Torch wt = Clone(proto, spot + new Vector3(0f, 1.97f, 1.22f), Quaternion.Euler(0f, 180f, 0f) * proto.UprightRotation);
        temp.Add(wt.gameObject);
        yield return Wait(0.3f);
        SetPitch(PitchTo(_carrier.GripPointWorld(wt)));
        yield return Wait(0.6f);
        yield return Pickup("pickup wall", wt);
        SetPitch(0f);
        Teleport(spot, 180f);
        SetMove(new Vector2(0f, 1f), false, false);
        yield return Held("held walk", 1.0f);
        SetMove(Vector2.zero, false, false);
        Teleport(spot, 180f);
        yield return Throw("throw walking", 0f, new Vector2(0f, 1f), false, false);

        // 3..7. Floor torches (lying), then throws sprinting / crouched / looking up / down.
        string[] labels = { "throw sprinting", "throw crouched", "throw look up 30", "throw look down 30", "throw look down 60" };
        float[] pitches = { 0f, 0f, -30f, 30f, 60f };
        for (int i = 0; i < labels.Length; i++)
        {
            Vector3 s = new(-6f + i * 3f, 0.05f, 4f);
            Teleport(s, 180f);
            yield return Wait(0.3f);
            Torch ft = FloorTorch(proto, 1.0f);
            temp.Add(ft.gameObject);
            yield return Wait(0.2f);
            SetPitch(PitchTo(_carrier.GripPointWorld(ft)));
            yield return Wait(0.6f);
            yield return Pickup(i == 0 ? "pickup floor" : $"pickup floor {i + 1}", ft);
            SetPitch(0f);
            Teleport(s, 0f);
            yield return Throw(labels[i], pitches[i], i == 0 ? new Vector2(0f, 1f) : Vector2.zero, i == 0, i == 1);
        }

        // 8. Interruptions: death-like cancel mid-throw and mid-pick-up must leave nothing stuck to the hand.
        {
            Vector3 s = new(9f, 0.05f, 4f);
            Teleport(s, 180f);
            yield return Wait(0.3f);
            Torch ft = FloorTorch(proto, 1.0f); temp.Add(ft.gameObject);
            SetPitch(PitchTo(_carrier.GripPointWorld(ft)));
            yield return Wait(0.5f);
            ft.Interact(_ch);
            yield return Wait(0.08f);
            _carrier.CancelAction();
            yield return Wait(0.3f);
            string a = $"cancel mid-pickup: carried {(_carrier.Carried != null)}, busy {_carrier.IsBusy}, torch reserved {ft.IsCarried}";
            yield return Pickup("pickup after cancel", ft);
            SetPitch(0f);
            yield return Wait(0.3f);
            DropM.Invoke(_carrier, null);
            yield return Wait(0.12f);
            _carrier.CancelAction();
            yield return Wait(0.3f);
            _sb.AppendLine(a);
            _sb.AppendLine($"cancel mid-throw: carried {(_carrier.Carried != null)}, busy {_carrier.IsBusy}, torch parent {(ft.transform.parent != null ? ft.transform.parent.name : "none")}, kinematic {(ft.TryGetComponent(out Rigidbody rb) && rb.isKinematic)}");
        }

        foreach (var g in temp) if (g != null) Destroy(g);
        if (RemoteLook) { SmoothF.SetValue(_arm, smoothWas); if (_cull != null) _cull.Apply(true); }
        Time.captureDeltaTime = 0f;
        SetPitch(0f);
        Report = _sb.ToString();
        Running = false;
    }
}
