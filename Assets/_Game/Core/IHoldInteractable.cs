/// <summary>
/// An interactable that can be driven by holding the Interact key instead of a single press.
/// InteractionSystem decides per-press: if <see cref="UseHold"/> returns true the interaction
/// is routed through OnHoldStart/OnHoldTick/OnHoldStop while the player keeps aiming at the
/// object and holding E; otherwise it falls back to the normal <see cref="IInteractable.Interact"/>
/// single-press path. This lets one object behave as a hold target in one state (e.g. charging a
/// repair) and a plain press target in another (e.g. flipping a switch once repaired).
/// </summary>
public interface IHoldInteractable : IInteractable
{
    /// <summary>True → this press should charge as a hold. False → treat as a normal press.</summary>
    bool UseHold(CharacterBase interactor);

    void OnHoldStart(CharacterBase interactor);

    /// <summary>Called every frame while the player holds Interact and keeps aiming at this object.</summary>
    void OnHoldTick(CharacterBase interactor, float deltaTime);

    /// <summary>Called when the hold ends — released, aimed away, or the object left range.</summary>
    void OnHoldStop(CharacterBase interactor);
}
