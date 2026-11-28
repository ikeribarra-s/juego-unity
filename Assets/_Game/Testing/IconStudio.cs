using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Icon studio (scene <c>Assets/Scenes/IconStudio.unity</c>, a stripped copy of TestScene). On Play it photographs the REAL
/// models for the inventory icons: kills <c>TestDummy</c> so the skeleton falls apart, takes each bone piece's actual mesh +
/// material (standing death pose, before the physics launch) and the Map / SaltBag prefabs, and renders each one with an
/// orthographic camera (neutral light, transparent background, point-filtered, no post-FX) to
/// <c>Icon_Bone_*.png</c> / <c>Icon_Map.png</c> / <c>Icon_SaltBag.png</c> (same names as before, so GUIDs, import settings and
/// the HUD wiring stay). A contact sheet goes to <c>Temp/IconStudio_sheet.png</c>.
/// Views are per bone group (<see cref="ViewFor"/>): orbit yaw / pitch around the piece + camera roll.
/// </summary>
public class IconStudio : MonoBehaviour
{
    [SerializeField] private int _resolution = 64;
    [SerializeField] private string _outDir = "Assets/_Game/UI/Icons/Inventory";
    [Tooltip("Frame padding around the model (1 = touching the edge).")]
    [SerializeField] private float _padding = 1.14f;
    [SerializeField] private GameObject _mapPrefab;
    [SerializeField] private GameObject _saltPrefab;
    [SerializeField] private bool _runOnStart = true;
    [SerializeField] private Color _keyColor = new(1f, 0.95f, 0.85f);
    [SerializeField] private float _keyIntensity = 1.7f;
    [SerializeField] private Color _ambient = new(0.62f, 0.60f, 0.64f);
    [Tooltip("ToonOutline width for the bone shots (m). The material default (0.02) is thick next to a 64 px icon.")]
    [SerializeField] private float _outlineWidth = 0.007f;

    public string Report { get; private set; }

    private static readonly Vector3 Stage = new(3000f, 0f, 3000f);

    private struct View { public float Yaw, Pitch, Roll; public View(float y, float p, float r) { Yaw = y; Pitch = p; Roll = r; } }

    // Orbit around the piece (model front = +Z): yaw to the character's right (+), pitch from above (+), camera roll.
    private static View ViewFor(BoneGroup g)
    {
        switch (g)
        {
            case BoneGroup.Ribcage:       return new View(18, 10, 0);
            case BoneGroup.Pelvis:        return new View(18, 22, 0);
            case BoneGroup.LeftUpperArm:  return new View(-28, 12, 42);
            case BoneGroup.RightUpperArm: return new View(28, 12, -42);
            case BoneGroup.LeftForearm:   return new View(-28, 12, 42);
            case BoneGroup.RightForearm:  return new View(28, 12, -42);
            case BoneGroup.LeftHand:      return new View(-70, 12, 30);
            case BoneGroup.RightHand:     return new View(70, 12, -30);
            case BoneGroup.LeftThigh:     return new View(-28, 12, 42);
            case BoneGroup.RightThigh:    return new View(28, 12, -42);
            case BoneGroup.LeftShin:      return new View(-28, 12, 42);
            case BoneGroup.RightShin:     return new View(28, 12, -42);
            case BoneGroup.LeftFoot:      return new View(-90, 24, 0);
            case BoneGroup.RightFoot:     return new View(90, 24, 0);
            default:                      return new View(20, 15, 0);
        }
    }

    private static string FileFor(BoneGroup g) => g switch
    {
        BoneGroup.Ribcage       => "Icon_Bone_Ribcage",
        BoneGroup.Pelvis        => "Icon_Bone_Pelvis",
        BoneGroup.LeftUpperArm  => "Icon_Bone_Humerus_L",
        BoneGroup.RightUpperArm => "Icon_Bone_Humerus_R",
        BoneGroup.LeftForearm   => "Icon_Bone_Forearm_L",
        BoneGroup.RightForearm  => "Icon_Bone_Forearm_R",
        BoneGroup.LeftHand      => "Icon_Bone_Hand_L",
        BoneGroup.RightHand     => "Icon_Bone_Hand_R",
        BoneGroup.LeftThigh     => "Icon_Bone_Femur_L",
        BoneGroup.RightThigh    => "Icon_Bone_Femur_R",
        BoneGroup.LeftShin      => "Icon_Bone_Shin_L",
        BoneGroup.RightShin     => "Icon_Bone_Shin_R",
        BoneGroup.LeftFoot      => "Icon_Bone_Foot_L",
        BoneGroup.RightFoot     => "Icon_Bone_Foot_R",
        _ => null,
    };

