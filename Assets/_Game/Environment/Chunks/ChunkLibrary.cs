using System;
using UnityEngine;

/// <summary>
/// The authored chunk set (CH_* prefabs) as data for <see cref="MultiSectionDungeonGenerator"/>: footprint,
/// doors, walkable tile mask and placement tags per chunk. Imported from
/// <c>Assets/Art/Environment/Chunks/chunks_manifest.json</c> by the editor builder
/// (Fluffterror › Living Map › Build Chunk Library) and assigned in the Inspector — runtime code never reads the JSON.
/// Mask chars: <c>#</c> rock/wall, <c>.</c> walkable, <c>o</c> walkable footprint occupied by a solid prop, <c>D</c> door ring.
/// </summary>
[CreateAssetMenu(fileName = "ChunkLibrary", menuName = "Fluffterror/Living Map/Chunk Library")]
public class ChunkLibrary : ScriptableObject
{
    [Serializable]
    public class Door
    {
        public string Label;          // N, S0, E1 …
        public char Side;             // N E S W (unrotated)
        public Vector3 LocalPos;      // socket position in prefab space (floor level, on the footprint edge)
        public Vector3 LocalForward;  // outward
    }

    [Serializable]
    public class Entry
    {
        public string Name;
        public GameObject Prefab;
        [Tooltip("corridor / room / setpiece / objective")]
        public string Role;
        [Tooltip("Footprint in tiles (x = west→east, y = south→north), unrotated.")]
        public Vector2Int Size;
        public float WallHeight = 4f;
        [Tooltip("SectionType names, or Any.")]
        public string[] Themes = Array.Empty<string>();
        public float Weight = 1f;
        public int Min, Max = 1;
        public int TorchesSuggested = 1;
        [Tooltip("Place near this chunk (e.g. the chewed corridor near the den).")]
        public string Near;
        [Tooltip("Call-out name if this chunk is a section landmark (e.g. \"the Bell Tower\").")]
        public string DisplayName;
        [Tooltip("Rows north-up, Size.y rows of Size.x chars.")]
        public string[] Mask = Array.Empty<string>();
        public Door[] Doors = Array.Empty<Door>();

        public bool AllowsTheme(MultiSectionDungeonGenerator.SectionType t)
        {
            foreach (var th in Themes)
                if (th == "Any" || th == t.ToString()) return true;
            return false;
        }

        public bool IsAnyTheme => Array.IndexOf(Themes, "Any") >= 0;

        /// <summary>Mask char at unrotated local tile (lx from west, lz from south).</summary>
        public char MaskAt(int lx, int lz)
        {
            int row = Size.y - 1 - lz;
            if (row < 0 || row >= Mask.Length || lx < 0 || lx >= Mask[row].Length) return '#';
            return Mask[row][lx];
        }
    }

    [SerializeField] private Entry[] _entries = Array.Empty<Entry>();
    public Entry[] Entries => _entries;

    public Entry Find(string name)
    {
        foreach (var e in _entries) if (e != null && e.Name == name) return e;
        return null;
    }

#if UNITY_EDITOR
    public void EditorSetEntries(Entry[] e) => _entries = e ?? Array.Empty<Entry>();
#endif
}
