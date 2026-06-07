using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class EvidencePickup : MonoBehaviour, IInteractable
{
    public enum EvidenceState { InWorld, Carried, AtExtractionZone, Destroyed }

    [SerializeField] private EvidenceItem _definition;

    public EvidenceItem Definition  => _definition;
    public EvidenceState State      { get; private set; } = EvidenceState.InWorld;
    public CharacterBase Carrier    { get; private set; }

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    // --- IInteractable ---

    public string GetPrompt() => $"Pick up {_definition.DisplayName}";

    public void Interact(CharacterBase interactor)
    {
        if (State != EvidenceState.InWorld) return;

        bool added = interactor.Inventory.TryAdd(this);
        if (!added)
            Debug.Log($"[Inventory] {interactor.name} inventory is full.");
    }

    // --- State transitions ---

    public void OnPickedUp(CharacterBase carrier)
    {
        State   = EvidenceState.Carried;
        Carrier = carrier;
        _rb.isKinematic = true;
        gameObject.SetActive(false);
    }

    public void OnDropped(Vector3 position)
    {
        State   = EvidenceState.InWorld;
        Carrier = null;
        transform.position  = position;
        _rb.isKinematic     = false;
        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        gameObject.SetActive(true);
    }

    public void OnEnteredExtractionZone()
    {
        if (State == EvidenceState.Destroyed) return;
        State = EvidenceState.AtExtractionZone;
    }

    public void OnExitedExtractionZone()
    {
        if (State != EvidenceState.AtExtractionZone) return;
        State = EvidenceState.InWorld;
    }

    public void Break()
    {
        if (!_definition.IsBreakable) return;
        State   = EvidenceState.Destroyed;
        Carrier = null;
        _rb.isKinematic = true;
        gameObject.SetActive(false);
        MissionManager.Instance.NotifyEvidenceDestroyed(this);
    }
}
