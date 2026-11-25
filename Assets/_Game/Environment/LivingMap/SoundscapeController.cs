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
            SoundManager.Instance.Play(_profile.WaterDrip, vol.RandomSurfacePoint());
        }
    }

    private void UpdateArea()
    {
        var layout = DungeonLayout.Current;
        MultiSectionDungeonGenerator.SectionType type = layout != null
            ? layout.SectionAt(_listener.position, out _)
            : MultiSectionDungeonGenerator.SectionType.Transition;
        SoundDefinition want = _profile.BedFor(type);
        if (want == _current) return;

        if (_debugLog) Debug.Log($"[Soundscape] area {type} → bed {(want != null ? want.name : "none")}", this);
        _currentArea = type.ToString();
        _current = want;

        // Swap: the playing bed becomes the fading one.
        (_a, _b) = (_b, _a);
        _bStartVolume = _duck > 0f ? _b.volume / _duck : _b.volume;
        _a.Stop();
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
        if (seen > 0) SoundManager.Instance.Play(_profile.Drip, pick);
    }
}
