using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

/// <summary>
/// Living Map §5.1 — the "ambient director". Keeps a tension value T ∈ [0,1] and a phase
/// (Calm → Build-up → Spike → Relief → Calm), drives <see cref="LightingMoodController"/> moods per phase and
/// spends a per-phase budget of ambient events (<see cref="AmbientEventDefinition"/>): torches that gutter and
/// die, light stutters, distant sounds, creature cues (faked while no creature can honour them), a sting.
///
/// Inputs: time (slow creep), creatures (<see cref="ICreatureTensionSource"/> via <see cref="ReportCreatureAlert"/>),
/// any system via <see cref="AddTension"/> (loud noises, broken things — Phase 2 noise bus), running generators
/// and the open extraction (permanent floors). It nudges, never kills: no event can hurt a player.
///
/// Uses gameplay <see cref="Random"/> on purpose (never the dungeon's seeded RNG). Ticks at
/// <see cref="DirectorProfile.TickRate"/> Hz; registries instead of Find* per tick. Put it on the GameManager.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Environment/Tension Director")]
public class TensionDirector : MonoBehaviour
{
    public enum Phase { Calm, BuildUp, Spike, Relief }

    [SerializeField] private DirectorProfile _profile;
    [Tooltip("Auto-found if empty.")]
    [SerializeField] private LightingMoodController _moods;
    [Tooltip("Blocks line of sight for 'heard, not seen' sound placement. Default: the Walls layer.")]
    [SerializeField] private LayerMask _occluders;

    [Header("Debug")]
    [SerializeField] private bool _debugLog;
    [Tooltip("Small IMGUI readout (phase, T, last event) in the top-right corner.")]
    [SerializeField] private bool _debugOverlay;

    public static TensionDirector Instance { get; private set; }

    public Phase CurrentPhase { get; private set; } = Phase.Calm;
    public float Tension { get; private set; }
    public float Floor => Mathf.Clamp01(_generatorFloor + _extractionFloor + _modifierFloor);
    public string LastEvent { get; private set; } = "-";
    public int EventsFired { get; private set; }
    public DirectorProfile Profile => _profile;

    public event Action<Phase> PhaseChanged;
    public event Action<AmbientEventDefinition> EventFired;

    private readonly List<ICreatureTensionSource> _creatures = new();
    private readonly List<CharacterBase> _players = new();
    private readonly Dictionary<AmbientEventDefinition, float> _cooldownUntil = new();
    private readonly List<AmbientEventDefinition> _candidates = new();
    private readonly List<TorchStateController> _torchPick = new();

