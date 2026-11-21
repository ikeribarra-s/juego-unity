/// <summary>
/// Something that can show a "you are looking at me" highlight (e.g. an outline border).
/// <see cref="InteractionSystem"/> toggles this on the object the player's reticle is over:
/// it fetches <c>GetComponentInChildren&lt;IHighlightable&gt;()</c> from the targeted collider and
/// calls <see cref="SetHighlighted"/> as the target changes. Implementations should be cheap to
/// toggle — the outline geometry/material is built once and just enabled/disabled here.
/// </summary>
public interface IHighlightable
{
    void SetHighlighted(bool on);
}
