using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cheap pixel-art atmosphere particles, all pooled and distance-gated (no lights, no shadows):
///  - dust motes (max 3) + smoke wisp (max 6) at lit, NOT carried wall torches within _torchRange
///    (instances follow the torch, no prefab edits);
///  - drip splash: <see cref="Splash"/> is called by SoundscapeController next to every drip sound;
///  - ceiling dust on a TensionDirector Spike (the listener's AcousticZone, cooldown);
///  - sparks on RUNNING generators within range.
/// Prefabs are built by Fluffterror > Atmosphere > Build Assets. Put it on the SoundManager object.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Rendering/Atmosphere FX")]
public class AtmosphereFX : MonoBehaviour
{
    public static AtmosphereFX Instance { get; private set; }

    [SerializeField] private ParticleSystem _motesPrefab, _smokePrefab, _splashPrefab, _ceilingDustPrefab, _sparksPrefab;
    [SerializeField] private float _torchRange = 15f;
    [SerializeField, Min(0)] private int _maxSmoke = 6, _maxMotes = 3, _maxSplash = 6, _maxSparks = 2;
    [SerializeField] private float _dustCooldown = 25f;
    [SerializeField] private float _splashRange = 25f;
    [Tooltip("Seconds between torch / generator assignment scans.")]
    [SerializeField] private float _scanInterval = 0.5f;

    private Transform _listener;
    private float _nextScan, _lastDust = -1e9f;
    private ParticleSystem[] _splashPool;
    private int _splashNext;
    private ParticleSystem _dust;
    private bool _subscribed;

