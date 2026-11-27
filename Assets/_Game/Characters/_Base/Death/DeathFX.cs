using System;
using UnityEngine;

/// <summary>
/// Pooled death effects + sound routing (Docs/Design/DeathFX.md). On GameManager, static <see cref="Instance"/>.
/// Pools are built in Start (3 dust + 3 chips systems, round-robin takeover); nothing is instantiated afterwards and
/// nothing allocates per call (reused <see cref="ParticleSystem.EmitParams"/>, fixed delayed-sound queue).
/// Hooks for later roster flaws are plain C# events: <see cref="BurstHappened"/>, <see cref="SkullRolled"/>.
/// </summary>
[DisallowMultipleComponent]
public class DeathFX : MonoBehaviour
{
    public static DeathFX Instance { get; private set; }

    [SerializeField] private DeathFXProfile _profile;
    [SerializeField] private ParticleSystem _dustPrefab;
    [SerializeField] private ParticleSystem _chipsPrefab;
    [SerializeField] private ParticleSystem _wispPrefab;
    [SerializeField] private int _poolSize = 3;
    [SerializeField] private bool _log;

    public DeathFXProfile Profile => _profile;
    public ParticleSystem WispPrefab => _wispPrefab;

    /// <summary>pos, isLocal. Hook for Lumi (loud, 35 m), Pompon (alarm), Fotchi (dramatic moment).</summary>
    public event Action<Vector3, bool> BurstHappened;
    /// <summary>skull position, roll direction (Coso split hook).</summary>
    public event Action<Vector3, Vector3> SkullRolled;

    // diagnostics (tests)
    public int BurstsPlayed { get; private set; }
    public int BurstsDropped { get; private set; }
    public int DustTakeovers { get; private set; }
    public int ChipsTakeovers { get; private set; }
    /// <summary>Time of the last delayed sound (sting / yip) that actually started.</summary>
    public float LastDelayedPlay { get; private set; }
    public float LastBurstTime { get; private set; }
    public float LastReadyTick { get; private set; }
    public int ReadyTicks { get; private set; }

    private ParticleSystem[] _dust, _chips;
    private int _dustNext, _chipsNext;
    private ParticleSystem.EmitParams _ep;

    private readonly float[] _burstTimes = new float[16];
    private readonly float[] _burstCost = new float[16];
    private int _burstHead;

