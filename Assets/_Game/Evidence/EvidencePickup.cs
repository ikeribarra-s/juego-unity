using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class EvidencePickup : MonoBehaviour, IInteractable
{
    public enum EvidenceState { InWorld, Carried, AtExtractionZone, Destroyed }

    [SerializeField] private EvidenceItem _definition;
    [Tooltip("Breakable items shatter when they hit anything faster than this (m/s)")]
    [SerializeField] private float _breakImpactSpeed = 7f;

    public EvidenceItem Definition  => _definition;
    public EvidenceState State      { get; private set; } = EvidenceState.InWorld;
    public CharacterBase Carrier    { get; private set; }
    public Rigidbody     Body       => _rb;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    // --- IInteractable ---

    public string GetPrompt() => $"Hold E to grab {_definition.DisplayName}";

    public void Interact(CharacterBase interactor)
    {
        if (State != EvidenceState.InWorld && State != EvidenceState.AtExtractionZone) return;
        if (interactor.Grabber == null) return;

        interactor.Grabber.TryGrab(this);
    }

    // --- State transitions ---

    public void OnGrabbed(CharacterBase carrier)
    {
        State   = EvidenceState.Carried;
        Carrier = carrier;
    }

    public void OnReleased()
    {
        if (State != EvidenceState.Carried) return;
        State   = EvidenceState.InWorld;
        Carrier = null;
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

    private void OnCollisionEnter(Collision collision)
    {
        if (State == EvidenceState.Destroyed) return;
        if (!_definition.IsBreakable) return;
        if (collision.relativeVelocity.magnitude < _breakImpactSpeed) return;

        Debug.Log($"[Evidence] {_definition.DisplayName} shattered on impact ({collision.relativeVelocity.magnitude:F1} m/s).");
        Break();
    }
}
