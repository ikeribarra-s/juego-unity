using System;
using UnityEngine;

/// <summary>
/// Weighted list of kit models the dungeon generator dresses a theme with (Living Map S3).
/// Same idea as <c>DungeonGenerator.PropGroup</c>, but a standalone asset so several generators /
/// themes can share it and designers can tune dressing without touching the scene.
/// Assigned in the Inspector — never loaded by path.
/// </summary>
[CreateAssetMenu(fileName = "PP_New", menuName = "Fluffterror/Living Map/Prop Palette")]
public class PropPalette : ScriptableObject
{
    public enum Mount
    {
        Floor,      // free-standing, anywhere on a free floor tile
        AgainstWall,// free-standing furniture pushed against a wall, facing into the room
        Wall,       // hung on a wall face (banner, window, sconce) — WallMount.PushOffWall
        Ceiling,    // hanging from the ceiling/top of a wall corner (cobwebs)
    }

    [Serializable]
    public class Entry
    {
        public GameObject Prefab;
        [Min(0f)] public float Weight = 1f;
        public Mount Placement = Mount.Floor;
        [Tooltip("Rotation applied on top of the facing (degrees). Kit FBX roots already carry their own -90° X; this is extra.")]
        public Vector3 RotationOffset = Vector3.zero;
        [Tooltip("Uniform random scale range.")]
        public Vector2 ScaleRange = Vector2.one;
        [Tooltip("Extra height above the floor (Floor/AgainstWall) or above the default mount height (Wall).")]
        public float YOffset = 0f;
        [Tooltip("Solid props get ONE BoxCollider sized to their bounds and block navigation tiles. " +
                 "Small clutter stays collider-free (walk-through, cheap).")]
        public bool Solid = true;
        [Tooltip("Random yaw in 90° steps (Floor) instead of facing the room.")]
        public bool RandomYaw = true;
        [Tooltip("Shadow casting. Small clutter off = fewer shadow casters.")]
        public bool CastShadows = true;
        [Tooltip("Shown on the run card / used by the director for landmarks (e.g. \"the Fox Statue\").")]
        public string DisplayName = "";
    }

    [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

    public Entry[] Entries => _entries;

#if UNITY_EDITOR
    /// <summary>Editor tooling (LivingMapAssetBuilder) fills palettes from the kits.</summary>
    public void EditorSetEntries(Entry[] entries) => _entries = entries ?? Array.Empty<Entry>();
#endif

    public bool IsValid
    {
        get
        {
            if (_entries == null) return false;
            foreach (var e in _entries) if (e != null && e.Prefab != null && e.Weight > 0f) return true;
            return false;
        }
    }

    /// <summary>Deterministic weighted pick (seeded generator RNG). Optional filter by mount.</summary>
    public Entry Pick(System.Random rng, Mount? only = null, Func<Entry, bool> filter = null)
    {
        if (_entries == null) return null;
        float total = 0f;
        foreach (var e in _entries)
            if (Usable(e, only, filter)) total += e.Weight;
        if (total <= 0f) return null;

        double roll = rng.NextDouble() * total;
        foreach (var e in _entries)
        {
            if (!Usable(e, only, filter)) continue;
            roll -= e.Weight;
            if (roll <= 0) return e;
        }
        // Float round-off: return the last usable one.
        for (int i = _entries.Length - 1; i >= 0; i--)
            if (Usable(_entries[i], only, filter)) return _entries[i];
        return null;
    }

    public bool Has(Mount mount)
    {
        if (_entries == null) return false;
        foreach (var e in _entries) if (Usable(e, mount, null)) return true;
        return false;
    }

    private static bool Usable(Entry e, Mount? only, Func<Entry, bool> filter) =>
        e != null && e.Prefab != null && e.Weight > 0f &&
        (!only.HasValue || e.Placement == only.Value) && (filter == null || filter(e));
}
