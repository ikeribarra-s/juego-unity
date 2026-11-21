using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Look-at border for an interactable. Implements <see cref="IHighlightable"/> so
/// <see cref="InteractionSystem"/> can switch it on while the player aims at this object.
///
/// Builds an inverted-hull outline: for every <see cref="MeshFilter"/> under this root it spawns a
/// hidden clone mesh rendered with the <c>Hidden/InteractableOutline</c> shader (back-face hull
/// pushed out along the silhouette). The clones are created once and just enabled/disabled, so the
/// per-frame cost while highlighted is one extra draw per mesh.
///
/// Drop this on the same GameObject as the interactable's collider (e.g. the PowerGenerator root).
/// Works for the authored generator model and the primitive fallback alike — it outlines whatever
/// meshes it finds in the hierarchy.
/// </summary>
[DisallowMultipleComponent]
public class InteractableOutline : MonoBehaviour, IHighlightable
{
    [Tooltip("Border colour (HDR — push above 1 for a glow).")]
    [SerializeField] private Color _color = new(1f, 0.82f, 0.35f, 1f);
    [Tooltip("Border thickness in roughly screen pixels (constant regardless of model scale/distance).")]
    [SerializeField, Range(0f, 30f)] private float _width = 6f;
    [Tooltip("Optional material override. Leave empty to use a runtime Hidden/InteractableOutline material.")]
    [SerializeField] private Material _materialOverride;
    [Tooltip("Build the outline clones in Awake. Off = build lazily the first time it's highlighted.")]
    [SerializeField] private bool _buildOnAwake = true;

    private readonly List<Renderer> _outlines = new();
    private Material _material;
    private bool _built;
    private bool _highlighted;

    private void Awake()
    {
        if (_buildOnAwake) Build();
    }

    public void SetHighlighted(bool on)
    {
        _highlighted = on;
        if (on && !_built) Build();
        for (int i = 0; i < _outlines.Count; i++)
            if (_outlines[i] != null) _outlines[i].enabled = on;
    }

    private void Build()
    {
        if (_built) return;
        _built = true;

        _material = _materialOverride;
        if (_material == null)
        {
            Shader shader = Shader.Find("Hidden/InteractableOutline");
            if (shader == null)
            {
                Debug.LogWarning("[InteractableOutline] Shader 'Hidden/InteractableOutline' not found — " +
                                 "no border will show. Make sure InteractableOutline.shader imported.");
                return;
            }
            _material = new Material(shader) { hideFlags = HideFlags.DontSave };
        }
        _material.SetColor("_OutlineColor", _color);
        _material.SetFloat("_OutlineWidth", _width);

        // Clone every mesh in the hierarchy. (No outline GO exists yet, so nothing to skip.)
        var filters = GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            if (!mf.TryGetComponent<MeshRenderer>(out _)) continue;

            var go = new GameObject("__Outline") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(mf.transform, false);

            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var omr = go.AddComponent<MeshRenderer>();
            omr.sharedMaterial = _material;
            omr.shadowCastingMode = ShadowCastingMode.Off;
            omr.receiveShadows = false;
            omr.lightProbeUsage = LightProbeUsage.Off;
            omr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            omr.enabled = _highlighted;
            _outlines.Add(omr);
        }
    }
}