    private float _nextTick, _tickDt;
    private float _phaseUntil;
    private float _eventBudget;
    private float _lastEventTime = -999f;
    private float _startTime;
    private float _generatorFloor, _extractionFloor, _modifierFloor;
    private float _nextPlayerScan;
    private bool _unlocked;
    private bool _subscribed;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
        if (_moods == null) _moods = FindAnyObjectByType<LightingMoodController>();
        if (_occluders == 0) _occluders = LayerMask.GetMask("Walls");
    }

    private void OnEnable()
    {
        PowerGenerator.AnyStateChanged += OnGeneratorChanged;
        TrySubscribeMission();
    }

    private void OnDisable()
    {
        PowerGenerator.AnyStateChanged -= OnGeneratorChanged;
        if (_subscribed && MissionManager.Instance != null) MissionManager.Instance.GeneratorsCompleted -= OnUnlocked;
        _subscribed = false;
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        _startTime = Time.time;
        TrySubscribeMission();
        ScanPlayers();
        if (_profile == null) { Debug.LogWarning("[TensionDirector] No DirectorProfile assigned — disabled.", this); enabled = false; }
    }

    private void TrySubscribeMission()
    {
        if (_subscribed || MissionManager.Instance == null) return;
        MissionManager.Instance.GeneratorsCompleted += OnUnlocked;
        _subscribed = true;
    }

    // ── Public API (hooks) ───────────────────────────────────────────────────

    /// <summary>Any system can feed tension (loud noise ≥ 0.6 → +0.1, a break → +0.15 …).</summary>
    public void AddTension(float amount, string reason = null)
    {
        Tension = Mathf.Clamp(Tension + amount, Floor, 1f);
        if (_debugLog && !string.IsNullOrEmpty(reason)) Debug.Log($"[TensionDirector] +{amount:F2} ({reason}) → T={Tension:F2}", this);
    }

    /// <summary>Permanent extra floor (run modifiers, e.g. Blackout starts a little tenser).</summary>
    public void SetModifierFloor(float floor) => _modifierFloor = Mathf.Clamp01(floor);

    public void RegisterCreature(ICreatureTensionSource c) { if (c != null && !_creatures.Contains(c)) _creatures.Add(c); }
    public void UnregisterCreature(ICreatureTensionSource c) => _creatures.Remove(c);

    /// <summary>A creature changed alert level: Investigate +0.15, Point +0.3, Chase → Spike now.</summary>
    public void ReportCreatureAlert(ICreatureTensionSource source, CreatureAlertLevel level)
    {
        if (_profile == null) return;
        switch (level)
        {
            case CreatureAlertLevel.Investigate: AddTension(_profile.CreatureInvestigate, "creature investigates"); break;
            case CreatureAlertLevel.Point:       AddTension(_profile.CreaturePoint, "creature points"); break;
            case CreatureAlertLevel.Chase:
                Tension = 1f;
                if (CurrentPhase != Phase.Spike) EnterPhase(Phase.Spike);
                else _phaseUntil = Time.time + Random.Range(_profile.SpikeDuration.x, _profile.SpikeDuration.y);
                break;
        }
    }

    /// <summary>Debug / scripted moments: jump straight to a phase.</summary>
    [ContextMenu("Force Spike")] public void ForceSpike() { Tension = 1f; EnterPhase(Phase.Spike); }
    [ContextMenu("Force Build-up")] public void ForceBuildUp() { Tension = Mathf.Max(Tension, _profile != null ? _profile.BuildUpAt : 0.35f); EnterPhase(Phase.BuildUp); }
    [ContextMenu("Force Calm")] public void ForceCalm() { Tension = Floor; EnterPhase(Phase.Calm); }
    [ContextMenu("Fire Random Event")] public void FireRandomEventNow() => TryFireEvent(force: true);

    // ── Tick ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (_profile == null) return;
        _tickDt += Time.deltaTime;
        if (Time.time < _nextTick) return;
        _nextTick = Time.time + 1f / Mathf.Max(0.2f, _profile.TickRate);
        float dt = _tickDt; _tickDt = 0f;

        if (Time.time >= _nextPlayerScan) ScanPlayers();
        Tick(dt);
    }

    private void Tick(float dt)
    {
        var p = _profile;
        bool chase = AnyCreatureChasing();

        switch (CurrentPhase)
        {
            case Phase.Calm:
                Tension += (p.CalmRise + Random.Range(0f, p.RiseJitter)) * dt;
                if (chase || Tension >= p.SpikeAt) EnterPhase(Phase.Spike);
                else if (Tension >= p.BuildUpAt) EnterPhase(Phase.BuildUp);
                break;
            case Phase.BuildUp:
                Tension += (p.BuildUpRise + Random.Range(0f, p.RiseJitter)) * dt;
                if (chase || Tension >= p.SpikeAt) EnterPhase(Phase.Spike);
                break;
            case Phase.Spike:
                Tension = 1f;
                if (chase) _phaseUntil = Mathf.Max(_phaseUntil, Time.time + 2f);   // hold while hunted
                if (Time.time >= _phaseUntil) EnterPhase(Phase.Relief);
                break;
            case Phase.Relief:
                float target = Mathf.Max(p.ReliefFloor, Floor);
                float rate = Mathf.Max(0.02f, (Tension - target) / Mathf.Max(1f, _phaseUntil - Time.time));
                Tension = Mathf.MoveTowards(Tension, target, rate * dt);
                if (chase) EnterPhase(Phase.Spike);
                else if (Time.time >= _phaseUntil) { Tension = target; EnterPhase(Phase.Calm); }
                break;
        }

        if (CurrentPhase != Phase.Spike && GroupedUp()) Tension -= p.GroupComfort * dt;
        Tension = Mathf.Clamp(Tension, CurrentPhase == Phase.Relief ? 0f : Floor, 1f);

        // Event budget for this phase.
        float perMinute = CurrentPhase switch
        {
            Phase.Calm => p.EventsPerMinute.x,
            Phase.BuildUp => p.EventsPerMinute.y,
            Phase.Spike => p.EventsPerMinute.z,
            _ => p.EventsPerMinute.w,
        };
        _eventBudget = Mathf.Min(_eventBudget + perMinute / 60f * dt * Random.Range(0.5f, 1.5f), 2f);
        if (_eventBudget >= 1f && Time.time - _startTime >= p.StartGrace && TryFireEvent(false)) _eventBudget -= 1f;
    }

    private void EnterPhase(Phase next)
    {
        if (CurrentPhase == next && next != Phase.Spike) return;
        Phase prev = CurrentPhase;
        CurrentPhase = next;
        var p = _profile;
        switch (next)
        {
            case Phase.Spike:
                _phaseUntil = Time.time + Random.Range(p.SpikeDuration.x, p.SpikeDuration.y);
                SetMood(p.SpikeMood, p.SpikeBlend);
                if (p.SpikeEntryEvent != null) Fire(p.SpikeEntryEvent);
                // The lights react: torches around the players stutter.
                StutterTorches(NearestPlayerPos(), 14f, 1.2f, 2.5f);
                break;
            case Phase.Relief:
                _phaseUntil = Time.time + Random.Range(p.ReliefDuration.x, p.ReliefDuration.y);
                SetMood(_unlocked ? p.UnlockedMood : p.ReliefMood, p.CalmBlend);
                _eventBudget = 0f;
                break;
            case Phase.BuildUp:
                SetMood(p.BuildUpMood, p.BuildUpBlend);
                break;
            default:
                SetMood(_unlocked ? p.UnlockedMood : p.CalmMood, p.CalmBlend);
                break;
        }
        if (_debugLog) Debug.Log($"[TensionDirector] {Time.time - _startTime:F0}s {prev} → {next} (T={Tension:F2})", this);
        PhaseChanged?.Invoke(next);
    }

    private void SetMood(LightingMood mood, float blend)
    {
        if (_moods != null) _moods.SetMood(mood, blend);
    }

    // ── Events ───────────────────────────────────────────────────────────────

    private bool TryFireEvent(bool force)
    {
        if (_profile.Events == null || _profile.Events.Length == 0) return false;
        if (!force && Time.time - _lastEventTime < _profile.MinEventGap) return false;

        _candidates.Clear();
        float total = 0f;
        foreach (var e in _profile.Events)
        {
            if (e == null || e.Weight <= 0f) continue;
            if (!force && !e.AllowedIn(CurrentPhase)) continue;
            if (_cooldownUntil.TryGetValue(e, out float until) && Time.time < until && !force) continue;
            _candidates.Add(e);
            total += e.Weight;
        }
        // Weighted pick; if the chosen one can't run (no torch in range…) try the others.
        while (_candidates.Count > 0)
        {
            float roll = Random.value * total;
            int i = 0;
            for (; i < _candidates.Count - 1; i++) { roll -= _candidates[i].Weight; if (roll <= 0f) break; }
            var pick = _candidates[i];
            if (Fire(pick)) return true;
            total -= pick.Weight;
            _candidates.RemoveAt(i);
        }
        return false;
    }

    private bool Fire(AmbientEventDefinition e)
    {
        if (_players.Count == 0) return false;
        Vector3 origin = NearestPlayerPos();
        bool ok = e.Kind switch
        {
            AmbientEventDefinition.EventKind.TorchGutter  => GutterTorch(e, origin, die: false),
            AmbientEventDefinition.EventKind.TorchDie     => GutterTorch(e, origin, die: true),
            AmbientEventDefinition.EventKind.LightStutter => StutterTorches(origin, e.Radius, e.Duration.x, e.Duration.y) > 0,
            AmbientEventDefinition.EventKind.DistantSound => PlayDistant(e, origin),
            AmbientEventDefinition.EventKind.CreatureCue  => PlayCreatureCue(e, origin),
            AmbientEventDefinition.EventKind.Sting        => PlaySting(e),
            _ => false,
        };
        if (!ok) return false;

        _lastEventTime = Time.time;
        _cooldownUntil[e] = Time.time + e.Cooldown;
        if (e.TensionAdd > 0f && CurrentPhase != Phase.Relief) Tension = Mathf.Min(1f, Tension + e.TensionAdd);
        LastEvent = $"{e.name} @ {Time.time - _startTime:F0}s ({CurrentPhase})";
        EventsFired++;
        if (_debugLog) Debug.Log($"[TensionDirector] {Time.time - _startTime:F0}s event {e.name} ({e.Kind}) in {CurrentPhase}, T={Tension:F2}", this);
        EventFired?.Invoke(e);
        return true;
    }

    private bool GutterTorch(AmbientEventDefinition e, Vector3 origin, bool die)
    {
        _torchPick.Clear();
        float minSq = e.Distance.x * e.Distance.x, maxSq = e.Distance.y * e.Distance.y;
        foreach (var t in TorchStateController.All)
        {
            if (t == null || !t.IsLit || t.IsCarried || t.Current == TorchStateController.State.Guttering) continue;
            float d = (t.transform.position - origin).sqrMagnitude;
            if (d >= minSq && d <= maxSq) _torchPick.Add(t);
        }
        if (_torchPick.Count == 0) return false;
        var torch = _torchPick[Random.Range(0, _torchPick.Count)];
        float relight = e.RelightAfter.y > 0f ? Random.Range(e.RelightAfter.x, e.RelightAfter.y) : -1f;
        torch.Gutter(Random.Range(e.Duration.x, e.Duration.y), die, relight);
        return true;
    }

    private int StutterTorches(Vector3 origin, float radius, float minDur, float maxDur)
    {
        int n = 0;
        float rSq = radius * radius;
        foreach (var t in TorchStateController.All)
        {
            if (t == null || !t.IsLit || t.IsCarried) continue;
            if ((t.transform.position - origin).sqrMagnitude > rSq) continue;
            t.Gutter(Random.Range(minDur, maxDur));
            n++;
        }
        return n;
    }

    private bool PlayDistant(AmbientEventDefinition e, Vector3 origin)
    {
        if (e.Sound == null || SoundManager.Instance == null) return false;
        if (!TryFindSoundSpot(origin, e.Distance, e.PreferOccluded, out Vector3 pos)) return false;
        SoundManager.Instance.Play(e.Sound, pos);
        return true;
    }

    private bool PlayCreatureCue(AmbientEventDefinition e, Vector3 origin)
    {
        // Truthful first: a real creature in range plays its own cue.
        foreach (var c in _creatures)
        {
            if (c == null) continue;
            float d = Vector3.Distance(c.Position, origin);
            if (d <= e.Distance.y * 1.5f && c.TryPlayCue(e.Cue)) return true;
        }
        // Otherwise fake it (allowed — LivingMap §12 Q4).
        return PlayDistant(e, origin);
    }

    private bool PlaySting(AmbientEventDefinition e)
    {
        if (e.Sound == null || SoundManager.Instance == null) return false;
        SoundManager.Instance.Play2D(e.Sound);
        return true;
    }

    /// <summary>A walkable spot in the distance band around <paramref name="origin"/>, preferably out of sight.</summary>
    public bool TryFindSoundSpot(Vector3 origin, Vector2 band, bool preferOccluded, out Vector3 pos)
    {
        Vector3 ear = origin + Vector3.up * 1.6f;
        Vector3 fallback = default; bool haveFallback = false;
        for (int i = 0; i < 12; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            float dist = Random.Range(band.x, band.y);
            Vector3 probe = origin + new Vector3(dir.x, 0f, dir.y) * dist;
            if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                if (!Physics.Raycast(probe + Vector3.up * 3f, Vector3.down, out RaycastHit rh, 8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                hit.position = rh.point;
            }
            Vector3 p = hit.position + Vector3.up * 1f;
            float real = Vector3.Distance(p, ear);
            if (real < band.x * 0.7f || real > band.y * 1.3f) continue;
            bool occluded = Physics.Linecast(ear, p, _occluders, QueryTriggerInteraction.Ignore);
            if (!preferOccluded || occluded) { pos = p; return true; }
            if (!haveFallback) { fallback = p; haveFallback = true; }
        }
        pos = fallback;
        return haveFallback;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void OnGeneratorChanged(PowerGenerator gen)
    {
        if (_profile == null) return;
        int running = 0;
        foreach (var g in PowerGenerator.All) if (g != null && g.IsRunning) running++;
        _generatorFloor = running * _profile.GeneratorFloor;
        Tension = Mathf.Max(Tension, Floor);
    }

    private void OnUnlocked()
    {
        if (_profile == null) return;
        _unlocked = true;
        _extractionFloor = _profile.ExtractionFloor;
        Tension = Mathf.Max(Tension, Floor);
        if (CurrentPhase == Phase.Calm) SetMood(_profile.UnlockedMood, _profile.CalmBlend);
    }

    private bool AnyCreatureChasing()
    {
        foreach (var c in _creatures)
            if (c != null && c.AlertLevel == CreatureAlertLevel.Chase) return true;
        return false;
    }

    private void ScanPlayers()
    {
        _nextPlayerScan = Time.time + 5f;
        _players.Clear();
        foreach (var cb in FindObjectsByType<CharacterBase>(FindObjectsInactive.Exclude))
            if (cb.isActiveAndEnabled) _players.Add(cb);
    }

    private Vector3 NearestPlayerPos()
    {
        // With one local player this is just the player; with more it is the first living one.
        foreach (var pl in _players)
            if (pl != null && (pl.Vitals == null || !pl.Vitals.IsDead)) return pl.transform.position;
        return _players.Count > 0 && _players[0] != null ? _players[0].transform.position : transform.position;
    }

    private bool GroupedUp()
    {
        int alive = 0;
        Vector3 first = default;
        bool grouped = true;
        foreach (var pl in _players)
        {
            if (pl == null || (pl.Vitals != null && pl.Vitals.IsDead)) continue;
            if (alive++ == 0) { first = pl.transform.position; continue; }
            if ((pl.transform.position - first).sqrMagnitude > _profile.GroupRadius * _profile.GroupRadius) grouped = false;
        }
        return alive >= 2 && grouped;
    }

    private GUIStyle _style;
    private void OnGUI()
    {
        if (!_debugOverlay) return;
        _style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperRight };
        GUI.Label(new Rect(Screen.width - 420, 8, 410, 60),
            $"Director {CurrentPhase}  T={Tension:F2} floor={Floor:F2}\nlast: {LastEvent}  (#{EventsFired})", _style);
    }
}
