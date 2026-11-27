using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pocket inventory slots holding <see cref="IInventoryItem"/>s. Evidence is still carried physically via
/// PhysicsGrabber (R.E.P.O.-style beam); what goes in here now are the bones of dead players
/// (<see cref="BonePickup"/>), later Mochín's large inventory. Slot count = <c>Stats.InventorySlots</c>.
/// The torch lives on <see cref="TorchCarrier"/> and does not take an inventory slot (the HUD draws it beside the items).
/// </summary>
public class InventorySystem : MonoBehaviour
{
    private CharacterBase _character;
    private readonly List<IInventoryItem> _items = new();

    public IReadOnlyList<IInventoryItem> Items => _items;
    public int  MaxSlots => _character != null && _character.Stats != null ? _character.Stats.InventorySlots : 3;
    public bool IsFull   => _items.Count >= MaxSlots;

    /// <summary>Fired after an item was added or removed.</summary>
    public event Action Changed;

    private void Awake()
    {
        _character = GetComponent<CharacterBase>();
    }

    /// <summary>Put <paramref name="item"/> in the first free slot. False when full or already inside.</summary>
    public bool TryAdd(IInventoryItem item)
    {
        if (item == null || IsFull || _items.Contains(item)) return false;
        _items.Add(item);
        Changed?.Invoke();
        return true;
    }

    public bool Remove(IInventoryItem item)
    {
        if (!_items.Remove(item)) return false;
        Changed?.Invoke();
        return true;
    }

    public bool Contains(IInventoryItem item) => _items.Contains(item);
}
