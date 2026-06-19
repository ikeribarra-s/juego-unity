using System;
using System.Collections;
using UnityEngine;

public enum LightingMood
{
    Normal,
    Tense,
    Blackout,
    Chase,
    Extraction
}

[Serializable]
public class LightingMoodProfile
{
    public LightingMood Mood = LightingMood.Normal;

    [Header("World Lighting")]
    public Color AmbientColor = new Color(0.05f, 0.055f, 0.07f);
    public Color FogColor = new Color(0.035f, 0.04f, 0.05f);
    [Range(0f, 0.12f)] public float FogDensity = 0.025f;

    [Header("Main Light")]
    public Color MainLightColor = new Color(0.75f, 0.82f, 1f);
    [Range(0f, 2f)] public float MainLightIntensity = 0.2f;

    [Header("Post FX")]
    [Range(0f, 1f)] public float GrainIntensity = 0.45f;
    [Range(0f, 1f)] public float VignetteIntensity = 0.45f;
    [Range(-100f, 0f)] public float Saturation = -20f;
    [Range(-2f, 0f)] public float PostExposure = -0.25f;
    public Color ColorFilter = new Color(0.82f, 0.88f, 1f);
    [Range(0f, 1f)] public float BloomIntensity = 0.28f;
    [Range(0f, 1f)] public float ChromaticIntensity = 0.18f;
    [Range(1, 32)] public int PixelSize = 1;
}

