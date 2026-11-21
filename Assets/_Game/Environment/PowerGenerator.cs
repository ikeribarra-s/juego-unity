using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A repairable machine ("generator") placed in the dungeon. Three of these are scattered across
/// the map by <see cref="MultiSectionDungeonGenerator"/>. Lifecycle:
///   • <b>Broken</b>   — hold E to fill the repair charge; when full it becomes Repaired.
///   • <b>Repaired</b> — press E once to start it running.
///   • <b>Running</b>  — powered on (drives the status light; future: ties into the mission goal).
///
/// Charge bleeds back down while nobody is repairing it, so it has to be finished in one go
/// (set <see cref="_chargeDrain"/> to 0 for a sticky/co-op-friendly charge instead).
///
/// Visuals are optional: assign an <see cref="_indicator"/> Renderer (or name a child "Indicator")
/// and/or a status <see cref="_statusLight"/> and they get recoloured per state.
/// </summary>
[DisallowMultipleComponent]
public class PowerGenerator : MonoBehaviour, IHoldInteractable
{
    public enum GenState { Broken, Repaired, Running }

    [Header("Repair")]
    [Tooltip("Seconds of holding E to fully repair from empty.")]
    [SerializeField] private float _repairTime = 4f;
    [Tooltip("Charge lost per second while NOT being repaired (0 = charge sticks where it is).")]
    [SerializeField] private float _chargeDrain = 0.4f;

    [Header("Run")]
    [Tooltip("Seconds it stays Running before shutting back to Repaired (0 = runs forever).")]
    [SerializeField] private float _runDuration = 0f;

    [Header("Skill checks (DBD-style)")]
    [Tooltip("Average skill checks per second of repair (roll uses chance×deltaTime).")]
    [SerializeField] private float _skillCheckChancePerSecond = 0.45f;
    [Tooltip("Minimum seconds between skill checks (and the delay before the first one).")]
    [SerializeField] private float _skillCheckMinGap = 1.5f;
    [Tooltip("Bonus charge added on a 'great' (red) hit, as a fraction of the bar.")]
    [SerializeField] private float _skillCheckGreatBonus = 0.06f;
    [Tooltip("Charge lost on a missed skill check, as a fraction of the bar (the regression penalty).")]
    [SerializeField] private float _skillCheckMissPenalty = 0.12f;

    [Header("Animation (optional)")]
    [Tooltip("Animator driven by this machine's state. Auto-found in children if left empty. " +
             "Build its controller via Fluffterror > Animation > Build Engine Animator.")]
    [SerializeField] private Animator _animator;
    [Tooltip("Handle revolutions/sec while repairing.")]
    [SerializeField] private float _crankSpinSpeed = 1f;
    [Tooltip("Handle revolutions/sec while running.")]
    [SerializeField] private float _runSpinSpeed = 2f;
    [Tooltip("How fast the handle eases up to / down from its target speed (rev/sec²). " +
             "Lower = smoother spin-up and a longer coast when you let go (no snap-back).")]
    [SerializeField] private float _spinAccel = 2.5f;

    [Header("Audio (optional)")]
    [Tooltip("Looping sound played while the player is hold-repairing.")]
    [SerializeField] private SoundDefinition _repairLoop;
    [Tooltip("Looping sound played while the machine is Running.")]
    [SerializeField] private SoundDefinition _runLoop;
    [Tooltip("One-shot when the repair charge fills.")]
    [SerializeField] private SoundDefinition _repairComplete;
    [Tooltip("One-shot when the machine is switched on.")]
    [SerializeField] private SoundDefinition _engineStart;
    [Tooltip("One-shot on a 'good' skill check hit.")]
    [SerializeField] private SoundDefinition _skillCheckGood;
    [Tooltip("One-shot on a 'great' (red) skill check hit.")]
    [SerializeField] private SoundDefinition _skillCheckGreat;
    [Tooltip("One-shot on a missed skill check (the bang).")]
    [SerializeField] private SoundDefinition _skillCheckMiss;

    [Header("Status visuals (optional)")]
    [Tooltip("Emissive renderer used as the status light. Auto-found from a child named 'Indicator' if left empty.")]
    [SerializeField] private Renderer _indicator;
    [Tooltip("Optional real light recoloured to match the state.")]
    [SerializeField] private Light _statusLight;
    [SerializeField] private Color _brokenColor   = new(0.9f, 0.12f, 0.08f);
    [SerializeField] private Color _repairedColor = new(0.95f, 0.75f, 0.15f);
    [SerializeField] private Color _runningColor  = new(0.2f, 1f, 0.3f);

    // --- runtime state ---
    public GenState State      { get; private set; } = GenState.Broken;
    public float    Charge     => _charge;          // 0..1 repair progress
    public bool     IsRepaired => State != GenState.Broken;
    public bool     IsRunning  => State == GenState.Running;

