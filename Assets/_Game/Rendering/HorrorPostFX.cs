using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Creates and owns a global post-processing Volume at runtime.
/// Drop this component on any persistent GameObject (e.g. GameManager).
/// Requires post-processing to be enabled on the active URP Renderer asset.
/// </summary>
public class HorrorPostFX : MonoBehaviour
{
    [Header("Film Grain")]
    [SerializeField, Range(0f, 1f)] private float _grainIntensity = 0.45f;
    [SerializeField, Range(0f, 1f)] private float _grainResponse  = 0.85f;

    [Header("Vignette")]
    [SerializeField, Range(0f, 1f)] private float _vignetteIntensity  = 0.45f;
    [SerializeField, Range(0f, 1f)] private float _vignetteSmoothness = 0.35f;

    [Header("Color Grading")]
    [Tooltip("Range -100 to 0: negative = desaturated")]
    [SerializeField, Range(-100f, 0f)] private float _saturation   = -20f;
    [Tooltip("EV offset. Negative = darker overall")]
    [SerializeField, Range(-2f, 0f)]   private float _postExposure = -0.25f;
    [SerializeField]                   private Color _colorFilter   = new Color(0.82f, 0.88f, 1f); // cold blue cast

    [Header("Bloom")]
    [SerializeField, Range(0f, 1f)] private float _bloomThreshold = 0.92f;
    [SerializeField, Range(0f, 1f)] private float _bloomIntensity = 0.28f;

    [Header("Chromatic Aberration")]
    [SerializeField, Range(0f, 1f)] private float _chromaticIntensity = 0.18f;

    [Header("Pixelation")]
    [Tooltip("1 = off. Higher = larger pixel blocks (PS1-style look).")]
    [SerializeField, Range(1, 32)] private int _pixelSize = 1;

    private Volume _volume;
    private FilmGrain _grain;
    private Vignette _vignette;
    private ColorAdjustments _colorAdjustments;
    private Bloom _bloom;
    private ChromaticAberration _chromaticAberration;

    // Authored pixel size (scene value at Awake). Mood looks may raise it but never go below it:
    // LightingMoodController's Normal/Extraction moods use 1, which turned the pixel look off.
    private int _basePixelSize = 1;

    public static HorrorPostFX Instance { get; private set; }

    // Death layer (DeathFXLocal): the alive look above, blended toward these by _deathT. Mild variant (DeathFX.md 12.2).
    private float _deathT;
    private float _deadSat = -35f, _deadExp = -0.3f, _deadVig = 0.5f, _deadBloom = 0.2f, _deadChroma = 0.22f, _deadGrain = 0.5f;
    private int _pulsesActive;

