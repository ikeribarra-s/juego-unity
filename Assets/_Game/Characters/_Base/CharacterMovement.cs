using UnityEngine;

[RequireComponent(typeof(CharacterBase))]
public class CharacterMovement : MonoBehaviour
{
    [SerializeField] protected Transform _cameraRoot;

    [Header("Crouch")]
    [SerializeField] private float _crouchHeight        = 0.9f;
    [SerializeField] private float _crouchCameraY       = 0.5f;
    [SerializeField] private float _crouchTransitionSpeed = 12f;

    protected CharacterBase       _character;
    protected CharacterController _controller;

    protected Vector2 _moveInput;
    protected Vector2 _lookInput;
    protected bool    _isSprinting;
    protected bool    _isCrouching;
    protected bool    _jumpRequested;
    protected float   _cameraPitch;
    protected float   _verticalVelocity;

    private float _standHeight;
    private float _standCenterY;
    private float _standCameraY;

    protected virtual void Awake()
    {
        _character  = GetComponent<CharacterBase>();
        _controller = GetComponent<CharacterController>();

        // Read stand values from whatever the user set up in the Inspector
        _standHeight  = _controller.height;
        _standCenterY = _controller.center.y;
        _standCameraY = _cameraRoot.localPosition.y;
    }

    protected virtual void OnEnable()
    {
        _character.Input.MoveEvent   += OnMove;
        _character.Input.LookEvent   += OnLook;
        _character.Input.SprintEvent += OnSprint;
        _character.Input.CrouchEvent += OnCrouch;
        _character.Input.JumpEvent   += OnJump;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    protected virtual void OnDisable()
    {
        _character.Input.MoveEvent   -= OnMove;
        _character.Input.LookEvent   -= OnLook;
        _character.Input.SprintEvent -= OnSprint;
        _character.Input.CrouchEvent -= OnCrouch;
        _character.Input.JumpEvent   -= OnJump;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    protected virtual void Update()
    {
        ApplyLook();
        ApplyCrouch();
        ApplyMovement();
    }

    // ── Look ─────────────────────────────────────────────────────────

    protected virtual void ApplyLook()
    {
        if (_lookInput == Vector2.zero) return;

        float sens = _character.Stats.LookSensitivity;

        transform.Rotate(Vector3.up * (_lookInput.x * sens));

        _cameraPitch = Mathf.Clamp(_cameraPitch - _lookInput.y * sens, -80f, 80f);
        _cameraRoot.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
    }

    // ── Crouch ───────────────────────────────────────────────────────

    protected virtual void ApplyCrouch()
    {
        float targetHeight  = _isCrouching ? _crouchHeight  : _standHeight;
        float targetCenterY = _isCrouching ? _crouchHeight * 0.5f : _standCenterY;
        float targetCameraY = _isCrouching ? _crouchCameraY : _standCameraY;

        _controller.height = targetHeight;
        _controller.center = new Vector3(0f, targetCenterY, 0f);

        var pos = _cameraRoot.localPosition;
        pos.y = Mathf.Lerp(pos.y, targetCameraY, Time.deltaTime * _crouchTransitionSpeed);
        _cameraRoot.localPosition = pos;
    }

    // ── Movement ─────────────────────────────────────────────────────

    protected virtual void ApplyMovement()
    {
        ApplyGravityAndJump();

        CharacterStats stats = _character.Stats;
        float speed = stats.MoveSpeed;
        if (_isSprinting) speed *= stats.SprintMultiplier;
        if (_isCrouching) speed *= stats.CrouchSpeedMultiplier;

        Vector3 move = transform.right   * _moveInput.x
                     + transform.forward * _moveInput.y;
        move         *= speed;
        move.y        = _verticalVelocity;

        _controller.Move(move * Time.deltaTime);
    }

    // Shared helper — call this at the top of any override of ApplyMovement
    protected void ApplyGravityAndJump()
    {
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        if (_controller.isGrounded && _jumpRequested)
        {
            _verticalVelocity = _character.Stats.JumpForce;
            _jumpRequested    = false;
        }

        _verticalVelocity += _character.Stats.Gravity * Time.deltaTime;
    }

    // ── Input callbacks ──────────────────────────────────────────────

    protected virtual void OnMove(Vector2 input)    => _moveInput   = input;
    protected virtual void OnLook(Vector2 input)    => _lookInput   = input;
    protected virtual void OnSprint(bool sprinting) => _isSprinting = sprinting;

    protected virtual void OnJump()
    {
        if (_controller.isGrounded)
            _jumpRequested = true;
    }

    protected virtual void OnCrouch(bool pressed)
    {
        if (pressed)
        {
            _isCrouching = true;
        }
        else
        {
            if (CanStand()) _isCrouching = false;
        }
    }

    private bool CanStand()
    {
        float castDistance = _standHeight - _crouchHeight;
        Vector3 origin     = transform.position + Vector3.up * (_crouchHeight - _controller.radius);
        return !Physics.SphereCast(origin, _controller.radius * 0.9f, Vector3.up, out _, castDistance);
    }
}
