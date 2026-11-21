using UnityEngine;

/// <summary>
/// A wall torch the player can pick up and carry (one at a time). Implements <see cref="IInteractable"/>,
/// so it works with the existing <see cref="InteractionSystem"/> raycast — press E while looking at it.
///
/// Put this on the torch prefab's <b>root</b>, alongside a <see cref="Collider"/> (the interaction ray
/// does <c>hit.collider.GetComponent&lt;IInteractable&gt;()</c>, so they must share the GameObject).
/// Pickup/placement is handled by <see cref="TorchCarrier"/> on the character — this just routes to it.
/// </summary>
[DisallowMultipleComponent]
public class Torch : MonoBehaviour, IInteractable
{
    [SerializeField] private string _prompt = "Press E to take torch";

    public bool IsCarried { get; private set; }

    public string GetPrompt() => IsCarried ? string.Empty : _prompt;

    public void Interact(CharacterBase interactor)
    {
        if (IsCarried || interactor == null) return;
        interactor.TorchCarrier?.TryCarry(this);
    }

    /// <summary>Set by <see cref="TorchCarrier"/> when this torch is picked up / put down.</summary>
    public void SetCarried(bool carried) => IsCarried = carried;
}
