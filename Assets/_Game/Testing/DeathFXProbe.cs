using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Dev probe for Docs/Design/DeathFX.md section 11 (Play mode). Add to GameManager, call <see cref="Run"/>; read <see cref="Report"/>.</summary>
public class DeathFXProbe : MonoBehaviour
{
    public string Report = "";
    public bool Done;
    public string ShotDir = "";
    private readonly StringBuilder _sb = new();

    private void Log(string s) { _sb.AppendLine(s); Report = _sb.ToString(); }

    public void Run() { Done = false; _sb.Clear(); StartCoroutine(Routine()); }

    private int ClackVoices()
    {
        var sm = SoundManager.Instance;
        var pool = (AudioSource[])typeof(SoundManager).GetField("_pool", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sm);
        var defs = (SoundDefinition[])typeof(SoundManager).GetField("_poolDef", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sm);
        int n = 0;
        for (int i = 0; i < pool.Length; i++) if (pool[i].isPlaying && defs[i] != null && defs[i].name == "SD_BoneClack") n++;
        return n;
    }

    private string Look()
    {
        var vol = HorrorPostFX.Instance.GetComponentInChildren<Volume>();
        vol.profile.TryGet(out FilmGrain g); vol.profile.TryGet(out Vignette v); vol.profile.TryGet(out ColorAdjustments c);
        vol.profile.TryGet(out Bloom b); vol.profile.TryGet(out ChromaticAberration ca);
        return $"grain {g.intensity.value:F2} vig {v.intensity.value:F2} sat {c.saturation.value:F1} exp {c.postExposure.value:F2} bloom {b.intensity.value:F2} chroma {ca.intensity.value:F2}";
    }

