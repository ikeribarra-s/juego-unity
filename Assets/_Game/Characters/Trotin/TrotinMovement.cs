using UnityEngine;

public class TrotinMovement : CharacterMovement
{
    // ── Tunables ────────────────────────────────────────────────────
    // A/D intentionally unused — Trotín is steered by mouse only

    [Header("Trotín – Sprint")]
    [SerializeField] private float _sprintMultiplier = 3f;
    [SerializeField] private float _sprintDuration   = 5f;
    [SerializeField] private float _sprintCooldown   = 25f;

    [Header("Trotín – Flying")]
    [SerializeField] private float _flyingBurstMultiplier = 2f;   // × sprint speed
    [SerializeField] private float _flyingDrag            = 1.5f; // exponential drag
    [SerializeField] private float _flyingUpward          = 2f;   // small hop on launch
    [SerializeField] private float _maxFlyingDuration     = 3f;

    // ── State ────────────────────────────────────────────────────────
    private enum State { Moving, Sprinting, Flying }
    private State _state = State.Moving;

    private Vector3 _flyingVelocity;
    private float   _sprintTimer;
    private float   _flyingTimer;
    private float   _cooldownTimer;

    public bool IsFlying    => _state == State.Flying;
    public bool IsSprinting => _state == State.Sprinting;
    public float SprintCooldownRemaining => Mathf.Max(0f, _cooldownTimer);
    public float SprintTimeRemaining     => Mathf.Max(0f, _sprintTimer);

    // ── Lifecycle ────────────────────────────────────────────────────
    protected override void OnEnable()
    {
        base.OnEnable();
        _character.Input.UseAbilityEvent += TryActivateSprint;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        _character.Input.UseAbilityEvent -= TryActivateSprint;
    }

    protected override void Update()
    {
        if (_cooldownTimer > 0f)
            _cooldownTimer -= Time.deltaTime;

        base.Update(); // ApplyLook + ApplyMovement
    }

    // ── Movement override ────────────────────────────────────────────
    protected override void ApplyMovement()
    {
        ApplyGravityAndJump();

        switch (_state)
        {
            case State.Moving:    HandleMoving();    break;
            case State.Sprinting: HandleSprinting(); break;
            case State.Flying:    HandleFlying();    break;
        }
    }

    private void HandleMoving()
    {
        Vector3 move = transform.forward * _character.Stats.MoveSpeed;
        move.y = _verticalVelocity;
        _controller.Move(move * Time.deltaTime);
    }

    private void HandleSprinting()
    {
        _sprintTimer -= Time.deltaTime;

        // S key (backward input) or timer expired → go flying
        if (_moveInput.y < -0.3f || _sprintTimer <= 0f)
        {
            EnterFlying();
            return;
        }

        float speed = _character.Stats.MoveSpeed * _sprintMultiplier;
        Vector3 move = transform.forward * speed;
        move.y = _verticalVelocity;
        _controller.Move(move * Time.deltaTime);
    }

    private void HandleFlying()
    {
        _flyingTimer -= Time.deltaTime;

        // Exponential drag on horizontal velocity
        _flyingVelocity = Vector3.Lerp(_flyingVelocity, Vector3.zero, _flyingDrag * Time.deltaTime);

        Vector3 move = _flyingVelocity;
        move.y = _verticalVelocity;
        _controller.Move(move * Time.deltaTime);

        float horizontalSpeed = new Vector2(_flyingVelocity.x, _flyingVelocity.z).magnitude;
        bool slowedDown = horizontalSpeed <= _character.Stats.MoveSpeed;

        if (slowedDown || _flyingTimer <= 0f)
            EnterMoving();
    }

    // ── State transitions ────────────────────────────────────────────
    private void TryActivateSprint()
    {
        if (_state != State.Moving) return;
        if (_cooldownTimer > 0f)
        {
            Debug.Log($"[Trotín] Sprint on cooldown ({_cooldownTimer:F1}s remaining).");
            return;
        }

        _state      = State.Sprinting;
        _sprintTimer = _sprintDuration;
        Debug.Log("[Trotín] Sprint activated!");
    }

    private void EnterFlying()
    {
        _state       = State.Flying;
        _flyingTimer = _maxFlyingDuration;
        _cooldownTimer = _sprintCooldown;

        float burstSpeed = _character.Stats.MoveSpeed * _sprintMultiplier * _flyingBurstMultiplier;
        _flyingVelocity  = transform.forward * burstSpeed;
        _verticalVelocity = _flyingUpward;

        Debug.Log($"[Trotín] FLYING! Burst speed: {burstSpeed:F1} m/s");
    }

    private void EnterMoving()
    {
        _state = State.Moving;
        _flyingVelocity = Vector3.zero;
        Debug.Log("[Trotín] Back to normal movement.");
    }

    // Block OnSprint — Shift does nothing for Trotín (his sprint is the Q ability)
    protected override void OnSprint(bool sprinting) { }
}
