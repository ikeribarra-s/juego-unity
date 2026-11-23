using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime real-time light budget for the procedural dungeon (Forward+).
///
/// The level has well over a hundred torches. Unshadowed point lights shine straight through walls,
/// so a torch in the next room lit floors where no source was visible ("floor lit from nowhere"),
/// and Forward+ silently drops lights past its visible-light cap. This component, every
/// <see cref="_refreshInterval"/> seconds:
///  • gives soft shadows to the <see cref="_maxShadowedLights"/> point/spot lights nearest the camera
///    (within <see cref="_shadowDistance"/>) so walls block their light where the player can see it;
///  • turns every other light's shadows off (shadow-map budget);
///  • disables lights farther than <see cref="_maxLightDistance"/> (fog hides them anyway) and
///    re-enables them when the camera comes back, keeping Forward+ under its light cap.
///
/// It only touches lights that were enabled when it found them; lights disabled by gameplay code
/// (e.g. a generator's status light) stay under that code's control. Put it on any manager object.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Rendering/Dynamic Light Budget")]
public class DynamicLightBudget : MonoBehaviour
{
    [Tooltip("Camera to measure from. Empty = Camera.main.")]
    [SerializeField] private Camera _camera;

    [Header("Shadows")]
    [Tooltip("How many point/spot lights may cast shadows at once (each point light = 6 shadow-map faces).")]
    [SerializeField, Range(0, 8)] private int _maxShadowedLights = 4;
    [Tooltip("Only lights this close to the camera are considered for shadows.")]
    [SerializeField, Min(1f)] private float _shadowDistance = 24f;
    [SerializeField] private LightShadows _shadowType = LightShadows.Soft;

    [Header("Culling")]
    [Tooltip("Lights whose sphere of influence is farther than this from the camera are switched off.")]
    [SerializeField, Min(5f)] private float _maxLightDistance = 45f;

    [Header("Timing")]
    [SerializeField, Min(0.02f)] private float _refreshInterval = 0.2f;
    [Tooltip("How often to look for new lights (thrown torches, regenerated dungeon, spawned enemies).")]
    [SerializeField, Min(0.2f)] private float _rescanInterval = 2f;

    private readonly List<Light> _lights = new();
    private readonly HashSet<Light> _culledByMe = new();
    private readonly List<(float dist, Light light)> _candidates = new();
    private float _nextRefresh;
    private float _nextRescan;

    public int ShadowedCount { get; private set; }
    public int ActiveCount   { get; private set; }

    private void OnEnable()
    {
        _nextRefresh = 0f;
        _nextRescan  = 0f;
    }

    private void OnDisable()
    {
        // Hand culled lights back so nothing stays dark if the budget is turned off.
        foreach (var l in _culledByMe) if (l != null) l.enabled = true;
        _culledByMe.Clear();
    }

    private void LateUpdate()
    {
        float now = Time.unscaledTime;
        if (now >= _nextRescan) { Rescan(); _nextRescan = now + _rescanInterval; }
        if (now >= _nextRefresh) { Refresh(); _nextRefresh = now + _refreshInterval; }
    }

    /// <summary>Force a full rescan + refresh now (e.g. right after regenerating the dungeon).</summary>
    public void RefreshNow()
    {
        Rescan();
        Refresh();
    }

    private void Rescan()
    {
        _lights.Clear();
        var all = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        foreach (var l in all)
        {
            if (l.type != LightType.Point && l.type != LightType.Spot) continue;
            if (!l.enabled && !_culledByMe.Contains(l)) continue; // someone else switched it off
            _lights.Add(l);
        }
        _culledByMe.RemoveWhere(l => l == null);
    }

    private void Refresh()
    {
        Camera cam = _camera != null ? _camera : Camera.main;
        if (cam == null) return;
        Vector3 eye = cam.transform.position;

        _candidates.Clear();
        int active = 0;
        for (int i = 0; i < _lights.Count; i++)
        {
            Light l = _lights[i];
            if (l == null || !l.gameObject.activeInHierarchy) continue;

            // A culled light that some other script disabled meanwhile is no longer ours.
            if (!l.enabled && !_culledByMe.Contains(l)) continue;

            float dist = Vector3.Distance(eye, l.transform.position);
            bool inRange = dist - l.range <= _maxLightDistance;
            if (inRange)
            {
                if (_culledByMe.Remove(l)) l.enabled = true;
                active++;
                if (dist <= _shadowDistance) _candidates.Add((dist, l));
                else if (l.shadows != LightShadows.None) l.shadows = LightShadows.None;
            }
            else if (l.enabled)
            {
                l.enabled = false;
                _culledByMe.Add(l);
            }
        }

        _candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
        int shadowed = 0;
        for (int i = 0; i < _candidates.Count; i++)
        {
            Light l = _candidates[i].light;
            LightShadows want = shadowed < _maxShadowedLights ? _shadowType : LightShadows.None;
            if (want != LightShadows.None) shadowed++;
            if (l.shadows != want) l.shadows = want;
        }

        ShadowedCount = shadowed;
        ActiveCount   = active;
    }
}
