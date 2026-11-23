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
    [Tooltip("Master multiplier for the ambient colors below. Keep LOW for an enclosed castle (torches " +
             "do the lighting); raise it only if interiors are too dark to navigate.")]
    [SerializeField, Range(0f, 4f)] private float _ambientIntensity = 0.55f;
    [SerializeField] private Color _ambientColor = new(0.20f, 0.22f, 0.28f);

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
    [Tooltip("A dim sky light. Indoors it's occluded by the roof (ceilings cast two-sided shadows); " +
             "keep it dim so it mainly tints the open-sky Graveyard rather than flooding interiors.")]
    [SerializeField] private bool _moonlight = true;
    [Tooltip("Optional: assign your own directional light. Left empty, a managed one is created as a child.")]
    [SerializeField] private Light _moonLight;
    [SerializeField] private Color _moonColor = new(0.45f, 0.55f, 0.85f);
    [SerializeField, Range(0f, 3f)] private float _moonIntensity = 0.25f;
    [SerializeField] private Vector3 _moonEuler = new(55f, 35f, 0f);
    [Tooltip("Must stay ON for the roof to keep the moon out of interiors.")]
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

    // One-click enclosed-castle-at-night look: dark torch-driven interior, faint cool moon for the
    // open Graveyard, close interior fog. Use this on an existing scene instance (changing the C#
    // field defaults does NOT touch values already serialized in the scene). Re-bake/regenerate isn't
    // needed — these are RenderSettings/light tweaks applied live.
    [ContextMenu("Preset: Castle Interior (dark, torch-lit)")]
    public void PresetCastleInterior()
    {
        _overrideAmbient  = true;
        _ambientStyle     = AmbientStyle.FlatColor;
        _ambientColor     = new Color(0.20f, 0.22f, 0.28f);
        _ambientIntensity = 0.55f;

        _overrideFog = true;
        _fogEnabled  = true;
        _fogMode     = FogMode.ExponentialSquared;
        _fogColor    = new Color(0.03f, 0.03f, 0.045f);
        _fogDensity  = 0.02f;

        _moonlight   = true;
        _moonColor   = new Color(0.45f, 0.55f, 0.85f);
        _moonIntensity = 0.25f;
        _moonShadows = true;

        EnsureMoon();
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

        // Reuse the scene's directional light instead of adding a second one. URP only shadows the
        // MAIN directional light; any extra directional is an unshadowed additional light that shines
        // through every roof and lights all interior floors uniformly ("light from nowhere").
        Light sceneDirectional = FindSceneDirectional();
        if (sceneDirectional != null) { _moonLight = sceneDirectional; return; }

        var go = new GameObject(MoonName);
        go.transform.SetParent(transform, false);
        _moonLight = go.AddComponent<Light>();
        _moonLight.type = LightType.Directional;
    }

    private static Light FindSceneDirectional()
    {
        if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional) return RenderSettings.sun;
        foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (l.type == LightType.Directional) return l;
        return null;
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
