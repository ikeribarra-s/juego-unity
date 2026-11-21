using UnityEngine;

/// <summary>
/// Something that can be killed (players for now; could be creatures later). The patrol enemy calls
/// <see cref="Kill"/> when it catches its target. Lives in Core next to the other interaction
/// interfaces (<see cref="IInteractable"/>, IHoldInteractable, IHighlightable) so anything that deals
/// lethal damage — enemies, traps, hazards — can reuse it without knowing the victim's concrete type.
/// </summary>
public interface IKillable
{
    bool IsDead { get; }

    /// <summary><paramref name="source"/> is whatever dealt the kill (may be null), for logging/credit.</summary>
    void Kill(GameObject source);
}
