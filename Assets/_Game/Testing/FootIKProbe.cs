using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Dev tool (Play mode): walks the real player CharacterController along waypoints (CharacterMovement
/// disabled, animator driven by the CC velocity as in game) and measures how far the lower foot's sole is
/// from the ground under it each frame (FootIK check), plus foot-height jitter while standing.
/// </summary>
public class FootIKProbe : MonoBehaviour
{
    public string Report = "";
    public bool Running;

    private Animator _anim;
    private CharacterController _cc;
    private float _ankle;
    private CartoonAnimator _cartoon;
    public float ScaleMin, ScaleMax;

    public void Walk(string label, Vector3 start, Vector3[] route, float speed, float stand = 0f)
    {
        if (!Running) StartCoroutine(Run(label, start, route, speed, stand));
    }

    // ---- per-frame foot discontinuity recorder (bug hunt) ----
    private Vector3 _pLocL, _pLocR, _pHip, _pWL, _pWR;
    private bool _pInit;
    private int _frames, _stepFrames, _nonStepChange;
    private float _maxStepLocJump, _maxStepLocY, _maxNonStepJump, _maxWorldJumpPlanted, _sumStepJump;
    private float _wMin = 9, _wMax = -9, _plantedSum;
    private int _plantedN;
    private readonly List<string> _log = new List<string>();

    public void ResetRec() { _pInit = false; _frames = _stepFrames = _nonStepChange = 0; _maxStepLocJump = _maxStepLocY = _maxNonStepJump = _maxWorldJumpPlanted = _sumStepJump = _plantedSum = 0; _plantedN = 0; _wMin = 9; _wMax = -9; _log.Clear(); }

    private void RecordFrame(FootIK ik)
    {
        var root = transform;
        var lf = _anim.GetBoneTransform(HumanBodyBones.LeftFoot); var rf = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
        var hip = _anim.GetBoneTransform(HumanBodyBones.Hips);
        Vector3 ll = root.InverseTransformPoint(lf.position), rl = root.InverseTransformPoint(rf.position), hl = root.InverseTransformPoint(hip.position);
        Vector3 wl = lf.position, wr = rf.position;
        float sl = 0f, sr = 0f;
        if (_pInit && Time.deltaTime < 0.04f)
        {
            // world-space slide of a planted foot (local ankle height below 0.09 in both frames)
            sl = (_pLocL.y < 0.09f && ll.y < 0.09f) ? new Vector2(wl.x - _pWL.x, wl.z - _pWL.z).magnitude : 0f;
            sr = (_pLocR.y < 0.09f && rl.y < 0.09f) ? new Vector2(wr.x - _pWR.x, wr.z - _pWR.z).magnitude : 0f;
            float s = Mathf.Max(sl, sr);
            if (s > 0f) { _plantedN++; _plantedSum += s; _maxWorldJumpPlanted = Mathf.Max(_maxWorldJumpPlanted, s); }
        }
        _pWL = wl; _pWR = wr;
        if (_pInit && Time.deltaTime >= 0.04f) { _pInit = false; _pLocL = ll; _pLocR = rl; _pHip = hl; return; }
        if (_pInit)
        {
            bool step = (hl - _pHip).sqrMagnitude > 1e-10f || (ll - _pLocL).sqrMagnitude > 1e-10f;
            float jl = (ll - _pLocL).magnitude, jr = (rl - _pLocR).magnitude;
            float j = Mathf.Max(jl, jr);
            float jy = Mathf.Max(Mathf.Abs(ll.y - _pLocL.y), Mathf.Abs(rl.y - _pLocR.y));
            _frames++;
            if (step) { _stepFrames++; _sumStepJump += j; _maxStepLocJump = Mathf.Max(_maxStepLocJump, j); _maxStepLocY = Mathf.Max(_maxStepLocY, jy); }
            else if (j > 1e-4f) { _nonStepChange++; _maxNonStepJump = Mathf.Max(_maxNonStepJump, j); }
            if (_log.Count < 400) _log.Add($"{(step ? "S" : ".")} dt={Time.deltaTime * 1000f:F0} L={ll.y:F3}/{ll.z:F3} R={rl.y:F3}/{rl.z:F3} sl={sl:F3} sr={sr:F3} lk={(ik.LeftLocked ? 1 : 0)}{(ik.RightLocked ? 1 : 0)}");
        }
        _wMin = Mathf.Min(_wMin, ik.GlobalWeight); _wMax = Mathf.Max(_wMax, ik.GlobalWeight);
        _pLocL = ll; _pLocR = rl; _pHip = hl; _pInit = true;
    }

    public string RecSummary() =>
        $"frames={_frames} stepFrames={_stepFrames} meanStepJump={(_stepFrames > 0 ? _sumStepJump / _stepFrames : 0):F3} maxStepJump={_maxStepLocJump:F3} maxStepYJump={_maxStepLocY:F3} nonStepChanges={_nonStepChange} maxNonStepJump={_maxNonStepJump:F4} plantedWorldSlide/frame max={_maxWorldJumpPlanted:F3} mean={(_plantedN > 0 ? _plantedSum / _plantedN : 0):F3} (n={_plantedN}) ikW={_wMin:F2}..{_wMax:F2}";
    public string RecLog(int from, int n) => string.Join("\n", _log.GetRange(Mathf.Min(from, _log.Count), Mathf.Min(n, Mathf.Max(0, _log.Count - from))));

    /// <summary>Moves the camera pitch from a to b at rate deg/s (rate 0 = hold a for `hold` s) while recording the carried
    /// torch viewport position, camera height and the torch-VP second difference (wave/snap detector).</summary>
    public void SweepFP(string label, float a, float b, float rate, float hold) => StartCoroutine(SweepRoutine(label, a, b, rate, hold));
    private IEnumerator SweepRoutine(string label, float a, float b, float rate, float hold)
    {
        var move = GetComponent<CharacterMovement>();
        var camRoot = GetComponent<CharacterBase>().CameraRoot;
        var cam = GetComponentInChildren<Camera>();
        var tc = GetComponent<TorchCarrier>();
        var f = typeof(CharacterMovement).GetField("_cameraPitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        float dur = rate > 0f ? Mathf.Abs(b - a) / rate : hold;
        float t = 0f; int n = 0;
        float minY = 9, maxY = -9, minX = 9, maxX = -9, minC = 99, maxC = -99, maxD2 = 0f;
        Vector2 p1 = default, p2 = default;
        // settle at start pitch first
        for (float s = 0f; s < 0.8f; s += Time.deltaTime) { f.SetValue(move, a); camRoot.localRotation = Quaternion.Euler(a, 0f, 0f); yield return null; }
        while (t <= dur)
        {
            float p = rate > 0f ? Mathf.MoveTowards(a, b, rate * t) : a;
            f.SetValue(move, p); camRoot.localRotation = Quaternion.Euler(p, 0f, 0f);
            yield return new WaitForEndOfFrame();
            t += Time.deltaTime;
            if (Time.deltaTime > 0.04f || tc.Carried == null) continue;
            var vp = cam.WorldToViewportPoint(tc.Carried.transform.position + Vector3.up * 0.15f);
            minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y); minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
            minC = Mathf.Min(minC, cam.transform.position.y); maxC = Mathf.Max(maxC, cam.transform.position.y);
            var cur = new Vector2(vp.x, vp.y);
            if (n >= 2) maxD2 = Mathf.Max(maxD2, (cur - 2f * p1 + p2).magnitude);
            p2 = p1; p1 = cur; n++;
            yield return null;
        }
        Report += $"SWEEP {label}: n={n} torchVP x {minX:F3}..{maxX:F3} y {minY:F3}..{maxY:F3} camY {minC:F3}..{maxC:F3} maxVPsecondDiff={maxD2:F4}\n";
    }

    public bool SuiteRunning;
    public void SweepSuite(string tag) => StartCoroutine(SuiteRoutine(tag));
    private IEnumerator SuiteRoutine(string tag)
    {
        SuiteRunning = true;
        yield return StartCoroutine(SweepRoutine(tag + " level", 0f, 0f, 0f, 1.5f));
        yield return StartCoroutine(SweepRoutine(tag + " +15 up", -15f, -15f, 0f, 1.5f));   // negative pitch = look up
        yield return StartCoroutine(SweepRoutine(tag + " -15 down", 15f, 15f, 0f, 1.5f));
        yield return StartCoroutine(SweepRoutine(tag + " sweep -20..20 @63", -20f, 20f, 63f, 0f));
        yield return StartCoroutine(SweepRoutine(tag + " sweep 20..-20 @63", 20f, -20f, 63f, 0f));
        SuiteRunning = false;
    }

    /// <summary>Camera pitch step response: sets CameraRoot pitch to +deg and records chest/head pitch (relative to start) each frame.</summary>
    public string PitchLog = "";
    public void PitchStep(float deg, float seconds) => StartCoroutine(PitchStepRoutine(deg, seconds));
    private IEnumerator PitchStepRoutine(float deg, float seconds)
    {
        var move = GetComponent<CharacterMovement>();
        var anim = GetComponentInChildren<Animator>();
        var chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
        var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        var head = anim.GetBoneTransform(HumanBodyBones.Head);
        var f = typeof(CharacterMovement).GetField("_cameraPitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        float P(Transform t) => Mathf.Asin(Mathf.Clamp(Vector3.Dot(t.up, transform.forward), -1f, 1f)) * Mathf.Rad2Deg;
        yield return new WaitForSeconds(0.5f);
        float c0 = P(chest), s0 = P(spine), h0 = P(head);
        float t = 0f; var sb = new StringBuilder();
        var camRoot = GetComponent<CharacterBase>().CameraRoot;
        f.SetValue(move, deg);
        camRoot.localRotation = Quaternion.Euler(deg, 0f, 0f);
        while (t < seconds)
        {
            yield return null; t += Time.deltaTime;
            f.SetValue(move, deg);
            camRoot.localRotation = Quaternion.Euler(deg, 0f, 0f);
            sb.AppendLine($"t={t * 1000f:F0}ms cam={transform.GetComponentInChildren<Camera>().transform.parent.localEulerAngles.x:F1} spine={P(spine) - s0:F2} chest={P(chest) - c0:F2} head={P(head) - h0:F2}");
        }
        PitchLog = sb.ToString();
    }

    /// <summary>Records the carried torch's viewport position + camera height over time (first-person framing check).</summary>
    public void TrackFP(string label, float seconds) => StartCoroutine(Track(label, seconds));

    private IEnumerator Track(string label, float seconds)
    {
        var cam = GetComponentInChildren<Camera>();
        var tc = GetComponent<TorchCarrier>();
        float t = 0f, minY = 9, maxY = -9, sumY = 0, minX = 9, maxX = -9, minC = 99, maxC = -99; int n = 0;
        while (t < seconds)
        {
            yield return null; t += Time.deltaTime;
            if (tc.Carried == null) continue;
            var vp = cam.WorldToViewportPoint(tc.Carried.transform.position + Vector3.up * 0.15f);
            minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y); sumY += vp.y;
            minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
            minC = Mathf.Min(minC, cam.transform.position.y); maxC = Mathf.Max(maxC, cam.transform.position.y);
            n++;
        }
        Report += $"FP {label}: n={n} torchVP x {minX:F3}..{maxX:F3} y {minY:F3}..{maxY:F3} mean {sumY / Mathf.Max(1, n):F3} camY {minC:F3}..{maxC:F3}\n";
    }

    private float Err(HumanBodyBones b)
    {
        var t = _anim.GetBoneTransform(b);
        var hs = Physics.RaycastAll(t.position + Vector3.up * 1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
        float gy = -99f;
        foreach (var h in hs) if (!h.collider.transform.IsChildOf(transform) && h.point.y > gy) gy = h.point.y;
        return t.position.y - _ankle - gy;
    }

    private IEnumerator Run(string label, Vector3 start, Vector3[] route, float speed, float stand)
    {
        Running = true;
        ResetRec();
        var move = GetComponent<CharacterMovement>();
        _cc = GetComponent<CharacterController>();
        _anim = GetComponentInChildren<Animator>();
        var ik = _anim.GetComponent<FootIK>();
        _ankle = (float)typeof(FootIK).GetField("_ankleHeight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ik);
        move.enabled = false;
        _cartoon = _anim.GetComponent<CartoonAnimator>();
        ScaleMin = 10f; ScaleMax = 0f;
        _cc.enabled = false; transform.position = start; _cc.enabled = true;
        yield return new WaitForSeconds(0.6f);

        var low = new List<float>(); var lj = new List<float>();
        float vy = 0f; int wp = 0; float timer = 0f; float standT = 0f;
        while ((wp < route.Length || standT < stand) && timer < 40f)
        {
            Vector3 step = Vector3.zero;
            if (wp < route.Length)
            {
                Vector3 to = route[wp] - transform.position; to.y = 0f;
                if (to.magnitude < 0.2f) { wp++; continue; }
                transform.rotation = Quaternion.LookRotation(to.normalized);
                step = to.normalized * speed * Time.deltaTime;
            }
            else standT += Time.deltaTime;
            vy = _cc.isGrounded ? -1f : vy - 20f * Time.deltaTime;
            _cc.Move(step + Vector3.up * vy * Time.deltaTime);
            timer += Time.deltaTime;
            yield return null;
            yield return new WaitForEndOfFrame(); // after every LateUpdate (FootIK re-solve), i.e. what is rendered
            if (_cartoon != null)
            {
                float sy = _cartoon.transform.localScale.y;
                ScaleMin = Mathf.Min(ScaleMin, sy); ScaleMax = Mathf.Max(ScaleMax, sy);
            }
            float l = Err(HumanBodyBones.LeftFoot), r = Err(HumanBodyBones.RightFoot);
            RecordFrame(ik);
            if (l > 50f || r > 50f) continue; // ray missed
            low.Add(Mathf.Min(l, r));
            lj.Add(l);
        }
        low.Sort();
        float sum = 0f; foreach (var v in low) sum += v;
        var sb = new StringBuilder();
        sb.Append($"{label}: frames={low.Count} lowerFootSole min={low[0]:F3} p10={low[low.Count / 10]:F3} median={low[low.Count / 2]:F3} p90={low[low.Count * 9 / 10]:F3} max={low[low.Count - 1]:F3} mean={sum / low.Count:F3}");
        if (stand > 0f)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = lj.Count / 3; i < lj.Count; i++) { mn = Mathf.Min(mn, lj[i]); mx = Mathf.Max(mx, lj[i]); }
            sb.Append($" standJitter(L foot range)={mx - mn:F4}");
        }
        Report += sb + "\n  REC " + RecSummary() + "\n";
        move.enabled = true;
        Running = false;
    }
}
