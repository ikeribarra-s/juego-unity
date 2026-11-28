using UnityEngine;

/// <summary>
/// An <see cref="IInventoryItem"/> that can leave the pockets and return to the world.
/// <see cref="InventorySystem.TryDrop"/> removes it from the slots, computes the drop pose and calls <see cref="Drop"/>;
/// the item re-activates itself there and takes the velocity.
/// </summary>
public interface IDroppable
{
    void Drop(CharacterBase dropper, Vector3 position, Quaternion rotation, Vector3 velocity);
}
