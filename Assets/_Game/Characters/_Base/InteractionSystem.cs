using UnityEngine;

public class InteractionSystem : MonoBehaviour
{
    [SerializeField] private float     _interactRange = 2.5f;
    [SerializeField] private LayerMask _interactLayer = ~0;

    private CharacterBase     _character;
    private Camera            _cam;
    private IInteractable     _currentTarget;
    private string            _currentPrompt;
    private IHoldInteractable _holdTarget;   // non-null while charging a hold interaction

    public string CurrentPrompt => _currentPrompt;

    private void Awake()
    {
        _character = GetComponent<CharacterBase>();
    }

    private void Start()
    {
        _cam = GetComponentInChildren<Camera>();
        if (_cam == null)
            Debug.LogError("[InteractionSystem] No Camera found in children. Attach a Camera to CameraRoot.");
    }

    private void OnEnable()
    {
        _character.Input.InteractStartedEvent  += OnInteract;
        _character.Input.InteractCanceledEvent += OnInteractCanceled;
    }

    private void OnDisable()
    {
        _character.Input.InteractStartedEvent  -= OnInteract;
        _character.Input.InteractCanceledEvent -= OnInteractCanceled;
        StopHold();
        ClearTarget();   // drop any lingering look-at highlight when the controller is disabled
    }

    private void Update()
    {
        if (_cam == null) return;

        Ray ray = new(_cam.transform.position, _cam.transform.forward);
        Debug.DrawRay(ray.origin, ray.direction * _interactRange, Color.cyan);

        if (Physics.Raycast(ray, out RaycastHit hit, _interactRange, _interactLayer))
        {
            // GetComponentInParent (not GetComponent) so a collider on a child mesh still resolves to
            // the interactable on the prefab root — otherwise e.g. a torch hit on its flame child would
            // miss the root Torch, and a pickup could re-parent only the child (leaving it on the wall).
            var interactable = hit.collider.GetComponentInParent<IInteractable>();

            if (interactable != null)
            {
                if (_currentTarget != interactable)
                {
                    SetHighlight(_currentTarget, false);
                    _currentTarget = interactable;
                    _currentPrompt = interactable.GetPrompt();
                    SetHighlight(_currentTarget, true);
                    Debug.Log($"[Interaction] Targeting: {hit.collider.name} — \"{_currentPrompt}\"");
                }
            }
            else
            {
                ClearTarget();
            }
        }
        else
        {
            ClearTarget();
        }

        // Drive an in-progress hold: keep charging only while still aiming at the same object.
        if (_holdTarget != null)
        {
            if (!ReferenceEquals(_currentTarget, _holdTarget))
            {
                StopHold();
            }
            else
            {
                _holdTarget.OnHoldTick(_character, Time.deltaTime);
                _currentPrompt = _holdTarget.GetPrompt();   // refresh HUD as the charge fills
            }
        }
    }

    private void OnInteract()
    {
        if (_currentTarget == null)
        {
            Debug.Log("[Interaction] E pressed — nothing in range.");
            return;
        }

        // Hold-capable target that wants a hold in its current state → start charging.
        if (_currentTarget is IHoldInteractable hold && hold.UseHold(_character))
        {
            _holdTarget = hold;
            _holdTarget.OnHoldStart(_character);
            return;
        }

        Debug.Log($"[Interaction] Interacting with \"{_currentTarget.GetPrompt()}\"");
        _currentTarget.Interact(_character);
    }

    private void OnInteractCanceled() => StopHold();

    private void StopHold()
    {
        if (_holdTarget == null) return;
        _holdTarget.OnHoldStop(_character);
        _holdTarget = null;
    }

    private void ClearTarget()
    {
        if (_currentTarget == null) return;
        SetHighlight(_currentTarget, false);
        _currentTarget = null;
        _currentPrompt = null;
    }

    // Toggle a look-at border on the target if it (or a child) carries an IHighlightable.
    private static void SetHighlight(IInteractable target, bool on)
    {
        if (target is Component c) c.GetComponentInChildren<IHighlightable>()?.SetHighlighted(on);
    }
}
