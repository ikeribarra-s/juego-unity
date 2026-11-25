using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shallow-water trigger (VOL_Water* boxes in the flooded crypt / sewer chunks; top of the box = water surface).
/// Static registry so footsteps and the soundscape can ask "is this point in water?" without physics queries.
/// Added to VOL_Water by ChunkPrefabBuilder.
/// </summary>
public class WaterVolume : MonoBehaviour
{
    public static readonly List<WaterVolume> All = new();

    /// <summary>Feet exactly on the surface (or a step above it while splashing) still count.</summary>
    private const float SurfaceTolerance = 0.12f;

    private Collider _col;

    private void Awake() => _col = GetComponent<Collider>();
    private void OnEnable() { if (_col == null) _col = GetComponent<Collider>(); if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    public Bounds Bounds => _col != null ? _col.bounds : new Bounds(transform.position, Vector3.zero);
    public float SurfaceY => Bounds.max.y;

    public bool Contains(Vector3 feetPos)
    {
        if (_col == null || !isActiveAndEnabled) return false;
        Bounds b = _col.bounds;
        return feetPos.x >= b.min.x && feetPos.x <= b.max.x && feetPos.z >= b.min.z && feetPos.z <= b.max.z
            && feetPos.y >= b.min.y - 0.05f && feetPos.y <= b.max.y + SurfaceTolerance;
    }

    public static WaterVolume TryFind(Vector3 feetPos)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].Contains(feetPos)) return All[i];
        return null;
    }

    /// <summary>Nearest volume whose closest point to <paramref name="pos"/> is within <paramref name="radius"/>.</summary>
    public static WaterVolume Nearest(Vector3 pos, float radius, out Vector3 closest)
    {
        WaterVolume best = null; closest = pos; float bd = radius * radius;
        for (int i = 0; i < All.Count; i++)
        {
            var w = All[i];
            if (w == null || !w.isActiveAndEnabled) continue;
            Vector3 c = w.ClosestSurfacePoint(pos);
            float d = (c - pos).sqrMagnitude;
            if (d <= bd) { bd = d; best = w; closest = c; }
        }
        return best;
    }

    public Vector3 ClosestSurfacePoint(Vector3 pos)
    {
        Bounds b = Bounds;
        Vector3 p = b.ClosestPoint(pos);
        p.y = b.max.y;
        return p;
    }

    public Vector3 RandomSurfacePoint()
    {
        Bounds b = Bounds;
        return new Vector3(Random.Range(b.min.x, b.max.x), b.max.y, Random.Range(b.min.z, b.max.z));
    }
}