    private struct Delayed { public float At; public SoundDefinition Def; public Vector3 Pos; public bool TwoD; }
    private readonly Delayed[] _queue = new Delayed[8];
    private int _queued;

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        _dust = BuildPool(_dustPrefab, "Dust");
        _chips = BuildPool(_chipsPrefab, "Chips");
    }

    private ParticleSystem[] BuildPool(ParticleSystem prefab, string label)
    {
        if (prefab == null) return Array.Empty<ParticleSystem>();
        var pool = new ParticleSystem[_poolSize];
        for (int i = 0; i < pool.Length; i++)
        {
            pool[i] = Instantiate(prefab, transform);
            pool[i].name = $"Death{label}_{i}";
            pool[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        return pool;
    }

    // ── API ─────────────────────────────────────────────────────────────────
    /// <summary>A skeleton just burst at <paramref name="pos"/>. Sound always plays; VFX when within budget / range.</summary>
    public void BurstAt(Vector3 pos, DeathFXProfile fx, bool isLocal)
    {
        DeathFXProfile p = fx != null ? fx : _profile;
        if (p == null) return;
        LastBurstTime = Time.time;
        if (p.Collapse != null && SoundManager.Instance != null) SoundManager.Instance.Play(p.Collapse, pos);
        if (isLocal && p.Sting != null) Queue(p.Sting, Vector3.zero, true, p.StingDelay);

        if (TakeBudget(1f, p.MaxBurstsPerSecond) && InRange(pos, p.VfxCullDistance))
        {
            float k = isLocal ? 1f : p.RemoteCountScale;
            EmitDust(pos + new Vector3(0f, 0.4f, 0f), Mathf.Max(1, Mathf.RoundToInt(10 * k)));
            EmitChips(pos + new Vector3(0f, 0.5f, 0f), Mathf.Max(1, Mathf.RoundToInt(8 * k)));
            BurstsPlayed++;
        }
        else BurstsDropped++;
        if (_log) Debug.Log($"[DeathFX] burst t={Time.time:F3} frame={Time.frameCount} local={isLocal} played={BurstsPlayed} dropped={BurstsDropped}");
        BurstHappened?.Invoke(pos, isLocal);
    }

    /// <summary>The skull rolled: roll sound, yip a moment later, wisp puff + eye pulse.</summary>
    public void SkullRoll(Vector3 pos, Vector3 dir, SkullWisp wisp, DeathFXProfile fx)
    {
        DeathFXProfile p = fx != null ? fx : _profile;
        if (p == null) return;
        if (SoundManager.Instance != null && p.SkullRoll != null) SoundManager.Instance.Play(p.SkullRoll, pos);
        if (p.SkullYip != null) Queue(p.SkullYip, pos, false, p.YipDelay);
        if (wisp != null)
        {
            if (TakeBudget(0.5f, p.MaxBurstsPerSecond)) wisp.Burst(p.WispRollBurst);
            wisp.PulseLight();
        }
        if (_log) Debug.Log($"[DeathFX] skull roll t={Time.time:F3}");
        SkullRolled?.Invoke(pos, dir);
    }

    /// <summary>Roll is ready again: 2D tick (caller guarantees the local owner).</summary>
    public void SkullReady(DeathFXProfile fx)
    {
        DeathFXProfile p = fx != null ? fx : _profile;
        LastReadyTick = Time.time; ReadyTicks++;
        if (_log) Debug.Log($"[DeathFX] skull ready tick t={Time.time:F3}");
        if (p != null && p.SkullReady != null && SoundManager.Instance != null) SoundManager.Instance.Play2D(p.SkullReady);
    }

    // ── internals ───────────────────────────────────────────────────────────
    private void Queue(SoundDefinition def, Vector3 pos, bool twoD, float delay)
    {
        if (delay <= 0f) { PlayNow(def, pos, twoD); return; }
        if (_queued >= _queue.Length) return;
        _queue[_queued++] = new Delayed { At = Time.time + delay, Def = def, Pos = pos, TwoD = twoD };
    }

    private void PlayNow(SoundDefinition def, Vector3 pos, bool twoD)
    {
        if (SoundManager.Instance == null) return;
        if (twoD) SoundManager.Instance.Play2D(def); else SoundManager.Instance.Play(def, pos);
        LastDelayedPlay = Time.time;
        if (_log) Debug.Log($"[DeathFX] delayed sound {def.name} t={Time.time:F3}");
    }

    private void Update()
    {
        for (int i = _queued - 1; i >= 0; i--)
        {
            if (Time.time < _queue[i].At) continue;
            PlayNow(_queue[i].Def, _queue[i].Pos, _queue[i].TwoD);
            _queue[i] = _queue[--_queued];
        }
    }

    private bool TakeBudget(float cost, float perSecond)
    {
        float now = Time.time, used = 0f;
        for (int i = 0; i < _burstTimes.Length; i++)
            if (now - _burstTimes[i] < 1f) used += _burstCost[i];
        if (used + cost > perSecond + 0.001f) return false;
        _burstTimes[_burstHead] = now; _burstCost[_burstHead] = cost;
        _burstHead = (_burstHead + 1) % _burstTimes.Length;
        return true;
    }

    private static bool InRange(Vector3 p, float max)
    {
        Camera c = Camera.main;
        return c == null || (c.transform.position - p).sqrMagnitude <= max * max;
    }

    private void EmitDust(Vector3 center, int count)
    {
        if (_dust == null || _dust.Length == 0) return;
        ParticleSystem ps = _dust[_dustNext];
        _dustNext = (_dustNext + 1) % _dust.Length;
        if (ps.particleCount > 0) DustTakeovers++;
        ps.Clear();
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = UnityEngine.Random.onUnitSphere;
            _ep.position = center + dir * (0.3f * UnityEngine.Random.value);
            _ep.velocity = dir * UnityEngine.Random.Range(0.8f, 1.6f);
            _ep.startLifetime = UnityEngine.Random.Range(0.6f, 0.9f);
            _ep.startSize = UnityEngine.Random.Range(0.25f, 0.40f);
            ps.Emit(_ep, 1);
        }
    }

    private void EmitChips(Vector3 center, int count)
    {
        if (_chips == null || _chips.Length == 0) return;
        ParticleSystem ps = _chips[_chipsNext];
        _chipsNext = (_chipsNext + 1) % _chips.Length;
        if (ps.particleCount > 0) ChipsTakeovers++;
        ps.Clear();
        for (int i = 0; i < count; i++)
        {
            Vector2 c = UnityEngine.Random.insideUnitCircle * Mathf.Tan(60f * Mathf.Deg2Rad);   // cone up, 60 deg
            Vector3 dir = new Vector3(c.x, 1f, c.y).normalized;
            _ep.position = center;
            _ep.velocity = dir * UnityEngine.Random.Range(2f, 4f);
            _ep.startLifetime = UnityEngine.Random.Range(0.5f, 0.8f);
            _ep.startSize = UnityEngine.Random.Range(0.03f, 0.06f);
            ps.Emit(_ep, 1);
        }
    }
}
