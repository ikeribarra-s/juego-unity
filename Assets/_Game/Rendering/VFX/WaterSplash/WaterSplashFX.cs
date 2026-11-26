using UnityEngine;

/// <summary>
/// Pooled pixel-art water particles (step crown, entry splash, flat ripple, drips). Static <see cref="Instance"/> like
/// AtmosphereFX; put it on GameManager. Callers (WaterSplashEmitter) pass world-space surface positions.
/// Everything is emitted from script with EmitParams (no shape module, no per-frame allocations); instances are only
/// round-robined so one system's particle cap is never the bottleneck. Global budget: _maxBurstsPerSecond
/// bursts per second, requests above it are dropped (ripples first). See Docs/Design/WaterSplashFX.md.
/// Prefabs + wiring: Fluffterror > VFX > Build Water Splash.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Rendering/Water Splash FX")]
public class WaterSplashFX : MonoBehaviour
{
    public static WaterSplashFX Instance { get; private set; }

    [SerializeField] private ParticleSystem _stepPrefab, _enterPrefab, _ripplePrefab, _dripsPrefab;
    [SerializeField, Min(1)] private int _stepPool = 8, _enterPool = 2, _ripplePool = 12, _dripsPool = 4;
    [Tooltip("Global cap of new bursts per second. Drips count half, ripples are the first to be dropped.")]
    [SerializeField] private float _maxBurstsPerSecond = 10f;
    [SerializeField] private bool _debugLog;

    [Header("Step crown")]
    [SerializeField] private Vector2 _stepLife = new Vector2(0.35f, 0.55f);
    [SerializeField] private Vector2 _stepSize = new Vector2(0.22f, 0.34f);
    [SerializeField] private float _stepRadius = 0.06f;
    [SerializeField] private Vector2 _stepOut = new Vector2(0.1f, 0.3f);
    [SerializeField] private Vector2 _stepUp = new Vector2(0.05f, 0.25f);
    [Header("Entry splash")]
    [SerializeField] private Vector2 _enterLife = new Vector2(0.7f, 1.1f);
    [SerializeField] private Vector2 _enterSize = new Vector2(0.5f, 0.8f);
    [SerializeField] private float _enterRadius = 0.3f;
    [Tooltip("Entry crown scale at k = 0 and k = 1 (multiplies Enter Size).")]
    [SerializeField] private Vector2 _enterScaleByK = new Vector2(0.7f, 1.5f);
    [Tooltip("Flung droplets on entry (WaterDrop sheet): count at k = 0 / 1, outward and upward speed (x lerp(0.6, 1.3, k)).")]
    [SerializeField] private Vector2Int _enterDroplets = new Vector2Int(10, 36);
    [SerializeField] private Vector2 _dropletOut = new Vector2(1.2f, 3f), _dropletUp = new Vector2(2.5f, 5f);
    [SerializeField] private Vector2 _dropletSize = new Vector2(0.1f, 0.16f), _dropletLife = new Vector2(0.7f, 1f);
    [Header("Jump-out (take-off from water): smaller than the entry")]
    [SerializeField] private Vector2 _jumpCrowns = new Vector2(2f, 4f);
    [SerializeField] private Vector2 _jumpSize = new Vector2(0.45f, 0.7f);
    [Tooltip("Crown scale at k = 0 and k = 1 (entry at k = 1 is x1.5 of a larger size).")]
    [SerializeField] private Vector2 _jumpScaleByK = new Vector2(0.7f, 1.1f);
    [SerializeField] private Vector2Int _jumpDroplets = new Vector2Int(8, 26);
    [SerializeField] private Vector2 _jumpDropletOut = new Vector2(0.2f, 0.9f), _jumpDropletUp = new Vector2(3f, 5.5f);
    [Header("Ripple / drips")]
    [SerializeField] private float _rippleLife = 0.6f;
    [SerializeField] private float _rippleLift = 0.02f;
    [SerializeField] private Vector2 _dripLife = new Vector2(0.45f, 0.7f);
    [SerializeField] private Vector2 _dripSize = new Vector2(0.035f, 0.05f);

    private ParticleSystem[] _step, _enter, _ripple, _drips;
    private int _iStep, _iEnter, _iRipple, _iDrips;
    private float _window, _used;
    private int _burstsThisSecond;
    private ParticleSystem.EmitParams _ep;

