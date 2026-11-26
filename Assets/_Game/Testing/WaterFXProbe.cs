using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Dev probe for WaterSplashFX (Play mode, TestScene with the WaterTestPit): wades the local player back and forth,
/// jumps in from the 2 m ledge and saves side / top-down PNGs (entry shots are timed from the frame the feet enter the
/// water), counts FootContact vs water-step sounds, and measures GC alloc per frame with the emitter on / off.
/// Add at runtime (execute_code), set OutDir, call Run(); read Log / Done. Not used by the game.
/// </summary>
public class WaterFXProbe : MonoBehaviour
{
    public string OutDir = "";
    public string Log = "";
    public bool Done;
    private CharacterBase _p;
    private Camera _cam;
    private RenderTexture _rt;
    private int _shot;
    private int _waterStepSounds, _waterEnterSounds, _contacts, _allStepSounds;

    private void Update()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    private void OnEnable() { Application.logMessageReceived += OnLog; }
    private void OnDisable() { Application.logMessageReceived -= OnLog; }

    private void OnLog(string msg, string stack, LogType t)
    {
        if (!msg.StartsWith("[CharacterAudio]")) return;
        if (msg.Contains("step ")) { _allStepSounds++; if (msg.Contains("SD_Water")) _waterStepSounds++; }
        if (msg.Contains("water enter")) _waterEnterSounds++;
    }

    public void Run() { StartCoroutine(Go()); }

    /// <summary>SampleScene: wade through the first chunk WaterVolume (flooded crypt / sewer) and shoot feet from the side.</summary>
    public void RunChunk() { StartCoroutine(GoChunk()); }

    private IEnumerator GoChunk()
    {
        _p = FindAnyObjectByType<CharacterBase>();
        var vol = WaterVolume.All.Count > 0 ? WaterVolume.All[0] : null;
        if (vol == null) { Log = "no WaterVolume in scene"; Done = true; yield break; }
        var go = new GameObject("ProbeCam"); _cam = go.AddComponent<Camera>(); _cam.fieldOfView = 50; _cam.nearClipPlane = 0.05f;
        _rt = new RenderTexture(768, 432, 24); _cam.targetTexture = _rt; _cam.enabled = false;
        Directory.CreateDirectory(OutDir);
        Bounds b = vol.Bounds;
        Vector3 start = new Vector3(b.center.x, b.max.y + 0.1f, b.center.z);
        Teleport(start, 0);
        Log = $"chunk water {vol.name} bounds={b.center} size={b.size} | ";
        yield return new WaitForSeconds(0.8f);
        Input("OnMove", new Vector2(0, 1));
        int n = 0; float t0 = Time.time;
        while (Time.time - t0 < 2f && n < 6)
        {
            Vector3 pp = _p.transform.position;
            if (Time.frameCount % 8 == 0) { Shot("chunk_feet", pp + new Vector3(2.4f, 0.6f, 0) , pp + Vector3.up * 0.2f); n++; }
            yield return null;
        }
        Input("OnMove", Vector2.zero);
        Log += $"player y={_p.transform.position.y:0.00} surface={b.max.y:0.00} inWater={vol.Contains(_p.transform.position)}";
        Destroy(go); Done = true;
    }