    private void Awake()
    {
        Instance = this;
        _basePixelSize = Mathf.Max(1, _pixelSize);
        var go = new GameObject("[HorrorPostFX]");
        go.transform.SetParent(transform);
        _volume          = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 10;
        _volume.profile  = BuildProfile();

        ApplyPixelation();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // Expose for runtime changes (e.g. entering a dream sequence)
    public int PixelSize
    {
        get => _pixelSize;
        set { _pixelSize = Mathf.Clamp(value, 1, 32); ApplyPixelation(); }
    }

    public void ApplyLook(
        float grainIntensity,
        float vignetteIntensity,
        float saturation,
        float postExposure,
        Color colorFilter,
        float bloomIntensity,
        float chromaticIntensity,
        int pixelSize)
    {
        _grainIntensity = Mathf.Clamp01(grainIntensity);
        _vignetteIntensity = Mathf.Clamp01(vignetteIntensity);
        _saturation = Mathf.Clamp(saturation, -100f, 0f);
        _postExposure = Mathf.Clamp(postExposure, -2f, 0f);
        _colorFilter = colorFilter;
        _bloomIntensity = Mathf.Clamp01(bloomIntensity);
        _chromaticIntensity = Mathf.Clamp01(chromaticIntensity);
        PixelSize = Mathf.Max(_basePixelSize, pixelSize);

        ApplyOverrides();
    }

    // Effective look = alive look (moods) blended toward the dead look. Dead never makes the image brighter / less
    // grainy than the alive one (Max / Min), so a mood that is already heavy stays as it is.
    private float EffGrain  => Mathf.Lerp(_grainIntensity, Mathf.Max(_grainIntensity, _deadGrain), _deathT);
    private float EffChroma => Mathf.Lerp(_chromaticIntensity, Mathf.Max(_chromaticIntensity, _deadChroma), _deathT);

    private void ApplyOverrides()
    {
        if (_grain != null) _grain.intensity.Override(EffGrain);
        if (_vignette != null) _vignette.intensity.Override(Mathf.Lerp(_vignetteIntensity, Mathf.Max(_vignetteIntensity, _deadVig), _deathT));
        if (_colorAdjustments != null)
        {
            _colorAdjustments.saturation.Override(Mathf.Lerp(_saturation, Mathf.Min(_saturation, _deadSat), _deathT));
            _colorAdjustments.postExposure.Override(Mathf.Lerp(_postExposure, Mathf.Min(_postExposure, _deadExp), _deathT));
            _colorAdjustments.colorFilter.Override(_colorFilter);
        }
        if (_bloom != null) _bloom.intensity.Override(Mathf.Lerp(_bloomIntensity, Mathf.Min(_bloomIntensity, _deadBloom), _deathT));
        if (_chromaticAberration != null) _chromaticAberration.intensity.Override(EffChroma);
    }

    /// <summary>Dead-look targets (DeathFXLocal passes the DeathFXProfile values).</summary>
    public void SetDeadLook(float saturation, float postExposure, float vignette, float bloom, float chromatic, float grain)
    {
        _deadSat = saturation; _deadExp = postExposure; _deadVig = vignette; _deadBloom = bloom; _deadChroma = chromatic; _deadGrain = grain;
    }

    /// <summary>0 = alive look, 1 = dead look. Safe to call every frame; a running pulse owns the value until it ends.</summary>
    public void SetDeathBlend(float t)
    {
        _deathT = Mathf.Clamp01(t);
        if (_pulsesActive == 0) ApplyOverrides();
    }

    private void ApplyPixelation()
    {
        if (PixelateFeature.Instance != null)
            PixelateFeature.Instance.BlockSize = _pixelSize;
    }

#if UNITY_EDITOR
    private void OnValidate() => ApplyPixelation();
#endif

    private VolumeProfile BuildProfile()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        _grain = profile.Add<FilmGrain>();
        _grain.type.Override(FilmGrainLookup.Thin1);
        _grain.intensity.Override(_grainIntensity);
        _grain.response.Override(_grainResponse);

        _vignette = profile.Add<Vignette>();
        _vignette.color.Override(Color.black);
        _vignette.center.Override(new Vector2(0.5f, 0.5f));
        _vignette.intensity.Override(_vignetteIntensity);
        _vignette.smoothness.Override(_vignetteSmoothness);
        _vignette.rounded.Override(true);

        _colorAdjustments = profile.Add<ColorAdjustments>();
        _colorAdjustments.saturation.Override(_saturation);
        _colorAdjustments.postExposure.Override(_postExposure);
        _colorAdjustments.colorFilter.Override(_colorFilter);

        _bloom = profile.Add<Bloom>();
        _bloom.threshold.Override(_bloomThreshold);
        _bloom.intensity.Override(_bloomIntensity);
        _bloom.scatter.Override(0.7f);

        _chromaticAberration = profile.Add<ChromaticAberration>();
        _chromaticAberration.intensity.Override(_chromaticIntensity);

        return profile;
    }

    // Call this from any system (e.g. creature jump-scare) to spike the effect
    public void PulseGrain(float intensity, float duration)
    {
        StartCoroutine(Pulse(true, intensity, duration));
    }

    /// <summary>Chromatic aberration rises from the current value to <paramref name="peak"/> and back (mirror of PulseGrain).</summary>
    public void PulseChromatic(float peak, float duration)
    {
        StartCoroutine(Pulse(false, peak, duration));
    }

    private System.Collections.IEnumerator Pulse(bool grain, float target, float duration)
    {
        _pulsesActive++;
        float original = grain ? EffGrain : EffChroma;
        float half = Mathf.Max(0.001f, duration * 0.5f);
        for (int phase = 0; phase < 2; phase++)
        {
            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                float k = t / half;
                float v = phase == 0 ? Mathf.Lerp(original, target, k) : Mathf.Lerp(target, original, k);
                if (grain) { if (_grain != null) _grain.intensity.Override(v); }
                else if (_chromaticAberration != null) _chromaticAberration.intensity.Override(v);
                yield return null;
            }
        }
        _pulsesActive--;
        if (_pulsesActive == 0) ApplyOverrides();   // exact final values, including the dead layer
    }
}
