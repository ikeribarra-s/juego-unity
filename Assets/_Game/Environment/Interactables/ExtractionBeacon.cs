using UnityEngine;

/// <summary>
/// Visual/audio feedback for the extraction zone's generator lock. Place on the same GameObject as
/// <see cref="ExtractionZone"/>.
///
/// While the generators are not all running, a dim red point light smoulders over the zone. When
/// <see cref="MissionManager.GeneratorsCompleted"/> fires it plays <see cref="_unlockSound"/> (2D),
/// spikes film grain via <see cref="HorrorPostFX.PulseGrain"/>, flashes the light and settles it into
/// a slow pulsing "way out" glow. If the objective locks again (finite generator run time) it fades
/// back to the locked look.
///
/// The light is created at runtime if <see cref="_light"/> is left empty, so it needs no scene setup.
/// </summary>
[RequireComponent(typeof(ExtractionZone))]
public class ExtractionBeacon : MonoBehaviour
{
    [Header("Light")]
    [Tooltip("Optional. A point light is created above the zone if empty.")]
    [SerializeField] private Light _light;
    [SerializeField] private float _lightHeight    = 2.5f;
    [SerializeField] private float _lightRange     = 9f;
    [SerializeField] private Color _lockedColor    = new(0.75f, 0.08f, 0.05f);
    [SerializeField] private float _lockedIntensity = 0.6f;
    [SerializeField] private Color _unlockedColor  = new(0.55f, 1f, 0.6f);
    [SerializeField] private float _unlockedIntensity = 3f;
    [Tooltip("Peak intensity of the flash when the zone unlocks.")]
    [SerializeField] private float _flashIntensity = 9f;
    [Tooltip("Seconds for the flash to settle into the unlocked glow.")]
    [SerializeField] private float _flashDecay     = 1.2f;
    [Tooltip("Unlocked glow pulse speed (cycles/sec) and depth (0..1).")]
    [SerializeField] private float _pulseSpeed     = 0.6f;
    [SerializeField, Range(0f, 1f)] private float _pulseDepth = 0.35f;
    [Tooltip("How fast the light blends between locked/unlocked looks.")]
    [SerializeField] private float _blendSpeed     = 2f;

    [Header("Unlock moment")]
    [Tooltip("Played 2D when extraction unlocks. Leave empty for silence.")]
    [SerializeField] private SoundDefinition _unlockSound;
    [SerializeField, Range(0f, 1f)] private float _grainPulse = 1f;
    [SerializeField] private float _grainPulseDuration = 1.5f;

    private float _unlockBlend;   // 0 = locked look, 1 = unlocked look
    private float _flash;         // extra intensity that decays after the unlock
    private bool  _subscribed;

    private void Start()
    {
        if (_light == null) _light = CreateLight();
        Subscribe();

        // Start in the right state (e.g. a mission that doesn't require generators).
        var mm = MissionManager.Instance;
        _unlockBlend = mm != null && mm.GeneratorsComplete ? 1f : 0f;
        Apply();
    }

    private void OnEnable()  { if (_light != null) Subscribe(); }
    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed || MissionManager.Instance == null) return;
        MissionManager.Instance.GeneratorsCompleted += OnUnlocked;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (MissionManager.Instance != null) MissionManager.Instance.GeneratorsCompleted -= OnUnlocked;
        _subscribed = false;
    }

    private void OnUnlocked()
    {
        _flash = _flashIntensity;

        if (_unlockSound != null && _unlockSound.IsValid && SoundManager.Instance != null)
            SoundManager.Instance.Play2D(_unlockSound);

        var fx = FindAnyObjectByType<HorrorPostFX>();
        if (fx != null) fx.PulseGrain(_grainPulse, _grainPulseDuration);
    }

    private void Update()
    {
        var mm = MissionManager.Instance;
        float target = mm != null && mm.GeneratorsComplete ? 1f : 0f;
        _unlockBlend = Mathf.MoveTowards(_unlockBlend, target, _blendSpeed * Time.deltaTime);

        if (_flash > 0f)
            _flash = Mathf.Max(0f, _flash - _flashIntensity / Mathf.Max(0.01f, _flashDecay) * Time.deltaTime);

        Apply();
    }

    private void Apply()
    {
        if (_light == null) return;

        float pulse = 1f - _pulseDepth * 0.5f * (1f + Mathf.Sin(Time.time * _pulseSpeed * Mathf.PI * 2f));
        float unlockedI = _unlockedIntensity * pulse;

        _light.color     = Color.Lerp(_lockedColor, _unlockedColor, _unlockBlend);
        _light.intensity = Mathf.Lerp(_lockedIntensity, unlockedI, _unlockBlend) + _flash;
    }

    private Light CreateLight()
    {
        var go = new GameObject("ExtractionBeaconLight");
        go.transform.SetParent(transform, false);

        // Centre over the trigger volume when there is one, regardless of the zone's pivot.
        Vector3 centre = TryGetComponent(out Collider col) ? col.bounds.center : transform.position;
        go.transform.position = new Vector3(centre.x, transform.position.y + _lightHeight, centre.z);

        var light = go.AddComponent<Light>();
        light.type    = LightType.Point;
        light.range   = _lightRange;
        light.shadows = LightShadows.None;   // cheap: the dungeon already has a tight light budget
        return light;
    }
}
