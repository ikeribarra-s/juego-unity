using UnityEngine;

/// <summary>
/// Drives per-instance emission on a Fluffterror/BlinnPhong material when the
/// player enters GlowDistance. Uses MaterialPropertyBlock so each item has its
/// own intensity without breaking GPU instancing.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class EvidenceGlow : MonoBehaviour
{
    [Header("Proximity")]
    [SerializeField] private float _glowDistance = 5f;
    [SerializeField] private float _fadeSpeed    = 4f;

    [Header("Glow")]
    [SerializeField] private Color _glowColor     = new Color(1f, 0.65f, 0f, 1f);
    [SerializeField] private float _maxIntensity   = 2.5f;

    [Header("Pulse")]
    [SerializeField] private float _pulseSpeed  = 2f;
    [SerializeField, Range(0f, 0.5f)] private float _pulseAmount = 0.15f;

    private Renderer              _renderer;
    private MaterialPropertyBlock _mpb;
    private EvidencePickup        _pickup;
    private CharacterBase         _player;
    private float                 _currentIntensity;

    private static readonly int EmissionColorID     = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityID = Shader.PropertyToID("_EmissionIntensity");

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb      = new MaterialPropertyBlock();
        _pickup   = GetComponent<EvidencePickup>(); // null if used on non-evidence objects
    }

    private void Start()
    {
        _player = FindAnyObjectByType<CharacterBase>();

        if (_player == null)
            Debug.LogWarning($"[EvidenceGlow] No CharacterBase found in scene — glow won't work.", this);

        if (!_renderer.sharedMaterial.HasProperty(EmissionIntensityID))
            Debug.LogWarning($"[EvidenceGlow] Material '{_renderer.sharedMaterial.name}' has no _EmissionIntensity. Assign a Fluffterror/BlinnPhong material.", this);
    }

    private void Update()
    {
        if (_player == null) return;
        if (_pickup != null && _pickup.State == EvidencePickup.EvidenceState.Destroyed) return;

        float dist            = Vector3.Distance(transform.position, _player.transform.position);
        float normalized      = Mathf.Clamp01(dist / _glowDistance);
        float targetIntensity = Mathf.SmoothStep(1f, 0f, normalized) * _maxIntensity;

        _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity, _fadeSpeed * Time.deltaTime);

        // Gentle pulse scales on top of the proximity-driven base intensity
        float pulse     = 1f + Mathf.Sin(Time.time * _pulseSpeed) * _pulseAmount;
        float intensity = _currentIntensity * pulse;

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(EmissionColorID, _glowColor);
        _mpb.SetFloat(EmissionIntensityID, intensity);
        _renderer.SetPropertyBlock(_mpb);
    }
}
