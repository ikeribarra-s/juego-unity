using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Living Map E1 — a torch that sputters and dies. Lives on the Torch prefab root next to <see cref="Torch"/>.
///
/// States: <b>Lit</b> → <b>Guttering</b> (choppy flame + light for a few seconds, then back to Lit or Out)
/// → <b>Out</b> (flame quad + light GameObject deactivated, embers stop). Out torches relight on a timer
/// (<see cref="Extinguish"/> with a delay) or when a player holding a lit torch presses E on them
/// (<see cref="Torch.Interact"/> routes here).
///
/// Light budget: going Out deactivates the light's GameObject, so <see cref="DynamicLightBudget"/> skips it
/// (it ignores inactive lights and never re-enables lights it didn't switch off) — dying torches lower the
/// Forward+ light count. Guttering scales the light through <see cref="LightFlicker.IntensityScale"/>.
///
/// Driven by <see cref="TensionDirector"/> (gameplay randomness, never the dungeon seed) and by
/// <see cref="BlackoutModifier"/>. Everything is found automatically in Awake; fields are optional overrides.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Environment/Torch State Controller")]
public class TorchStateController : MonoBehaviour
{
    public enum State { Lit, Guttering, Out }

    [Header("Parts (auto-found if empty)")]
    [Tooltip("Object holding the flame quad + the point light. Deactivated when Out. Default: child named 'FlameFlipbook'.")]
    [SerializeField] private Transform _flame;
    [SerializeField] private ParticleSystem[] _embers;

    [Header("Guttering")]
    [Tooltip("Light/flame multiplier range while sputtering.")]
    [SerializeField] private Vector2 _gutterRange = new(0.12f, 0.85f);
    [Tooltip("Seconds between sputter steps (random in range).")]
    [SerializeField] private Vector2 _gutterStep = new(0.05f, 0.16f);
    [SerializeField, Min(0.05f)] private float _fadeOutTime = 0.35f;
    [SerializeField, Min(0.05f)] private float _relightTime = 0.7f;

    [Header("Sound (optional)")]
    [SerializeField] private SoundDefinition _gutterSound;
    [SerializeField] private SoundDefinition _outSound;
    [SerializeField] private SoundDefinition _relightSound;

    private static readonly List<TorchStateController> s_all = new();
    /// <summary>Every enabled torch in the scene (registry — no Find* per frame).</summary>
    public static IReadOnlyList<TorchStateController> All => s_all;

    private Torch _torch;
    private LightFlicker[] _flickers;
    private Light[] _lights;
    private float[] _lightBase;      // used only for lights without a LightFlicker
    private Vector3 _flameScale = Vector3.one;
    private float _scale = 1f;       // current flame/light multiplier
    private float _stepTimer, _stepTarget = 1f;
    private float _stateTimer;       // remaining time in Guttering / fade
    private float _relightAt = -1f;  // Time.time to relight (Out), -1 = stays out
    private bool _dieAfterGutter;
    private float _pendingRelightDelay = -1f;
    private bool _fadingOut, _fadingIn;

    public State Current { get; private set; } = State.Lit;
    public bool IsLit => Current != State.Out;
    public bool IsCarried => _torch != null && _torch.IsCarried;

    private void Awake()
    {
        _torch = GetComponent<Torch>();
        if (_flame == null) _flame = FindDeep(transform, "FlameFlipbook");
        if (_embers == null || _embers.Length == 0) _embers = GetComponentsInChildren<ParticleSystem>(true);
        _flickers = GetComponentsInChildren<LightFlicker>(true);
        _lights = GetComponentsInChildren<Light>(true);
        _lightBase = new float[_lights.Length];
        for (int i = 0; i < _lights.Length; i++) _lightBase[i] = _lights[i].intensity;
        if (_flame != null) _flameScale = _flame.localScale;
    }

    private void OnEnable() => s_all.Add(this);
    private void OnDisable() => s_all.Remove(this);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Sputter for <paramref name="seconds"/>; then die (if <paramref name="thenDie"/>) or recover.</summary>
    public void Gutter(float seconds, bool thenDie = false, float relightAfter = -1f)
    {
        if (Current == State.Out) return;
        Current = State.Guttering;
        _stateTimer = Mathf.Max(0.2f, seconds);
        _dieAfterGutter = thenDie;
        _pendingRelightDelay = relightAfter;
        _fadingIn = false;
        Play(_gutterSound);
    }

