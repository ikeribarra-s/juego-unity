using UnityEngine;

/// <summary>
/// The skull's afterlife glint (Docs/Design/DeathFX.md 3d): a faint teal wisp particle system (local space, follows the
/// skull; ParticleSystem built once from <see cref="DeathFX.WispPrefab"/>) and a small teal eye light
/// (<see cref="ShadowlessLight"/>). Starts <see cref="Begin"/> seconds after death, fades in, stops beyond the cull
/// distance, <see cref="Burst"/> / <see cref="PulseLight"/> on every roll. Optional hum loop (off by default).
/// Added to every skull by <see cref="SkeletonDisassembler"/>.
/// </summary>
public class SkullWisp : MonoBehaviour
{
    private DeathFXProfile _fx;
    private ParticleSystem _ps;
    private Light _light;
    private AudioSource _hum;
    private float _startAt = float.MaxValue;
    private float _fade;
    private float _emitAcc;
    private float _pulseUntil, _pulseEaseEnd;
    private ParticleSystem.EmitParams _ep;

    public Light EyeLight => _light;
    public ParticleSystem Wisp => _ps;
    public float Fade => _fade;
    public bool Culled { get; private set; }

    public void Init(DeathFXProfile fx, ParticleSystem prefab, float delay)
    {
        _fx = fx;
        _startAt = Time.time + delay;
        if (prefab != null)
        {
            _ps = Instantiate(prefab, transform);
            _ps.name = "SkullWispFX";
            _ps.transform.localPosition = Vector3.zero;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        var go = new GameObject("SkullEyeLight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = fx != null ? fx.EyeLocalPosition : new Vector3(0f, 0.02f, 0.08f);
        _light = go.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = fx != null ? fx.EyeColor : new Color(0.5f, 0.84f, 0.76f);
        _light.range = fx != null ? fx.EyeRange : 1.6f;
        _light.intensity = 0f;
        _light.shadows = LightShadows.None;
        go.AddComponent<ShadowlessLight>();
        _light.enabled = false;

        if (fx != null && fx.SkullHumEnabled && fx.SkullHum != null)
        {
            _hum = gameObject.AddComponent<AudioSource>();
            var d = fx.SkullHum;
            _hum.clip = d.GetClip(); _hum.loop = true; _hum.volume = 0f; _hum.spatialBlend = 1f;
            _hum.minDistance = d.MinDistance; _hum.maxDistance = d.MaxDistance; _hum.outputAudioMixerGroup = d.MixerGroup;
            _hum.playOnAwake = false; _hum.dopplerLevel = 0f;
        }
    }

    /// <summary>Emit extra wisp particles now (roll).</summary>
    public void Burst(int n)
    {
        if (_ps == null || Culled || _fade <= 0f) return;
        for (int i = 0; i < n; i++) EmitOne();
    }

    /// <summary>Eye light x2 for a moment, then ease back.</summary>
    public void PulseLight()
    {
        if (_fx == null) return;
        _pulseUntil = Time.time + _fx.EyePulseHold;
        _pulseEaseEnd = _pulseUntil + _fx.EyePulseEase;
    }

    private void EmitOne()
    {
        _ep.position = Vector3.zero;   // local space
        _ep.velocity = new Vector3(Random.Range(-0.15f, 0.15f), 0.25f, Random.Range(-0.15f, 0.15f));
        _ep.startLifetime = Random.Range(0.9f, 1.3f);
        _ep.startSize = Random.Range(0.08f, 0.12f);
        _ps.Emit(_ep, 1);
    }

    private void Update()
    {
        if (_fx == null || Time.time < _startAt) return;
        float dt = Time.deltaTime;
        _fade = Mathf.MoveTowards(_fade, 1f, _fx.WispFadeIn > 0f ? dt / _fx.WispFadeIn : 1f);

        Camera cam = Camera.main;
        float max = _fx.WispCullDistance;
        Culled = cam != null && (cam.transform.position - transform.position).sqrMagnitude > max * max;

        float mult = 1f;
        if (Time.time < _pulseUntil) mult = _fx.EyePulseMultiplier;
        else if (Time.time < _pulseEaseEnd) mult = Mathf.Lerp(_fx.EyePulseMultiplier, 1f, 1f - (_pulseEaseEnd - Time.time) / _fx.EyePulseEase);
        _light.enabled = !Culled;
        _light.intensity = _fx.EyeIntensity * _fade * mult;

        if (_ps != null && !Culled)
        {
            _emitAcc += _fx.WispRate * _fade * dt;
            while (_emitAcc >= 1f) { _emitAcc -= 1f; EmitOne(); }
        }

        if (_hum != null)
        {
            bool near = !Culled && cam != null && (cam.transform.position - transform.position).sqrMagnitude < 12f * 12f;
            _hum.volume = Mathf.MoveTowards(_hum.volume, near ? _fx.SkullHum.Volume * _fade : 0f, dt);
            if (near && !_hum.isPlaying) _hum.Play(); else if (!near && _hum.volume <= 0f && _hum.isPlaying) _hum.Pause();
        }
    }
}
