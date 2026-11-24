using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime stand-in for occlusion culling on the generated map (baked occlusion data can't cover geometry placed
/// by the generator at runtime, and the editor bake is minutes long on the full map). Every
/// <see cref="_interval"/> s it switches the renderers of each authored chunk (children of the generated
/// <c>Chunks</c> root) off when the chunk's bounds are farther than <see cref="_cullDistance"/> from the camera —
/// beyond that the fog has already eaten them. Colliders, lights and gameplay stay untouched. Lights are handled
/// by <see cref="DynamicLightBudget"/>; LODGroups still do the near/far detail swap inside the range.
/// Put it on the GameManager next to the light budget.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Rendering/Chunk Distance Culler")]
public class ChunkDistanceCuller : MonoBehaviour
{
    [Tooltip("Chunks whose bounds are farther than this (m) from the camera stop rendering.")]
    [SerializeField, Min(10f)] private float _cullDistance = 60f;
    [SerializeField, Min(0.05f)] private float _interval = 0.25f;
    [SerializeField] private Camera _camera;

    private class Group { public Bounds Bounds; public Renderer[] Renderers; public bool Visible = true; }
    private readonly List<Group> _groups = new();
    private float _next;
    private Transform _root;

    public int CulledCount { get; private set; }

    private void Start() => Collect();

    /// <summary>Rescan (call after a runtime regeneration).</summary>
    public void Collect()
    {
        _groups.Clear();
        var layout = DungeonLayout.Current != null ? DungeonLayout.Current : FindAnyObjectByType<DungeonLayout>();
        _root = layout != null ? layout.transform : null;
        if (_root == null) return;
        foreach (var ac in _root.GetComponentsInChildren<AuthoredChunk>(true))
        {
            var rs = ac.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            _groups.Add(new Group { Bounds = b, Renderers = rs });
        }
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _next || _groups.Count == 0) return;
        _next = Time.unscaledTime + _interval;
        Camera cam = _camera != null ? _camera : Camera.main;
        if (cam == null) return;
        Vector3 eye = cam.transform.position;
        float maxSq = _cullDistance * _cullDistance;
        int culled = 0;
        foreach (var g in _groups)
        {
            bool visible = g.Bounds.SqrDistance(eye) <= maxSq;
            if (!visible) culled++;
            if (visible == g.Visible) continue;
            g.Visible = visible;
            foreach (var r in g.Renderers) if (r != null) r.forceRenderingOff = !visible;
        }
        CulledCount = culled;
    }

    private void OnDisable()
    {
        foreach (var g in _groups)
            if (!g.Visible) { g.Visible = true; foreach (var r in g.Renderers) if (r != null) r.forceRenderingOff = false; }
    }
}