    // Renders Camera.main into a 1280x720 RT (the editor Game view is tiny) and saves a PNG.
    private IEnumerator Shot(string name)
    {
        if (string.IsNullOrEmpty(ShotDir)) yield break;
        yield return new WaitForEndOfFrame();
        System.IO.Directory.CreateDirectory(ShotDir);
        Camera cam = Camera.main;
        var rt = new RenderTexture(1280, 720, 24);
        var prevT = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prevT;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex); rt.Release(); Destroy(rt);
    }

    private IEnumerator Routine()
    {
        var fx = DeathFX.Instance;
        var keys = FindAnyObjectByType<DeathDebugKeys>();
        var player = CharacterBase.FindLocal();
        Log($"pixelSize BlockSize={PixelateFeature.Instance?.BlockSize}");
        Log("alive look: " + Look());
        var children0 = fx.transform.childCount;

        Time.captureFramerate = 60;   // fixed 1/60 game time: timings are exact
        // stage the nearest dummy 3.2 m in front of the camera so the burst is on screen
        Camera cam = Camera.main;
        foreach (var cb in FindObjectsByType<CharacterBase>(FindObjectsInactive.Exclude))
        {
            if (cb == player || cb.Vitals.IsDead) continue;
            Vector3 fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
            var cc = cb.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            cb.transform.position = player.transform.position + fwd * 3.2f; cb.transform.rotation = Quaternion.LookRotation(-fwd);
            if (cc != null) cc.enabled = true;
            break;
        }
        yield return null; yield return null;
        // 1. kill a dummy (K) and watch the burst: clack voices, timing
        int maxClack = 0; float t0 = Time.time; int f0 = Time.frameCount;
        var victim = keys.KillOther();
        Log($"K killed {victim?.name}; burst at t+{fx.LastBurstTime - t0:F3}s frame+{Time.frameCount - f0}; dust/chips played={fx.BurstsPlayed}");
        long alloc0 = System.GC.GetAllocatedBytesForCurrentThread(); long maxAlloc = 0;
        for (int i = 0; i < 12; i++) { yield return null; long a = System.GC.GetAllocatedBytesForCurrentThread(); maxAlloc = System.Math.Max(maxAlloc, a - alloc0); alloc0 = a; if (i == 3) yield return Shot("01_burst_dust"); }
        Log($"max managed alloc in a frame during/after the burst (editor, incl. rendering) = {maxAlloc} B");
        while (Time.time - t0 < 2f) { maxClack = Mathf.Max(maxClack, ClackVoices()); yield return null; }
        Log($"max simultaneous SD_BoneClack voices over 2 s = {maxClack} (cap 4)");

        // 2. kill the other dummy + self in the same second: budget, takeover
        keys.KillOther();
        yield return new WaitForSeconds(0.05f);

        // 3. self kill: post FX timeline
        float ts = Time.time;
        bool pxBefore = PixelateFeature.Instance.BlockSize == 3;
        keys.KillSelf();
        Log($"F9 self kill; burst frame ok; sting expected at +0.15");
        float[] marks = { 0f, 0.1f, 0.3f, 0.4f, 0.8f, 1.6f };
        int mi = 0; float maxGrain = 0, maxChroma = 0;
        float stingAt = -1;
        while (Time.time - ts < 3.2f)
        {
            float e = Time.time - ts;
            var vol = HorrorPostFX.Instance.GetComponentInChildren<Volume>();
            vol.profile.TryGet(out FilmGrain g); vol.profile.TryGet(out ChromaticAberration ca);
            maxGrain = Mathf.Max(maxGrain, g.intensity.value); maxChroma = Mathf.Max(maxChroma, ca.intensity.value);
            if (stingAt < 0 && fx.LastDelayedPlay > ts) stingAt = fx.LastDelayedPlay - ts;
            if (mi < marks.Length && e >= marks[mi]) { Log($"t={e:F2}  {Look()}"); mi++; }
            if (e > 0.3f && e < 0.35f) yield return Shot("02_dead_kick");
            if (e > 2.9f && e < 2.95f) yield return Shot("03_dead_held");
            yield return null;
        }
        Log($"kick peak grain {maxGrain:F2} chroma {maxChroma:F2}; sting started at +{stingAt:F3}s (0.15 +-0.02)");
        Log("BlockSize in dead state = " + PixelateFeature.Instance.BlockSize);

        // 4. wisp / skull
        var skull = player.GetComponent<SkeletonDisassembler>().LivingSkull;
        var wisp = skull.GetComponent<SkullWisp>();
        Log($"wisp fade {wisp.Fade:F2}, eye light enabled={wisp.EyeLight.enabled} intensity {wisp.EyeLight.intensity:F2}, wisp particles={wisp.Wisp.particleCount}");
        yield return Shot("04_skull_wisp");
        yield return null; yield return null;

        // 5. roll + yip timing, ready tick
        while (!skull.RollReady) yield return null;
        float tr = Time.time; int ticksBefore = fx.ReadyTicks; fx.GetType();
        skull.TryRoll();
        float yipAt = -1;
        float baseline = fx.LastDelayedPlay;
        for (int i = 0; i < 40; i++) { yield return null; if (yipAt < 0 && fx.LastDelayedPlay > baseline) yipAt = fx.LastDelayedPlay - tr; }
        Log($"roll: yip started +{yipAt:F3}s after the roll (0.06 +-0.02); eye pulse intensity={wisp.EyeLight.intensity:F2}; wisp particles={wisp.Wisp.particleCount}");
        yield return Shot("05_skull_roll");
        float cd = skull.LastCooldown;
        while (fx.ReadyTicks == ticksBefore && Time.time - tr < 12f) yield return null;
        Log($"ready tick at +{fx.LastReadyTick - tr:F2}s (cooldown {cd:F2}s), ticks={fx.ReadyTicks - ticksBefore}");

        // 6. GC alloc per frame (idle, wisp emitting)
        long a0 = System.GC.GetAllocatedBytesForCurrentThread(); long worst = 0;
        for (int i = 0; i < 120; i++) { yield return null; long a = System.GC.GetAllocatedBytesForCurrentThread(); worst = System.Math.Max(worst, a - a0); a0 = a; }
        Log($"idle 120 frames (wisp emitting, skull IMGUI): worst frame alloc {worst} B");

        // 7. pool takeover: 6 bursts, budget
        int p0 = fx.BurstsPlayed, d0 = fx.BurstsDropped, kids = fx.transform.childCount;
        for (int i = 0; i < 6; i++) { fx.BurstAt(player.transform.position + Vector3.right * 2f, null, false); yield return new WaitForSeconds(0.15f); }
        Log($"6 bursts in 0.9 s: played {fx.BurstsPlayed - p0}, dropped {fx.BurstsDropped - d0}; dust takeovers {fx.DustTakeovers}; children {kids} -> {fx.transform.childCount} (no Instantiate)");

        // 8. revive
        var local = player.GetComponent<DeathFXLocal>();
        local.Revive(0.6f);
        yield return new WaitForSeconds(0.7f);
        Log("after Revive(0.6)+0.1: " + Look() + $"  blend={local.Blend:F2}  BlockSize={PixelateFeature.Instance.BlockSize}");
        Time.captureFramerate = 0;
        Done = true;
    }
}
