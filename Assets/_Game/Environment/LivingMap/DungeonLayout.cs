using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime summary of the generated level, written by <see cref="MultiSectionDungeonGenerator"/> on the
/// generated root (so it is serialized with the scene and rebuilt by every Generate). Read by the
/// Living Map systems that need to know "where am I / where is what" without touching the tile grid:
/// <see cref="SoundscapeController"/> (which area bed to play, where drips are), <see cref="TensionDirector"/>
/// (landmarks for callouts), <see cref="RunSetup"/> (seed + place name for the run card).
/// Positions are world space at generation time (the dungeon never moves).
/// </summary>
[DisallowMultipleComponent]
public class DungeonLayout : MonoBehaviour
{
    [Serializable]
    public class Section
    {
        public MultiSectionDungeonGenerator.SectionType Type;
        [Tooltip("Tile bounds in the generator's local grid (1 tile = 1 m).")]
        public RectInt Rect;
        public string LandmarkName;
        public Vector3 LandmarkPosition;
        public bool HasLandmark;
    }

    [SerializeField] private int _seed;
    [SerializeField] private Section[] _sections = Array.Empty<Section>();
    [SerializeField] private RectInt[] _deadEndRooms = Array.Empty<RectInt>();
    [SerializeField] private Vector3[] _dripPoints = Array.Empty<Vector3>();
    [SerializeField] private Vector3 _extraction;
    [SerializeField] private Vector3 _playerStart;
    [SerializeField] private int _startToExtractionWalk;
    [SerializeField] private int _sectionLinks;
    [SerializeField] private int _extraLoops;
    [Header("Authored chunks")]
    [SerializeField] private string[] _chunks = Array.Empty<string>();
    [Tooltip("SOCKET_Hide_* — hiding spots (future hiding logic / creature search points).")]
    [SerializeField] private Vector3[] _hidePoints = Array.Empty<Vector3>();
    [Tooltip("VOL_Water centres (flooded crypt, sewer) — for a future Flooded modifier.")]
    [SerializeField] private Vector3[] _waterPoints = Array.Empty<Vector3>();
    [SerializeField] private bool _hasDen;
    [Tooltip("SOCKET_Den — Chucho's den drop point (CreatureDen).")]
    [SerializeField] private Vector3 _den;

    public static DungeonLayout Current { get; private set; }

    public int Seed => _seed;
    public IReadOnlyList<Section> Sections => _sections;
    public IReadOnlyList<RectInt> DeadEndRooms => _deadEndRooms;
    public IReadOnlyList<Vector3> DripPoints => _dripPoints;
    public Vector3 Extraction => _extraction;
    public Vector3 PlayerStart => _playerStart;
    public int StartToExtractionWalk => _startToExtractionWalk;
    public int SectionLinks => _sectionLinks;
    public int ExtraLoops => _extraLoops;
    public IReadOnlyList<string> Chunks => _chunks;
    public IReadOnlyList<Vector3> HidePoints => _hidePoints;
    public IReadOnlyList<Vector3> WaterPoints => _waterPoints;
    public bool HasDen => _hasDen;
    public Vector3 Den => _den;

    public void SetChunkData(string[] chunks, Vector3[] hide, Vector3[] water, bool hasDen, Vector3 den)
    {
        _chunks = chunks ?? Array.Empty<string>();
        _hidePoints = hide ?? Array.Empty<Vector3>();
        _waterPoints = water ?? Array.Empty<Vector3>();
        _hasDen = hasDen;
        _den = den;
    }

    private void OnEnable() => Current = this;
    private void OnDisable() { if (Current == this) Current = null; }

    /// <summary>Called by the generator after a build.</summary>
    public void Set(int seed, Section[] sections, RectInt[] deadEnds, Vector3[] drips,
                    Vector3 extraction, Vector3 start, int startWalk, int links, int loops)
    {
        _seed = seed;
        _sections = sections ?? Array.Empty<Section>();
        _deadEndRooms = deadEnds ?? Array.Empty<RectInt>();
        _dripPoints = drips ?? Array.Empty<Vector3>();
        _extraction = extraction;
        _playerStart = start;
        _startToExtractionWalk = startWalk;
        _sectionLinks = links;
        _extraLoops = loops;
        Current = this;
    }

    /// <summary>Section type at a world position (Transition = corridor / outside every section).</summary>
    public MultiSectionDungeonGenerator.SectionType SectionAt(Vector3 world, out int index)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        int x = Mathf.FloorToInt(local.x), y = Mathf.FloorToInt(local.z);
        for (int i = 0; i < _sections.Length; i++)
        {
            RectInt r = _sections[i].Rect;
            if (x >= r.xMin && x < r.xMax && y >= r.yMin && y < r.yMax) { index = i; return _sections[i].Type; }
        }
        index = -1;
        return MultiSectionDungeonGenerator.SectionType.Transition;
    }

    /// <summary>Name of the landmark closest to a world position (for callouts / run card), or null.</summary>
    public string NearestLandmark(Vector3 world, out float distance)
    {
        string best = null; distance = float.MaxValue;
        foreach (var s in _sections)
        {
            if (!s.HasLandmark) continue;
            float d = Vector3.Distance(world, s.LandmarkPosition);
            if (d < distance) { distance = d; best = s.LandmarkName; }
        }
        return best;
    }
}
