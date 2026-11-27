using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Dev probe: stands the local character idle and records, every frame, root height, CharacterController grounded
/// state, Animator state/bools and the hips/head bone heights; also saves third-person PNGs. Add at runtime
/// (<c>IdleJitterProbe.Begin(seconds, outDir)</c>), read <c>IdleJitterProbe.Report</c> when <c>Done</c>.
/// </summary>
public class IdleJitterProbe : MonoBehaviour
{
    public static string Report = "";
    public static bool Done;

    struct Sample { public string seg; public Vector3 cam, camLocal; public float camPitch; public float t, rootY, hipsY, headY, modelLocalY, vy; public bool cc, animGrounded, landing, freeFall; public string state; }

    CharacterBase _char; Animator _anim; Transform _hips, _head, _model; CharacterController _cc;
    readonly List<Sample> _s = new();
    float _duration, _t0; string _dir; Camera _cam; RenderTexture _rt; int _shot; float _nextShot;
    Camera _mainCam; bool _finish; bool _started; string _seg = "idle"; public static bool Scenario; public static bool SpotsMode; public static bool ToggleMode; public static string[] SpotNames; public static Vector3[] SpotPos;

    public static void Begin(float seconds, string outDir)
    {
        foreach (var o in FindObjectsByType<IdleJitterProbe>(FindObjectsSortMode.None)) Destroy(o.gameObject);
        Done = false; Report = "";
        var go = new GameObject("IdleJitterProbe");
        var p = go.AddComponent<IdleJitterProbe>();
        p._duration = seconds; p._dir = outDir; Directory.CreateDirectory(outDir);
    }

    void Start()
    {
        _char = CharacterBase.FindLocal();
        _cc = _char.Controller; _mainCam = Camera.main;
        _anim = _char.GetComponentInChildren<Animator>();
        _model = _anim.transform;
        _hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
        _head = _anim.GetBoneTransform(HumanBodyBones.Head);

        var camGo = new GameObject("IdleProbeCam");
        _cam = camGo.AddComponent<Camera>();
        _cam.fieldOfView = 40; _cam.nearClipPlane = 0.1f;
        _rt = new RenderTexture(640, 480, 24);
        _cam.targetTexture = _rt;
        _cam.enabled = false;
        _t0 = Time.unscaledTime; _nextShot = 0f; _started = true;
        if (Scenario) StartCoroutine(RunScenario());
    }

    static void Call(object o, string name, params object[] a)
    {
        var m = typeof(CharacterMovement).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        m.Invoke(o, a);
    }

    System.Collections.IEnumerator Wait(float sec) { float e = 0; while (e < sec) { e += Time.deltaTime; yield return null; } }

