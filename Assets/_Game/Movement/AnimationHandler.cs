using UnityEngine;

/// <summary>
/// Bridges the custom CharacterController-based movement to Unity's Starter Assets
/// humanoid Animator Controller (StarterAssetsThirdPerson). Each frame it reads
/// movement state from <see cref="CharacterMovement"/> and pushes it into the
/// controller's parameters: float "Speed", float "MotionSpeed", bools "Grounded",
/// "Jump", "FreeFall".
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
    [Tooltip("Grace period (s) after leaving the ground before FreeFall is set — avoids flicker on small steps.")]
    [SerializeField] private float _fallTimeout = 0.15f;
    [Tooltip("How long (s) after a jump to treat the character as airborne, regardless of " +
             "CharacterController.isGrounded (which lags a frame after the jump impulse).")]
    [SerializeField] private float _jumpHoldTime = 0.2f;

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
    [SerializeField] private float _lookSmoothing = 12f;

    // Parameter hashes for the StarterAssetsThirdPerson controller.
    private static readonly int SpeedHash       = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
    private static readonly int GroundedHash    = Animator.StringToHash("Grounded");
    private static readonly int JumpHash        = Animator.StringToHash("Jump");
    private static readonly int FreeFallHash    = Animator.StringToHash("FreeFall");

    private Animator          _animator;
    private CharacterMovement _movement;
    private Transform         _lookSource;   // CameraRoot — carries the look yaw + pitch

    private float   _fallTimer;
    private float   _jumpHold;
    private Vector3 _smoothedLookPoint;
    private bool    _lookInit;

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        var character = GetComponentInParent<CharacterBase>();
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
        _animator.SetBool(JumpHash, true);
        _animator.SetBool(FreeFallHash, false);
        // Force the airborne state to register even though isGrounded lags a frame.
        _jumpHold  = _jumpHoldTime;
        _fallTimer = _fallTimeout;
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

        // Planar speed (m/s) drives the locomotion blend tree.
        float speed = _movement.PlanarVelocity.magnitude;
        _animator.SetFloat(SpeedHash, speed, _speedDampTime, Time.deltaTime);
        _animator.SetFloat(MotionSpeedHash, 1f);

        if (grounded)
        {
            _fallTimer = _fallTimeout;
            _animator.SetBool(JumpHash, false);
            _animator.SetBool(FreeFallHash, false);
        }
        else
        {
            if (_fallTimer > 0f) _fallTimer -= Time.deltaTime;

            // Enter FreeFall once we're past the jump apex (descending). Basing this on
            // vertical velocity makes it scale automatically with Gravity / JumpForce —
            // a higher jump or weaker gravity simply spends longer in the rising state.
            // The timer still suppresses flicker on small steps where isGrounded briefly
            // drops without any real fall.
            bool descending = _movement.VerticalVelocity < -0.2f;
            if (_fallTimer <= 0f && descending)
                _animator.SetBool(FreeFallHash, true);
        }
    }

    // ── Procedural head/neck look ───────────────────────────────────────────────
    // Called by the Animator after the animation pass — only fires when the layer
    // has "IK Pass" enabled in the controller.
    private void OnAnimatorIK(int layerIndex)
    {
        if (!_headLook || _lookSource == null) return;

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
        _smoothedLookPoint = Vector3.Lerp(_smoothedLookPoint, target, Time.deltaTime * _lookSmoothing);

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