    private class Slot { public ParticleSystem Ps; public Component Owner; public bool Active; public Transform Anchor; public Vector3 Offset; }
    private readonly List<Slot> _smoke = new(), _motes = new(), _sparks = new();
    private readonly Dictionary<Transform, Transform> _flameCache = new();
    private struct Cand { public Component C; public float D; }
    private readonly List<Cand> _cands = new();

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        // TODO(scene reload / DontDestroyOnLoad): listener, pools and the PhaseChanged subscription are bound once.
        var l = FindAnyObjectByType<AudioListener>();
        if (l != null) _listener = l.transform;
        if (_splashPrefab != null)
        {
            _splashPool = new ParticleSystem[_maxSplash];
            for (int i = 0; i < _maxSplash; i++) _splashPool[i] = Spawn(_splashPrefab);
        }
        if (_ceilingDustPrefab != null) _dust = Spawn(_ceilingDustPrefab);
        Fill(_smoke, _smokePrefab, _maxSmoke);
        Fill(_motes, _motesPrefab, _maxMotes);
        Fill(_sparks, _sparksPrefab, _maxSparks);
        Subscribe();
    }

    private void OnEnable() { Subscribe(); }
    private void OnDisable()
    {
        if (_subscribed && TensionDirector.Instance != null) TensionDirector.Instance.PhaseChanged -= OnPhase;
        _subscribed = false;
    }

    private void Subscribe()
    {
        if (_subscribed || TensionDirector.Instance == null) return;
        TensionDirector.Instance.PhaseChanged += OnPhase;
        _subscribed = true;
    }

    private ParticleSystem Spawn(ParticleSystem prefab)
    {
        var ps = Instantiate(prefab, transform);
        ps.gameObject.SetActive(true);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    private void Fill(List<Slot> list, ParticleSystem prefab, int n)
    {
        if (prefab == null) return;
        for (int i = 0; i < n; i++) list.Add(new Slot { Ps = Spawn(prefab) });
    }

    // one-shots

    public static void Splash(Vector3 pos)
    {
        var fx = Instance;
        if (fx == null || fx._splashPool == null || fx._listener == null || fx._splashPool.Length == 0) return;
        if ((pos - fx._listener.position).sqrMagnitude > fx._splashRange * fx._splashRange) return;
        var ps = fx._splashPool[fx._splashNext++ % fx._splashPool.Length];
        ps.transform.position = pos + Vector3.up * 0.03f;
        ps.Emit(1);
    }

    private void OnPhase(TensionDirector.Phase p)
    {
        if (p != TensionDirector.Phase.Spike || _dust == null || _listener == null) return;
        if (Time.time - _lastDust < _dustCooldown) return;
        // Zone the listener is in; ceiling = zone roof - 1 m (zone boxes are taller than the chunk, roofs have no collider).
        var best = AcousticZone.TryFind(_listener.position, out _);
        if (best == null) return;
        _lastDust = Time.time;
        Vector3 top = best.TopCentre;
        top.y -= 1f;
        _dust.transform.position = new Vector3(_listener.position.x, top.y, _listener.position.z);
        _dust.Play(true);
    }

    // torches / generators

    private void Update()
    {
        Subscribe();
        if (_listener == null) return;
        if (Time.time >= _nextScan)
        {
            _nextScan = Time.time + _scanInterval;
            if (_smoke.Count > 0 || _motes.Count > 0)
            {
                _cands.Clear();
                var all = TorchStateController.All;
                float r2 = _torchRange * _torchRange;
                for (int i = 0; i < all.Count; i++)
                {
                    var t = all[i];
                    if (t == null || !t.IsLit || t.IsCarried) continue;
                    float d = (t.transform.position - _listener.position).sqrMagnitude;
                    if (d <= r2) _cands.Add(new Cand { C = t, D = d });
                }
                _cands.Sort((a, b) => a.D.CompareTo(b.D));
                Assign(_smoke, new Vector3(0f, 0.22f, 0f));
                Assign(_motes, Vector3.zero);
            }
            if (_sparks.Count > 0)
            {
                _cands.Clear();
                float r2 = _torchRange * _torchRange * 1.7f;
                foreach (var g in PowerGenerator.All)
                {
                    if (g == null || !g.IsRunning) continue;
                    float d = (g.transform.position - _listener.position).sqrMagnitude;
                    if (d <= r2) _cands.Add(new Cand { C = g, D = d });
                }
                _cands.Sort((a, b) => a.D.CompareTo(b.D));
                Assign(_sparks, Vector3.up * 1.2f);
            }
        }
        Follow(_smoke); Follow(_motes); Follow(_sparks);
    }

    private void Assign(List<Slot> slots, Vector3 offset)
    {
        int want = Mathf.Min(slots.Count, _cands.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (!s.Active) continue;
            if (s.Owner == null) { Release(s); continue; }
            bool keep = false;
            for (int k = 0; k < want; k++) if (_cands[k].C == s.Owner) { keep = true; break; }
            if (!keep) Release(s);
        }
        for (int k = 0; k < want; k++)
        {
            var c = _cands[k].C;
            bool has = false; Slot free = null;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Active && slots[i].Owner == c) { has = true; break; }
                if (free == null && !slots[i].Active) free = slots[i];
            }
            if (has || free == null) continue;
            free.Owner = c; free.Active = true;
            free.Anchor = AnchorFor(c);
            free.Offset = offset;
            free.Ps.transform.position = free.Anchor.position + offset;
            free.Ps.Play(true);
        }
    }

    private Transform AnchorFor(Component c)
    {
        if (c is not TorchStateController) return c.transform;
        if (_flameCache.TryGetValue(c.transform, out var a) && a != null) return a;
        a = FindDeep(c.transform, "FlameFlipbook") ?? c.transform;
        _flameCache[c.transform] = a;
        return a;
    }

    private static Transform FindDeep(Transform t, string n)
    {
        if (t.name == n) return t;
        for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), n); if (r != null) return r; }
        return null;
    }

    private static void Release(Slot s)
    {
        s.Ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);   // live particles fade out
        s.Owner = null; s.Anchor = null; s.Active = false;
    }

    private static void Follow(List<Slot> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s.Owner == null)                       // Unity-null: owner destroyed (or never set)
            {
                if (s.Active) Release(s);
                continue;
            }
            if (s.Anchor == null) { Release(s); continue; }
            if (s.Owner is TorchStateController t && (t.IsCarried || !t.IsLit)) { Release(s); continue; }
            s.Ps.transform.position = s.Anchor.position + s.Offset;
        }
    }
}