    private Camera _cam;
    private RenderTexture _rt;
    private readonly List<(string name, Texture2D tex)> _shots = new();

    private IEnumerator Start()
    {
        if (!_runOnStart) yield break;
        yield return null;
        yield return RunAll();
    }

    [ContextMenu("Run (Play mode)")]
    public void Run() { if (Application.isPlaying) StartCoroutine(RunAll()); }

    public IEnumerator RunAll()
    {
        var log = new StringBuilder();
        SetupStage();

        // ── the skeleton falls apart: real pieces ─────────────────────────
        var dummyGo = GameObject.Find("TestDummy");
        if (dummyGo == null) { Report = "TestDummy not found"; Debug.LogError("[IconStudio] " + Report); yield break; }
        dummyGo.transform.rotation = Quaternion.identity;   // facing +Z so the baked pieces are in model orientation
        for (int i = 0; i < 20; i++) yield return null;      // let the idle pose settle

        var dis = dummyGo.GetComponent<SkeletonDisassembler>();
        var vitals = dummyGo.GetComponent<CharacterVitals>();
        vitals.Kill(null);
        yield return null;   // pieces exist now (still at the standing pose; the physics step has not run yet)

        var pieces = new List<BonePiece>(dis.Pieces);
        foreach (var p in pieces)
        {
            if (p.Group == BoneGroup.Skull) continue;
            string file = FileFor(p.Group);
            var mf = p.GetComponent<MeshFilter>();
            if (file == null || mf == null) continue;
            var go = new GameObject("Shot_" + file);
            go.transform.position = Stage;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = p.Renderer.sharedMaterial;
            var mpb = new MaterialPropertyBlock();        // never inherit the first-person head cull / glow
            mpb.SetFloat("_HideHead", 0f); mpb.SetFloat("_CullRadius", 0f); mpb.SetFloat("_EmissionIntensity", 0f);
            mpb.SetFloat("_OutlineWidth", _outlineWidth);
            mr.SetPropertyBlock(mpb);
            Shoot(go, ViewFor(p.Group), file);
            DestroyImmediate(go);
            log.AppendLine($"{file}: {mf.sharedMesh.vertexCount} verts");
        }
        if (dis.RemainsRoot != null) Destroy(dis.RemainsRoot.gameObject);

        // ── the props ─────────────────────────────────────────────────────
        yield return ShootPrefab(_mapPrefab,  "Icon_Map",     new View(12, 52, 0), log);
        yield return ShootPrefab(_saltPrefab, "Icon_SaltBag", new View(24, 14, 0), log);

        WriteAll(log);
        Report = log.ToString();
        Debug.Log("[IconStudio] done:\n" + Report);
    }

    private IEnumerator ShootPrefab(GameObject prefab, string file, View view, StringBuilder log)
    {
        if (prefab == null) { log.AppendLine(file + ": prefab missing"); yield break; }
        var go = Instantiate(prefab, Stage, Quaternion.identity);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
        yield return null;
        Shoot(go, view, file);
        DestroyImmediate(go);
        log.AppendLine(file + ": ok");
    }

    // ── stage ───────────────────────────────────────────────────────────────
    private void SetupStage()
    {
        // Everything that would tint / pixelate / relight the shot is off.
        foreach (var mb in FindObjectsByType<MonoBehaviour>())
        {
            switch (mb.GetType().Name)
            {
                case "HorrorPostFX": case "LightingMoodController": case "DungeonLightingController":
                case "DynamicLightBudget": case "TensionDirector": case "SoundscapeController": case "AtmosphereFX":
                    mb.enabled = false; break;
            }
        }
        if (PixelateFeature.Instance != null) PixelateFeature.Instance.BlockSize = 1;
        foreach (var l in FindObjectsByType<Light>()) l.enabled = false;
        var vol = FindAnyObjectByType<UnityEngine.Rendering.Volume>();
        if (vol != null) vol.enabled = false;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = _ambient;
        RenderSettings.fog = false;
        RenderSettings.skybox = null;

        var keyGo = new GameObject("StudioKey");
        var key = keyGo.AddComponent<Light>();
        key.type = LightType.Directional; key.color = _keyColor; key.intensity = _keyIntensity; key.shadows = LightShadows.None;
        keyGo.transform.rotation = Quaternion.Euler(42f, -38f, 0f);

        var camGo = new GameObject("StudioCamera");
        _cam = camGo.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0, 0, 0, 0);
        _cam.nearClipPlane = 0.01f; _cam.farClipPlane = 50f;
        _cam.allowHDR = false; _cam.allowMSAA = false;
        _cam.enabled = false;   // rendered by hand
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.renderShadows = false;

