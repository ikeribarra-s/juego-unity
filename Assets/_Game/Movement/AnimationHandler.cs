using UnityEngine;

/// <summary>
/// Bridges the custom CharacterController-based movement to Unity's Starter Assets
/// humanoid Animator Controller (StarterAssetsThirdPerson). Each frame it reads
/// movement state from <see cref="CharacterMovement"/> and pushes it into the
/// controller's parameters (PlayerAnimator.controller, UAL locomotion clips):
///   floats MoveX / MoveZ (planar velocity in character-local space, m/s, damped), Speed (|planar|, damped),
///   VelocityY, MotionSpeed; bools Grounded, FreeFall (airborne &gt; _fallTimeout), Crouch; trigger Jump.
///
/// Also drives a procedural head/neck look via Unity's built-in LookAt IK so the
/// character's head turns toward wherever the player is aiming the camera.
///
/// No movement is driven from here — the CharacterController remains the source of
/// truth and the animator only *reflects* what it already did (Root Motion OFF).
///
/// Lives on the model child (the GameObject that carries the Animator). Requires the
/// Animator layer to have "IK Pass" enabled for the head look to run.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimationHandler : MonoBehaviour
{
    [Header("Locomotion Tuning")]
    [Tooltip("Damping (s) applied to the Speed parameter so the locomotion blend is smooth.")]
    [SerializeField] private float _speedDampTime = 0.1f;
    [Tooltip("Time (s) in the air before FreeFall (-> InAir pose) is set — avoids flicker on small steps / ledges.")]
    [SerializeField] private float _fallTimeout = 0.2f;
    [Tooltip("How long (s) after a jump to treat the character as airborne, regardless of " +
             "CharacterController.isGrounded (which lags a frame after the jump impulse).")]
    [SerializeField] private float _jumpHoldTime = 0.2f;

    [Tooltip("Start the landing pose this long (s) before touchdown. Covers the stepped Animator (up to ~50 ms late) and the " +
             "InAir -> JumpLand fade, so the hanging leg of the fall pose never sinks into the floor. 0 = land on contact.")]
    [SerializeField] private float _landLead = 0.13f;

    [Header("Head Look IK")]
    [SerializeField] private bool _headLook = true;
    [Tooltip("Overall blend of the whole look-at solve.")]
    [SerializeField, Range(0f, 1f)] private float _lookWeight  = 1f;
    [Tooltip("How much the spine/chest turns toward the look target.")]
    [SerializeField, Range(0f, 1f)] private float _bodyWeight  = 0.25f;
    [Tooltip("How much the head turns toward the look target.")]
    [SerializeField, Range(0f, 1f)] private float _headWeight  = 0.85f;
    [Tooltip("How much the eyes turn (needs eye bones on the avatar, otherwise ignored).")]
    [SerializeField, Range(0f, 1f)] private float _eyesWeight  = 0.5f;
    [Tooltip("Clamps how far the look can rotate from forward (0 = no clamp, 1 = fully clamped).")]
    [SerializeField, Range(0f, 1f)] private float _clampWeight = 0.5f;
    [Tooltip("Far distance used as the look target when the aim ray hits nothing.")]
    [SerializeField] private float _lookDistance = 20f;
    [Tooltip("The look target is never placed closer than this, so the neck doesn't crank unnaturally.")]
    [SerializeField] private float _minLookDistance = 0.6f;
    [Tooltip("Layers the aim ray can hit. Exclude the player/character layer so the ray ignores its own body.")]
    [SerializeField] private LayerMask _lookRayMask = ~0;
    [Tooltip("Look-target response (1/s), applied with the REAL time since the last IK evaluation. OnAnimatorIK only runs on " +
             "RetroAnimationStepper steps, so a per-call 'dt * k' lerp (the old code) used one frame's dt per ~50 ms step and " +
             "lagged ~0.25 s. 0 = no smoothing.")]
    [SerializeField] private float _lookResponse = 40f;
    [Tooltip("Debug: use the legacy per-call lerp (dt of one frame) to reproduce the old lag.")]
    [SerializeField] private bool _legacyLookLag;
    private float _lastLookIkTime = -1f;

    // Parameter hashes for the StarterAssetsThirdPerson controller.
    private static readonly int SpeedHash       = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int GroundedHash    = Animator.StringToHash("Grounded");
    private static readonly int JumpHash        = Animator.StringToHash("Jump");       // trigger
    private static readonly int FreeFallHash    = Animator.StringToHash("FreeFall");
    private static readonly int MoveXHash       = Animator.StringToHash("MoveX");
    private static readonly int MoveZHash       = Animator.StringToHash("MoveZ");
    private static readonly int VelocityYHash   = Animator.StringToHash("VelocityY");
    private static readonly int CrouchHash      = Animator.StringToHash("Crouch");
    private static readonly int LandingHash     = Animator.StringToHash("Landing");    // falling and about to touch down

    private Animator          _animator;
    private CharacterMovement _movement;
    private CharacterBase     _character;
    private readonly RaycastHit[] _landHits = new RaycastHit[8];
    private Transform         _lookSource;   // CameraRoot — carries the look yaw + pitch

    private float   _fallTimer;
    private float   _jumpHold;
    private Vector3 _smoothedLookPoint;
    private bool    _lookInit;

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        var character = GetComponentInParent<CharacterBase>();
        _character  = character;
        _movement   = GetComponentInParent<CharacterMovement>();
        _lookSource = character != null ? character.CameraRoot : null;

        if (_movement == null)
            Debug.LogError("[AnimationHandler] No CharacterMovement found in parents. " +
                           "Place this on the model child of a character.", this);
    }

    private void OnEnable()
    {
        if (_movement != null) _movement.Jumped += OnJumped;
    }

    private void OnDisable()
    {
        if (_movement != null) _movement.Jumped -= OnJumped;
    }

    private void OnJumped()
    {
        _animator.SetTrigger(JumpHash);
        _animator.SetBool(FreeFallHash, false);
        // Force the airborne state to register even though isGrounded lags a frame.
        _jumpHold  = _jumpHoldTime;
        _fallTimer = 0f;   // airborne clock restarts: FreeFall only after _fallTimeout in the air
    }

    private void Update()
    {
        if (_movement == null) return;

        bool grounded = _movement.IsGrounded;

        // Right after a jump, isGrounded is still true for a frame or two. Treat the
        // character as airborne during the hold window so Jump/Grounded don't reset early.
        if (_jumpHold > 0f)
        {
            _jumpHold -= Time.deltaTime;
            grounded = false;
        }

        _animator.SetBool(GroundedHash, grounded);

        // Planar velocity in the character's local space (x = right, z = forward), m/s. SetFloat's dampTime uses the
        // real frame dt; the Animator itself is only evaluated on RetroAnimationStepper steps, which read the latest value.
        Vector3 pv = _movement.PlanarVelocity;
        Vector3 local = _movement.transform.InverseTransformDirection(pv);
        _animator.SetFloat(MoveXHash, local.x, _speedDampTime, Time.deltaTime);
        _animator.SetFloat(MoveZHash, local.z, _speedDampTime, Time.deltaTime);
        _animator.SetFloat(SpeedHash, pv.magnitude, _speedDampTime, Time.deltaTime);
        _animator.SetFloat(VelocityYHash, _movement.VerticalVelocity);
        _animator.SetFloat(MotionSpeedHash, 1f);
        _animator.SetBool(CrouchHash, _movement.IsCrouching);
        _animator.SetBool(LandingHash, !grounded && AboutToLand());

        if (grounded)
        {
            _fallTimer = 0f;
            _animator.SetBool(FreeFallHash, false);
        }
        else
        {
            // Airborne without a jump (ledge, step-down): FreeFall -> InAir after _fallTimeout. Small steps never
            // reach it. (A jump goes through the Jump trigger -> JumpStart -> InAir instead.)
            _fallTimer += Time.deltaTime;
            if (_fallTimer >= _fallTimeout) _animator.SetBool(FreeFallHash, true);
        }
    }

    // True while falling and the floor under the capsule will be reached within _landLead seconds (gravity included).
    private bool AboutToLand()
    {
        float fall = -_movement.VerticalVelocity;
        if (_landLead <= 0f || fall <= 0.5f || _character == null || _character.Controller == null || _character.Stats == null) return false;

        float g     = -_character.Stats.Gravity;
        float reach = fall * _landLead + 0.5f * g * _landLead * _landLead;   // distance fallen in _landLead
        float r     = _character.Controller.radius * 0.9f;
        Transform root = _movement.transform;
        Vector3 origin = root.position + Vector3.up * (r + 0.1f);
        int n = Physics.SphereCastNonAlloc(origin, r, Vector3.down, _landHits, reach + 0.1f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!_landHits[i].collider.transform.IsChildOf(root) && _landHits[i].normal.y > 0.5f) return true;
        return false;
    }

    // ── Procedural head/neck look ───────────────────────────────────────────────
    // Called by the Animator after the animation pass — only fires when the layer
    // has "IK Pass" enabled in the controller.
    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || !_headLook || _lookSource == null) return;   // the torch arm layer has its own IK pass (pick-up reach)

        // Aim a ray along the look direction so the head converges on whatever the
        // player is actually looking at; fall back to a far point when it hits nothing.
        Vector3 origin = _lookSource.position;
        Vector3 dir    = _lookSource.forward;
        Vector3 target = origin + dir * _lookDistance;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, _lookDistance, _lookRayMask, QueryTriggerInteraction.Ignore))
            target = hit.point;

        // Don't let the target sit so close that the neck twists unnaturally.
        if ((target - origin).sqrMagnitude < _minLookDistance * _minLookDistance)
            target = origin + dir * _minLookDistance;

        if (!_lookInit) { _smoothedLookPoint = target; _lookInit = true; }
        float stepDt = _lastLookIkTime < 0f ? 0f : Mathf.Clamp(Time.time - _lastLookIkTime, 0f, 0.25f);
        _lastLookIkTime = Time.time;
        float k = _legacyLookLag ? Mathf.Clamp01(Time.deltaTime * 12f)
                : (_lookResponse <= 0f ? 1f : 1f - Mathf.Exp(-stepDt * _lookResponse));
        _smoothedLookPoint = Vector3.Lerp(_smoothedLookPoint, target, k);

        _animator.SetLookAtWeight(_lookWeight, _bodyWeight, _headWeight, _eyesWeight, _clampWeight);
        _animator.SetLookAtPosition(_smoothedLookPoint);
    }

    // ── Animation events fired by the Starter Assets clips ──────────────────────
    // CharacterAudio already handles footsteps/landing, so these are intentional
    // no-ops that keep Unity from logging "no receiver" warnings. The receiver must
    // sit on the same GameObject as the Animator — which is exactly here.
    private void OnFootstep(AnimationEvent _) { }
    private void OnLand(AnimationEvent _) { }
}
