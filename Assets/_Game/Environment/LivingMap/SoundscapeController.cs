using UnityEngine;

/// <summary>
/// Living Map soundscape (E5 drips + E9 area loops). Plays ONE quiet 2D bed for the area the listener is
/// in (per section theme, from <see cref="DungeonLayout"/>), crossfading on two dedicated AudioSources
/// (≤ 2 beds at once, doc §8), and fires drip one-shots through the <see cref="SoundManager"/> pool from
/// the drip points the generator stored. The existing global <c>AmbientAudio</c> music bed is untouched.
/// Distant sounds are director events (<see cref="TensionDirector"/>). Put it on the SoundManager object.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Audio/Soundscape Controller")]
public class SoundscapeController : MonoBehaviour
{
    [SerializeField] private SoundscapeProfile _profile;
    [Tooltip("Listener transform. Empty = the AudioListener in the scene.")]
    [SerializeField] private Transform _listener;
    [Tooltip("Log area changes.")]
    [SerializeField] private bool _debugLog;
    [Tooltip("Bed volume multiplier while the TensionDirector is in Spike (≈ -4 dB) so the sting/creature cue reads.")]
    [SerializeField, Range(0f, 1f)] private float _spikeDuck = 0.6f;
    [Tooltip("Seconds to duck in / recover.")]
    [SerializeField, Min(0.05f)] private float _duckTime = 1.5f;

    private AudioSource _a, _b;          // _a = current, _b = fading out
    private SoundDefinition _current;
    private float _fade = 1f;            // 0→1 progress of the running crossfade
    private float _nextAreaCheck, _nextDrip;
    private float _duck = 1f;                // current bed multiplier (spike duck)
    private string _currentArea = "";

    // Place beds: hysteresis on AcousticZone changes + flip counter + room stingers.
    private AcousticZone _zone;
    private float _lastSting = -1e9f;
    private readonly System.Collections.Generic.HashSet<string> _stung = new();
    public int AreaFlips { get; private set; }

    public string CurrentArea => _currentArea;
    public SoundDefinition CurrentBed => _current;

    private void Awake()
    {
        _a = MakeSource("Bed A");
        _b = MakeSource("Bed B");
    }

    private AudioSource MakeSource(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
        return s;
    }

    private void Start()
    {
        // TODO(scene reload / DontDestroyOnLoad): the cached listener and zone refs are not re-resolved after a scene change.
        if (_listener == null)
        {
            var l = FindAnyObjectByType<AudioListener>();
            if (l != null) _listener = l.transform;
        }
        _nextDrip = Time.time + 2f;
    }

    private void Update()
    {
        if (_profile == null || _listener == null) return;

        if (Time.time >= _nextAreaCheck)
        {
            _nextAreaCheck = Time.time + _profile.AreaCheckInterval;
            UpdateArea();
        }

        // Spike duck (smoothed), applied every frame together with the crossfade.
        bool spike = TensionDirector.Instance != null && TensionDirector.Instance.CurrentPhase == TensionDirector.Phase.Spike;
        _duck = Mathf.MoveTowards(_duck, spike ? _spikeDuck : 1f, Time.deltaTime / _duckTime);

        if (_fade < 1f) _fade = Mathf.Min(1f, _fade + Time.deltaTime / _profile.CrossfadeTime);
        float target = _current != null ? _current.Volume * _profile.BedVolume : 0f;
        _a.volume = target * _fade * _duck;
        _b.volume = _b.isPlaying ? _bStartVolume * (1f - _fade) * _duck : 0f;
        if (_fade >= 1f && _b.isPlaying) _b.Stop();

        if (Time.time >= _nextDrip) PlayDrip();
        UpdateWater();
    }

    private float _bStartVolume;

    // ── Water (3D loop at the nearest WaterVolume + its own drip timer) ──────
    private AudioSource _water;
    private float _nextWaterDrip;

    private void UpdateWater()
    {
        if (_profile.WaterLoop == null || !_profile.WaterLoop.IsValid) return;
        var vol = WaterVolume.Nearest(_listener.position, _profile.WaterRadius, out Vector3 closest);
        if (_water == null)
        {
            var go = new GameObject("Water Loop");
            go.transform.SetParent(transform, false);
            _water = go.AddComponent<AudioSource>();
            _water.loop = true; _water.playOnAwake = false; _water.spatialBlend = 1f; _water.volume = 0f;
            _water.rolloffMode = AudioRolloffMode.Logarithmic;
            var d = _profile.WaterLoop;
            _water.minDistance = d.MinDistance; _water.maxDistance = d.MaxDistance;
            _water.outputAudioMixerGroup = d.MixerGroup;
            _water.clip = d.GetClip();
        }
        float target = 0f;
        if (vol != null)
        {
            _water.transform.position = closest;
            float dist = Vector3.Distance(_listener.position, closest);
            target = _profile.WaterLoop.Volume * _profile.BedVolume * Mathf.Clamp01(1f - dist / _profile.WaterRadius) * _duck;
            if (!_water.isPlaying) _water.Play();
        }
        _water.volume = Mathf.MoveTowards(_water.volume, target, Time.deltaTime * 0.5f);
        if (vol == null && _water.isPlaying && _water.volume <= 0.001f) _water.Pause();

        if (vol != null && _profile.WaterDrip != null && SoundManager.Instance != null && Time.time >= _nextWaterDrip)
        {
            _nextWaterDrip = Time.time + Random.Range(_profile.WaterDripInterval.x, _profile.WaterDripInterval.y);
            Vector3 sp = vol.RandomSurfacePoint();
            SoundManager.Instance.Play(_profile.WaterDrip, sp);
            AtmosphereFX.Splash(sp);
        }
    }

