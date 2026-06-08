using UnityEngine;

/// Place on the Camera GameObject (same one with the AudioListener).
/// Casts 4 horizontal rays on the Walls layer and smoothly drives
/// AudioReverbFilter so the reverb changes as the player moves through spaces.
[RequireComponent(typeof(AudioReverbFilter))]
public class EnvironmentReverb : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] private LayerMask _wallLayer;
    [SerializeField] private float     _maxRayDistance = 10f;
    [SerializeField] private float     _updateInterval = 0.1f;   // seconds between probes

    [Header("Smoothing")]
    [SerializeField] private float _smoothSpeed = 2.5f;          // lerp speed (per second)

    // AudioReverbFilter value ranges (Unity mB):
    //   room / roomHF        : -10000 … 0
    //   reverbLevel          : -10000 … +2000   ← MUST be positive to be audible
    //   reflectionsLevel     : -10000 … +1000
    //   decayTime            : 0.1 … 20 seconds
    //   diffusion / density  : 0 … 100

    [Header("Open Space  (avg ray > maxDist/2)")]
    [SerializeField] private float _openDecayTime    =  0.3f;
    [SerializeField] private float _openReverb       = -10000f;  // fully dry
    [SerializeField] private float _openReflections  = -10000f;
    [SerializeField] private float _openDiffusion    =  0f;
    [SerializeField] private float _openDensity      =  0f;

    [Header("Large Hall  (avg ray ~ maxDist/2)")]
    [SerializeField] private float _hallDecayTime    =  3.0f;
    [SerializeField] private float _hallReverb       =  1500f;   // very wet tail
    [SerializeField] private float _hallReflections  =  200f;
    [SerializeField] private float _hallDiffusion    =  100f;
    [SerializeField] private float _hallDensity      =  100f;

    [Header("Small Room  (all rays < 2 m)")]
    [SerializeField] private float _smallDecayTime   =  1.5f;
    [SerializeField] private float _smallReverb      =  1000f;   // bathroom-level wet
    [SerializeField] private float _smallReflections = -300f;
    [SerializeField] private float _smallDiffusion   =  100f;
    [SerializeField] private float _smallDensity     =  60f;

    private AudioReverbFilter _filter;
    private float             _probeTimer;

    private float _tDecay, _tReverb, _tReflections, _tDiffusion, _tDensity;

    private static readonly Vector3[] Dirs =
        { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

    private void Awake()
    {
        _filter              = GetComponent<AudioReverbFilter>();
        _filter.reverbPreset = AudioReverbPreset.User;
        SetTargets(_openDecayTime, _openReverb, _openReflections, _openDiffusion, _openDensity);
        ApplyImmediate();
    }

    private void Update()
    {
        _probeTimer += Time.deltaTime;
        if (_probeTimer >= _updateInterval)
        {
            _probeTimer = 0f;
            Probe();
        }

        float dt = Time.deltaTime * _smoothSpeed;
        _filter.decayTime        = Mathf.Lerp(_filter.decayTime,        _tDecay,       dt);
        _filter.reverbLevel      = Mathf.Lerp(_filter.reverbLevel,      _tReverb,      dt);
        _filter.reflectionsLevel = Mathf.Lerp(_filter.reflectionsLevel, _tReflections, dt);
        _filter.diffusion        = Mathf.Lerp(_filter.diffusion,        _tDiffusion,   dt);
        _filter.density          = Mathf.Lerp(_filter.density,          _tDensity,     dt);
    }

    private void Probe()
    {
        Vector3 origin = transform.position;
        float   total  = 0f;

        foreach (var dir in Dirs)
        {
            if (Physics.Raycast(origin, dir, out RaycastHit hit, _maxRayDistance, _wallLayer))
            {
                total += hit.distance;
                Debug.DrawRay(origin, dir * hit.distance, Color.cyan,  _updateInterval);
            }
            else
            {
                total += _maxRayDistance;
                Debug.DrawRay(origin, dir * _maxRayDistance, Color.grey, _updateInterval);
            }
        }

        // t = 0 → fully enclosed, t = 1 → fully open
        float t = Mathf.Clamp01((total / Dirs.Length) / _maxRayDistance);

        // [0..0.5] small room → hall;  [0.5..1] hall → open
        if (t <= 0.5f)
        {
            float b = t * 2f;
            SetTargets(
                Mathf.Lerp(_smallDecayTime,   _hallDecayTime,   b),
                Mathf.Lerp(_smallReverb,      _hallReverb,      b),
                Mathf.Lerp(_smallReflections, _hallReflections, b),
                Mathf.Lerp(_smallDiffusion,   _hallDiffusion,   b),
                Mathf.Lerp(_smallDensity,     _hallDensity,     b));
        }
        else
        {
            float b = (t - 0.5f) * 2f;
            SetTargets(
                Mathf.Lerp(_hallDecayTime,   _openDecayTime,   b),
                Mathf.Lerp(_hallReverb,      _openReverb,      b),
                Mathf.Lerp(_hallReflections, _openReflections, b),
                Mathf.Lerp(_hallDiffusion,   _openDiffusion,   b),
                Mathf.Lerp(_hallDensity,     _openDensity,     b));
        }
    }

    private void SetTargets(float decay, float reverb, float refl, float diff, float dens)
    {
        _tDecay       = decay;
        _tReverb      = reverb;
        _tReflections = refl;
        _tDiffusion   = diff;
        _tDensity     = dens;
    }

    private void ApplyImmediate()
    {
        _filter.decayTime        = _tDecay;
        _filter.reverbLevel      = _tReverb;
        _filter.reflectionsLevel = _tReflections;
        _filter.diffusion        = _tDiffusion;
        _filter.density          = _tDensity;
    }
}
