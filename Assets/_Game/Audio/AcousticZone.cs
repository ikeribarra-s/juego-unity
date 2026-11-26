using System.Collections.Generic;
using UnityEngine;

/// <summary>Acoustic character of a space. Each value maps to a reverb preset in <see cref="EnvironmentReverb"/>.</summary>
public enum Acoustic { Small, Hall, Cave, Crypt, Sewer, Tower, Open }

/// <summary>
/// Marks a chunk (or any volume) with an acoustic type. No collider: the volume is a local box tested
/// mathematically, so it can never interfere with physics queries, NavMesh baking or interaction rays.
/// <see cref="EnvironmentReverb"/> asks <see cref="TryFind"/> where the listener is and blends toward the preset.
/// Added to the authored chunk prefabs by ChunkPrefabBuilder (by chunk name).
/// </summary>
[DisallowMultipleComponent]
public class AcousticZone : MonoBehaviour
{
    public static readonly List<AcousticZone> All = new();

    [SerializeField] private Acoustic _type = Acoustic.Small;
    [Tooltip("Box in this transform's local space.")]
    [SerializeField] private Vector3 _localCenter = new(0f, 2f, 0f);
    [SerializeField] private Vector3 _size = new(8f, 4f, 8f);

    public Acoustic Type => _type;
    private string _name;
    /// <summary>Cached GameObject name (no per-call allocation).</summary>
    public string ZoneName => _name ??= name;

    /// <summary>Signed distance to the nearest edge: positive inside, negative outside (metres).</summary>
    public float SignedEdge(Vector3 worldPos)
    {
        Vector3 p = transform.InverseTransformPoint(worldPos) - _localCenter;
        Vector3 h = _size * 0.5f; Vector3 s = transform.lossyScale;
        return Mathf.Min((h.x - Mathf.Abs(p.x)) * Mathf.Abs(s.x), (h.z - Mathf.Abs(p.z)) * Mathf.Abs(s.z), h.y - Mathf.Abs(p.y));
    }

    /// <summary>World-space centre of the zone's roof (ceiling effects).</summary>
    public Vector3 TopCentre => transform.TransformPoint(_localCenter + Vector3.up * (_size.y * 0.5f));

    public void Configure(Acoustic type, Vector3 localCenter, Vector3 size)
    {
        _type = type; _localCenter = localCenter; _size = size;
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    /// <summary>Inside test; <paramref name="edgeDistance"/> = metres from the nearest horizontal edge (≥ 0 inside).</summary>
    public bool Contains(Vector3 worldPos, out float edgeDistance)
    {
        Vector3 p = transform.InverseTransformPoint(worldPos) - _localCenter;
        Vector3 h = _size * 0.5f;
        // lossyScale is 1 for chunks; keep the metric in world units anyway
        Vector3 s = transform.lossyScale;
        float dx = (h.x - Mathf.Abs(p.x)) * Mathf.Abs(s.x);
        float dz = (h.z - Mathf.Abs(p.z)) * Mathf.Abs(s.z);
        float dy = h.y - Mathf.Abs(p.y);
        edgeDistance = Mathf.Min(dx, dz);
        return dx >= 0f && dz >= 0f && dy >= 0f;
    }

    /// <summary>Best zone containing the point (the one the point is deepest inside). Null when outside all.</summary>
    public static AcousticZone TryFind(Vector3 worldPos, out float edgeDistance)
    {
        AcousticZone best = null; edgeDistance = 0f;
        for (int i = 0; i < All.Count; i++)
        {
            var z = All[i];
            if (z == null) continue;
            if (z.Contains(worldPos, out float d) && (best == null || d > edgeDistance)) { best = z; edgeDistance = d; }
        }
        return best;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.6f);
        Gizmos.DrawWireCube(_localCenter, _size);
    }
}