        _rt = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1, filterMode = FilterMode.Point };
        _cam.targetTexture = _rt;
    }

    private void Shoot(GameObject go, View v, string file)
    {
        var renderers = go.GetComponentsInChildren<MeshRenderer>();
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (var r in renderers) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
        Vector3 center = bounds.center;

        // Camera orbit around the model (front = +Z), roll around the view axis.
        Quaternion orbit = Quaternion.Euler(v.Pitch, v.Yaw, 0f);
        Vector3 camPos = center + orbit * Vector3.forward * 5f;
        Quaternion rot = Quaternion.LookRotation(center - camPos, orbit * Vector3.up) * Quaternion.Euler(0, 0, v.Roll);
        Vector3 right = rot * Vector3.right, up = rot * Vector3.up;

        // Exact fit: project every vertex onto the camera plane.
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            var m = mf.sharedMesh; if (m == null) continue;
            var l2w = mf.transform.localToWorldMatrix;
            // Non-readable imported meshes (the prop FBXs): fall back to the 8 corners of the mesh bounds.
            Vector3[] vs;
            if (m.isReadable) vs = m.vertices;
            else
            {
                var b = m.bounds; vs = new Vector3[8]; int k = 0;
                for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                    vs[k++] = b.center + Vector3.Scale(b.extents, new Vector3(sx, sy, sz));
            }
            foreach (var p in vs)
            {
                Vector3 w = l2w.MultiplyPoint3x4(p) - center;
                float x = Vector3.Dot(w, right), y = Vector3.Dot(w, up);
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
        }
        float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
        float half = Mathf.Max(maxX - minX, maxY - minY) * 0.5f * _padding;

        _cam.transform.SetPositionAndRotation(camPos + right * cx + up * cy, rot);
        _cam.orthographicSize = Mathf.Max(half, 0.01f);
        _cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        var tex = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.ReadPixels(new Rect(0, 0, _resolution, _resolution), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        _shots.Add((file, tex));
    }

    // ── output ──────────────────────────────────────────────────────────────
    private void WriteAll(StringBuilder log)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string dir = Path.Combine(root, _outDir);
        Directory.CreateDirectory(dir);
        foreach (var (name, tex) in _shots)
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());

        // contact sheet (4 columns, x3 point scale, dark checker-free background like the HUD slot)
        int cols = 4, rows = Mathf.CeilToInt(_shots.Count / (float)cols), s = 3, cell = _resolution * s + 8;
        var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, false);
        var bg = new Color(0.15f, 0.15f, 0.15f, 1f);
        var px = new Color[sheet.width * sheet.height];
        for (int i = 0; i < px.Length; i++) px[i] = bg;
        sheet.SetPixels(px);
        for (int n = 0; n < _shots.Count; n++)
        {
            int ox = (n % cols) * cell + 4, oy = (rows - 1 - n / cols) * cell + 4;
            var src = _shots[n].tex.GetPixels();
            for (int y = 0; y < _resolution; y++)
                for (int x = 0; x < _resolution; x++)
                {
                    var c = src[y * _resolution + x];
                    if (c.a < 0.01f) continue;
                    var o = Color.Lerp(bg, new Color(c.r, c.g, c.b, 1f), c.a);
                    for (int dy = 0; dy < s; dy++) for (int dx = 0; dx < s; dx++) sheet.SetPixel(ox + x * s + dx, oy + y * s + dy, o);
                }
        }
        sheet.Apply();
        string sheetPath = Path.Combine(root, "Temp", "IconStudio_sheet.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sheetPath));
        File.WriteAllBytes(sheetPath, sheet.EncodeToPNG());
        log.AppendLine("sheet: " + sheetPath);
        log.AppendLine($"wrote {_shots.Count} icons to {_outDir}");
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }
}