    /// <summary>Fired when this generator finishes repairing.</summary>
    public event Action<PowerGenerator> RepairCompleted;
    /// <summary>Fired when this generator is switched on.</summary>
    public event Action<PowerGenerator> StartedRunning;

    /// <summary>Every live generator in the scene. Handy for a "all repaired?" win check.</summary>
    public static readonly List<PowerGenerator> All = new();
    /// <summary>Fired (with the generator that changed) whenever any generator's state changes.</summary>
    public static event Action<PowerGenerator> AnyStateChanged;

    public static int RepairedCount
    {
        get { int n = 0; foreach (var g in All) if (g.IsRepaired) n++; return n; }
    }

    private float    _charge;
    private float    _runTimer;
    private float    _lastTickTime = -999f;   // last frame OnHoldTick ran (for drain gating)
    private Material _indicatorMat;            // per-instance so colour changes don't leak

    private bool            _repairing;        // true while the player is actively hold-charging
    private float           _skillCheckCooldown; // time until the next skill check may roll
    private float           _spinSpeed;        // current handle speed multiplier (eased toward target)
    private AudioSource     _loopSource;       // dedicated source for the repair/run loops
    private SoundDefinition _activeLoop;       // which loop def is currently playing (null = silent)

    private static readonly int SpinSpeedHash = Animator.StringToHash(EngineAnimParams.SpinSpeed);

    private void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();

        _loopSource = gameObject.AddComponent<AudioSource>();
        _loopSource.playOnAwake = false;
        _loopSource.loop        = true;
        _loopSource.spatialBlend = 1f;

