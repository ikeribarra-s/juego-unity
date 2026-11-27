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
    [Tooltip("Radius (m) of the sphere swept along the look ray to find aim-assist targets (bones). Each target's own AssistRadius decides how far from the ray it can be.")]
    [SerializeField] private float _assistSweepRadius = 0.6f;
    [Tooltip("Reach (m) from the eye for aim-assist targets. Longer than _interactRange on purpose: the eye is ~2.3 m up, so a bone on the floor " +
             "1.7 m away is already 2.9 m from the camera and was out of the 2.5 m range (floor pick-ups only worked within ~1 m).")]
    [SerializeField] private float _assistRange = 3.2f;
    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly RaycastHit[] _assistHits = new RaycastHit[32];
    private readonly RaycastHit[] _blockHits = new RaycastHit[16];

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

        // GetComponentInParent (not GetComponent) so a collider on a child mesh still resolves to
        // the interactable on the prefab root — otherwise e.g. a torch hit on its flame child would
        // miss the root Torch, and a pickup could re-parent only the child (leaving it on the wall).
        IInteractable interactable = null;
        if (RaycastIgnoringSelf(ray, out RaycastHit hit))
            interactable = hit.collider.GetComponentInParent<IInteractable>();

        // Nothing directly under the crosshair: a forgiving circular zone around the ray (thin bones on the floor).
        if (interactable == null)
            interactable = FindAimAssistTarget(ray);

        if (interactable != null)
        {
            if (_currentTarget != interactable)
            {
                SetHighlight(_currentTarget, false);
                _currentTarget = interactable;
                _currentPrompt = interactable is IContextualPrompt cp ? cp.GetPrompt(_character) : interactable.GetPrompt();
                SetHighlight(_currentTarget, true);
                Debug.Log($"[Interaction] Targeting: {(interactable as Component)?.name} — \"{_currentPrompt}\"");
            }
        }
        else
        {
            ClearTarget();
        }

        // Looker-dependent prompt (e.g. a bone says "Inventory full"): re-read every frame, implementations return cached strings.
        if (_currentTarget is IContextualPrompt contextual && _holdTarget == null)
            _currentPrompt = contextual.GetPrompt(_character);

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

    // Nearest hit that is not one of our own colliders: the camera sits above the character's capsules, so looking
    // steeply down (bones / items on the floor) used to hit the player's own CapsuleCollider first.
    private bool RaycastIgnoringSelf(Ray ray, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(ray, _hits, _interactRange, _interactLayer);
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].collider.transform.IsChildOf(transform)) continue;
            if (_hits[i].distance < bestDist) { bestDist = _hits[i].distance; best = _hits[i]; }
        }
        return bestDist < float.MaxValue;
    }

    // Circular aim assist: a sphere sweep along the look ray finds IAimAssistTarget interactables whose centre lies within
    // their AssistRadius of the ray; the one nearest the ray wins. A target is skipped when something solid is between the
    // eye and it (so a bone behind a wall can't be picked), and the one we already target gets a small hysteresis so the
    // highlight doesn't flip between two bones that are almost equally close.
    private IInteractable FindAimAssistTarget(Ray ray)
    {
        int n = Physics.SphereCastNonAlloc(ray, _assistSweepRadius, _assistHits, _assistRange, _interactLayer, QueryTriggerInteraction.Ignore);
        IInteractable best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Collider col = _assistHits[i].collider;
            if (col == null || col.transform.IsChildOf(transform)) continue;
            var target = col.GetComponentInParent<IAimAssistTarget>();
            if (target is not IInteractable interactable || target is not Component tc || !tc.gameObject.activeInHierarchy) continue;

            Vector3 centre = col.bounds.center;
            Vector3 toCentre = centre - ray.origin;
            float along = Vector3.Dot(toCentre, ray.direction);
            if (along < 0f || along > _assistRange) continue;
            float off = Vector3.Cross(ray.direction, toCentre).magnitude;   // distance from the look ray
            if (off > target.AssistRadius) continue;
            if (ViewBlocked(ray.origin, centre, tc.transform)) continue;

            float score = off - (ReferenceEquals(interactable, _currentTarget) ? 0.05f : 0f);
            if (score < bestScore) { bestScore = score; best = interactable; }
        }
        return best;
    }

    // True when a solid (non-trigger, not us, not the target itself) collider lies on the segment eye -> point.
    private bool ViewBlocked(Vector3 from, Vector3 to, Transform target)
    {
        Vector3 d = to - from;
        float dist = d.magnitude;
        if (dist < 0.01f) return false;
        int n = Physics.RaycastNonAlloc(from, d / dist, _blockHits, dist, _interactLayer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Transform t = _blockHits[i].collider.transform;
            if (t.IsChildOf(transform) || t.IsChildOf(target)) continue;
            // Loose remains (other bones) and carried items don't block the view of a bone lying behind them.
            if (_blockHits[i].collider.GetComponentInParent<IAimAssistTarget>() != null) continue;
            return true;
        }
        return false;
    }

    private void OnInteract()
    {
        if (_currentTarget is Component tc && (tc == null || !tc.gameObject.activeInHierarchy)) ClearTarget();
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
