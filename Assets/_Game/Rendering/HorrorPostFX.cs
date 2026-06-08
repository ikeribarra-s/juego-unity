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

    private Volume _volume;

    private void Awake()
    {
        var go = new GameObject("[HorrorPostFX]");
        go.transform.SetParent(transform);
        _volume           = go.AddComponent<Volume>();
        _volume.isGlobal  = true;
        _volume.priority  = 10;
        _volume.profile   = BuildProfile();
    }

    private VolumeProfile BuildProfile()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        var grain = profile.Add<FilmGrain>();
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(_grainIntensity);
        grain.response.Override(_grainResponse);

        var vignette = profile.Add<Vignette>();
        vignette.color.Override(Color.black);
        vignette.center.Override(new Vector2(0.5f, 0.5f));
        vignette.intensity.Override(_vignetteIntensity);
        vignette.smoothness.Override(_vignetteSmoothness);
        vignette.rounded.Override(true);

        var color = profile.Add<ColorAdjustments>();
        color.saturation.Override(_saturation);
        color.postExposure.Override(_postExposure);
        color.colorFilter.Override(_colorFilter);

        var bloom = profile.Add<Bloom>();
        bloom.threshold.Override(_bloomThreshold);
        bloom.intensity.Override(_bloomIntensity);
        bloom.scatter.Override(0.7f);

        var ca = profile.Add<ChromaticAberration>();
        ca.intensity.Override(_chromaticIntensity);

        return profile;
    }

    // Call this from any system (e.g. creature jump-scare) to spike the effect
    public void PulseGrain(float intensity, float duration)
    {
        StartCoroutine(GrainPulse(intensity, duration));
    }

    private System.Collections.IEnumerator GrainPulse(float targetIntensity, float duration)
    {
        if (!_volume.profile.TryGet<FilmGrain>(out var grain)) yield break;

        float original = _grainIntensity;
        float half     = duration * 0.5f;
        float t        = 0f;

        while (t < half)
        {
            grain.intensity.Override(Mathf.Lerp(original, targetIntensity, t / half));
            t += Time.deltaTime;
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            grain.intensity.Override(Mathf.Lerp(targetIntensity, original, t / half));
            t += Time.deltaTime;
            yield return null;
        }

        grain.intensity.Override(original);
    }
}
