/// <summary>
/// Optional extra for an <see cref="IInteractable"/> whose prompt depends on who is looking (e.g. a bone says
/// "Inventory full" when the looker has no free slot). <see cref="InteractionSystem"/> re-reads it EVERY frame
/// while the object is targeted, so implementations should return cached strings (no per-frame allocation).
/// </summary>
public interface IContextualPrompt
{
    string GetPrompt(CharacterBase interactor);
}
