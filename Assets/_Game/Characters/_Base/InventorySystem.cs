using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pocket inventory slots. Evidence is now carried physically via PhysicsGrabber
/// (R.E.P.O.-style beam), so nothing flows through here yet — kept for Phase 2
/// abilities (e.g. Mochín's large inventory).
/// </summary>
public class InventorySystem : MonoBehaviour
{
    private CharacterBase _character;
    private readonly List<EvidencePickup> _items = new();

    public IReadOnlyList<EvidencePickup> Items => _items;
    public int  MaxSlots => _character.Stats.InventorySlots;
    public bool IsFull   => _items.Count >= MaxSlots;

    private void Awake()
    {
        _character = GetComponent<CharacterBase>();
    }
}
