using UnityEngine;

/// <summary>
/// Procedural cartoony layer stacked on top of the Mixamo clips (squash and stretch, bouncier hips,
/// lean into acceleration / turns with overshoot, bigger right-arm swing). Lives on the model child.
///
/// Body motion (hips bounce + lean) is applied in <c>OnAnimatorIK</c>, i.e. on the same stepped
/// frames as the body (<see cref="RetroAnimationStepper"/>), so the retro look is preserved and bones
/// are never accumulated. Squash scales the model transform in LateUpdate (smooth, every frame).
///
/// First person: the local player's camera is anchored to the neck and the lantern arm is placed in the
/// chest/view frame, so squash, hip bounce and lean are scaled by <see cref="_localStrength"/> (default 0)
/// on the local player: camera and lantern framing stay exactly as before. Remote players get all of it.
/// The right-arm boost applies to everyone; the left arm is never touched.
/// </summary>
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(60)]
public class CartoonAnimator : MonoBehaviour
{
    [Tooltip("Master toggle for the whole layer.")]
    [SerializeField] private bool _enabled = true;
    [Tooltip("Treat this model as the local first-person player. Auto = has a NeckFollowCamera with a camera.")]
    [SerializeField] private LocalMode _localMode = LocalMode.Auto;
    [Tooltip("Strength of squash / hip bounce / lean on the local first-person player (0 keeps camera and lantern untouched).")]
    [SerializeField, Range(0f, 1f)] private float _localStrength = 0f;

    [Header("Squash and stretch (jump / land)")]
    [Tooltip("Max scale change (0.12 = +/-12 % height, volume preserving).")]
    [SerializeField, Range(0f, 0.3f)] private float _squashAmount = 0.12f;
    [Tooltip("Pre-jump dip (fraction of squash amount) applied instantly at take-off, then springs into the stretch. No gameplay delay.")]
    [SerializeField, Range(0f, 1f)] private float _anticipation = 0.5f;
    [SerializeField, Range(0f, 1f)] private float _takeoffStretch = 0.8f;
    [Tooltip("Landing squash strength (scales with impact speed).")]
    [SerializeField, Range(0f, 1.5f)] private float _landSquash = 1f;
    [Tooltip("Extra stretch while rising / falling fast.")]
    [SerializeField, Range(0f, 1f)] private float _airStretch = 0.35f;
    [SerializeField] private float _springStiffness = 190f;
    [SerializeField] private float _springDamping = 13f;

    [Header("Bouncier walk")]
    [Tooltip("Multiplier on the vertical hip oscillation of the clip (1 = unchanged, 1.6 = noticeably bouncier).")]
    [SerializeField, Range(1f, 3f)] private float _hipBounce = 1.6f;
    [Tooltip("Lean (degrees) forward per m/s^2 of forward acceleration; the spring overshoots when stopping.")]
    [SerializeField] private float _leanPerAccel = 0.9f;
    [SerializeField, Range(0f, 25f)] private float _maxLean = 9f;
    [Tooltip("Roll (degrees) per (rad/s of turning x m/s) — leans into turns.")]
    [SerializeField] private float _turnRollGain = 1.2f;
    [SerializeField, Range(0f, 25f)] private float _maxRoll = 8f;
    [SerializeField] private float _leanStiffness = 90f;
    [SerializeField] private float _leanDamping = 7f;

    [Header("Right arm swing")]
    [Tooltip("Extra swing of the right upper arm: degrees per metre of forward/back hand offset from the shoulder.")]
    [SerializeField, Range(0f, 120f)] private float _rightArmSwing = 45f;
    [SerializeField, Range(0f, 60f)] private float _maxArmExtra = 28f;

    [Header("Crouch")]
    [Tooltip("Same rate as CharacterMovement._crouchTransitionSpeed. Only drives CrouchBlend (NeckFollowCamera's eye offset): " +
             "the model is no longer squashed while crouched, the crouch clip plays as authored.")]
    [SerializeField] private float _crouchRate = 12f;
    private float _crouchT;

    private enum LocalMode { Auto, Local, Remote }

    /// <summary>Networking: this copy belongs to another player (full squash / bounce / lean strength). Call before Awake.</summary>
    public void SetRemote() => _localMode = LocalMode.Remote;

    private static readonly int GroundedHash = Animator.StringToHash("Grounded");

    private Animator _animator;
    private CharacterMovement _movement;
    private Transform _root;
    private Transform _hips, _rUpper, _rHand;
    private Vector3 _baseScale;
    private bool _isLocal;

    // squash spring
    private float _sq, _sqVel;
    private bool _wasGrounded = true;
    private float _minVy;
    private float _airTime;

    // lean spring state (pitch fwd+, roll right+)
    private float _pitch, _pitchVel, _roll, _rollVel;
    private Vector3 _prevVel;
    private float _accelSm;
    private float _prevYaw;
    private float _yawRateSm;

    // hips bounce reference
    private float _hipAvg;
    private bool _hipInit;
    private float _lastIk = -1f;
    private float _armExtra;

