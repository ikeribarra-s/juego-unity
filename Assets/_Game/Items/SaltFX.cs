using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The pouring-salt effects, all pixel-art (point-sampled sheets) like the rest of the game's VFX:
/// a stream of falling grain sprites from the bag mouth to the floor (ParticleSystem, emitted from script on a ballistic arc
/// so each grain lands exactly on the target), a dust puff + tick + floor pile where grains land (a few landings per second),
/// and the piles themselves: alpha-clipped, point-filtered floor quads aligned to the surface that grow when more salt falls
/// on them. Created on first use by <see cref="Get"/>; piles stay for the rest of the scene (oldest recycled past
/// <see cref="SaltFXProfile.MaxPiles"/>). Everything is pooled / reused: no per-frame allocations while pouring.
/// </summary>
public class SaltFX : MonoBehaviour
{
    public static SaltFX Instance { get; private set; }

    private struct Landing { public float Time; public Vector3 Pos, Normal; }
    private struct Pile { public Transform T; public float Size; }

    private SaltFXProfile _p;
    private ParticleSystem _stream, _puffs;
    private readonly Mesh[] _meshes = new Mesh[8];
    private readonly List<Landing> _pending = new();
    private readonly List<Pile> _piles = new();
    private int _recycle;
    private float _grainAcc, _impactAcc;
    private int _epsilon;
    private ParticleSystem.EmitParams _ep;

    // debug / test counters
    public int PileCount => _piles.Count;
    public int GrainsEmitted { get; private set; }
    public int PuffsEmitted { get; private set; }
    public int LandSounds { get; private set; }

    public static SaltFX Get(SaltFXProfile profile)
    {
        if (Instance != null) return Instance;
        var go = new GameObject("SaltFX");
        var fx = go.AddComponent<SaltFX>();
        fx.Init(profile);
        Instance = fx;
        return fx;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ── setup ───────────────────────────────────────────────────────────────
    private void Init(SaltFXProfile p)
    {
        _p = p;
        _stream = MakeSystem("Stream", p.Grain, 4, 1, false, 256);
        _puffs  = MakeSystem("Puffs",  p.Puff,  4, 4, true,  48);
        for (int i = 0; i < _meshes.Length; i++) _meshes[i] = PileMesh(i);
    }

    private ParticleSystem MakeSystem(string name, Material mat, int tilesX, int tilesY, bool overLife, int max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.playOnAwake = false; m.loop = false; m.duration = 1f; m.maxParticles = max; m.startSpeed = 0f; m.startRotation = 0f;
        m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m.gravityModifier = overLife ? 0f : 1f;
        var e = ps.emission; e.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var t = ps.textureSheetAnimation; t.enabled = true; t.mode = ParticleSystemAnimationMode.Grid;
        t.numTilesX = tilesX; t.numTilesY = tilesY;
        if (overLife)
        {
            t.animation = ParticleSystemAnimationType.WholeSheet;
            t.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        }
        else
        {
            t.animation = ParticleSystemAnimationType.WholeSheet;
            t.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            t.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.99f);   // a random grain shape
        }
        t.cycleCount = 1;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.View;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        ps.Play();   // emits only from script, but must be "playing" to accept Emit()
        return ps;
    }

    /// <summary>Unit quad lying in the XZ plane (normal +Y) showing cell <paramref name="i"/> of the 4x2 pile sheet.</summary>
    private static Mesh PileMesh(int i)
    {
        int col = i % 4, row = i / 4;
        float u0 = col / 4f, u1 = (col + 1) / 4f, v1 = 1f - row / 2f, v0 = 1f - (row + 1) / 2f;
        var m = new Mesh { name = "SaltPile_" + i };
        m.vertices  = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
        m.normals   = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        m.uv        = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.RecalculateBounds();
        return m;
    }

    // ── pouring ─────────────────────────────────────────────────────────────
    /// <summary>Fall time (s) from <paramref name="mouth"/> to <paramref name="target"/> with a small upward kick.</summary>
    private static float FallTime(Vector3 mouth, Vector3 target, float v0up)
    {
        float h = Mathf.Max(0.05f, mouth.y - target.y), g = -Physics.gravity.y;
        return (v0up + Mathf.Sqrt(v0up * v0up + 2f * g * h)) / g;
    }

