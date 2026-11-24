using UnityEngine;

/// <summary>How alarmed a creature is — the director's main tension input (Creature_01.md §3 states).</summary>
public enum CreatureAlertLevel { Calm, Investigate, Point, Chase }

/// <summary>Sounds the director may ask a creature to make (or fake, if no creature can).</summary>
public enum CreatureCue { BellJingle, Growl, Howl }

/// <summary>
/// Hook between creatures and the <see cref="TensionDirector"/>. A creature registers itself
/// (<see cref="TensionDirector.RegisterCreature"/>) and reports alert changes
/// (<see cref="TensionDirector.ReportCreatureAlert"/>); in return the director may ask it to play a cue.
///
/// Fairness rule (Creature_01 + LivingMap §12 Q4): the director is allowed to FAKE creature cues (a false
/// bell jingle) while no real creature can honour the request. When a creature implements
/// <see cref="TryPlayCue"/> for real (Chucho's bell), the director asks it first so the cue is truthful.
/// </summary>
public interface ICreatureTensionSource
{
    Vector3 Position { get; }
    CreatureAlertLevel AlertLevel { get; }

    /// <summary>Play the cue from the creature itself. Return false if it can't (no sound, busy, dead) —
    /// the director then falls back to its own fake cue.</summary>
    bool TryPlayCue(CreatureCue cue);
}
