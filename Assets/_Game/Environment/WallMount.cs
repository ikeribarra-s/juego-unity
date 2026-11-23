using UnityEngine;

/// <summary>
/// Placement helper for wall-mounted props (torches, sconces). Generators place a fixture's pivot on
/// the wall surface; for a lantern whose pivot is its centre that buries half the model — and its
/// flame light — inside the wall, so the wall shadows/occludes it. <see cref="PushOffWall"/> measures
/// the placed prop's mesh bounds along the wall normal and slides it out until its back face sits
/// <c>gap</c> metres in front of the wall.
/// </summary>
public static class WallMount
{
    /// <param name="prop">The placed prop (already rotated/scaled).</param>
    /// <param name="wallSurface">Any world point on the wall face.</param>
    /// <param name="intoRoom">World direction pointing away from the wall into the room.</param>
    /// <param name="gap">Clearance between the wall face and the prop's back, metres.</param>
    public static void PushOffWall(Transform prop, Vector3 wallSurface, Vector3 intoRoom, float gap)
    {
        if (prop == null) return;
        intoRoom.y = 0f;
        PlaceInFrontOf(prop, wallSurface, intoRoom, gap);
    }

    /// <summary>
    /// Move <paramref name="prop"/> along <paramref name="normal"/> so its mesh bounds start exactly
    /// <paramref name="gap"/> in front of the plane through <paramref name="planePoint"/> (e.g. rest a
    /// torch on a pedestal top with normal = up).
    /// </summary>
    public static void PlaceInFrontOf(Transform prop, Vector3 planePoint, Vector3 normal, float gap)
    {
        if (prop == null || normal.sqrMagnitude < 1e-6f) return;
        normal.Normalize();
        if (!TryMinAlong(prop, normal, out float back)) return;
        float plane = Vector3.Dot(planePoint, normal);
        prop.position += normal * (plane + gap - back);
    }

    // Smallest projection of the prop's mesh vertices-bounds corners onto dir (world space).
    private static bool TryMinAlong(Transform prop, Vector3 dir, out float min)
    {
        min = float.PositiveInfinity;
        bool any = false;
        foreach (var mf in prop.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null || !r.enabled) continue;
            Bounds b = mf.sharedMesh.bounds;
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float d = Vector3.Dot(m.MultiplyPoint3x4(c), dir);
                if (d < min) min = d;
                any = true;
            }
        }
        return any;
    }
}