    private void Input(string method, object arg)
    {
        typeof(CharacterMovement).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_p.Movement, new[] { arg });
    }

    private static void SetField(object o, string f, object v)
    {
        var fi = o.GetType().GetField(f, BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null) fi.SetValue(o, v);
    }

    private void Teleport(Vector3 pos, float yaw)
    {
        _p.Controller.enabled = false; _p.transform.position = pos; _p.transform.rotation = Quaternion.Euler(0, yaw, 0); _p.Controller.enabled = true;
    }

    private void Shot(string tag, Vector3 camPos, Vector3 look)
    {
        _cam.transform.position = camPos; _cam.transform.LookAt(look);
        _cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = _rt;
        var t = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
        t.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0); t.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(OutDir, $"{_shot++:00}_{tag}.png"), t.EncodeToPNG());
        Destroy(t);
    }

    // walk back and forth inside the pool (flip yaw at the ends)
    private IEnumerator Wade(Vector3 c, float seconds, bool sprint, System.Action perFrame = null)
    {
        Input("OnMove", new Vector2(0, 1)); Input("OnSprint", sprint);
        float t0 = Time.time, lastFlip = -1f;
        while (Time.time - t0 < seconds)
        {
            Vector3 d = _p.transform.position - c;
            if (Time.time - lastFlip > 0.3f && Mathf.Abs(d.z) > 1.6f)
            {
                _p.transform.rotation = Quaternion.Euler(0, d.z > 0 ? 180 : 0, 0); lastFlip = Time.time;
            }
            perFrame?.Invoke();
            yield return null;
        }
        Input("OnSprint", false); Input("OnMove", Vector2.zero);
    }

    private IEnumerator Go()
    {
        _p = FindAnyObjectByType<CharacterBase>();
        var pit = GameObject.Find("WaterTestPit").transform;
        var vol = pit.GetComponentInChildren<WaterVolume>();
        var audio = _p.GetComponent<CharacterAudio>();
        var emitter = _p.GetComponent<WaterSplashEmitter>();
        var fx = WaterSplashFX.Instance;
        var go = new GameObject("ProbeCam"); _cam = go.AddComponent<Camera>(); _cam.fieldOfView = 50; _cam.nearClipPlane = 0.05f;
        _rt = new RenderTexture(768, 432, 24); _cam.targetTexture = _rt; _cam.enabled = false;
        _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        Vector3 c = pit.position;
        Directory.CreateDirectory(OutDir);
        var sb = new StringBuilder();

        // ---- A. FootContact vs sounds over a wade (debug logs on) ----
        SetField(audio, "_debugLog", true);
        audio.FootContact += () => _contacts++;
        Teleport(c + new Vector3(0, 0.05f, -4.5f), 0);
        yield return new WaitForSeconds(0.6f);
        int s0 = _allStepSounds, w0 = _waterStepSounds, e0 = _waterEnterSounds, c0 = _contacts, st0 = fx.StepBursts, r0 = fx.RippleBursts;
        // walk in from the dry edge, wade 8 s, then leave
        Input("OnMove", new Vector2(0, 1));
        yield return new WaitForSeconds(0.7f);                 // reach the pool edge
        yield return Wade(c, 8f, false);
        Input("OnMove", Vector2.zero);
        yield return new WaitForSeconds(0.3f);
        sb.AppendLine($"[A walk-in+wade 8s] FootContact={_contacts - c0} stepSounds(all)={_allStepSounds - s0} waterStepSounds={_waterStepSounds - w0} " +
                      $"waterEnterSounds={_waterEnterSounds - e0} fxStepBursts={fx.StepBursts - st0} rippleBursts={fx.RippleBursts - r0} dropped={fx.Dropped}");
        SetField(audio, "_debugLog", false);

        // ---- B. entry from the 2 m ledge, shots timed from the entry frame ----
        SetField(fx, "_debugLog", true); SetField(emitter, "_debugLog", true);
        Teleport(c + new Vector3(-4.5f, 2.05f, 0), 90);
        yield return new WaitForSeconds(0.5f);
        e0 = _waterEnterSounds; int en0 = fx.EnterBursts;
        SetField(audio, "_debugLog", true);
        Input("OnMove", new Vector2(0, 1));
        float tEnter = -1f; int next = 0; float[] marks = { 0.05f, 0.15f, 0.3f, 0.6f };
        float t0 = Time.time;
        Vector3 entryPos = Vector3.zero;
        while (Time.time - t0 < 4f && next < marks.Length)
        {
            Vector3 pp = _p.transform.position;
            if (tEnter < 0f && vol.Contains(pp)) { tEnter = Time.time; entryPos = pp; Input("OnMove", Vector2.zero); sb.AppendLine($"[B] entered water at t={tEnter - t0:0.00}s pos={pp}"); }
            if (tEnter >= 0f && Time.time - tEnter >= marks[next])
            {
                string m = (marks[next] * 1000f).ToString("000");
                Vector3 ep = new Vector3(entryPos.x, c.y + 0.25f, entryPos.z);
                Shot($"enter_side_{m}ms", ep + new Vector3(0, 0.9f, -3.2f), ep + Vector3.up * 0.7f);
                Shot($"enter_top_{m}ms", ep + new Vector3(0, 4.5f, 0.01f), ep);
                next++;
            }
            yield return null;
        }
        yield return new WaitForSeconds(1f);
        sb.AppendLine($"[B] entry bursts={fx.EnterBursts - en0} water-enter sounds={_waterEnterSounds - e0}");
        SetField(audio, "_debugLog", false); SetField(fx, "_debugLog", false); SetField(emitter, "_debugLog", false);

        // ---- C. wade top-down shots (ripples), timed from each ripple burst ----
        Teleport(c + new Vector3(-0.6f, 0.05f, -1.4f), 0);
        yield return new WaitForSeconds(0.3f);
        Input("OnMove", new Vector2(0, 1));
        int n = 0; float tt = Time.time; int lastRip = fx.RippleBursts; float tRip = -1f; Vector3 ripPos = Vector3.zero; int nm = 0;
        float[] rm = { 0.1f, 0.25f, 0.4f, 0.55f };
        while (Time.time - tt < 3f && n < 8)
        {
            if (fx.RippleBursts != lastRip && tRip < 0f) { lastRip = fx.RippleBursts; tRip = Time.time; ripPos = _p.transform.position; nm = 0; }
            if (tRip >= 0f && Time.time - tRip >= rm[nm])
            {
                Shot($"wade_top_{(rm[nm] * 1000f):000}ms", new Vector3(ripPos.x, c.y + 2.4f, ripPos.z + 0.01f), new Vector3(ripPos.x, c.y + 0.25f, ripPos.z)); n++;
                sb.AppendLine($"[C] +{rm[nm]:0.00}s ripples alive={fx.RipplesAlive} total={fx.RippleBursts}");
                if (++nm >= rm.Length) tRip = -1f;
            }
            if (Mathf.Abs(_p.transform.position.z - c.z) > 1.6f) Input("OnMove", Vector2.zero);
            yield return null;
        }
        Input("OnMove", Vector2.zero);

        // ---- E. feet in the (transparent) water, side shots while wading ----
        Teleport(c + new Vector3(0f, 0.05f, -1.4f), 0);
        yield return new WaitForSeconds(0.3f);
        Input("OnMove", new Vector2(0, 1));
        n = 0; tt = Time.time;
        while (Time.time - tt < 1.2f && n < 4)
        {
            if (Time.frameCount % 8 == 0) { Vector3 pp = _p.transform.position; Shot("feet_side", pp + new Vector3(2.4f, 0.55f, 0), pp + Vector3.up * 0.2f); n++; }
            yield return null;
        }
        Input("OnMove", Vector2.zero);
        yield return new WaitForSeconds(0.5f);

        // ---- F. jump out of water: standing, then a sprint jump onto the edge. Shots from the take-off event ----
        bool jumped = false; float tJump = -1f;
        _p.Movement.Jumped += () => { jumped = true; tJump = Time.time; };
        SetField(fx, "_debugLog", true); SetField(emitter, "_debugLog", true);
        int jo0 = fx.JumpOutBursts;
        foreach (bool sprintJump in new[] { false, true })
        {
            jumped = false; tJump = -1f;
            Teleport(sprintJump ? c + new Vector3(-1.5f, 0.05f, 0f) : c + new Vector3(0f, 0.05f, 0f), 90);
            yield return new WaitForSeconds(0.5f);
            if (sprintJump) { Input("OnMove", new Vector2(0, 1)); Input("OnSprint", true); yield return new WaitForSeconds(0.3f); }
            typeof(CharacterMovement).GetMethod("OnJump", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_p.Movement, null);
            Vector3 takeoff = _p.transform.position; int nm2 = 0; float[] jm = { 0.05f, 0.15f, 0.3f };
            float tj0 = Time.time;
            while (Time.time - tj0 < 2.5f && nm2 < jm.Length)
            {
                if (jumped && Time.time - tJump >= jm[nm2])
                {
                    string tag = (sprintJump ? "jumpout_sprint_" : "jumpout_stand_") + (jm[nm2] * 1000f).ToString("000") + "ms";
                    Vector3 ep = new Vector3(takeoff.x, c.y + 0.3f, takeoff.z);
                    Shot(tag, ep + new Vector3(sprintJump ? 0.5f : 0f, 1.0f, -3.4f), ep + Vector3.up * 0.8f + (sprintJump ? Vector3.right * 0.8f : Vector3.zero));
                    nm2++;
                }
                yield return null;
            }
            yield return new WaitForSeconds(sprintJump ? 1.2f : 0.2f);
            sb.AppendLine($"[F] {(sprintJump ? "sprint" : "standing")} jump: Jumped fired={jumped} jumpOut bursts so far={fx.JumpOutBursts - jo0} endPos={_p.transform.position} inWater={vol.Contains(_p.transform.position)}");
            Input("OnSprint", false); Input("OnMove", Vector2.zero);
            yield return new WaitForSeconds(0.8f);
        }
        SetField(fx, "_debugLog", false); SetField(emitter, "_debugLog", false);

        // ---- D. GC alloc per frame, emitter on vs off ----
        using (var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
        {
            foreach (bool on in new[] { true, false, true })
            {
                emitter.enabled = on;
                Teleport(c + new Vector3(-1.2f, 0.05f, -1.2f), 0);
                yield return new WaitForSeconds(0.3f);
                long sum = 0, max = 0; int frames = 0; int steps0 = fx.StepBursts, rip0 = fx.RippleBursts, drop0 = fx.Dropped;
                yield return Wade(c, 10f, false, () =>
                {
                    long v = rec.LastValue; sum += v; if (v > max) max = v; frames++;
                });
                sb.AppendLine($"[D] emitter {(on ? "ON " : "OFF")}: frames={frames} GC alloc/frame mean={(frames > 0 ? sum / (double)frames : 0):0.0} B max={max} B " +
                              $"(stepBursts={fx.StepBursts - steps0} ripples={fx.RippleBursts - rip0} dropped={fx.Dropped - drop0})");
            }
        }
        emitter.enabled = true;
        Log = sb.ToString();
        Destroy(go); Done = true;
    }
}
