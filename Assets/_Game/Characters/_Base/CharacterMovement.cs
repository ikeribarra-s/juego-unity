using UnityEngine;

[RequireComponent(typeof(CharacterBase))]
public class CharacterMovement : MonoBehaviour
{
    [Header("Crouch")]
    [SerializeField] private float _crouchHeight          = 0.9f;
    [SerializeField] private float _crouchCameraY         = 0.5f;
    [SerializeField] private float _crouchTransitionSpeed = 12f;
    [SerializeField] private bool  _debugCrouch           = false;

    protected CharacterBase       _character;
    protected CharacterController _controller;

    protected Vector2 _moveInput;
    protected Vector2 _lookInput;
    protected bool    _isSprinting;
    protected bool    _isCrouching;
    protected bool    _crouchHeld;      // raw input state — _isCrouching can lag it while a ceiling blocks standing
    protected bool    _jumpRequested;
    protected float   _cameraPitch;
    protected float   _verticalVelocity;

    public bool IsSprinting => _isSprinting;
    public bool IsCrouching => _isCrouching;

    // ── State exposed for AnimationHandler (the animator only reflects movement) ──
    public bool  IsGrounded       => _controller != null && _controller.isGrounded;
    public float VerticalVelocity => _verticalVelocity;
    public Vector3 PlanarVelocity
    {
        get
        {
            if (_controller == null) return Vector3.zero;
            Vector3 v = _controller.velocity;
            v.y = 0f;
            return v;
        }
    }

    /// <summary>Fired the frame a jump impulse is applied. Consumed by AnimationHandler.</summary>
    public event System.Action Jumped;

    private float _standHeight;
    private float _standCameraY;
    private readonly RaycastHit[] _standHits = new RaycastHit[8];

    protected virtual void Awake()
    {
        _character  = GetComponent<CharacterBase>();
        _controller = GetComponent<CharacterController>();

        _standHeight  = _controller.height;
        _standCameraY = _character.CameraRoot != null ? _character.CameraRoot.localPosition.y : 1.6f;
    }

    protected virtual void OnEnable()
    {
        if (_character == null) _character = GetComponent<CharacterBase>();

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
        if (_character == null) return;

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
        if (_character.Stats == null) return;
        ApplyLook();
        ApplyCrouch();
        ApplyMovement();
    }

    // ── Look ─────────────────────────────────────────────────────────

    protected virtual void ApplyLook()
    {
        if (_lookInput == Vector2.zero || _character.CameraRoot == null) return;

        float sens = _character.Stats.LookSensitivity;
        transform.Rotate(Vector3.up * (_lookInput.x * sens));

        _cameraPitch = Mathf.Clamp(_cameraPitch - _lookInput.y * sens, -80f, 80f);
        _character.CameraRoot.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
    }

    // ── Crouch ───────────────────────────────────────────────────────

    protected virtual void ApplyCrouch()
    {
        if (_character.CameraRoot == null) return;

        // Hold-to-crouch: once the key is released, stand as soon as there is headroom. Re-checked
        // every frame so releasing under a low ceiling stands you up automatically on leaving it
        // (previously it only checked once on release, so you stayed crouched until re-pressing → felt like a toggle).
        if (_isCrouching && !_crouchHeld && CanStand())
        {
            _isCrouching = false;
            if (_debugCrouch) Debug.Log("[Crouch] headroom clear — standing up", this);
        }

        float targetHeight  = _isCrouching ? _crouchHeight : _standHeight;
        float targetCameraY = _isCrouching ? _crouchCameraY : _standCameraY;

        // Lerp the collider in lockstep with the camera so hitbox and view stay in sync.
        // Center tracks height/2 every frame so the feet stay planted at y=0 during the blend.
        float h = Mathf.Lerp(_controller.height, targetHeight, Time.deltaTime * _crouchTransitionSpeed);
        _controller.height = h;
        // + skinWidth: the controller rests one skin width above the floor, so lifting the capsule by it puts the
        // character root (and the model's soles) exactly on the floor.
        _controller.center = new Vector3(0f, h * 0.5f + _controller.skinWidth, 0f);

        var pos = _character.CameraRoot.localPosition;
        pos.y = Mathf.Lerp(pos.y, targetCameraY, Time.deltaTime * _crouchTransitionSpeed);
        _character.CameraRoot.localPosition = pos;
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

    protected void ApplyGravityAndJump()
    {
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        if (_controller.isGrounded && _jumpRequested)
        {
            _verticalVelocity = _character.Stats.JumpForce;
            _jumpRequested    = false;
            Jumped?.Invoke();
        }

        _verticalVelocity += _character.Stats.Gravity * Time.deltaTime;
    }

    // ── Input callbacks ──────────────────────────────────────────────

    protected virtual void OnMove(Vector2 input)    => _moveInput   = input;
    protected virtual void OnLook(Vector2 input)    => _lookInput   = input;
    protected virtual void OnSprint(bool sprinting) => _isSprinting = sprinting;

    protected virtual void OnJump()
    {
        // Space doubles as the skill-check hit while repairing a generator — swallow the jump then.
        if (SkillCheckController.Active) return;
        if (_controller.isGrounded)
            _jumpRequested = true;
    }

    protected virtual void OnCrouch(bool pressed)
    {
        if (_debugCrouch)
            Debug.Log($"[Crouch] event received: pressed={pressed}, wasCrouching={_isCrouching}", this);

        _crouchHeld = pressed;
        if (pressed)
            _isCrouching = true;
        else if (_debugCrouch && !CanStand())
            Debug.Log("[Crouch] release blocked — ceiling above, staying crouched until clear", this);
        // Standing up on release happens in ApplyCrouch (same frame if clear, later if blocked).
    }

    private bool CanStand()
    {
        // Cast from the top of the current (crouched) capsule up through the gap that
        // standing would reclaim. Starting at the capsule top avoids the self-overlap
        // that would otherwise make SphereCast ignore a low ceiling.
        float radius = _controller.radius;
        Vector3 top  = transform.position + Vector3.up * (_controller.center.y + _controller.height * 0.5f - radius);
        float gap    = _standHeight - _controller.height;
        if (gap <= 0f) return true;

        // Ignore triggers (extraction zone etc.) and the character's own colliders/held items,
        // which would otherwise pin the player crouched.
        int count = Physics.SphereCastNonAlloc(top, radius * 0.9f, Vector3.up, _standHits, gap + 0.05f,
                                               ~0, QueryTriggerInteraction.Ignore);
        Collider blocker = null;
        for (int i = 0; i < count; i++)
        {
            Collider c = _standHits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            blocker = c;
            break;
        }
        bool blocked = blocker != null;

        if (_debugCrouch)
        {
            Debug.DrawRay(top, Vector3.up * (gap + 0.05f), blocked ? Color.red : Color.green, 1f);
            if (blocked)
                Debug.Log($"[Crouch] CanStand=false, blocked by '{blocker.name}'", this);
        }

        return !blocked;
    }
}