    public int BurstsLastSecond { get; private set; }
    public int Dropped { get; private set; }
    /// <summary>Lifetime emit counters (debug / probes).</summary>
    public int StepBursts { get; private set; }
    public int EnterBursts { get; private set; }
    public int RippleBursts { get; private set; }
    public int DripEmits { get; private set; }
    public int JumpOutBursts { get; private set; }
    public int RipplesAlive { get { int n = 0; if (_ripple != null) for (int i = 0; i < _ripple.Length; i++) n += _ripple[i].particleCount; return n; } }

    private void Awake() { Instance = this; Build(); }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Build()
    {
        _step = Pool(_stepPrefab, _stepPool); _enter = Pool(_enterPrefab, _enterPool);
        _ripple = Pool(_ripplePrefab, _ripplePool); _drips = Pool(_dripsPrefab, _dripsPool);
    }

    private ParticleSystem[] Pool(ParticleSystem prefab, int n)
    {
        if (prefab == null) return null;
        var a = new ParticleSystem[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = Instantiate(prefab, transform);
            a[i].gameObject.SetActive(true);
            a[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        return a;
    }

    private bool Budget(float cost, bool lowPriority)
    {
        float now = Time.unscaledTime;
        if (now - _window >= 1f) { BurstsLastSecond = _burstsThisSecond; _burstsThisSecond = 0; _used = 0f; _window = now; }
        float cap = _maxBurstsPerSecond - (lowPriority ? 2f : 0f);
        if (_used + cost > cap) { Dropped++; return false; }
        _used += cost; _burstsThisSecond++;
        return true;
    }

    private static ParticleSystem Next(ParticleSystem[] pool, ref int i) { return pool[i++ % pool.Length]; }

    /// <summary>Crown(s) of spray around a foot (count 3 per crown sprite). surfacePos is on the water surface.</summary>
    public void Step(Vector3 surfacePos, int count, float sizeMul = 1f, float speedMul = 1f)
    {
        if (_step == null || count <= 0 || !Budget(1f, false)) return;
        Radial(Next(_step, ref _iStep), surfacePos, Mathf.Clamp(Mathf.CeilToInt(count / 3f), 1, 6), _stepRadius * Mathf.Sqrt(sizeMul), _stepLife, _stepSize, sizeMul,
               _stepOut, _stepUp, speedMul);
 StepBursts++;
        if (_debugLog) Debug.Log("[WaterSplashFX] step x" + count + " @ " + surfacePos);
    }

    /// <summary>Big splash on entering the water; k in 0..1 is the impact intensity. Tall crowns + flung droplets.</summary>
    public void Enter(Vector3 surfacePos, float k)
    {
        if (_enter == null || !Budget(1f, false)) return;
        k = Mathf.Clamp01(k);
        int n = Mathf.RoundToInt(Mathf.Lerp(3f, 6f, k));
        float scale = Mathf.Lerp(_enterScaleByK.x, _enterScaleByK.y, k);
        Radial(Next(_enter, ref _iEnter), surfacePos, n, _enterRadius, _enterLife, _enterSize, scale,
               new Vector2(0.15f, 0.4f), new Vector2(0.05f, 0.25f), 1f);
        if (_drips != null)
        {
            int d = Mathf.RoundToInt(Mathf.Lerp(_enterDroplets.x, _enterDroplets.y, k));
            float s = Mathf.Lerp(0.6f, 1.3f, k);
            var ps = Next(_drips, ref _iDrips);
            for (int i = 0; i < d; i++)
            {
                float a = Random.value * Mathf.PI * 2f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                _ep = default;
                _ep.position = surfacePos + new Vector3(ca * _enterRadius, 0.05f, sa * _enterRadius);
                float o = Random.Range(_dropletOut.x, _dropletOut.y) * s;
                _ep.velocity = new Vector3(ca * o, Random.Range(_dropletUp.x, _dropletUp.y) * s, sa * o);
                _ep.startLifetime = Random.Range(_dropletLife.x, _dropletLife.y);
                _ep.startSize = Random.Range(_dropletSize.x, _dropletSize.y);
                ps.Emit(_ep, 1);
            }
        }
        EnterBursts++;
        if (_debugLog) Debug.Log("[WaterSplashFX] enter k=" + k.ToString("0.00") + " crowns=" + n + " scale=" + scale.ToString("0.00") + " @ " + surfacePos);
    }

    /// <summary>Take-off splash when jumping out of water; k in 0..1 from horizontal speed. Crowns + droplets thrown mostly up.</summary>
    public void JumpOut(Vector3 surfacePos, float k)
    {
        if (_enter == null || !Budget(1f, false)) return;
        k = Mathf.Clamp01(k);
        int n = Mathf.RoundToInt(Mathf.Lerp(_jumpCrowns.x, _jumpCrowns.y, k));
        float scale = Mathf.Lerp(_jumpScaleByK.x, _jumpScaleByK.y, k);
        Radial(Next(_enter, ref _iEnter), surfacePos, n, _stepRadius * 2f, _enterLife, _jumpSize, scale,
               new Vector2(0.05f, 0.25f), new Vector2(0.05f, 0.25f), 1f);
        if (_drips != null)
        {
            int d = Mathf.RoundToInt(Mathf.Lerp(_jumpDroplets.x, _jumpDroplets.y, k));
            float s = Mathf.Lerp(0.8f, 1.1f, k);
            var ps = Next(_drips, ref _iDrips);
            for (int i = 0; i < d; i++)
            {
                float a = Random.value * Mathf.PI * 2f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                _ep = default;
                _ep.position = surfacePos + new Vector3(ca * 0.2f, 0.05f, sa * 0.2f);
                float o = Random.Range(_jumpDropletOut.x, _jumpDropletOut.y);
                _ep.velocity = new Vector3(ca * o, Random.Range(_jumpDropletUp.x, _jumpDropletUp.y) * s, sa * o);
                _ep.startLifetime = Random.Range(_dropletLife.x, _dropletLife.y);
                _ep.startSize = Random.Range(_dropletSize.x, _dropletSize.y);
                ps.Emit(_ep, 1);
            }
        }
        JumpOutBursts++;
        if (_debugLog) Debug.Log("[WaterSplashFX] jump-out k=" + k.ToString("0.00") + " crowns=" + n + " @ " + surfacePos);
    }

    /// <summary>Flat ring on the surface; size = diameter in metres.</summary>
    public void Ripple(Vector3 surfacePos, float size)
    {
        if (_ripple == null || !Budget(1f, true)) return;
        var ps = Next(_ripple, ref _iRipple);
        _ep = default;
        _ep.position = surfacePos + Vector3.up * _rippleLift;
        _ep.velocity = Vector3.zero;
        _ep.startLifetime = _rippleLife;
        _ep.startSize = size;
        _ep.rotation3D = new Vector3(90f, 0f, 0f);
        ps.Emit(_ep, 1);
        RippleBursts++;
        if (_debugLog) Debug.Log("[WaterSplashFX] ripple size=" + size.ToString("0.00") + " @ " + _ep.position + " alive=" + ps.particleCount);
    }

    /// <summary>One falling droplet at pos (already placed on the body by the caller).</summary>
    public void Drip(Vector3 pos)
    {
        if (_drips == null || !Budget(0.5f, false)) return;
        var ps = Next(_drips, ref _iDrips);
        _ep = default;
        _ep.position = pos; _ep.velocity = Vector3.zero;
        _ep.startLifetime = Random.Range(_dripLife.x, _dripLife.y);
        _ep.startSize = Random.Range(_dripSize.x, _dripSize.y);
        ps.Emit(_ep, 1);
        DripEmits++;
    }

    private void Radial(ParticleSystem ps, Vector3 center, int n, float radius, Vector2 life, Vector2 size, float sizeMul,
                        Vector2 outSpeed, Vector2 upSpeed, float speedMul)
    {
        for (int i = 0; i < n; i++)
        {
            float a = Random.value * Mathf.PI * 2f, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            _ep = default;
            float sz = Random.Range(size.x, size.y) * sizeMul;
            _ep.position = center + new Vector3(ca * radius, sz * 0.45f, sa * radius);   // sprite base sits on the surface
            float o = Random.Range(outSpeed.x, outSpeed.y) * speedMul;
            _ep.velocity = new Vector3(ca * o, Random.Range(upSpeed.x, upSpeed.y) * speedMul, sa * o);
            _ep.startLifetime = Random.Range(life.x, life.y);
            _ep.startSize = sz;
            ps.Emit(_ep, 1);
        }
    }
}
