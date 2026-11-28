using UnityEngine;

/// <summary>
/// Put on any component of a pocket item's GameObject to make it usable from the inventory: hold <b>F</b>
/// (<see cref="HandItemController"/>). <see cref="HeldPrefab"/> is currently not shown (the held-bag arm animation was removed
/// 2026-11-28). The item GameObject itself is inactive while pocketed, so it
/// does no work of its own: <see cref="UseTick"/> is driven by the controller every frame the key is held.
/// </summary>
public interface IUsableItem
{
    /// <summary>Short HUD hint, e.g. "hold to pour salt".</summary>
    string UseHint { get; }
    /// <summary>Visual shown in the right hand while pocketed (just the model, no physics / scripts). Null = nothing shown.</summary>
    GameObject HeldPrefab { get; }
    /// <summary>False when there is nothing left to use.</summary>
    bool CanUse { get; }
    /// <summary>Called every frame while the use key is held. Return true when the item is used up (it is removed and destroyed).</summary>
    bool UseTick(CharacterBase user, HandItemController hand, float dt);
    /// <summary>The key was released, the item was dropped or the player died.</summary>
    void UseEnd(CharacterBase user, HandItemController hand);
}
