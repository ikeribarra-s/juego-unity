using System;
using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "InputReader", menuName = "Fluffterror/Input Reader")]
public class InputReader : ScriptableObject
{
    [SerializeField] private InputActionAsset _actions;

    // --- Events that characters subscribe to ---
    public event Action<Vector2> MoveEvent;
    public event Action<Vector2> LookEvent;
    public event Action InteractStartedEvent;
    public event Action InteractCanceledEvent;
    public event Action UseAbilityEvent;
    public event Action DropItemEvent;
    public event Action<bool> CrouchEvent;    // true = pressed, false = released
    public event Action<bool> SprintEvent;    // true = pressed, false = released

    private InputAction _move;
    private InputAction _look;
    private InputAction _interact;
    private InputAction _useAbility;
    private InputAction _dropItem;
    private InputAction _crouch;
    private InputAction _sprint;

    private void OnEnable()
    {
        if (_actions == null) return;

        var playerMap = _actions.FindActionMap("Player", throwIfNotFound: true);

        _move       = playerMap.FindAction("Move",       throwIfNotFound: true);
        _look       = playerMap.FindAction("Look",       throwIfNotFound: true);
        _interact   = playerMap.FindAction("Interact",   throwIfNotFound: true);
        _useAbility = playerMap.FindAction("UseAbility", throwIfNotFound: true);
        _dropItem   = playerMap.FindAction("DropItem",   throwIfNotFound: true);
        _crouch     = playerMap.FindAction("Crouch",     throwIfNotFound: true);
        _sprint     = playerMap.FindAction("Sprint",     throwIfNotFound: true);

        _move.performed       += OnMove;
        _move.canceled        += OnMoveCanceled;
        _look.performed       += OnLook;
        _look.canceled        += OnLookCanceled;
        _interact.started     += OnInteractStarted;
        _interact.canceled    += OnInteractCanceled;
        _useAbility.performed += OnUseAbility;
        _dropItem.performed   += OnDropItem;
        _crouch.performed     += OnCrouchPressed;
        _crouch.canceled      += OnCrouchReleased;
        _sprint.performed     += OnSprintPressed;
        _sprint.canceled      += OnSprintReleased;

        playerMap.Enable();
    }

    private void OnDisable()
    {
        if (_move == null) return;

        _move.performed       -= OnMove;
        _move.canceled        -= OnMoveCanceled;
        _look.performed       -= OnLook;
        _look.canceled        -= OnLookCanceled;
        _interact.started     -= OnInteractStarted;
        _interact.canceled    -= OnInteractCanceled;
        _useAbility.performed -= OnUseAbility;
        _dropItem.performed   -= OnDropItem;
        _crouch.performed     -= OnCrouchPressed;
        _crouch.canceled      -= OnCrouchReleased;
        _sprint.performed     -= OnSprintPressed;
        _sprint.canceled      -= OnSprintReleased;

        _actions?.FindActionMap("Player")?.Disable();
    }

    private void OnMove(InputAction.CallbackContext ctx)         => MoveEvent?.Invoke(ctx.ReadValue<Vector2>());
    private void OnMoveCanceled(InputAction.CallbackContext ctx) => MoveEvent?.Invoke(Vector2.zero);
    private void OnLook(InputAction.CallbackContext ctx)         => LookEvent?.Invoke(ctx.ReadValue<Vector2>());
    private void OnLookCanceled(InputAction.CallbackContext ctx) => LookEvent?.Invoke(Vector2.zero);
    private void OnInteractStarted(InputAction.CallbackContext ctx)  => InteractStartedEvent?.Invoke();
    private void OnInteractCanceled(InputAction.CallbackContext ctx) => InteractCanceledEvent?.Invoke();
    private void OnUseAbility(InputAction.CallbackContext ctx)   => UseAbilityEvent?.Invoke();
    private void OnDropItem(InputAction.CallbackContext ctx)     => DropItemEvent?.Invoke();
    private void OnCrouchPressed(InputAction.CallbackContext ctx)  => CrouchEvent?.Invoke(true);
    private void OnCrouchReleased(InputAction.CallbackContext ctx) => CrouchEvent?.Invoke(false);
    private void OnSprintPressed(InputAction.CallbackContext ctx)  => SprintEvent?.Invoke(true);
    private void OnSprintReleased(InputAction.CallbackContext ctx) => SprintEvent?.Invoke(false);
}
