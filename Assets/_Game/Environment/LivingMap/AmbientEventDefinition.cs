using System;
using UnityEngine;

/// <summary>
/// One kind of thing the <see cref="TensionDirector"/> can make happen (Living Map §5.2).
/// Data only — the director executes it by <see cref="Kind"/>. Nothing here can hurt a player:
/// the director nudges, never kills.
/// </summary>
[CreateAssetMenu(fileName = "AE_New", menuName = "Fluffterror/Living Map/Ambient Event")]
public class AmbientEventDefinition : ScriptableObject
{
    public enum EventKind
    {
        TorchGutter,      // nearby torch sputters, recovers
        TorchDie,         // a torch near (not next to) the players gutters and goes out for a while
        LightStutter,     // every torch in a radius sputters at once
        DistantSound,     // one-shot 3D sound 15–35 m away, preferably behind a wall
        CreatureCue,      // bell jingle/growl: real creature if one can, otherwise faked
        Sting,            // 2D sting (spike entry)
    }

    [Flags]
    public enum PhaseMask
    {
        None = 0,
        Calm = 1 << 0,
        BuildUp = 1 << 1,
        Spike = 1 << 2,
        Relief = 1 << 3,
    }

    public EventKind Kind = EventKind.DistantSound;
    public PhaseMask Phases = PhaseMask.Calm | PhaseMask.BuildUp;
    [Min(0f)] public float Weight = 1f;
    [Tooltip("Seconds before this event can fire again.")]
    [Min(0f)] public float Cooldown = 20f;

    [Header("Where")]
    [Tooltip("Distance band from the nearest player (m). Torch events pick torches in this band; sounds are placed in it.")]
    public Vector2 Distance = new(12f, 30f);
    [Tooltip("Prefer a spot the players can't see (a wall between them).")]
    public bool PreferOccluded = true;
    [Tooltip("LightStutter radius (m).")]
    public float Radius = 12f;

    [Header("What")]
    public SoundDefinition Sound;
    public CreatureCue Cue = CreatureCue.BellJingle;
    [Tooltip("Gutter duration (s).")]
    public Vector2 Duration = new(1.5f, 3.5f);
    [Tooltip("TorchDie: seconds until it relights by itself (x..y). y <= 0 = stays out until a player relights it.")]
    public Vector2 RelightAfter = new(25f, 60f);
    [Tooltip("Tension added when this fires (a scare feeds itself a little).")]
    [Range(0f, 0.5f)] public float TensionAdd = 0.03f;

    public bool AllowedIn(TensionDirector.Phase phase) => (Phases & phase switch
    {
        TensionDirector.Phase.Calm => PhaseMask.Calm,
        TensionDirector.Phase.BuildUp => PhaseMask.BuildUp,
        TensionDirector.Phase.Spike => PhaseMask.Spike,
        _ => PhaseMask.Relief,
    }) != 0;
}
