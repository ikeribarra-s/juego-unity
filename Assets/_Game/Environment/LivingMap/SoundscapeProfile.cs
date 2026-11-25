using System;
using UnityEngine;

/// <summary>
/// Living Map E5 + E9 data: one quiet area loop per section theme and the drip one-shots.
/// (Distant sounds — E4 — are director events, see <see cref="AmbientEventDefinition"/>.)
/// Loudness: files are mastered to the `ambient` role (≈ -30 LUFS, Docs/ArtDirection.md §9), so the
/// SoundDefinition volumes stay near 1 and <see cref="BedVolume"/> is the global trim.
/// </summary>
[CreateAssetMenu(fileName = "SSP_New", menuName = "Fluffterror/Living Map/Soundscape Profile")]
public class SoundscapeProfile : ScriptableObject
{
    [Serializable]
    public class Bed
    {
        public MultiSectionDungeonGenerator.SectionType Section;
        public SoundDefinition Loop;
    }

    [Header("Area beds (2D, crossfaded)")]
    public Bed[] Beds = Array.Empty<Bed>();
    [Tooltip("Bed for corridors / generator rooms / anything without its own entry.")]
    public SoundDefinition DefaultBed;
    [Range(0f, 1f)] public float BedVolume = 0.8f;
    [Min(0.1f)] public float CrossfadeTime = 4f;
    [Tooltip("How often the listener's area is re-evaluated (s).")]
    [Min(0.1f)] public float AreaCheckInterval = 0.5f;

    [Header("Drips (3D one-shots from generator-placed points)")]
    public SoundDefinition Drip;
    [Tooltip("Only drip points within this distance of the listener play.")]
    public float DripRadius = 22f;
    [Tooltip("Seconds between drips (random in range) — one at a time, never a rain.")]
    public Vector2 DripInterval = new(1.2f, 4f);

    [Header("Water (flooded crypt / sewer, WaterVolume)")]
    [Tooltip("Quiet 3D loop placed at the closest point of the nearest water volume.")]
    public SoundDefinition WaterLoop;
    public SoundDefinition WaterDrip;
    [Tooltip("Water loop / drips are audible only within this distance of a water volume.")]
    public float WaterRadius = 16f;
    public Vector2 WaterDripInterval = new(1.5f, 4f);

    public SoundDefinition BedFor(MultiSectionDungeonGenerator.SectionType type)
    {
        foreach (var b in Beds) if (b != null && b.Section == type && b.Loop != null) return b.Loop;
        return DefaultBed;
    }
}
