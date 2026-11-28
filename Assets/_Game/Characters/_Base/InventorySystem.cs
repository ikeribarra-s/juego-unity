using System;
using System.Collections;
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

    [Header("Drop")]
    [Tooltip("Metres in front of the camera the item appears at (pulled in when a wall is closer).")]
    [SerializeField] private float _dropDistance = 0.9f;
    [SerializeField] private float _dropForwardSpeed = 2.2f;
    [SerializeField] private float _dropUpSpeed = 1.2f;
    [Tooltip("The dropped item ignores the dropper's colliders for this long, so it can't get shoved into the player.")]
    [SerializeField] private float _dropIgnoreTime = 0.7f;

    private void Awake()
    {
        _character = GetComponent<CharacterBase>();
    }

    private void OnEnable()
    {
        if (_character != null && _character.Input != null) _character.Input.DropInventoryEvent += OnDropInventory;
    }

    private void OnDisable()
    {
        if (_character != null && _character.Input != null) _character.Input.DropInventoryEvent -= OnDropInventory;
    }

    private void OnDropInventory()
    {
        if (_items.Count > 0) TryDrop(_items[_items.Count - 1]);   // the most recently picked-up item
    }

    /// <summary>The usable item held in the right hand / used by F (most recently picked-up first), or null.</summary>
    public IUsableItem FindUsable(out IInventoryItem item)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i] is Component c && c.TryGetComponent<IUsableItem>(out var usable)) { item = _items[i]; return usable; }
        }
        item = null;
        return null;
    }

    /// <summary>Take <paramref name="item"/> out of the slots and put it back in the world in front of the player.</summary>
    public bool TryDrop(IInventoryItem item)
    {
        if (item is not IDroppable droppable || !_items.Contains(item)) return false;

        var cam = GetComponentInChildren<Camera>(true);
        Vector3 origin  = cam != null ? cam.transform.position : transform.position + Vector3.up * 1.6f;
        Vector3 forward = cam != null ? cam.transform.forward  : transform.forward;
        Vector3 pos = origin + forward * _dropDistance;
        // A wall right in front: stop short of it instead of spawning inside.
        if (Physics.Raycast(origin, forward, out var hit, _dropDistance + 0.15f, ~0, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform))
            pos = origin + forward * Mathf.Max(0.1f, hit.distance - 0.15f);

        Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
        Quaternion rot = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat.normalized) : transform.rotation;
        Vector3 vel = forward * _dropForwardSpeed + Vector3.up * _dropUpSpeed;

        _items.Remove(item);
        droppable.Drop(_character, pos, rot, vel);
        Changed?.Invoke();

        if (item is Component c) StartCoroutine(IgnoreDropperRoutine(c));
        return true;
    }

    private IEnumerator IgnoreDropperRoutine(Component item)
    {
        var mine   = GetComponentsInChildren<Collider>(true);
        var theirs = item.GetComponentsInChildren<Collider>(true);
        foreach (var a in mine) foreach (var b in theirs) if (a != null && b != null) Physics.IgnoreCollision(a, b, true);
        yield return new WaitForSeconds(_dropIgnoreTime);
        foreach (var a in mine) foreach (var b in theirs) if (a != null && b != null) Physics.IgnoreCollision(a, b, false);
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
