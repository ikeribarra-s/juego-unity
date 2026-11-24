using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev tools for iterating on the generated map (used from the Unity MCP / menu):
/// a schematic route map PNG (sections, corridors, chunk tiles, blockers, objectives, landmarks) and a lit
/// top-down overview render. Menu: Fluffterror › Living Map › Debug › …
/// </summary>
public static class LivingMapDebugTools
{
    private const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

    [MenuItem("Fluffterror/Living Map/Debug/Dump Route Map")]
    private static void DumpMenu() => Debug.Log(DumpMap(Path.Combine(Application.dataPath, "../Temp/route_map.png")));

    /// <summary>Schematic map: section colours (lighter = reserved lanes), chunk tiles (warm, darker = chunk rock),
    /// red = blockers, green start, blue exit, yellow generators, magenta evidence, white landmarks.</summary>
    public static string DumpMap(string path, int scale = 4)
    {
        var gen = Object.FindAnyObjectByType<MultiSectionDungeonGenerator>();
        var T = typeof(MultiSectionDungeonGenerator);
        var floor = (bool[,])T.GetField("_floor", BF).GetValue(gen);
        var res = (bool[,])T.GetField("_reserved", BF).GetValue(gen);
        var blk = (bool[,])T.GetField("_blocked", BF).GetValue(gen);
        var owner = (int[,])T.GetField("_chunkOwner", BF).GetValue(gen);
        if (floor == null) return "no grid (generate in this domain first)";
        int w = floor.GetLength(0), h = floor.GetLength(1), S = scale;
        var root = gen.transform.Find("_multiSectionRoot");
        var layout = root.GetComponent<DungeonLayout>();
        var px = new Color32[w * S * h * S];
        Color32 Sec(MultiSectionDungeonGenerator.SectionType t) => t switch
        {
            MultiSectionDungeonGenerator.SectionType.GothicRuins => new Color32(90, 90, 130, 255),
            MultiSectionDungeonGenerator.SectionType.Graveyard => new Color32(70, 110, 80, 255),
            MultiSectionDungeonGenerator.SectionType.HellPillars => new Color32(120, 60, 50, 255),
            MultiSectionDungeonGenerator.SectionType.Catacombs => new Color32(120, 110, 70, 255),
            MultiSectionDungeonGenerator.SectionType.Cavern => new Color32(60, 100, 120, 255),
            _ => new Color32(85, 85, 85, 255),
        };
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                Color32 c = new(15, 15, 20, 255);
                bool chunk = owner != null && owner[x, y] != 0;
                if (chunk) c = floor[x, y] ? new Color32(205, 160, 95, 255) : new Color32(70, 52, 35, 255);
                else if (floor[x, y])
                {
                    c = Sec(layout.SectionAt(root.TransformPoint(new Vector3(x + 0.5f, 0, y + 0.5f)), out _));
                    if (res[x, y]) c = new Color32((byte)(c.r + 40), (byte)(c.g + 40), (byte)(c.b + 40), 255);
                }
                if (blk[x, y] && floor[x, y]) c = chunk ? new Color32(150, 60, 40, 255) : new Color32(230, 40, 40, 255);
                for (int i = 0; i < S; i++) for (int j = 0; j < S; j++) px[(y * S + j) * w * S + x * S + i] = c;
            }
        void Dot(Vector3 world, Color32 c, int r)
        {
            var l = root.InverseTransformPoint(world); int cx = (int)(l.x * S), cy = (int)(l.z * S);
            for (int i = -r; i <= r; i++) for (int j = -r; j <= r; j++)
            { int X = cx + i, Y = cy + j; if (X >= 0 && Y >= 0 && X < w * S && Y < h * S) px[Y * w * S + X] = c; }
        }
        foreach (var s in layout.Sections) if (s.HasLandmark) Dot(s.LandmarkPosition, new Color32(255, 255, 255, 255), 6);
        foreach (var g in PowerGenerator.All) if (g != null) Dot(g.transform.position, new Color32(255, 220, 0, 255), 7);
        foreach (var g in Object.FindObjectsByType<PowerGenerator>(FindObjectsInactive.Exclude)) if (g.transform.IsChildOf(gen.transform)) Dot(g.transform.position, new Color32(255, 220, 0, 255), 7);
        foreach (var e in Object.FindObjectsByType<EvidencePickup>(FindObjectsInactive.Exclude)) Dot(e.transform.position, new Color32(255, 0, 255, 255), 6);
        Dot(layout.Extraction, new Color32(0, 150, 255, 255), 9);
        Dot(layout.PlayerStart, new Color32(0, 255, 0, 255), 9);
        if (layout.HasDen) Dot(layout.Den, new Color32(255, 120, 0, 255), 6);
        var tex = new Texture2D(w * S, h * S, TextureFormat.RGB24, false);
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        return $"seed {layout.Seed}, walk {layout.StartToExtractionWalk}, links {layout.SectionLinks} (+{layout.ExtraLoops} loops), chunks {layout.Chunks.Count}: {string.Join(", ", layout.Chunks)}";
    }

    /// <summary>Lit top-down render of the whole map (ceilings face down, so they are culled from above).</summary>
    public static string TopDown(string path, int size = 1400)
    {
        var go = new GameObject("TmpShotCam"); var cam = go.AddComponent<Camera>();
        var gen = Object.FindAnyObjectByType<MultiSectionDungeonGenerator>();
        var root = gen.transform.Find("_multiSectionRoot");
        Bounds b = new(root.position, Vector3.zero);
        foreach (var r in root.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        cam.transform.position = new Vector3(b.center.x, 320f, b.center.z); cam.transform.rotation = Quaternion.Euler(90, 0, 0);
        cam.fieldOfView = 2f * Mathf.Atan(Mathf.Max(b.extents.x, b.extents.z) * 1.05f / (320f - b.center.y)) * Mathf.Rad2Deg; cam.farClipPlane = 700;
        var amb = RenderSettings.ambientLight; var fog = RenderSettings.fog;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f); RenderSettings.fog = false;
        var rt = new RenderTexture(size, size, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderSettings.ambientLight = amb; RenderSettings.fog = fog;
        cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(go); Object.DestroyImmediate(tex);
        return path;
    }

    /// <summary>Free camera shot (uses the scene's lighting; pass amb &gt; 0 to lift ambient for a readability check).</summary>
    public static string Shot(string path, Vector3 pos, Vector3 lookAt, float amb = 0f, int w = 1280, int h = 720, float fov = 70f)
    {
        var go = new GameObject("TmpShotCam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = fov; cam.nearClipPlane = 0.05f;
        cam.transform.position = pos; cam.transform.LookAt(lookAt);
        var a0 = RenderSettings.ambientLight; if (amb > 0) RenderSettings.ambientLight = new Color(amb, amb, amb * 1.08f);
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderSettings.ambientLight = a0; cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(go); Object.DestroyImmediate(tex);
        return path;
    }

    /// <summary>Render the main (player) camera to a PNG — real post FX in Play mode.</summary>
    public static string MainCam(string path, int w = 1280, int h = 720)
    {
        var cam = Camera.main; var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG()); rt.Release(); Object.DestroyImmediate(tex);
        return path;
    }

    /// <summary>List placed chunks: name, centre, yaw.</summary>
    public static string ListChunks()
    {
        var gen = Object.FindAnyObjectByType<MultiSectionDungeonGenerator>();
        var root = gen.transform.Find("_multiSectionRoot/Chunks");
        if (root == null) return "no chunks";
        var sb = new System.Text.StringBuilder();
        foreach (Transform c in root) sb.AppendLine($"{c.name} {c.position:F0} yaw {c.eulerAngles.y:F0}");
        return sb.ToString();
    }

    /// <summary>Teleport the player (Play mode) to a NavMesh spot <paramref name="dist"/> m from target, facing it.</summary>
    public static string PlayerLookAt(Vector3 target, float dist)
    {
        var player = Object.FindAnyObjectByType<CharacterBase>();
        foreach (float d in new[] { dist, dist + 1.5f, dist - 1.5f, dist + 3f })
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8;
                Vector3 p = target + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                if (!UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 1.0f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - target.y) > 1.2f) continue;
                if (Physics.Linecast(hit.position + Vector3.up * 1.6f, target + Vector3.up * 1.2f, LayerMask.GetMask("Walls"))) continue;
                var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
                player.transform.position = hit.position + Vector3.up * 0.1f;
                var look = target - hit.position; look.y = 0;
                player.transform.rotation = Quaternion.LookRotation(look); cc.enabled = true;
                return "moved to " + hit.position;
            }
        return "no spot";
    }
}