    /// <summary>Put the torch out now. <paramref name="relightAfter"/> &gt; 0 relights it after that many seconds.</summary>
    public void Extinguish(float relightAfter = -1f, bool silent = false)
    {
        if (Current == State.Out) return;
        Current = State.Out;
        _fadingOut = !silent;
        _fadingIn = false;
        _stateTimer = _fadeOutTime;
        _relightAt = relightAfter > 0f ? Time.time + relightAfter : -1f;
        foreach (var ps in _embers) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (silent) { _scale = 0f; ApplyScale(); SetFlameActive(false); }
        else Play(_outSound);
    }

    /// <summary>Light it again (fades in with a little sputter).</summary>
    public void Relight()
    {
        if (Current != State.Out) return;
        Current = State.Lit;
        _relightAt = -1f;
        _fadingOut = false;
        _fadingIn = true;
        _stateTimer = _relightTime;
        _scale = 0.05f;
        SetFlameActive(true);
        ApplyScale();
        foreach (var ps in _embers) if (ps != null) ps.Play(true);
        Play(_relightSound);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        switch (Current)
        {
            case State.Guttering: TickGutter(); break;
            case State.Out:       TickOut();    break;
            case State.Lit:
                if (_fadingIn) TickFadeIn();
                break;
        }
    }

    private void TickGutter()
    {
        _stateTimer -= Time.deltaTime;
        _stepTimer -= Time.deltaTime;
        if (_stepTimer <= 0f)
        {
            _stepTimer = Random.Range(_gutterStep.x, _gutterStep.y);
            _stepTarget = Random.Range(_gutterRange.x, _gutterRange.y);
        }
        _scale = Mathf.MoveTowards(_scale, _stepTarget, Time.deltaTime * 12f);
        ApplyScale();

        if (_stateTimer > 0f) return;
        if (_dieAfterGutter)
        {
            Current = State.Lit; // so Extinguish runs
            Extinguish(_pendingRelightDelay);
        }
        else
        {
            Current = State.Lit;
            _fadingIn = true;
            _stateTimer = 0.4f;
        }
    }

    private void TickOut()
    {
        if (_fadingOut)
        {
            _stateTimer -= Time.deltaTime;
            _scale = Mathf.Max(0f, _scale - Time.deltaTime / _fadeOutTime);
            ApplyScale();
            if (_stateTimer <= 0f || _scale <= 0f)
            {
                _fadingOut = false;
                _scale = 0f;
                ApplyScale();
                SetFlameActive(false);
            }
        }
        if (_relightAt > 0f && Time.time >= _relightAt && !IsCarried) Relight();
    }

    private void TickFadeIn()
    {
        // A short wobble up to full brightness.
        _scale = Mathf.MoveTowards(_scale, 1f, Time.deltaTime / Mathf.Max(0.05f, _relightTime));
        float wobble = _scale < 1f ? Random.Range(0.85f, 1.05f) : 1f;
        ApplyScale(wobble);
        if (_scale >= 1f) { _fadingIn = false; ApplyScale(); }
    }

    private void ApplyScale(float wobble = 1f)
    {
        float s = Mathf.Clamp01(_scale * wobble);
        foreach (var f in _flickers) if (f != null) f.IntensityScale = s;
        for (int i = 0; i < _lights.Length; i++)
        {
            if (_lights[i] == null || _lights[i].GetComponent<LightFlicker>() != null) continue;
            _lights[i].intensity = _lightBase[i] * s;
        }
        if (_flame != null) _flame.localScale = _flameScale * Mathf.Lerp(0.25f, 1f, Mathf.Sqrt(s));
    }

    private void SetFlameActive(bool on)
    {
        if (_flame != null && _flame.gameObject.activeSelf != on) _flame.gameObject.SetActive(on);
    }

    private void Play(SoundDefinition def)
    {
        if (def != null && SoundManager.Instance != null) SoundManager.Instance.Play(def, transform.position);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
