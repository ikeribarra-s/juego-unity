using UnityEngine;

/// <summary>
/// A plain pocket item lying in the world (map, salt bag ...): look at it + E puts it in the looker's
/// <see cref="InventorySystem"/> (the object is deactivated, not destroyed), X drops it again
/// (<see cref="IDroppable"/>: re-activated in front of the dropper, with a small toss; it keeps its Rigidbody).
/// Prompts are cached strings. Thin props use <see cref="IAimAssistTarget"/> so the crosshair need not be pixel-exact.
/// </summary>
[DisallowMultipleComponent]
public class ItemPickup : MonoBehaviour, IInteractable, IContextualPrompt, IInventoryItem, IDroppable, IAimAssistTarget
{
    [SerializeField] private string _displayName = "Item";
    [SerializeField] private string _subtitle = "";
    [SerializeField] private Sprite _icon;
    [Tooltip("Circular zone (m) around the crosshair in which the item can be targeted.")]
    [SerializeField, Min(0f)] private float _assistRadius = 0.35f;
    [SerializeField] private SoundDefinition _pickupSound;
    [SerializeField] private SoundDefinition _dropSound;
    [Tooltip("Minimum impact speed (m/s) that plays the drop sound when it lands.")]
    [SerializeField] private float _landSoundSpeed = 1.2f;

    private Rigidbody _rb;
    private string _prompt, _fullPrompt;
    private float _nextLandSound;

    /// <summary>Who holds it (null while it lies in the world).</summary>
    public CharacterBase Holder { get; private set; }
    public float AssistRadius => _assistRadius;

    public string DisplayName => _displayName;
    public string Subtitle    => _subtitle;
    public Sprite Icon        => _icon;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _prompt     = $"Press E to pick up {_displayName}";
        _fullPrompt = $"{_displayName} - inventory full";
    }

    public string GetPrompt() => _prompt ?? $"Press E to pick up {_displayName}";
    public string GetPrompt(CharacterBase interactor) =>
        interactor != null && interactor.Inventory != null && interactor.Inventory.IsFull ? _fullPrompt : GetPrompt();

    public void Interact(CharacterBase interactor)
    {
        if (interactor == null || interactor.Inventory == null || Holder != null) return;
        if (!interactor.Inventory.TryAdd(this)) return;   // full

        Holder = interactor;
        if (_pickupSound != null && SoundManager.Instance != null) SoundManager.Instance.Play(_pickupSound, transform.position);
        gameObject.SetActive(false);   // kept (not destroyed) so it can be dropped
    }

    public void Drop(CharacterBase dropper, Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        Holder = null;
        transform.SetPositionAndRotation(position, rotation);
        gameObject.SetActive(true);
        if (_rb != null)
        {
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = Random.insideUnitSphere * 2f;
            _rb.WakeUp();
        }
        _nextLandSound = Time.time + 0.1f;
    }

    private void OnCollisionEnter(Collision c)
    {
        if (_dropSound == null || SoundManager.Instance == null || Time.time < _nextLandSound) return;
        if (c.relativeVelocity.sqrMagnitude < _landSoundSpeed * _landSoundSpeed) return;
        _nextLandSound = Time.time + 0.15f;
        SoundManager.Instance.Play(_dropSound, transform.position);
    }

    /// <summary>Second HUD line (e.g. the salt left in a bag).</summary>
    public void SetSubtitle(string s) => _subtitle = s;

    public void Setup(string displayName, string subtitle, Sprite icon, SoundDefinition pickup, SoundDefinition drop)
    {
        _displayName = displayName; _subtitle = subtitle; _icon = icon; _pickupSound = pickup; _dropSound = drop;
        _prompt     = $"Press E to pick up {_displayName}";
        _fullPrompt = $"{_displayName} - inventory full";
    }
}
