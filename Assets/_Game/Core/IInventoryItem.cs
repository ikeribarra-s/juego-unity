/// <summary>
/// Anything that can sit in an <see cref="InventorySystem"/> slot (bones of a dead player, later Mochín's evidence).
/// <c>InventoryHUD</c> draws <see cref="DisplayName"/> + <see cref="Subtitle"/> per slot.
/// </summary>
public interface IInventoryItem
{
    /// <summary>Slot title, e.g. "Ribcage".</summary>
    string DisplayName { get; }
    /// <summary>Second slot line, e.g. "Base's" (bone owner) or "10 pts" (evidence value).</summary>
    string Subtitle { get; }
}