    public float SquashValue => _sq;
    /// <summary>0 = standing, 1 = fully crouched (follows the capsule crouch transition).</summary>
    public float CrouchBlend => _crouchT;
    public bool IsLocal => _isLocal;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _movement = GetComponentInParent<CharacterMovement>();
        var cb = GetComponentInParent<CharacterBase>();
        _root = cb != null ? cb.transform : transform.root;
        _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        _rUpper = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _rHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
        _baseScale = transform.localScale;
        _prevYaw = _root.eulerAngles.y;
        switch (_localMode)
        {
            case LocalMode.Local: _isLocal = true; break;
            case LocalMode.Remote: _isLocal = false; break;
            default:
                var nfc = GetComponent<NeckFollowCamera>();
                _isLocal = nfc != null && nfc.enabled;
                break;
        }
    }

    private void OnEnable()
    {
        if (_movement != null) _movement.Jumped += OnJumped;
    }

    private void OnDisable()
    {
        if (_movement != null) _movement.Jumped -= OnJumped;
        transform.localScale = _baseScale;
    }

    private float BodyStrength => !_enabled ? 0f : (_isLocal ? _localStrength : 1f);

    private void OnJumped()
    {
        // Instant dip + upward spring kick = anticipation and stretch with no input delay.
        _sq = -_anticipation;
        _sqVel = 20f * _takeoffStretch;
        _minVy = 0f;
    }

    private void Update()
    {
        if (_movement == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // ---- squash spring ----
        bool grounded = _animator.GetBool(GroundedHash);
        float vy = _movement.VerticalVelocity;
        if (!grounded)
        {
            _airTime += dt;
            _minVy = Mathf.Min(_minVy, vy);
        }
        else if (!_wasGrounded)
        {
            // landed: kick the spring by impact speed (ignore tiny hops / step-downs)
            if (_airTime > 0.12f)
            {
                float impact = Mathf.Clamp01(-_minVy / 12f);
                _sqVel -= (6f + 14f * impact) * _landSquash;
            }
            _airTime = 0f;
            _minVy = 0f;
        }
        _wasGrounded = grounded;

        float target = 0f;
        if (!grounded && _airTime > 0.05f)
            target = Mathf.Clamp(vy / 8f, -1f, 1f) * _airStretch;
        float a = -_springStiffness * (_sq - target) - _springDamping * _sqVel;
        _sqVel += a * dt;
        _sq += _sqVel * dt;
        _sq = Mathf.Clamp(_sq, -1.4f, 1.4f);

        // ---- lean springs ----
        Vector3 v = _movement.PlanarVelocity;
        Vector3 acc = (v - _prevVel) / dt;
        _prevVel = v;
        float fwdAcc = Vector3.Dot(acc, _root.forward);
        _accelSm = Mathf.Lerp(_accelSm, fwdAcc, 1f - Mathf.Exp(-dt * 12f));

        float yaw = _root.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(_prevYaw, yaw) * Mathf.Deg2Rad / dt;
        _prevYaw = yaw;
        _yawRateSm = Mathf.Lerp(_yawRateSm, yawRate, 1f - Mathf.Exp(-dt * 10f));

        float speed = v.magnitude;
        float pitchT = Mathf.Clamp(_accelSm * _leanPerAccel, -_maxLean, _maxLean);
        float rollT = Mathf.Clamp(-_yawRateSm * Mathf.Min(speed, 6f) * _turnRollGain, -_maxRoll, _maxRoll);
        // Mild constant forward lean while running faster.
        pitchT += Mathf.Clamp01(speed / 8f) * 2.5f;

        _pitchVel += (-_leanStiffness * (_pitch - pitchT) - _leanDamping * _pitchVel) * dt;
        _pitch += _pitchVel * dt;
        _rollVel += (-_leanStiffness * (_roll - rollT) - _leanDamping * _rollVel) * dt;
        _roll += _rollVel * dt;
    }

    private void LateUpdate()
    {
        float s = _isLocal ? 0f : BodyStrength; // local camera/lantern: never scale the model
        float x = _sq * _squashAmount * s;
        float y = 1f + x;
        float xz = 1f / Mathf.Sqrt(Mathf.Max(0.5f, y));

        float ct = (_movement != null && _movement.IsCrouching) ? 1f : 0f;
        _crouchT = Mathf.Lerp(_crouchT, ct, Mathf.Clamp01(Time.deltaTime * _crouchRate));
        transform.localScale = new Vector3(_baseScale.x * xz, _baseScale.y * y, _baseScale.z * xz);
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || !_enabled) return;

        float now = Time.time;
        float dt = _lastIk < 0f ? 0.0416f : Mathf.Clamp(now - _lastIk, 0.001f, 0.2f);
        _lastIk = now;
        float k = BodyStrength;

        if (_hips != null && k > 0f)
        {
            // Amplify the clip's own vertical hip oscillation (stays in phase with the steps).
            float hy = _root.InverseTransformPoint(_hips.position).y;
            if (!_hipInit) { _hipAvg = hy; _hipInit = true; }
            _hipAvg = Mathf.Lerp(_hipAvg, hy, 1f - Mathf.Exp(-dt * 3f));
            float extra = (hy - _hipAvg) * (_hipBounce - 1f) * k;
            extra = Mathf.Clamp(extra, -0.08f, 0.08f);

            _animator.bodyPosition += _root.up * extra;

            Quaternion lean = Quaternion.AngleAxis(_roll * k, _root.forward)
                            * Quaternion.AngleAxis(_pitch * k, _root.right);
            _animator.bodyRotation = lean * _animator.bodyRotation;
        }

        // Right arm only, never the torch arm.
        if (_rightArmSwing > 0f && _rUpper != null && _rHand != null)
        {
            float z = _root.InverseTransformPoint(_rHand.position).z - _root.InverseTransformPoint(_rUpper.position).z;
            float deg = Mathf.Clamp(-z * _rightArmSwing, -_maxArmExtra, _maxArmExtra);
            _armExtra = deg;
            Quaternion worldDelta = Quaternion.AngleAxis(deg, _root.right);
            Quaternion world = worldDelta * _rUpper.rotation;
            Quaternion local = Quaternion.Inverse(_rUpper.parent.rotation) * world;
            _animator.SetBoneLocalRotation(HumanBodyBones.RightUpperArm, local);
        }
    }
}