        if (_indicator == null)
        {
            Transform t = transform.Find("Indicator");
            if (t != null) t.TryGetComponent(out _indicator);
        }
        if (_indicator != null)
        {
            _indicatorMat = _indicator.material;   // instances the material for this generator
            _indicatorMat.EnableKeyword("_EMISSION");
        }
        if (_statusLight == null) _statusLight = GetComponentInChildren<Light>();
    }

    private void OnEnable()
    {
        All.Add(this);
        ApplyVisuals();
    }

    private void OnDisable() => All.Remove(this);

    // ── IInteractable ────────────────────────────────────────────────────────
    public string GetPrompt() => State switch
    {
        GenState.Broken   => _charge > 0f ? $"Repairing… {Mathf.RoundToInt(_charge * 100f)}%"
                                          : "Hold E to repair",
        GenState.Repaired => "Press E to start",
        GenState.Running  => "Running",
        _                 => string.Empty,
    };

    public void Interact(CharacterBase interactor)
    {
        // Single-press path: only meaningful once repaired (turn it on).
        if (State == GenState.Repaired) StartRunning();
    }

    // ── IHoldInteractable ─────────────────────────────────────────────────────
    public bool UseHold(CharacterBase interactor) => State == GenState.Broken;

    public void OnHoldStart(CharacterBase interactor)
    {
        _repairing = true;
        _skillCheckCooldown = _skillCheckMinGap;   // grace period before the first check
    }

    public void OnHoldTick(CharacterBase interactor, float deltaTime)
    {
        if (State != GenState.Broken) return;
        _lastTickTime = Time.time;
        _charge = Mathf.Clamp01(_charge + deltaTime / Mathf.Max(0.01f, _repairTime));

        DriveSkillChecks(deltaTime);

        ApplyVisuals();
        if (_charge >= 1f) CompleteRepair();
    }

    public void OnHoldStop(CharacterBase interactor)
    {
        _repairing = false;
        if (SkillCheckController.Instance != null) SkillCheckController.Instance.Cancel();
    }

    // Feed the repair bar and occasionally pop a DBD-style skill check while repairing.
    private void DriveSkillChecks(float deltaTime)
    {
        SkillCheckController sc = SkillCheckController.Instance;
        if (sc == null) return;

        sc.ReportRepairProgress(_charge);

        _skillCheckCooldown -= deltaTime;
        if (sc.IsBusy || _skillCheckCooldown > 0f || _charge >= 1f) return;

        if (UnityEngine.Random.value < _skillCheckChancePerSecond * deltaTime)
        {
            sc.Begin(OnSkillCheckResult);
            _skillCheckCooldown = _skillCheckMinGap;
        }
    }

    private void OnSkillCheckResult(SkillCheckResult result)
    {
        switch (result)
        {
            case SkillCheckResult.Great:
                _charge = Mathf.Clamp01(_charge + _skillCheckGreatBonus);
                PlayOneShot(_skillCheckGreat);
                break;
            case SkillCheckResult.Good:
                PlayOneShot(_skillCheckGood);
                break;
            case SkillCheckResult.Miss:
                _charge = Mathf.Clamp01(_charge - _skillCheckMissPenalty);
                PlayOneShot(_skillCheckMiss);
                break;
        }
        ApplyVisuals();
        if (State == GenState.Broken && _charge >= 1f) CompleteRepair();   // a great can finish it
    }

    // ── behaviour ─────────────────────────────────────────────────────────────
    private void Update()
    {
        // Drain partial charge once the player stops feeding it (gated off OnHoldTick's timestamp,
        // so component update order can't accidentally drain on the same frame as a repair tick).
        if (State == GenState.Broken && _charge > 0f && _chargeDrain > 0f
            && Time.time - _lastTickTime > 0.05f)
        {
            _charge = Mathf.Clamp01(_charge - _chargeDrain * Time.deltaTime);
            ApplyVisuals();
        }

        if (State == GenState.Running && _runDuration > 0f)
        {
            _runTimer -= Time.deltaTime;
            if (_runTimer <= 0f) SetState(GenState.Repaired);
        }

        UpdateHandleSpin();
        UpdateLoopAudio();
    }

    // Ease the handle's spin speed toward its target so it spins up smoothly and *coasts* to a stop
    // when you let go — the looping spin state just slows to 0 and freezes wherever it is, so the
    // handle never snaps back to its start angle.
    private void UpdateHandleSpin()
    {
        if (_animator == null) return;
        float target = _repairing                ? _crankSpinSpeed
                     : State == GenState.Running  ? _runSpinSpeed
                     : 0f;
        _spinSpeed = Mathf.MoveTowards(_spinSpeed, target, _spinAccel * Time.deltaTime);
        _animator.SetFloat(SpinSpeedHash, _spinSpeed);
    }

    // Drive the looping repair/run sound on the dedicated source; only restarts when the def changes.
    private void UpdateLoopAudio()
    {
        SoundDefinition want = _repairing               ? _repairLoop
                            : State == GenState.Running ? _runLoop
                            : null;
        if (want == _activeLoop) return;
        _activeLoop = want;

        if (want != null && want.IsValid)
        {
            _loopSource.clip                  = want.GetClip();
            _loopSource.volume                = want.Volume;
            _loopSource.pitch                 = want.GetPitch();
            _loopSource.spatialBlend          = want.SpatialBlend;
            _loopSource.outputAudioMixerGroup = want.MixerGroup;
            _loopSource.Play();
        }
        else
        {
            _loopSource.Stop();
        }
    }

    private void PlayOneShot(SoundDefinition def)
    {
        if (def != null && SoundManager.Instance != null)
            SoundManager.Instance.Play(def, transform.position);
    }

    private void CompleteRepair()
    {
        _charge = 1f;
        SetState(GenState.Repaired);
        PlayOneShot(_repairComplete);
        RepairCompleted?.Invoke(this);
    }

    private void StartRunning()
    {
        _runTimer = _runDuration;
        SetState(GenState.Running);
        PlayOneShot(_engineStart);
        StartedRunning?.Invoke(this);
    }

    private void SetState(GenState next)
    {
        if (State == next) return;
        State = next;

        // Cranking stops once we leave Broken; the handle keeps coasting via UpdateHandleSpin.
        if (next != GenState.Broken) _repairing = false;

        ApplyVisuals();
        AnyStateChanged?.Invoke(this);
    }

    private void ApplyVisuals()
    {
        Color c;
        float intensity;
        switch (State)
        {
            case GenState.Broken:
                c = Color.Lerp(_brokenColor, _repairedColor, _charge);
                intensity = 1.5f;
                break;
            case GenState.Repaired:
                c = _repairedColor;
                intensity = 2f;
                break;
            default: // Running — pulse so it reads as "alive"
                c = _runningColor;
                intensity = 2.5f + Mathf.Sin(Time.time * 6f) * 0.8f;
                break;
        }

        if (_indicatorMat != null)
        {
            _indicatorMat.SetColor("_BaseColor", c);
            _indicatorMat.SetColor("_EmissionColor", c * intensity);
        }
        if (_statusLight != null)
        {
            _statusLight.color = c;
            _statusLight.enabled = State != GenState.Broken || _charge > 0.01f;
        }
    }

    private void LateUpdate()
    {
        // Keep the running pulse animating without spamming SetState.
        if (State == GenState.Running) ApplyVisuals();
    }
}

/// <summary>
/// Single source of truth for the engine Animator's parameter names and the animated handle path,
/// shared between <see cref="PowerGenerator"/> (sets the params at runtime) and the editor
/// EngineAnimatorBuilder (declares the params + authors the clips). Keep them in sync here.
/// </summary>
public static class EngineAnimParams
{
    /// <summary>Float — speed multiplier on the single looping spin state. 0 = frozen (handle coasts
    /// to a stop in place, no snap-back); &gt;0 = spinning at that many revolutions/sec.</summary>
    public const string SpinSpeed = "SpinSpeed";

    /// <summary>Animator-relative path of the handle the clips rotate. Name your handle child this.</summary>
    public const string HandlePath = "Handle";
    /// <summary>Transform euler component the handle spins around. Change the axis (x/y/z) to match your model.</summary>
    public const string SpinProperty = "localEulerAnglesRaw.x";
}
