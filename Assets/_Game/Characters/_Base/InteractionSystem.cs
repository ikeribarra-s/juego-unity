using UnityEngine;

public class InteractionSystem : MonoBehaviour
{
    [SerializeField] private float     _interactRange = 2.5f;
    [SerializeField] private LayerMask _interactLayer = ~0;

    private CharacterBase _character;
    private Camera        _cam;
    private IInteractable _currentTarget;
    private string        _currentPrompt;

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
        _character.Input.InteractStartedEvent += OnInteract;
    }

    private void OnDisable()
    {
        _character.Input.InteractStartedEvent -= OnInteract;
    }

    private void Update()
    {
        if (_cam == null) return;

        Ray ray = new(_cam.transform.position, _cam.transform.forward);
        Debug.DrawRay(ray.origin, ray.direction * _interactRange, Color.cyan);

        if (Physics.Raycast(ray, out RaycastHit hit, _interactRange, _interactLayer))
        {
            var interactable = hit.collider.GetComponent<IInteractable>();

            if (interactable != null)
            {
                if (_currentTarget != interactable)
                {
                    _currentTarget = interactable;
                    _currentPrompt = interactable.GetPrompt();
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
    }

    private void OnInteract()
    {
        if (_currentTarget == null)
        {
            Debug.Log("[Interaction] E pressed — nothing in range.");
            return;
        }

        Debug.Log($"[Interaction] Interacting with \"{_currentTarget.GetPrompt()}\"");
        _currentTarget.Interact(_character);
    }

    private void ClearTarget()
    {
        if (_currentTarget == null) return;
        _currentTarget = null;
        _currentPrompt = null;
    }
}
