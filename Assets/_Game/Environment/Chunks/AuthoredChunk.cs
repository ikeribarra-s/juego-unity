using System.Collections.Generic;
using UnityEngine;

// Root component of an authored map chunk prefab (CH_*, built from ArtSource/Environment/Chunks by
// Editor/ChunkPrefabBuilder). Holds the chunk's grid footprint / wall height and adapts the chunk to the
// section theme it is placed in:
//   - Collar: a per-door slab above the chunk's own wall height, scaled so a taller generator corridor
//     (Gothic/Transition 5 m, Hell 6.5 m, Cavern 7.5 m) is closed above the door lintel.
//   - Roof:   an upward-facing cap over the footprint for open-sky themes (Graveyard).
// Sockets (SOCKET_*) are plain child transforms; +Z = facing.
[DisallowMultipleComponent]
public class AuthoredChunk : MonoBehaviour
{
    [Tooltip("Footprint in 1 m tiles (X = east-west, Y = north-south), unrotated.")]
    [SerializeField] private Vector2Int _footprintTiles = new Vector2Int(8, 8);
    [Tooltip("Height of the chunk's own walls/ceiling (m).")]
    [SerializeField] private float _wallHeight = 4f;
    [SerializeField] private GameObject _collar;
    [SerializeField] private GameObject _roof;

    public Vector2Int FootprintTiles => _footprintTiles;
    public float WallHeight => _wallHeight;

    /// <summary>Editor-time setup (ChunkPrefabBuilder).</summary>
    public void Configure(Vector2Int footprint, float wallHeight, GameObject collar, GameObject roof)
    {
        _footprintTiles = footprint;
        _wallHeight = wallHeight;
        _collar = collar;
        _roof = roof;
    }

    /// <summary>
    /// Adapt to the section the chunk sits in: <paramref name="corridorHeight"/> = the generator's wall
    /// height for the tiles outside the doors (WallHeightFor). Open sky = no ceiling (Graveyard).
    /// </summary>
    public void FitToTheme(float corridorHeight, bool openSky)
    {
        if (_roof != null) _roof.SetActive(openSky);
        if (_collar == null) return;
        float extra = corridorHeight - _wallHeight;
        bool needed = !openSky && extra > 0.01f;
        _collar.SetActive(needed);
        if (needed)
        {
            var s = _collar.transform.localScale;
            _collar.transform.localScale = new Vector3(s.x, extra, s.z);
        }
    }

    /// <summary>All socket transforms whose name starts with "SOCKET_" + <paramref name="kind"/>.</summary>
    public List<Transform> GetSockets(string kind)
    {
        var list = new List<Transform>();
        string prefix = "SOCKET_" + kind;
        foreach (Transform t in transform)
            if (t.name.StartsWith(prefix)) list.Add(t);
        return list;
    }
}