    /// <summary>One tick of pouring: <paramref name="amount01"/> = fraction of a full-rate tick (the last tick of an almost empty bag).</summary>
    public void Pour(Vector3 mouth, Vector3 target, Vector3 normal, float dt, float amount01 = 1f)
    {
        _grainAcc += _p.GrainsPerSecond * dt * amount01;
        int n = (int)_grainAcc; _grainAcc -= n;
        for (int i = 0; i < n; i++) EmitGrain(mouth, target);

        _impactAcc += _p.ImpactsPerSecond * dt * amount01;
        int k = (int)_impactAcc; _impactAcc -= k;
        for (int i = 0; i < k; i++)
        {
            Vector2 d = Random.insideUnitCircle * _p.FloorSpread;
            Vector3 right = Vector3.Cross(normal, Vector3.forward);
            if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(normal, Vector3.right);
            right.Normalize();
            Vector3 fwd = Vector3.Cross(right, normal);
            _pending.Add(new Landing { Time = Time.time + FallTime(mouth, target, -0.4f), Pos = target + right * d.x + fwd * d.y, Normal = normal });
        }
    }

    /// <summary>The flow starts: a quick fan of extra grains.</summary>
    public void Burst(Vector3 mouth, Vector3 target)
    {
        for (int i = 0; i < _p.StartBurst; i++) EmitGrain(mouth, target);
    }

    private void EmitGrain(Vector3 mouth, Vector3 target)
    {
        // leaves the mouth already falling (no upward kick) and lands close ahead (HandItemController._targetRange 0.5-1.4 m):
        // a near-straight falling stream instead of a wide parabola thrown 2.6 m
        float v0 = Random.Range(-0.6f, -0.2f);
        Vector3 from = mouth + Random.insideUnitSphere * _p.MouthSpread;
        Vector3 to = target + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * _p.FloorSpread * 0.8f;
        float T = Mathf.Max(0.08f, FallTime(from, to, v0));
        Vector3 flat = (to - from); flat.y = 0f;
        _ep.position = from;
        _ep.velocity = flat / T + Vector3.up * v0;
        _ep.startLifetime = T;
        _ep.startSize = Random.Range(_p.GrainSize.x, _p.GrainSize.y);
        float b = _p.GrainBrightness * Random.Range(0.9f, 1f);
        _ep.startColor = new Color(b, b, b, 1f);
        _stream.Emit(_ep, 1);
        GrainsEmitted++;
    }

    // ── landings ────────────────────────────────────────────────────────────
    private void Update()
    {
        float now = Time.time;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var l = _pending[i];
            if (l.Time > now) continue;
            _pending[i] = _pending[_pending.Count - 1]; _pending.RemoveAt(_pending.Count - 1);
            Land(l.Pos, l.Normal);
        }
    }

    private void Land(Vector3 pos, Vector3 normal)
    {
        _ep.position = pos + normal * 0.03f;
        _ep.velocity = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.15f, 0.4f), Random.Range(-0.15f, 0.15f));
        _ep.startLifetime = _p.PuffLife * Random.Range(0.8f, 1.2f);
        _ep.startSize = Random.Range(_p.PuffSize.x, _p.PuffSize.y);
        float b = _p.GrainBrightness;
        _ep.startColor = new Color(b, b, b, 0.9f);
        _puffs.Emit(_ep, 1);
        PuffsEmitted++;

        AddPile(pos, normal);

        if (_p.Land != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.Play(_p.Land, pos);
            LandSounds++;
        }
    }

    private void AddPile(Vector3 pos, Vector3 normal)
    {
        float merge2 = _p.MergeRadius * _p.MergeRadius;
        int from = Mathf.Max(0, _piles.Count - 48);
        for (int i = _piles.Count - 1; i >= from; i--)
        {
            var pile = _piles[i];
            if (pile.T == null) continue;
            if ((pile.T.position - pos).sqrMagnitude > merge2 * 4f) continue;   // within ~2x the radius: grow it
            pile.Size = Mathf.Min(_p.MaxPileSize, pile.Size + _p.GrowStep);
            pile.T.localScale = new Vector3(pile.Size, 1f, pile.Size);
            _piles[i] = pile;
            return;
        }

        Transform t;
        float size = Random.Range(_p.PileSize.x, _p.PileSize.y);
        int variant = Random.Range(0, _meshes.Length);
        if (_piles.Count >= _p.MaxPiles)
        {
            // recycle the oldest
            int idx = _recycle++ % _piles.Count;
            var old = _piles[idx];
            t = old.T;
            t.GetComponent<MeshFilter>().sharedMesh = _meshes[variant];
            _piles[idx] = new Pile { T = t, Size = size };
        }
        else
        {
            var go = new GameObject("SaltPile");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _meshes[variant];
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _p.Pile;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            t = go.transform;
            _piles.Add(new Pile { T = t, Size = size });
        }
        _epsilon = (_epsilon + 1) % 8;   // tiny height steps so overlapping piles don't z-fight
        t.SetPositionAndRotation(pos + normal * (0.006f + _epsilon * 0.0007f),
                                 Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        t.localScale = new Vector3(size, 1f, size);
    }
}
