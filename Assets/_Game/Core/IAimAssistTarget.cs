/// <summary>
/// Optional marker for an <see cref="IInteractable"/> that is hard to hit with the single centre ray (thin bones on the floor):
/// <see cref="InteractionSystem"/> also targets it when the crosshair is within <see cref="AssistRadius"/> metres of it
/// (a circular zone around the look ray) and nothing solid blocks the view.
/// </summary>
public interface IAimAssistTarget
{
    /// <summary>Radius in metres of the forgiving zone around the look ray.</summary>
    float AssistRadius { get; }
}
