using System;
using System.Collections.Generic;
using UnityEngine;

public class InventorySystem : MonoBehaviour
{
    private CharacterBase _character;
    private readonly List<EvidencePickup> _items = new();

    public IReadOnlyList<EvidencePickup> Items => _items;
    public int MaxSlots  => _character.Stats.InventorySlots;
    public bool IsFull   => _items.Count >= MaxSlots;

    public event Action<EvidencePickup> ItemAdded;
    public event Action<EvidencePickup> ItemRemoved;

    private void Awake()
    {
        _character = GetComponent<CharacterBase>();
    }

    private void OnEnable()
    {
        _character.Input.DropItemEvent += DropLastItem;
    }

    private void OnDisable()
    {
        _character.Input.DropItemEvent -= DropLastItem;
    }

    public bool TryAdd(EvidencePickup item)
    {
        if (IsFull) return false;
        _items.Add(item);
        item.OnPickedUp(_character);
        ItemAdded?.Invoke(item);
        return true;
    }

    public void Drop(EvidencePickup item)
    {
        if (!_items.Contains(item)) return;
        _items.Remove(item);
        item.OnDropped(transform.position + transform.forward * 0.8f);
        ItemRemoved?.Invoke(item);
    }

    public void DropAll()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
            Drop(_items[i]);
    }

    private void DropLastItem()
    {
        if (_items.Count == 0) return;
        Drop(_items[^1]);
    }
}