/// <summary>
/// Centralizes stylized scene lighting for PS2-ish horror moods.
/// Drives ambient/fog, an optional key light, and the runtime HorrorPostFX volume.
/// </summary>
public class LightingMoodController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Light _mainLight;
    [SerializeField] private HorrorPostFX _postFX;

    [Header("Startup")]
    [SerializeField] private LightingMood _startingMood = LightingMood.Normal;
    [SerializeField] private bool _applyOnStart = true;
    [SerializeField, Min(0f)] private float _defaultTransitionDuration = 1.25f;

    [Header("Profiles")]
    [SerializeField] private LightingMoodProfile[] _profiles =
    {
        new LightingMoodProfile
        {
            Mood = LightingMood.Normal,
            AmbientColor = new Color(0.045f, 0.05f, 0.065f),
            FogColor = new Color(0.035f, 0.04f, 0.05f),
            FogDensity = 0.02f,
            MainLightColor = new Color(0.75f, 0.82f, 1f),
            MainLightIntensity = 0.2f,
            GrainIntensity = 0.42f,
            VignetteIntensity = 0.42f,
            Saturation = -18f,
            PostExposure = -0.25f,
            ColorFilter = new Color(0.82f, 0.88f, 1f),
            BloomIntensity = 0.24f,
            ChromaticIntensity = 0.14f,
            PixelSize = 1
        },
        new LightingMoodProfile
        {
            Mood = LightingMood.Tense,
            AmbientColor = new Color(0.035f, 0.04f, 0.05f),
            FogColor = new Color(0.03f, 0.038f, 0.038f),
            FogDensity = 0.035f,
            MainLightColor = new Color(0.62f, 0.78f, 0.65f),
            MainLightIntensity = 0.14f,
            GrainIntensity = 0.58f,
            VignetteIntensity = 0.56f,
            Saturation = -32f,
            PostExposure = -0.45f,
            ColorFilter = new Color(0.72f, 0.86f, 0.72f),
            BloomIntensity = 0.18f,
            ChromaticIntensity = 0.22f,
            PixelSize = 2
        },
        new LightingMoodProfile
        {
            Mood = LightingMood.Blackout,
            AmbientColor = new Color(0.012f, 0.014f, 0.018f),
            FogColor = new Color(0.01f, 0.012f, 0.016f),
            FogDensity = 0.055f,
            MainLightColor = new Color(0.35f, 0.42f, 0.55f),
            MainLightIntensity = 0.04f,
            GrainIntensity = 0.7f,
            VignetteIntensity = 0.72f,
            Saturation = -45f,
            PostExposure = -0.85f,
            ColorFilter = new Color(0.65f, 0.74f, 0.95f),
            BloomIntensity = 0.08f,
            ChromaticIntensity = 0.28f,
            PixelSize = 3
        },
        new LightingMoodProfile
        {
            Mood = LightingMood.Chase,
            AmbientColor = new Color(0.055f, 0.025f, 0.025f),
            FogColor = new Color(0.06f, 0.018f, 0.015f),
            FogDensity = 0.04f,
            MainLightColor = new Color(1f, 0.38f, 0.28f),
            MainLightIntensity = 0.28f,
            GrainIntensity = 0.68f,
            VignetteIntensity = 0.22f,
            Saturation = -12f,
            PostExposure = -0.38f,
            ColorFilter = new Color(1f, 0.62f, 0.55f),
            BloomIntensity = 0.34f,
            ChromaticIntensity = 0.34f,
            PixelSize = 2
        },
        new LightingMoodProfile
        {
            Mood = LightingMood.Extraction,
            AmbientColor = new Color(0.045f, 0.055f, 0.04f),
            FogColor = new Color(0.04f, 0.06f, 0.042f),
            FogDensity = 0.026f,
            MainLightColor = new Color(0.65f, 1f, 0.68f),
            MainLightIntensity = 0.35f,
            GrainIntensity = 0.38f,
            VignetteIntensity = 0.38f,
            Saturation = -10f,
            PostExposure = -0.18f,
            ColorFilter = new Color(0.82f, 1f, 0.78f),
            BloomIntensity = 0.42f,
            ChromaticIntensity = 0.12f,
            PixelSize = 1
        }
    };

    private Coroutine _transitionRoutine;
    private LightingMoodProfile _activeProfile;

    public LightingMood CurrentMood { get; private set; }

    private void Reset()
    {
        _mainLight = FindMainLight();
        _postFX = FindAnyObjectByType<HorrorPostFX>();
    }

    private void Awake()
    {
        if (_mainLight == null)
            _mainLight = FindMainLight();

        if (_postFX == null)
            _postFX = FindAnyObjectByType<HorrorPostFX>();
    }

    private void Start()
    {
        if (_applyOnStart)
            SetMoodImmediate(_startingMood);
    }

    public void SetMood(LightingMood mood)
    {
        SetMood(mood, _defaultTransitionDuration);
    }

    public void SetMood(LightingMood mood, float duration)
    {
        if (!TryGetProfile(mood, out var target))
        {
            Debug.LogWarning($"LightingMoodController: no profile found for mood '{mood}'.", this);
            return;
        }

        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);

        _transitionRoutine = StartCoroutine(TransitionTo(target, Mathf.Max(0f, duration)));
    }

    public void SetMoodImmediate(LightingMood mood)
    {
        if (!TryGetProfile(mood, out var profile))
        {
            Debug.LogWarning($"LightingMoodController: no profile found for mood '{mood}'.", this);
            return;
        }

        if (_transitionRoutine != null)
        {
            StopCoroutine(_transitionRoutine);
            _transitionRoutine = null;
        }

        ApplyProfile(profile);
        CurrentMood = mood;
    }

    private IEnumerator TransitionTo(LightingMoodProfile target, float duration)
    {
        if (duration <= 0f)
        {
            ApplyProfile(target);
            CurrentMood = target.Mood;
            _transitionRoutine = null;
            yield break;
        }

        LightingMoodProfile from = CaptureCurrentProfile();
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            ApplyBlend(from, target, t);
            yield return null;
        }

        ApplyProfile(target);
        CurrentMood = target.Mood;
        _transitionRoutine = null;
    }

    private bool TryGetProfile(LightingMood mood, out LightingMoodProfile profile)
    {
        for (int i = 0; i < _profiles.Length; i++)
        {
            if (_profiles[i] != null && _profiles[i].Mood == mood)
            {
                profile = _profiles[i];
                return true;
            }
        }

        profile = null;
        return false;
    }

    private Light FindMainLight()
    {
        if (RenderSettings.sun != null)
            return RenderSettings.sun;

        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type == LightType.Directional)
                return lights[i];
        }

        return lights.Length > 0 ? lights[0] : null;
    }

    private LightingMoodProfile CaptureCurrentProfile()
    {
        LightingMoodProfile post = _activeProfile ?? new LightingMoodProfile();

        return new LightingMoodProfile
        {
            Mood = CurrentMood,
            AmbientColor = RenderSettings.ambientLight,
            FogColor = RenderSettings.fogColor,
            FogDensity = RenderSettings.fogDensity,
            MainLightColor = _mainLight != null ? _mainLight.color : Color.white,
            MainLightIntensity = _mainLight != null ? _mainLight.intensity : 0f,
            GrainIntensity = post.GrainIntensity,
            VignetteIntensity = post.VignetteIntensity,
            Saturation = post.Saturation,
            PostExposure = post.PostExposure,
            ColorFilter = post.ColorFilter,
            BloomIntensity = post.BloomIntensity,
            ChromaticIntensity = post.ChromaticIntensity,
            PixelSize = post.PixelSize
        };
    }

    private void ApplyBlend(LightingMoodProfile from, LightingMoodProfile to, float t)
    {
        LightingMoodProfile blended = new LightingMoodProfile
        {
            Mood = to.Mood,
            AmbientColor = Color.Lerp(from.AmbientColor, to.AmbientColor, t),
            FogColor = Color.Lerp(from.FogColor, to.FogColor, t),
            FogDensity = Mathf.Lerp(from.FogDensity, to.FogDensity, t),
            MainLightColor = Color.Lerp(from.MainLightColor, to.MainLightColor, t),
            MainLightIntensity = Mathf.Lerp(from.MainLightIntensity, to.MainLightIntensity, t),
            GrainIntensity = Mathf.Lerp(from.GrainIntensity, to.GrainIntensity, t),
            VignetteIntensity = Mathf.Lerp(from.VignetteIntensity, to.VignetteIntensity, t),
            Saturation = Mathf.Lerp(from.Saturation, to.Saturation, t),
            PostExposure = Mathf.Lerp(from.PostExposure, to.PostExposure, t),
            ColorFilter = Color.Lerp(from.ColorFilter, to.ColorFilter, t),
            BloomIntensity = Mathf.Lerp(from.BloomIntensity, to.BloomIntensity, t),
            ChromaticIntensity = Mathf.Lerp(from.ChromaticIntensity, to.ChromaticIntensity, t),
            PixelSize = Mathf.RoundToInt(Mathf.Lerp(from.PixelSize, to.PixelSize, t))
        };

        ApplyProfile(blended);
    }

    private void ApplyProfile(LightingMoodProfile profile)
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = profile.AmbientColor;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = profile.FogColor;
        RenderSettings.fogDensity = profile.FogDensity;

        if (_mainLight != null)
        {
            _mainLight.color = profile.MainLightColor;
            _mainLight.intensity = profile.MainLightIntensity;
        }

        if (_postFX != null)
        {
            _postFX.ApplyLook(
                profile.GrainIntensity,
                profile.VignetteIntensity,
                profile.Saturation,
                profile.PostExposure,
                profile.ColorFilter,
                profile.BloomIntensity,
                profile.ChromaticIntensity,
                profile.PixelSize);
        }

        _activeProfile = CloneProfile(profile);
    }

    private LightingMoodProfile CloneProfile(LightingMoodProfile profile)
    {
        return new LightingMoodProfile
        {
            Mood = profile.Mood,
            AmbientColor = profile.AmbientColor,
            FogColor = profile.FogColor,
            FogDensity = profile.FogDensity,
            MainLightColor = profile.MainLightColor,
            MainLightIntensity = profile.MainLightIntensity,
            GrainIntensity = profile.GrainIntensity,
            VignetteIntensity = profile.VignetteIntensity,
            Saturation = profile.Saturation,
            PostExposure = profile.PostExposure,
            ColorFilter = profile.ColorFilter,
            BloomIntensity = profile.BloomIntensity,
            ChromaticIntensity = profile.ChromaticIntensity,
            PixelSize = profile.PixelSize
        };
    }
}