    System.Collections.IEnumerator RunScenario()
    {
        var mv = _char.Movement; _duration = 1e9f;
        if (ToggleMode)
        {
            var handler = _anim.GetComponent<AnimationHandler>(); var fik = _anim.GetComponent<FootIK>(); var cart = _anim.GetComponent<CartoonAnimator>();
            var step = _anim.GetComponent<RetroAnimationStepper>(); var neck = _anim.GetComponent<NeckFollowCamera>();
            void Set(object o, string f, object v) { if (o == null) return; o.GetType().GetField(f, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(o, v); }
            _seg = "idle_baseline"; yield return Wait(6f); Shot();
            Set(handler, "_headLook", false); _seg = "idle_noHeadLook"; yield return Wait(6f);
            Set(handler, "_headLook", true); Set(fik, "_enabled", false); _seg = "idle_noFootIK"; yield return Wait(6f);
            Set(fik, "_enabled", true); Set(cart, "_enabled", false); _seg = "idle_noCartoon"; yield return Wait(6f);
            Set(cart, "_enabled", true); Set(neck, "_smoothTime", 0f); _seg = "idle_camNoSmooth"; yield return Wait(6f);
            Set(neck, "_smoothTime", 0.05f);
            Set(handler, "_headLook", false); Set(fik, "_enabled", false); Set(cart, "_enabled", false); _seg = "idle_allOff"; yield return Wait(6f);
            if (step != null) { step.enabled = false; _seg = "idle_allOff_noStepper"; yield return Wait(6f); step.enabled = true; }
            Set(handler, "_headLook", true); Set(fik, "_enabled", true); Set(cart, "_enabled", true);
            _finish = true; yield break;
        }
        if (SpotsMode)
        {
            for (int i = 0; i < SpotNames.Length; i++)
            {
                _cc.enabled = false;
                Vector3 p = SpotPos[i];
                if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit h, 10f, ~0, QueryTriggerInteraction.Ignore)) p.y = h.point.y;
                _char.transform.position = p;
                _cc.enabled = true;
                _seg = "idle_" + SpotNames[i]; yield return Wait(6f); Shot();
            }
            _finish = true; yield break;
        }
        _seg = "A_idle"; yield return Wait(4f); Shot();
        Call(mv, "OnMove", new Vector2(0, 1)); _seg = "walk"; yield return Wait(2f);
        Call(mv, "OnMove", Vector2.zero); _seg = "B_idle_after_walk"; yield return Wait(5f); Shot();
        Call(mv, "OnJump"); _seg = "jump"; yield return Wait(1.2f);
        _seg = "C_idle_after_jump"; yield return Wait(5f); Shot();
        Call(mv, "OnCrouch", true); _seg = "crouched"; yield return Wait(1.5f);
        Call(mv, "OnCrouch", false); _seg = "D_idle_after_uncrouch"; yield return Wait(5f); Shot();
        Call(mv, "OnSprint", true); Call(mv, "OnMove", new Vector2(0, 1)); _seg = "sprint"; yield return Wait(1.5f);
        Call(mv, "OnMove", Vector2.zero); Call(mv, "OnSprint", false); _seg = "E_idle_after_sprint"; yield return Wait(5f); Shot();
        Call(mv, "OnMove", new Vector2(1, 0)); _seg = "strafe"; yield return Wait(1f);
        Call(mv, "OnMove", Vector2.zero); _seg = "F_idle_after_strafe"; yield return Wait(5f); Shot();
        _finish = true;
    }

    void Update()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    void LateUpdate()
    {
        if (!_started || Done) return;
        float t = Time.unscaledTime - _t0;
        var info = _anim.GetCurrentAnimatorStateInfo(0);
        string st = _anim.IsInTransition(0) ? "T:" + info.shortNameHash : "S:" + info.shortNameHash;
        _s.Add(new Sample
        {
            seg = _seg, cam = _mainCam ? _mainCam.transform.position : Vector3.zero, camLocal = _mainCam ? _char.transform.InverseTransformPoint(_mainCam.transform.position) : Vector3.zero, camPitch = _mainCam ? _mainCam.transform.eulerAngles.x : 0f, t = t, rootY = _char.transform.position.y, hipsY = _hips.position.y - _char.transform.position.y,
            headY = _head.position.y - _char.transform.position.y, modelLocalY = _model.localPosition.y,
            vy = _char.Movement != null ? _char.Movement.VerticalVelocity : 0f,
            cc = _cc.isGrounded, animGrounded = _anim.GetBool("Grounded"), landing = _anim.GetBool("Landing"),
            freeFall = _anim.GetBool("FreeFall"), state = st
        });

        if (!Scenario && t >= _nextShot && _shot < 6) { Shot(); _nextShot += _duration / 6f; }
        if (_finish || (!Scenario && t >= _duration)) Finish();
    }

    void Shot()
    {
        Vector3 c = _char.transform.position + Vector3.up * 1.2f;
        _cam.transform.position = c + _char.transform.forward * 4.2f + _char.transform.right * 1.2f + Vector3.up * 0.2f;
        _cam.transform.LookAt(c);
        _cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = _rt;
        var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(_dir, $"idle_{_shot:00}.png"), tex.EncodeToPNG());
        Destroy(tex); _shot++;
    }

    void Finish()
    {
        Done = true;
        var sb = new StringBuilder();
        int n = _s.Count;
        float Min(System.Func<Sample, float> f) { float m = float.MaxValue; foreach (var s in _s) m = Mathf.Min(m, f(s)); return m; }
        float Max(System.Func<Sample, float> f) { float m = float.MinValue; foreach (var s in _s) m = Mathf.Max(m, f(s)); return m; }
        int ungrounded = 0, animUngrounded = 0, landing = 0, freeFall = 0, stateChanges = 0, flips = 0;
        string prevState = _s[0].state; bool prevCc = _s[0].cc;
        float maxHipsStep = 0f, sumHipsStep = 0f;
        for (int i = 0; i < n; i++)
        {
            var s = _s[i];
            if (!s.cc) ungrounded++; if (!s.animGrounded) animUngrounded++; if (s.landing) landing++; if (s.freeFall) freeFall++;
            if (s.state != prevState) { stateChanges++; sb.AppendLine($"  state change @{s.t:F2}s {prevState} -> {s.state}"); prevState = s.state; }
            if (s.cc != prevCc) { flips++; prevCc = s.cc; }
            if (i > 0) { float d = Mathf.Abs(s.hipsY - _s[i - 1].hipsY); maxHipsStep = Mathf.Max(maxHipsStep, d); sumHipsStep += d; }
        }
        sb.Insert(0, $"frames {n} over {_s[n - 1].t:F1}s\n" +
            $"rootY range {Min(s => s.rootY):F4}..{Max(s => s.rootY):F4}  (span {(Max(s => s.rootY) - Min(s => s.rootY)) * 1000f:F1} mm)\n" +
            $"hips-above-root range {Min(s => s.hipsY):F4}..{Max(s => s.hipsY):F4}  (span {(Max(s => s.hipsY) - Min(s => s.hipsY)) * 1000f:F1} mm), max frame step {maxHipsStep * 1000f:F1} mm, mean {sumHipsStep / (n - 1) * 1000f:F2} mm\n" +
            $"head-above-root span {(Max(s => s.headY) - Min(s => s.headY)) * 1000f:F1} mm; model localY span {(Max(s => s.modelLocalY) - Min(s => s.modelLocalY)) * 1000f:F2} mm\n" +
            $"cc.isGrounded false frames {ungrounded}, flips {flips}; anim Grounded false {animUngrounded}; Landing true {landing}; FreeFall true {freeFall}; vy range {Min(s => s.vy):F2}..{Max(s => s.vy):F2}\n" +
            $"anim state changes {stateChanges}\n");
        if (_mainCam != null)
        {
            float cmn = 9, cmx = -9, lmn = 9, lmx = -9, xmn = 9, xmx = -9, zmn = 9, zmx = -9, pmn = 999, pmx = -999, maxDy = 0, sumDy = 0; int rev = 0, big2 = 0; float prevDy = 0;
            for (int i = 0; i < n; i++)
            {
                var c = _s[i];
                cmn = Mathf.Min(cmn, c.cam.y); cmx = Mathf.Max(cmx, c.cam.y);
                lmn = Mathf.Min(lmn, c.camLocal.y); lmx = Mathf.Max(lmx, c.camLocal.y);
                xmn = Mathf.Min(xmn, c.camLocal.x); xmx = Mathf.Max(xmx, c.camLocal.x);
                zmn = Mathf.Min(zmn, c.camLocal.z); zmx = Mathf.Max(zmx, c.camLocal.z);
                pmn = Mathf.Min(pmn, c.camPitch); pmx = Mathf.Max(pmx, c.camPitch);
                if (i > 0)
                {
                    float dy = c.cam.y - _s[i - 1].cam.y; maxDy = Mathf.Max(maxDy, Mathf.Abs(dy)); sumDy += Mathf.Abs(dy);
                    if (Mathf.Abs(dy) > 0.0002f) { if (prevDy != 0 && Mathf.Sign(dy) != Mathf.Sign(prevDy)) rev++; prevDy = dy; }
                    if (Mathf.Abs(dy) > 0.002f) big2++;
                }
            }
            sb.AppendLine("--- CAMERA (Camera.main) ---");
            sb.AppendLine($"world Y span {(cmx - cmn) * 1000f:F2} mm; local-to-character Y {lmn:F4}..{lmx:F4} (span {(lmx - lmn) * 1000f:F2} mm), X span {(xmx - xmn) * 1000f:F2} mm, Z span {(zmx - zmn) * 1000f:F2} mm");
            sb.AppendLine($"frame-to-frame |dY| max {maxDy * 1000f:F2} mm, mean {sumDy / (n - 1) * 1000f:F3} mm, frames with |dY|>2mm: {big2}, direction reversals (>0.2mm): {rev}, pitch span {(pmx - pmn):F3} deg");
        }
        sb.AppendLine("--- per segment (idle segments skip first 1.0s settle) ---");
        string cur = null; float segStart = 0; int cnt = 0, ung = 0; float mn = 9, mx = -9, mxs = 0, hmn = 9, hmx = -9; int big = 0; float prevH = 0; bool havePrev = false; float cymn = 9, cymx = -9, cmaxs = 0; int c2 = 0; float prevCy = 0; bool havePC = false;
        void Flush() { if (cur != null && cnt > 0) sb.AppendLine($"{cur,-24} frames {cnt,5} CAM span {(cymx - cymn) * 1000f,6:F1} mm maxStep {cmaxs * 1000f,5:F1} >2mm {c2,3} | hipsSpan {(mx - mn) * 1000f,6:F1} mm headSpan {(hmx - hmn) * 1000f,6:F1} mm maxStep {mxs * 1000f,5:F1} mm steps>3mm {big,3} ungrounded {ung}"); }
        foreach (var x in _s)
        {
            if (x.seg != cur) { Flush(); cur = x.seg; segStart = x.t; cnt = 0; ung = 0; mn = 9; mx = -9; mxs = 0; hmn = 9; hmx = -9; big = 0; havePrev = false; cymn = 9; cymx = -9; cmaxs = 0; c2 = 0; havePC = false; }
            if (!x.cc) ung++;
            if (cur.Contains("idle") && x.t - segStart < 1.0f) { havePrev = false; continue; }
            cymn = Mathf.Min(cymn, x.cam.y); cymx = Mathf.Max(cymx, x.cam.y);
            if (havePC) { float cd = Mathf.Abs(x.cam.y - prevCy); cmaxs = Mathf.Max(cmaxs, cd); if (cd > 0.002f) c2++; }
            prevCy = x.cam.y; havePC = true;
            cnt++; mn = Mathf.Min(mn, x.hipsY); mx = Mathf.Max(mx, x.hipsY); hmn = Mathf.Min(hmn, x.headY); hmx = Mathf.Max(hmx, x.headY);
            if (havePrev) { float d = Mathf.Abs(x.headY - prevH); mxs = Mathf.Max(mxs, d); if (d > 0.003f) big++; }
            prevH = x.headY; havePrev = true;
        }
        Flush();
        Report = sb.ToString();
        File.WriteAllText(Path.Combine(_dir, "report.txt"), Report);
        var csv = new StringBuilder("seg,camY,camLocalY,t,rootY,hipsY,headY,modelLocalY,vy,cc,animGrounded,landing,freeFall,state\n");
        foreach (var s in _s) csv.AppendLine($"{s.seg},{s.cam.y:F5},{s.camLocal.y:F5},{s.t:F4},{s.rootY:F5},{s.hipsY:F5},{s.headY:F5},{s.modelLocalY:F5},{s.vy:F3},{s.cc},{s.animGrounded},{s.landing},{s.freeFall},{s.state}");
        File.WriteAllText(Path.Combine(_dir, "samples.csv"), csv.ToString());
        if (_rt != null) _rt.Release();
        Destroy(_cam.gameObject);
    }
}
