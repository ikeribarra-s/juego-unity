#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Headless-ish capture harness for the refine loop. It lets an external agent
/// drive the OPEN Unity editor (no batch mode needed) via a trigger file:
///
///   RefineCaptures/trigger.txt   (written by the agent; content = seed CSV)
///       -> harness generates the dungeon per seed in a THROWAWAY additive scene
///          (the user's open scene is never touched / dirtied)
///       -> writes per-seed: schematic PNG (grid-derived), top-down render PNG,
///          and appends connectivity metrics to RefineCaptures/metrics.txt
///   RefineCaptures/done.txt      (written by the harness when finished: "ok" or "ERROR ...")
///
/// Batch-mode fallback: -executeMethod RefineHarness.Capture (reads seeds.txt).
///
/// This file lives under Assets/Editor and is purely an editor tool — it is not
/// part of the shipped game and is deleted/ignored at build time.
/// </summary>
[InitializeOnLoad]
public static class RefineHarness
{
    private const string Dir = "RefineCaptures";
    private const string Trigger = "RefineCaptures/trigger.txt";
    private const string Done = "RefineCaptures/done.txt";
    private const string Seeds = "RefineCaptures/seeds.txt";

    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    static RefineHarness()
    {
        EditorApplication.update += Poll;
    }

    private static double _next;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 0.4;

        if (!File.Exists(Trigger)) return;

        string csv;
        try { csv = File.ReadAllText(Trigger).Trim(); }
        catch { return; } // file mid-write; retry next tick

        try { File.Delete(Trigger); } catch { /* ignore */ }