    private SoundDefinition _bDef;                   // bed currently fading out on _b
    private MultiSectionDungeonGenerator.SectionType _type = MultiSectionDungeonGenerator.SectionType.Transition;
    private AcousticZone _candZone;
    private MultiSectionDungeonGenerator.SectionType _candType;
    private float _candSince, _lastChange = -1e9f;
    private bool _areaInit, _candActive;

    private void UpdateArea()
    {
        var layout = DungeonLayout.Current;
        Vector3 pos = _listener.position;
        var rawType = layout != null ? layout.SectionAt(pos, out _) : MultiSectionDungeonGenerator.SectionType.Transition;
        var rawZone = AcousticZone.TryFind(pos, out float rawEdge);
        // Hold the committed zone while the listener is still within ZoneSwitchEdge of it (zones are the chunk
        // footprints: they touch, so a doorway is "just outside" one and "just inside" the next).
        if (_areaInit && _zone != null && _zone.SignedEdge(pos) > -_profile.ZoneSwitchEdge
            && !(rawZone != null && rawZone != _zone && rawEdge > _profile.ZoneSwitchEdge))
            rawZone = _zone;

        // Every change of (zone, section type) must persist ZoneSwitchTime before it is committed.
        if (!_areaInit) { _areaInit = true; Commit(rawZone, rawType); }
        else if (rawZone == _zone && rawType == _type) _candActive = false;
        else
        {
            if (!_candActive || _candZone != rawZone || _candType != rawType)
            { _candZone = rawZone; _candType = rawType; _candSince = Time.time; _candActive = true; }
            if (Time.time - _candSince >= _profile.ZoneSwitchTime) { Commit(rawZone, rawType); _candActive = false; }
        }

        SoundDefinition want = _profile.ZoneBed(_zone, _type, out _) ?? _profile.BedFor(_type);
        if (want == _current) return;                                              // same bed: no flip, crossfade untouched
        if (Time.time - _lastChange < _profile.CrossfadeTime && _current != null) return;   // let the running crossfade finish
        SwitchBed(want);
    }

    private void Commit(AcousticZone zone, MultiSectionDungeonGenerator.SectionType type)
    {
        bool zoneChanged = zone != _zone;
        _zone = zone; _type = type;
        if (zoneChanged && zone != null) TryStinger(zone);
    }

    private void SwitchBed(SoundDefinition want)
    {
        AreaFlips++;
        _lastChange = Time.time;
        if (_debugLog) Debug.Log($"[Soundscape] area {_type} → bed {(want != null ? want.name : "none")}", this);
        _currentArea = _zone != null ? $"{_type}/{_zone.ZoneName}" : _type.ToString();
        float curTarget = _current != null ? _current.Volume * _profile.BedVolume : 0f;

        if (want != null && want == _bDef && _b.isPlaying)
        {
            // Returning to the bed that is fading out: swap back and continue from where the fade is.
            (_a, _b) = (_b, _a);
            (_current, _bDef) = (want, _current);
            _fade = 1f - _fade;
            _bStartVolume = curTarget;
            return;
        }

        // Keep the louder source as the fading-out one, reuse the quieter one for the new bed.
        if (_a.isPlaying && _b.isPlaying && _b.volume > _a.volume)
        {
            // _b louder than _a: it stays as _b; _a (quieter) is reused. Nothing to swap.
            _bStartVolume = _duck > 0f ? _b.volume / _duck : _b.volume;
        }
        else
        {
            (_a, _b) = (_b, _a);                 // old current becomes the fading one
            _bDef = _current;
            _bStartVolume = _duck > 0f ? _b.volume / _duck : _b.volume;
        }
        if (_a.isPlaying) _a.Stop();
        _current = want;
        if (want != null && want.IsValid)
        {
            _a.clip = want.GetClip();
            _a.pitch = 1f;
            _a.outputAudioMixerGroup = want.MixerGroup;
            _a.volume = 0f;
            // Start mid-loop so two sections sharing a file never sound like a restart.
            _a.time = Random.Range(0f, Mathf.Max(0f, _a.clip.length - 0.1f));
            _a.Play();
        }
        _fade = 0f;
    }

    private void TryStinger(AcousticZone z)
    {
        _profile.ZoneBed(z, _type, out var named);
        if (named == null || named.Sting == null || SoundManager.Instance == null) return;
        if (Time.timeSinceLevelLoad < 3f) return;   // spawn / teleport at load is not an 'entry'
        if (Time.time - _lastSting < _profile.StingCooldown || !_stung.Add(named.NameContains)) return;
        _lastSting = Time.time;
        SoundManager.Instance.Play2D(named.Sting);   // cue: not ducked
        if (_debugLog) Debug.Log($"[Soundscape] sting {named.Sting.name} ({z.ZoneName})", this);
    }

    private void PlayDrip()
    {
        _nextDrip = Time.time + Random.Range(_profile.DripInterval.x, _profile.DripInterval.y);
        var layout = DungeonLayout.Current;
        if (layout == null || _profile.Drip == null || SoundManager.Instance == null) return;
        var pts = layout.DripPoints;
        if (pts.Count == 0) return;

        // Reservoir-pick one point in range (no allocations).
        float rSq = _profile.DripRadius * _profile.DripRadius;
        Vector3 me = _listener.position;
        int seen = 0; Vector3 pick = default;
        for (int i = 0; i < pts.Count; i++)
        {
            if ((pts[i] - me).sqrMagnitude > rSq) continue;
            seen++;
            if (Random.Range(0, seen) == 0) pick = pts[i];
        }
        if (seen > 0) { SoundManager.Instance.Play(_profile.Drip, pick); AtmosphereFX.Splash(pick); }
    }
}
