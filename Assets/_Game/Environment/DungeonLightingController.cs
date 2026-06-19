using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Live, designer-tweakable lighting setup for the dungeon. Drives scene ambient + fog and an
/// optional directional "moon" light, applying changes immediately in the editor (and on play) —
/// no need to re-run the generator to see a lighting tweak.
///
/// Put this on any GameObject in the scene. When present, MultiSectionDungeonGenerator skips its
/// own built-in ambient/fog/moon so the two don't fight; the per-section fill lights it places in
/// the world are unaffected.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Dungeon Lighting Controller")]
public class DungeonLightingController : MonoBehaviour
{
    public enum AmbientStyle { FlatColor, Gradient }

    [Header("When to apply")]
    [Tooltip("Push these settings live while editing (not playing).")]
    [SerializeField] private bool _applyInEditMode = true;
    [Tooltip("Push these settings when entering / during play mode.")]
    [SerializeField] private bool _applyInPlayMode = true;

    [Header("Ambient")]
    [SerializeField] private bool _overrideAmbient = true;
    [SerializeField] private AmbientStyle _ambientStyle = AmbientStyle.FlatColor;
    [Tooltip("Master multiplier for the ambient colors below. Raise this if you can't see anything.")]
    [SerializeField, Range(0f, 4f)] private float _ambientIntensity = 1.4f;
    [SerializeField] private Color _ambientColor = new(0.30f, 0.33f, 0.42f);

    [Header("…gradient ambient (when style = Gradient)")]
    [SerializeField] private Color _ambientSky = new(0.34f, 0.40f, 0.55f);
    [SerializeField] private Color _ambientEquator = new(0.24f, 0.26f, 0.32f);
    [SerializeField] private Color _ambientGround = new(0.10f, 0.10f, 0.12f);

    [Header("Fog")]
    [SerializeField] private bool _overrideFog = true;
    [SerializeField] private bool _fogEnabled = true;
    [SerializeField] private FogMode _fogMode = FogMode.ExponentialSquared;
    [SerializeField] private Color _fogColor = new(0.04f, 0.04f, 0.06f);
    [Tooltip("Used by Exponential / ExponentialSquared. Keep low on a big map (~0.01) or you can't see far.")]
    [SerializeField, Range(0f, 0.1f)] private float _fogDensity = 0.012f;
    [Tooltip("Used by Linear fog only.")]
    [SerializeField] private float _fogLinearStart = 10f;
    [SerializeField] private float _fogLinearEnd = 140f;

    [Header("Moonlight (directional)")]
    [SerializeField] private bool _moonlight = true;
    [Tooltip("Optional: assign your own directional light. Left empty, a managed one is created as a child.")]
    [SerializeField] private Light _moonLight;
    [SerializeField] private Color _moonColor = new(0.5f, 0.6f, 0.9f);
    [SerializeField, Range(0f, 3f)] private float _moonIntensity = 0.8f;
    [SerializeField] private Vector3 _moonEuler = new(55f, 35f, 0f);
    [SerializeField] private bool _moonShadows = true;

    private const string MoonName = "Moon (managed)";

    private void OnEnable()
    {
        EnsureMoon();
        Apply();
    }

    // Inspector edits → apply immediately. (No GameObject creation here — that's not allowed in
    // OnValidate; the moon is created in OnEnable / Update instead.)
    private void OnValidate()
    {
        if (isActiveAndEnabled) Apply();
    }

    private void Update()
    {
        // ExecuteAlways: in edit mode keep settings live and recreate the moon if it was deleted.
        if (Application.isPlaying) return;
        if (_moonlight && _moonLight == null) EnsureMoon();
        Apply();
    }

    [ContextMenu("Apply Now")]
    public void Apply()
    {
        if (Application.isPlaying ? !_applyInPlayMode : !_applyInEditMode) return;
        ApplyAmbient();
        ApplyFogSettings();
        ApplyMoon();
    }

    private void ApplyAmbient()
    {
        if (!_overrideAmbient) return;
        if (_ambientStyle == AmbientStyle.FlatColor)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = _ambientColor * _ambientIntensity;
        }
        else
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = _ambientSky * _ambientIntensity;
            RenderSettings.ambientEquatorColor = _ambientEquator * _ambientIntensity;
            RenderSettings.ambientGroundColor = _ambientGround * _ambientIntensity;
        }
        RenderSettings.ambientIntensity = _ambientIntensity; // honored when a skybox is the source
    }

    private void ApplyFogSettings()
    {
        if (!_overrideFog) return;
        RenderSettings.fog = _fogEnabled;
        RenderSettings.fogMode = _fogMode;
        RenderSettings.fogColor = _fogColor;
        RenderSettings.fogDensity = _fogDensity;
        RenderSettings.fogStartDistance = _fogLinearStart;
        RenderSettings.fogEndDistance = _fogLinearEnd;
    }

    private void EnsureMoon()
    {
        if (!_moonlight || _moonLight != null) return;

        Transform existing = transform.Find(MoonName);
        if (existing != null) { _moonLight = existing.GetComponent<Light>(); return; }

        var go = new GameObject(MoonName);
        go.transform.SetParent(transform, false);
        _moonLight = go.AddComponent<Light>();
        _moonLight.type = LightType.Directional;
    }

    private void ApplyMoon()
    {
        if (_moonLight == null) return;
        _moonLight.enabled = _moonlight;
        if (!_moonlight) return;

        _moonLight.type = LightType.Directional;
        _moonLight.color = _moonColor;
        _moonLight.intensity = _moonIntensity;
        _moonLight.shadows = _moonShadows ? LightShadows.Soft : LightShadows.None;

        // Only steer the rotation of the moon we manage; leave a user-assigned light's transform alone.
        if (_moonLight.gameObject.name == MoonName)
            _moonLight.transform.localEulerAngles = _moonEuler;
    }
}
