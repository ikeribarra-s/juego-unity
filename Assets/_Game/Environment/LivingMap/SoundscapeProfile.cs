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

    [Serializable]
    public class AcousticBed
    {
        public Acoustic Type;
        public SoundDefinition Loop;
        [Tooltip("Small only: use this bed when the section is a Transition (corridor).")]
        public bool OnlyInTransition;
    }

    [Serializable]
    public class NamedRoomBed
    {
        [Tooltip("Matched (contains) against the AcousticZone GameObject name, e.g. CH_Room_Ritual.")]
        public string NameContains;
        public SoundDefinition Loop;
        [Tooltip("2D cue on the first entry per run (not ducked). Optional.")]
        public SoundDefinition Sting;
    }

    [Header("Place beds (override the section bed inside an AcousticZone)")]
    public AcousticBed[] AcousticBeds = Array.Empty<AcousticBed>();
    [Tooltip("Checked before AcousticBeds.")]
    public NamedRoomBed[] NamedBeds = Array.Empty<NamedRoomBed>();
    [Tooltip("Listener must be this deep inside a new zone (m)...")]
    [Min(0f)] public float ZoneSwitchEdge = 0.5f;
    [Tooltip("...for this long (s) before the bed switches.")]
    [Min(0f)] public float ZoneSwitchTime = 1f;
    [Tooltip("Global cooldown between room stingers (s).")]
    [Min(0f)] public float StingCooldown = 90f;

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

    public SoundDefinition ZoneBed(AcousticZone z, MultiSectionDungeonGenerator.SectionType section, out NamedRoomBed named)
    {
        named = null;
        if (z == null) return null;
        string n = z.ZoneName;
        foreach (var b in NamedBeds)
            if (b != null && !string.IsNullOrEmpty(b.NameContains) && n.Contains(b.NameContains, StringComparison.OrdinalIgnoreCase))
            { named = b; if (b.Loop != null) return b.Loop; break; }
        foreach (var b in AcousticBeds)
        {
            if (b == null || b.Loop == null || b.Type != z.Type) continue;
            if (b.OnlyInTransition && section != MultiSectionDungeonGenerator.SectionType.Transition) continue;
            return b.Loop;
        }
        return null;
    }
}