        try
        {
            string report = RunCapture(csv);
            File.WriteAllText(Done, "ok\n" + report);
            Debug.Log("[RefineHarness] capture complete\n" + report);
        }
        catch (Exception e)
        {
            File.WriteAllText(Done, "ERROR: " + e + "\n");
            Debug.LogException(e);
        }
    }

    /// <summary>Batch-mode entry point (-executeMethod RefineHarness.Capture).</summary>
    public static void Capture()
    {
        string csv = File.Exists(Seeds) ? File.ReadAllText(Seeds).Trim() : "1,2,3,4";
        string report = RunCapture(csv);
        File.WriteAllText(Done, "ok\n" + report);
    }

    // ------------------------------------------------------------------

    private static string RunCapture(string seedCsv)
    {
        Directory.CreateDirectory(Dir);

        var seeds = new List<int>();
        foreach (string p in seedCsv.Split(','))
            if (int.TryParse(p.Trim(), out int v)) seeds.Add(v);
        if (seeds.Count == 0) seeds.Add(12345);

        var sb = new StringBuilder();
        sb.AppendLine($"# capture {DateTime.Now:yyyy-MM-dd HH:mm:ss}  seeds={string.Join(",", seeds)}");

        // Work in a throwaway additive scene so the user's scene is never dirtied.
        Scene prevActive = EditorSceneManager.GetActiveScene();
        Scene temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(temp);

        try
        {
            foreach (int seed in seeds)
            {
                var host = new GameObject("__RefineHost");
                try
                {
                    var gen = host.AddComponent<MultiSectionDungeonGenerator>();
                    SetPrivate(gen, "Seed", seed);
                    gen.Generate();

                    string metrics = CaptureSchematic(gen, seed);
                    string rendered = TryCaptureTopDown(gen, seed);
                    sb.AppendLine($"seed {seed}: {metrics}{rendered}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
        }
        finally
        {
            if (prevActive.IsValid()) EditorSceneManager.SetActiveScene(prevActive);
            EditorSceneManager.CloseScene(temp, true);
        }

        File.WriteAllText(Path.Combine(Dir, "metrics.txt"), sb.ToString());
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Schematic: grid-derived top-down floor plan. 100% reliable (no GPU),
    // the best view for judging connectivity / dead boxes / transitions.
    // ------------------------------------------------------------------

    private static string CaptureSchematic(MultiSectionDungeonGenerator gen, int seed)
    {
        Type t = typeof(MultiSectionDungeonGenerator);
        var floor = (bool[,])t.GetField("_floor", F).GetValue(gen);
        var reserved = (bool[,])t.GetField("_reserved", F).GetValue(gen);
        Array section = (Array)t.GetField("_section", F).GetValue(gen);
        int w = (int)t.GetField("_width", F).GetValue(gen);
        int h = (int)t.GetField("_height", F).GetValue(gen);

        int scale = Mathf.Clamp(1200 / Mathf.Max(w, h), 2, 12);
        int W = w * scale, H = h * scale;

        var bg = new Color32(10, 10, 18, 255);
        var resTint = new Color32(255, 255, 255, 255);

        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = bg;

        int floorCount = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!floor[x, y]) continue;
                floorCount++;
                int sv = Convert.ToInt32(section.GetValue(x, y));
                Color32 c = SectionColor(sv);
                if (reserved[x, y]) c = Lerp(c, resTint, 0.45f); // highlight guaranteed routes
                FillTile(px, W, H, x, y, scale, c);
            }
        }

        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.SetPixels32(px);
        tex.Apply();
        // PNG origin is bottom-left already (SetPixels32 row 0 = bottom); flip so
        // +y (north) is up in the image to match the gizmo orientation.
        File.WriteAllBytes(Path.Combine(Dir, $"seed_{seed}_plan.png"), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        // Connectivity: flood fill from the first floor tile, count reachable.
        int reachable = FloodReachable(floor, w, h, out int components);
        float connPct = floorCount > 0 ? 100f * reachable / floorCount : 0f;

        return $"grid={w}x{h} floor={floorCount} reachable={reachable} ({connPct:F1}%) islands={components} ";
    }

    private static Color32 SectionColor(int sectionValue)
    {
        // SectionType { GothicRuins=0, Graveyard=1, HellPillars=2, Transition=3 }
        switch (sectionValue)
        {
            case 0: return new Color32(120, 120, 235, 255); // gothic  - blue
            case 1: return new Color32(95, 220, 130, 255);  // grave   - green
            case 2: return new Color32(230, 80, 55, 255);   // hell    - red
            default: return new Color32(225, 205, 70, 255); // transit - yellow
        }
    }

    private static void FillTile(Color32[] px, int W, int H, int tx, int ty, int scale, Color32 c)
    {
        int x0 = tx * scale, y0 = ty * scale;
        for (int dy = 0; dy < scale; dy++)
        {
            int row = (y0 + dy) * W;
            for (int dx = 0; dx < scale; dx++)
                px[row + x0 + dx] = c;
        }
    }

    private static int FloodReachable(bool[,] floor, int w, int h, out int components)
    {
        var seen = new bool[w, h];
        var stack = new Stack<Vector2Int>();
        components = 0;
        int best = 0, totalSeen = 0;

        for (int sy = 0; sy < h; sy++)
        {
            for (int sx = 0; sx < w; sx++)
            {
                if (!floor[sx, sy] || seen[sx, sy]) continue;
                components++;
                int size = 0;
                stack.Push(new Vector2Int(sx, sy));
                seen[sx, sy] = true;
                while (stack.Count > 0)
                {
                    Vector2Int p = stack.Pop();
                    size++; totalSeen++;
                    Push(floor, seen, stack, p.x + 1, p.y, w, h);
                    Push(floor, seen, stack, p.x - 1, p.y, w, h);
                    Push(floor, seen, stack, p.x, p.y + 1, w, h);
                    Push(floor, seen, stack, p.x, p.y - 1, w, h);
                }
                if (size > best) best = size;
            }
        }
        return best; // largest connected component
    }

    private static void Push(bool[,] floor, bool[,] seen, Stack<Vector2Int> s, int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        if (!floor[x, y] || seen[x, y]) return;
        seen[x, y] = true;
        s.Push(new Vector2Int(x, y));
    }

    // ------------------------------------------------------------------
    // Pretty top-down camera render (URP SubmitRenderRequest). Best-effort.
    // ------------------------------------------------------------------

    private static string TryCaptureTopDown(MultiSectionDungeonGenerator gen, int seed)
    {
        try { return CaptureTopDown(gen, seed); }
        catch (Exception e) { return $"[render skipped: {e.Message}] "; }
    }

    private static string CaptureTopDown(MultiSectionDungeonGenerator gen, int seed)
    {
        var renderers = gen.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return "[no renderers] ";

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

        int W = 1600;
        float aspect = b.size.x / Mathf.Max(0.001f, b.size.z);
        int H = Mathf.Clamp(Mathf.RoundToInt(W / Mathf.Max(0.1f, aspect)), 256, 1600);

        var camGo = new GameObject("__RefineCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = (float)W / H;
        float needV = b.size.z * 0.5f;
        float needH = b.size.x * 0.5f / cam.aspect;
        cam.orthographicSize = Mathf.Max(needV, needH) * 1.03f;
        cam.transform.position = new Vector3(b.center.x, b.max.y + 40f, b.center.z);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;

        // Flat bright ambient + no fog so the layout is readable (restored after).
        bool fog = RenderSettings.fog;
        Color amb = RenderSettings.ambientLight;
        AmbientMode ambMode = RenderSettings.ambientMode;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(1.5f, 1.5f, 1.6f); // lift the near-black materials

        // Strong top-down key light so the dark themed floors actually read.
        var lightGo = new GameObject("__RefineLight");
        var sun = lightGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.6f;
        sun.color = Color.white;
        sun.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(75f, 25f, 0f);

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        try
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req))
                RenderPipeline.SubmitRenderRequest(cam, req);
            else
                cam.targetTexture = rt; // legacy fallback path
        }
        catch
        {
            cam.targetTexture = rt;
            cam.Render();
        }

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        File.WriteAllBytes(Path.Combine(Dir, $"seed_{seed}_top.png"), tex.EncodeToPNG());

        cam.targetTexture = null;
        RenderSettings.fog = fog;
        RenderSettings.ambientLight = amb;
        RenderSettings.ambientMode = ambMode;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(camGo);
        UnityEngine.Object.DestroyImmediate(lightGo);
        return $"render={W}x{H} ";
    }

    // ------------------------------------------------------------------

    private static void SetPrivate(object obj, string field, object value)
    {
        FieldInfo fi = obj.GetType().GetField(field, F);
        if (fi == null) throw new Exception($"field '{field}' not found on {obj.GetType().Name}");
        fi.SetValue(obj, value);
    }

    private static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
}
#endif
