using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Stops a torch carried by the LOCAL player from blowing out the first-person view.
///
/// The torch light sits inside the flame, a few centimetres from the lantern cage and the fist and
/// ~0.4 m from the eye. With inverse-square falloff the cage/hand receive hundreds of times the light
/// a wall does, so they clip to white and bloom over the screen. URP Rendering Layers (Light Layers,
/// enabled in PC_RPAsset) fix that without touching how the world is lit:
///  • while carried, every renderer under the local character (body, arm, the lantern itself) is moved
///    to the <see cref="ViewmodelLayer"/> only;
///  • the held light keeps the Default layer only → it lights the world exactly as before, but not the
///    viewmodel (and the viewmodel doesn't cast shadows from it);
///  • every OTHER light also gets the viewmodel layer (see <see cref="EnsureReachesViewmodel"/>), so the
///    player is still lit by wall torches, the moon, etc.;
///  • a weak, short-range, shadowless "fill" light on the viewmodel layer at the flame gives the cage
///    and hand a warm glow from their own flame.
/// Everything is restored on drop/throw. Remote players (no main camera) are left untouched, so they
/// see the normal torch light on the lantern and the carrier.
///
/// Auto-added next to <see cref="TorchCarrier"/>.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Rendering/Held Torch Lighting")]
public class HeldTorchLighting : MonoBehaviour
{
    /// <summary>Rendering layer bit used for the first-person viewmodel ("Light Layer 1").</summary>
    public const int ViewmodelLayerIndex = 1;
    public static uint ViewmodelLayer => 1u << ViewmodelLayerIndex;
    private const uint DefaultLayer = 1u;

    [Tooltip("Fill light that only reaches the viewmodel (cage + hand), placed at the flame.")]
    [SerializeField] private float _fillIntensity = 0.12f;
    [SerializeField] private float _fillRange = 1.2f;
    [Tooltip("How often to give lights spawned while carrying the viewmodel layer.")]
    [SerializeField, Min(0.1f)] private float _relayerInterval = 1f;

    private static readonly HashSet<Light> s_heldLights = new();

    private TorchCarrier _carrier;
    private Light _heldLight;
    private RenderingLayerMask _heldSavedLayers;
    private Light _fill;
    private readonly List<(Renderer r, uint mask)> _saved = new();
    private float _nextRelayer;

    public bool Active => _heldLight != null;

    private void Awake() => _carrier = GetComponent<TorchCarrier>();

    private void OnEnable()
    {
        if (_carrier == null) _carrier = GetComponent<TorchCarrier>();
        if (_carrier == null) return;
        _carrier.TorchPickedUp += OnPickedUp;
        _carrier.TorchDropped  += OnDropped;
        if (_carrier.Carried != null) OnPickedUp(_carrier.Carried);
    }

    private void OnDisable()
    {
        if (_carrier != null)
        {
            _carrier.TorchPickedUp -= OnPickedUp;
            _carrier.TorchDropped  -= OnDropped;
        }
        Restore();
    }

    private void Update()
    {
        if (!Active || Time.unscaledTime < _nextRelayer) return;
        _nextRelayer = Time.unscaledTime + _relayerInterval;
        RelayerWorldLights();
    }

    /// <summary>True for a torch light currently held by a local player (must NOT reach the viewmodel).</summary>
    public static bool IsHeldLight(Light l) => l != null && s_heldLights.Contains(l);

    /// <summary>Add the viewmodel layer to a world light so the local player's body is still lit by it.</summary>
    public static void EnsureReachesViewmodel(Light l)
    {
        if (l == null || IsHeldLight(l)) return;
        var data = l.GetUniversalAdditionalLightData();
        uint layers = data.renderingLayers;
        if ((layers & ViewmodelLayer) != 0) return;
        data.renderingLayers = layers | ViewmodelLayer;
    }

    private bool IsLocalPlayer()
    {
        Camera main = Camera.main;
        return main != null && main.transform.IsChildOf(transform);
    }

    private void OnPickedUp(Torch torch)
    {
        Restore();
        if (torch == null || !IsLocalPlayer()) return;

        _heldLight = torch.GetComponentInChildren<Light>(includeInactive: true);
        if (_heldLight == null) return;

        // Held light: world only.
        var data = _heldLight.GetUniversalAdditionalLightData();
        _heldSavedLayers = data.renderingLayers;
        data.renderingLayers = DefaultLayer;
        s_heldLights.Add(_heldLight);

        // Viewmodel = everything under the character (body, arm, and the lantern now parented to it).
        _saved.Clear();
        foreach (var r in GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            _saved.Add((r, r.renderingLayerMask));
            r.renderingLayerMask = ViewmodelLayer;
        }

        RelayerWorldLights();
        _nextRelayer = Time.unscaledTime + _relayerInterval;

        // Weak own-flame glow for the cage + hand.
        var go = new GameObject("ViewmodelFill");
        go.transform.SetParent(_heldLight.transform, false);
        _fill = go.AddComponent<Light>();
        _fill.type      = LightType.Point;
        _fill.color     = _heldLight.color;
        _fill.intensity = _fillIntensity;
        _fill.range     = _fillRange;
        _fill.shadows   = LightShadows.None;
        _fill.GetUniversalAdditionalLightData().renderingLayers = ViewmodelLayer;
    }

    private void OnDropped(Torch torch) => Restore();

    private static void RelayerWorldLights()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            EnsureReachesViewmodel(l);
    }

    private void Restore()
    {
        foreach (var (r, mask) in _saved)
            if (r != null) r.renderingLayerMask = mask;
        _saved.Clear();

        if (_heldLight != null)
        {
            _heldLight.GetUniversalAdditionalLightData().renderingLayers = _heldSavedLayers;
            s_heldLights.Remove(_heldLight);
            _heldLight = null;
        }

        if (_fill != null)
        {
            if (Application.isPlaying) Destroy(_fill.gameObject);
            else DestroyImmediate(_fill.gameObject);
            _fill = null;
        }
    }
}
