using UnityEngine;

/// <summary>
/// Raises the left arm while the character carries a torch. Blends the Animator layer
/// <c>LeftArm_Torch</c> (Override, left-arm + left-fingers mask, built by
/// <c>Fluffterror > Animation > Build Torch Hold</c>) in on <see cref="TorchCarrier.TorchPickedUp"/>
/// and out on <see cref="TorchCarrier.TorchDropped"/>; every other animation keeps playing on the rest
/// of the body. While the layer exists it hands TorchCarrier the left-hand bone + grip, and TorchCarrier
/// attaches the torch <b>rigidly</b> to the hand (handle in the fist, zero lag).
///
/// <b>Pick-up / throw.</b> If the layer has the <c>TorchPickup</c> / <c>TorchThrow</c> states it sets
/// <see cref="TorchCarrier.AnimatedActions"/>, plays them on <see cref="TorchCarrier.PickupStarted"/> /
/// <see cref="TorchCarrier.ThrowStarted"/> and calls <see cref="TorchCarrier.CompletePickup"/> (end of the pick-up: the torch
/// pops into the hand, it never moves before) / <see cref="TorchCarrier.ReleaseThrow"/> (release frame) at the
/// <see cref="TorchActionTimings"/> times: local view on its stepped action clock, others when the stepped Animator <i>shows</i>
/// that frame. For other players
/// (stepped, on the Animator's step frames only) the pick-up clip is a generic forward reach that a two-bone IK pulls onto
/// the torch handle (wall, floor, pedestal), with a forward bend toward low torches; the throw clip gets a torso wind-up
/// twist / lean and a follow-through. The local player's body gets neither (the camera follows the neck).
///
/// <b>First-person view (local player).</b> <see cref="RetroAnimationStepper"/> poses the body only on
/// stepped frames while the camera moves every frame, so the raised hand (and the torch in it) would tick
/// on screen. When <see cref="_smoothLocalView"/> is on, every LateUpdate (after the camera moved, before
/// TorchCarrier) this takes the latest <i>animated</i> hand pose, expresses it in the <b>chest frame</b>
/// (camera position, upper-chest orientation relative to its bind pose), SmoothDamps it there, and re-solves
/// the arm with an analytic two-bone IK (upper arm / forearm, elbow kept on the animated bend plane) so the
/// hand glides every frame. In the chest frame the hand only moves with the arm animation — not with the
/// head-look IK leaning/twisting the spine (which lags the mouse) nor the stepped body. The smoothed pose is then
/// placed in the camera's view frame (yaw + pitch, pitch clamped to <see cref="_pitchFollowLimit"/>) like a
/// viewmodel, so looking around neither drifts the lantern across the screen nor trails behind the mouse; the
/// rotation follows only part of the pitch (<see cref="_pitchTiltFollow"/>) so the torch stays nearly upright.
/// The IK reach is kept in a comfortable band so the elbow never snaps straight. Only the left arm is touched;
/// the body stays stepped. Remote viewers see the plain animated pose. During a <b>throw</b> the smoothed hold is
/// frozen and the hand follows a spline of view-space offsets (small wind-up, release forward, soft follow-through); during
/// a <b>pick-up</b> the hand reaches for the torch handle, closes, and comes back empty into the hold. Both are <b>stepped</b>
/// like the body: the action time (and layer weight) is only sampled on <see cref="RetroAnimationStepper"/>'s step frames, while
/// the result stays placed in the view frame every frame (no camera-relative ticks). The idle hold is never stepped.
///
/// Place on the model child (the GameObject with the Animator).
/// </summary>
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(50)] // after NeckFollowCamera (0), before TorchCarrier (100)
public class TorchArmAnimator : MonoBehaviour
{
    [SerializeField] private string _layerName = "LeftArm_Torch";
    [Tooltip("Seconds to raise / lower the arm.")]
    [SerializeField] private float _blendTime = 0.25f;

    [Header("Grip (torch in the fist)")]
    [Tooltip("Fist centre relative to the left-hand bone, in metres along the bone's axes. The torch handle passes through it.")]
    [SerializeField] private Vector3 _gripOffset = new(0f, 0.092f, 0.034f);
    [Tooltip("Rotation from the hand bone's frame to the torch's upright frame (shaft along +Y). " +
             "Default: +Y = the hand's +X (index side, the curled-finger loop axis of the 5-finger skeleton rig), +Z = hand +Z.")]
    [SerializeField] private Vector3 _gripEuler = new(0f, 0f, 270f);

    [Header("First-person arm smoothing")]
    [Tooltip("Local player only: smooth the raised hand every frame in camera space (two-bone IK) so it " +
             "doesn't tick with the stepped body animation. Off = raw stepped animation (e.g. remote players).")]
    [SerializeField] private bool _smoothLocalView = true;
    [Tooltip("SmoothDamp time (s) of the hand position in the chest frame.")]
    [SerializeField] private float _handSmoothTime = 0.1f;
    [Tooltip("Exponential smoothing speed of the hand rotation in the chest frame (higher = snappier).")]
    [SerializeField] private float _handRotationSharpness = 10f;
    [Tooltip("First-person framing (local view only): view-space offset (m) added to the smoothed hand target. " +
             "Keep it small — the animated pose already frames the lantern; a big offset pushes the IK target out of reach.")]
    [SerializeField] private Vector3 _viewOffset = new(0.07f, 0.03f, 0.10f);
    [Tooltip("Aspect ratio the framing is tuned for. On a narrower window the hand moves toward the centre so the lantern keeps " +
             "its horizontal place on screen instead of being cropped by the left edge.")]
    [SerializeField] private float _referenceAspect = 1.78f;
    [Tooltip("How much of the shoulder's gait motion (seen from the camera) the hand follows. 0 = hand pinned to the screen: " +
             "the running torso twist (shoulder moves ~30 cm) is then absorbed by the elbow, which flaps. 1 = the arm keeps its " +
             "shape and the lantern swings fully with the shoulder.")]
    [SerializeField, Range(0f, 1f)] private float _shoulderFollow = 0.6f;
    [Tooltip("Time (s) of the slow shoulder baseline: only motion faster than this (the gait) is followed, so leaning and " +
             "crouching do not move the framing.")]
    [SerializeField] private float _shoulderBaselineTime = 0.4f;
    [Tooltip("Local view: the shoulder joint is smoothed (SmoothDamp, s) in the view frame by turning the collarbone, so it " +
             "glides instead of jumping with the 24 fps body steps (up to ~13 cm per step when running). 0 = off.")]
    [SerializeField] private float _shoulderSmoothTime = 0.08f;
    [Tooltip("Largest collarbone correction (deg).")]
    [SerializeField] private float _maxClavicleAngle = 30f;

    [Header("Torso while holding the torch (all players)")]
    [Tooltip("Fraction of the gait's upper-body twist removed while the torch is raised. The UAL run twists the chest ~70 deg, " +
             "swinging the torch shoulder ~30 cm in 24 fps steps; someone holding up a lantern keeps the torso steadier.")]
    [SerializeField, Range(0f, 1f)] private float _torsoTwistDamping = 0.75f;
    [Tooltip("Time (s) of the twist baseline: only the gait oscillation around it is damped (turning is not).")]
    [SerializeField] private float _twistBaselineTime = 1f;
    [Tooltip("How much the hand follows the camera pitch (1 = locked in view like a viewmodel, 0 = level).")]
    [SerializeField, Range(0f, 1f)] private float _pitchFollow = 1f;
    [Tooltip("How much the torch tilts with the camera pitch (0 = always upright, 1 = fixed in view). A little tilt keeps " +
             "the lantern's size/height on screen steadier when looking up or down.")]
    [SerializeField, Range(0f, 1f)] private float _pitchTiltFollow = 0.5f;
    [Tooltip("Pitch (deg) beyond which the hand stops following the camera, so it never chases an unreachable target.")]
    [SerializeField] private float _pitchFollowLimit = 30f;
    [Tooltip("Max degrees the lantern follows the view when looking UP. The arm is already at its comfortable reach looking level; " +
             "following further up made the IK fall short and the lantern clip into the camera.")]
    [SerializeField] private float _pitchUpFollowLimit = 0f;
    [Tooltip("Soft IK reach limit as a fraction of the arm length. The target is eased in before full extension so " +
             "the elbow never snaps straight (the straight-arm singularity makes the elbow wave).")]
    [SerializeField, Range(0.8f, 1f)] private float _maxReach = 0.99f;
    [Tooltip("Comfortable reach band (fraction of the arm length) the IK target is kept in. Looking up used to stretch the arm " +
             "straight (no defined elbow direction: it snapped on fast flicks) and looking down folded it to ~50 deg.")]
    [SerializeField, Range(0.3f, 0.8f)] private float _minReach = 0.55f;
    [SerializeField, Range(0.6f, 0.98f)] private float _comfortReach = 0.88f;
    [Tooltip("Preferred elbow direction in the character's axes (x right, y up, z forward): down and a little out.")]
    [SerializeField] private Vector3 _elbowHint = new(-0.45f, -1f, -0.1f);
    [Tooltip("Blend between the animated elbow direction (0) and the hint (1).")]
    [SerializeField, Range(0f, 1f)] private float _elbowHintWeight = 0.7f;
    [Tooltip("How fast (1/s) the elbow direction may turn. The animated elbow and the chest change in 24 fps steps; unsmoothed, " +
             "the elbow snapped up to ~13 cm in one frame after fast looks up/down.")]
    [SerializeField] private float _elbowTurnSharpness = 18f;

    [Header("Pick-up / throw")]
    [Tooltip("Seconds the arm layer takes to come in when a pick-up starts (the clip starts from a hanging arm).")]
    [SerializeField] private float _pickupBlendIn = 0.06f;
    [Tooltip("Other players (stepped): strength of the IK pulling the reaching hand onto the torch handle.")]
    [SerializeField, Range(0f, 1f)] private float _pickupReachIk = 1f;
    [Tooltip("Other players: forward bend (deg per metre the handle lies below the arm's reach) toward a low torch, and its maximum.")]
    [SerializeField] private float _pickupBendPerMetre = 70f;
    [SerializeField] private float _pickupMaxBend = 55f;
    [Tooltip("Other players: torso yaw (deg, - = torch shoulder back) at wind-up, held wind-up, release, follow-through.")]
    [SerializeField] private Vector4 _throwTorsoYaw = new(-6f, -9f, 5f, 8f);
    [Tooltip("Other players: torso lean (deg, + = forward) at wind-up, held wind-up, release, follow-through.")]
    [SerializeField] private Vector4 _throwTorsoLean = new(-2f, -3f, 3f, 5f);
    [Tooltip("Local view: IK reach limit (fraction of the arm) while reaching / throwing — the arm may extend further than when holding.")]
    [SerializeField, Range(0.8f, 0.99f)] private float _actionReach = 0.96f;
    [Tooltip("Local view throw: hand offsets from the frozen hold target, in the view frame (x right, y up, z forward, m), at " +
             "the wind-up, the held wind-up, the release pose and the follow-through (a calm lob: the lantern rises a little, then " +
             "swings forward).")]
    [SerializeField] private Vector3 _throwWindUp     = new(-0.04f, 0.08f, -0.01f);
    [SerializeField] private Vector3 _throwWindUpHold = new(-0.05f, 0.10f, -0.02f);
    [SerializeField] private Vector3 _throwRelease    = new(0.05f, 0.08f, 0.15f);
    [SerializeField] private Vector3 _throwFollow     = new(0.08f, -0.16f, 0.10f);
    [Tooltip("Local view throw: torch tilt (deg about the view's right axis, + = top forward) at wind-up, held wind-up, release, follow-through.")]
    [SerializeField] private Vector4 _throwTilt = new(-10f, -16f, 18f, 35f);
    [Tooltip("Elbow direction (character axes) while the arm is cocked back for the throw: out to the side.")]
    [SerializeField] private Vector3 _throwElbowHint = new(-1f, -0.35f, 0.15f);

    private Animator     _animator;
    private TorchCarrier _carrier;
    private Transform    _upper, _lower, _hand, _chest, _neck;
    private NeckFollowCamera _neckCam;
    private Quaternion   _chestBindInv = Quaternion.identity; // inverse of the chest's bind rotation relative to the model
    private Camera       _camera;
    private int          _layer = -1;
    private float        _blend;     // linear 0..1, eased when applied
    private float        _target;

    // Last animated (stepped) local rotations, and what the IK wrote last frame (to detect a new step).
    private Quaternion _animUpper, _animLower, _animHand;
    private Quaternion _wroteUpper, _wroteLower, _wroteHand;
    private bool       _hasWritten;
    private Vector3    _smoothPos, _smoothVel;   // chest frame (camera position, chest orientation vs bind)
    private Quaternion _smoothRot;               // chest frame
    private bool       _smoothInit;
    private Vector3    _shoulderBase, _shoulderBaseVel;   // view frame, slow baseline of the shoulder
    private bool       _shoulderInitDone;
    private Transform  _spine, _rightUpper, _clavicle;
    private Quaternion _animClav, _wroteClav;
    private Vector3    _shSmooth, _shSmoothVel;
    private bool       _shSmoothInit;
    private Vector3    _poleLocal;   // smoothed elbow direction, in the model's local axes
    private bool       _poleInit;
    private float      _twistBase, _lastTwistTime = -1f;
    private bool       _twistInit;

    // Pick-up / throw.
    private enum Mode { None, Pickup, Throw }
    private Mode       _mode;
    private float      _modeStart;
    private bool       _grabbed, _released;
    private Torch      _pickupTorch;
    private Vector3    _grabWorld;               // fist centre at the grab (stepped IK target afterwards)
    private Vector3    _grabView;                // local view: hand target at the grab, in the view frame
    private Vector3    _startView;               // local view: hand at the start of the pick-up, in the view frame
    private bool       _startValid;
    private Vector3    _frozenHeld;              // local view: view-frame hold target when the throw started
    private bool       _frozenValid;
    private float      _reachLimit = -1f;
    private float      _hintThrow;               // 0..1 blend toward _throwElbowHint
    // Local view: the action is stepped like the body (sampled on RetroAnimationStepper's step frames).
    private RetroAnimationStepper _stepper;
    private bool       _stepInit;
    private float      _stepT, _stepW, _ownStepTime, _grabT, _lastStepTime;
    private Vector3    _stepHeld;

    private static readonly float[] ThrowKeyTimes =
    {
        0f, TorchActionTimings.ThrowWindUp, TorchActionTimings.ThrowWindUpHold, TorchActionTimings.ThrowReleasePose,
        TorchActionTimings.ThrowFollow, TorchActionTimings.ThrowDuration,
    };
    private readonly Vector4[] _throwKeys = new Vector4[6];
    private readonly Vector4[] _torsoKeys = new Vector4[6];
    private Transform  _chestBone;
    private Quaternion _wroteSpineR, _wroteUpperR;   // other players: what the action pose wrote on the last step
    private bool       _actionWritten;

    /// <summary>Seconds since the current pick-up / throw started (or -1).</summary>
    public float ActionTime => _mode == Mode.None ? -1f : Time.time - _modeStart;
    public bool  InAction   => _mode != Mode.None;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _upper    = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _lower    = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        _hand     = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
        _chest    = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (_chest == null) _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
        if (_chest == null) _chest = _animator.GetBoneTransform(HumanBodyBones.Spine);
        // Awake runs before the Animator first poses the rig, so this is the bind (T-pose) orientation.
        if (_chest != null) _chestBindInv = Quaternion.Inverse(Quaternion.Inverse(transform.rotation) * _chest.rotation);
        _neck     = _animator.GetBoneTransform(HumanBodyBones.Neck);
        _spine    = _animator.GetBoneTransform(HumanBodyBones.Spine);
        _clavicle = _animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
        _rightUpper = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _chestBone  = _animator.GetBoneTransform(HumanBodyBones.Chest);
        _neckCam  = GetComponent<NeckFollowCamera>();
        _stepper  = GetComponent<RetroAnimationStepper>();
        _layer    = _animator.GetLayerIndex(_layerName);
        if (_layer < 0)
            Debug.LogWarning($"[TorchArmAnimator] Animator has no '{_layerName}' layer — run " +
                             "Fluffterror > Animation > Build Torch Hold.", this);
    }

    // CharacterBase auto-adds TorchCarrier in its Awake, so resolve it in Start (after every Awake).
    private void Start()
    {
        CharacterBase character = GetComponentInParent<CharacterBase>();
        _carrier = character != null ? character.TorchCarrier : GetComponentInParent<TorchCarrier>();
        _camera  = character != null ? character.GetComponentInChildren<Camera>() : null;
        Subscribe();
    }

    private void OnEnable()  => Subscribe();
    private void OnDisable() => Unsubscribe();

    private bool HasActionStates =>
        _layer >= 0 && _animator.HasState(_layer, TorchActionTimings.ThrowHash) && _animator.HasState(_layer, TorchActionTimings.PickupHash);

    private void Subscribe()
    {
        if (_carrier == null) return;
        Unsubscribe(); // idempotent: Start and OnEnable may both run
        _carrier.TorchPickedUp   += OnPickedUp;
        _carrier.TorchDropped    += OnDropped;
        _carrier.PickupStarted   += OnPickupStarted;
        _carrier.ThrowStarted    += OnThrowStarted;
        _carrier.ActionCancelled += OnActionCancelled;
        if (_layer >= 0 && _hand != null) _carrier.SetHandAnchor(_hand, _gripOffset, Quaternion.Euler(_gripEuler));
        _carrier.AnimatedActions = _hand != null && HasActionStates;
        _target = _carrier.HasTorch ? 1f : 0f;   // torch may already be held at start
    }

    private void Unsubscribe()
    {
        if (_carrier == null) return;
        _carrier.TorchPickedUp   -= OnPickedUp;
        _carrier.TorchDropped    -= OnDropped;
        _carrier.PickupStarted   -= OnPickupStarted;
        _carrier.ThrowStarted    -= OnThrowStarted;
        _carrier.ActionCancelled -= OnActionCancelled;
        _carrier.SetHandAnchor(null, Vector3.zero, Quaternion.identity);
        _carrier.AnimatedActions = false;   // the carrier finishes a running action on its own (instantly / fallback)
        _mode = Mode.None;
    }

    private void OnPickupStarted(Torch torch)
    {
        _mode = Mode.Pickup; _modeStart = Time.time; _grabbed = false; _pickupTorch = torch; _startValid = false; _stepInit = false;
        _animator.Play(TorchActionTimings.PickupHash, _layer, 0f);
        _target = 1f;
    }

    private void OnPickedUp(Torch torch)
    {
        _target = 1f;
        if (_mode == Mode.Pickup) _mode = Mode.None;   // end of the pick-up clip: arm already in the hold pose
        else if (_layer >= 0)
        {
            _mode = Mode.None;
            _animator.Play(TorchActionTimings.HoldHash, _layer, 0f);   // instant pick-up
        }
    }

    private void OnThrowStarted(Torch _)
    {
        _mode = Mode.Throw; _modeStart = Time.time; _released = false; _frozenValid = false; _stepInit = false;
        _animator.Play(TorchActionTimings.ThrowHash, _layer, 0f);
        _target = 1f;
    }

    private void OnDropped(Torch _)
    {
        if (_mode == Mode.Throw && _released) return;   // released on the throw's release frame: follow-through keeps playing
        _mode = Mode.None; _target = 0f;
    }

    private void OnActionCancelled()
    {
        _mode = Mode.None;
        _target = _carrier != null && _carrier.HasTorch ? 1f : 0f;
    }

    private void Update()
    {
        if (_layer < 0) return;
        float rate = _blendTime > 0f ? 1f / _blendTime : 1000f;
        float t = Time.time - _modeStart;
        switch (_mode)
        {
            case Mode.Pickup:
                _target = 1f;
                rate = _pickupBlendIn > 0f ? 1f / _pickupBlendIn : 1000f;
                if (t >= TorchActionTimings.PickupDuration + 1f) _mode = Mode.None;   // safety; normally OnPickedUp ends it
                break;
            case Mode.Throw:
                _target = t < TorchActionTimings.ThrowBlendOut ? 1f : 0f;
                if (_target == 0f) rate = 1f / Mathf.Max(0.01f, TorchActionTimings.ThrowDuration - TorchActionTimings.ThrowBlendOut);
                if (_released && t >= TorchActionTimings.ThrowDuration) { _mode = Mode.None; _target = _carrier.HasTorch ? 1f : 0f; }
                break;
        }
        _blend = Mathf.MoveTowards(_blend, _target, Time.deltaTime * rate);
        _animator.SetLayerWeight(_layer, Weight);
    }

    /// <summary>
    /// True once the action reaches <paramref name="time"/>: in the local view on its stepped action clock, otherwise when the
    /// stepped Animator actually shows that frame of the state (or has already left it: the pick-up ends exactly at the clip end,
    /// where the exit transition fires). Falls back to real time + 0.1 s.
    /// </summary>
    private bool FrameShown(int stateHash, float time, float elapsed)
    {
        if (LocalView || elapsed >= time + 0.1f) return elapsed >= time;
        AnimatorStateInfo si = _animator.GetCurrentAnimatorStateInfo(_layer);
        if (si.shortNameHash == stateHash) return si.normalizedTime * si.length >= time - 1e-4f;
        return elapsed >= time;   // not started yet (Play applies on the next step) or already exited
    }

    /// <summary>True on the frames the body takes a new stepped pose (RetroAnimationStepper), or on an own 24 fps clock without it.</summary>
    private bool StepThisFrame()
    {
        if (_stepper != null && _stepper.isActiveAndEnabled) return _stepper.LastStepFrame == Time.frameCount;
        if (Time.time - _ownStepTime < 1f / 24f) return false;
        _ownStepTime = Time.time;
        return true;
    }

    private bool LocalView => _smoothLocalView && _camera != null;

    /// <summary>Networking: remote copies use the plain animated arm instead of the first-person viewmodel.</summary>
    public void SetLocalView(bool on) => _smoothLocalView = on;

    private float Weight => Mathf.SmoothStep(0f, 1f, _blend);

    // Runs on the stepped Animator evaluations (Base Layer has IK Pass). Bones hold the freshly animated pose here.
    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || _torsoTwistDamping <= 0f || _spine == null || _upper == null || _rightUpper == null) return;
        float w = Weight;
        if (w <= 0f) { _twistInit = false; return; }

        Vector3 up   = transform.up;
        Vector3 line = Vector3.ProjectOnPlane(_rightUpper.position - _upper.position, up);
        float   yaw  = Vector3.SignedAngle(transform.right, line, up);
        float   now  = Time.time;
        float   dt   = _lastTwistTime < 0f ? 0f : Mathf.Clamp(now - _lastTwistTime, 0f, 0.25f);
        _lastTwistTime = now;
        if (!_twistInit) { _twistBase = yaw; _twistInit = true; }
        _twistBase = Mathf.LerpAngle(_twistBase, yaw, _twistBaselineTime > 0f ? 1f - Mathf.Exp(-dt / _twistBaselineTime) : 1f);

        float counter = -Mathf.DeltaAngle(_twistBase, yaw) * _torsoTwistDamping * w;
        Quaternion world = Quaternion.AngleAxis(counter, up) * _spine.rotation;
        _animator.SetBoneLocalRotation(HumanBodyBones.Spine, Quaternion.Inverse(_spine.parent.rotation) * world);
    }

    /// <summary>
    /// Other players: on each stepped Animator frame (fresh pose), bend / twist the torso for the action and pull the reaching hand
    /// onto the torch handle (two-bone IK). Between steps the bones keep this pose, so it stays on the 24 fps retro look.
    /// </summary>
    private void SteppedActionPose()
    {
        if (_mode == Mode.None || _spine == null) { _actionWritten = false; return; }
        bool fresh = !_actionWritten || _spine.localRotation != _wroteSpineR || _upper.localRotation != _wroteUpperR;
        if (!fresh) return;

        float t = Time.time - _modeStart;
        float yaw = 0f, lean = 0f, reachW = 0f;
        Vector3 fist = _grabWorld;
        if (_mode == Mode.Throw)
        {
            Vector4 k = Spline(t, _torsoKeys, new Vector4(_throwTorsoYaw.x, _throwTorsoLean.x), new Vector4(_throwTorsoYaw.y, _throwTorsoLean.y),
                               new Vector4(_throwTorsoYaw.z, _throwTorsoLean.z), new Vector4(_throwTorsoYaw.w, _throwTorsoLean.w), Vector4.zero);
            yaw = k.x; lean = k.y;
        }
        else if (_mode == Mode.Pickup)
        {
            float env;
            if (!_grabbed && _pickupTorch != null)
            {
                fist = _carrier.GripPointWorld(_pickupTorch);
                env = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, TorchActionTimings.PickupGrab, t));
            }
            else env = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TorchActionTimings.PickupGrab, TorchActionTimings.PickupDuration, t));
            float arm = Vector3.Distance(_upper.position, _lower.position) + Vector3.Distance(_lower.position, _hand.position);
            float below = _upper.position.y - 0.8f * arm - fist.y;   // how far the handle is below a comfortable reach
            lean   = Mathf.Clamp(below * _pickupBendPerMetre, 0f, _pickupMaxBend) * env;
            reachW = env * _pickupReachIk;
        }

        if (yaw != 0f || lean != 0f)
        {
            // Half on the spine, half on the chest: a curved back rather than a hinge.
            Quaternion r = Quaternion.AngleAxis(yaw * 0.5f, transform.up) * Quaternion.AngleAxis(lean * 0.5f, transform.right);
            _spine.rotation = r * _spine.rotation;
            if (_chestBone != null) _chestBone.rotation = r * _chestBone.rotation;
        }
        if (reachW > 0f)
        {
            Vector3 wrist = fist - _hand.rotation * _gripOffset;
            Quaternion handRot = _hand.rotation;
            SolveTwoBone(Vector3.Lerp(_hand.position, wrist, reachW), 0.98f, smoothPole: false);
            _hand.rotation = handRot;
        }
        _wroteSpineR = _spine.localRotation;
        _wroteUpperR = _upper.localRotation;
        _actionWritten = true;
    }

    private Quaternion ViewFrame(Transform cam, out Quaternion level, out float pitch)
    {
        level = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
        Vector3 fwd = Quaternion.Inverse(level) * cam.forward;
        pitch = -Mathf.Atan2(fwd.y, new Vector2(fwd.x, fwd.z).magnitude) * Mathf.Rad2Deg; // + = looking down
        // Looking UP is clamped much harder than down: the shoulder swings behind the eye (0.39 m at 30 deg up) and the short
        // arm can't hold the lantern out there, so the IK fell short and drove the lantern into the camera. Kept at the level
        // pose, the lantern drops off the bottom of the screen instead.
        pitch = Mathf.Clamp(pitch, -_pitchUpFollowLimit, _pitchFollowLimit) * _pitchFollow;
        return level * Quaternion.Euler(pitch, 0f, 0f);
    }

    private void LateUpdate()
    {
        if (_layer < 0 || _upper == null || _lower == null || _hand == null) return;
        if (LocalView) PoseLocalArm();
        else SteppedActionPose();
        FireActionFrames();
    }

    /// <summary>
    /// Grab / pick-up end / release on the frame the action reaches it — here, once the arm pose of this frame is final, so the
    /// torch pops into (or leaves from) the hand exactly where it is drawn.
    /// </summary>
    private void FireActionFrames()
    {
        if (_carrier == null) return;
        float t = LocalView && _stepInit ? _stepT : Time.time - _modeStart;
        if (_mode == Mode.Pickup && !_grabbed && FrameShown(TorchActionTimings.PickupHash, TorchActionTimings.PickupGrab, t))
        {
            // Fist closes: remember where (the hand comes back empty from here; the torch has not moved).
            _grabbed   = true;
            _grabT     = t;
            _grabWorld = _hand.position + _hand.rotation * _gripOffset;
            if (_camera != null)
                _grabView = Quaternion.Inverse(ViewFrame(_camera.transform, out _, out _)) * (_hand.position - _camera.transform.position);
        }
        if (_mode == Mode.Pickup && _grabbed && FrameShown(TorchActionTimings.PickupHash, TorchActionTimings.PickupDuration, t))
            _carrier.CompletePickup();   // the torch pops into the hand -> OnPickedUp (or OnActionCancelled)
        else if (_mode == Mode.Throw && !_released && FrameShown(TorchActionTimings.ThrowHash, TorchActionTimings.ThrowRelease, t))
        {
            _released = true;
            _carrier.ReleaseThrow();   // -> TorchDropped (ignored, _released) -> TorchThrown
        }
    }

    private void PoseLocalArm()
    {

        // 1. Recover the latest animated pose. On frames the stepper didn't update the Animator the bones
        //    still hold what the IK wrote, so only re-cache when they differ (= a new animated step).
        if (!_hasWritten || _upper.localRotation != _wroteUpper || _lower.localRotation != _wroteLower ||
            _hand.localRotation != _wroteHand || (_clavicle != null && _clavicle.localRotation != _wroteClav))
        {
            if (_clavicle != null) _animClav = _clavicle.localRotation;
            _animUpper = _upper.localRotation;
            _animLower = _lower.localRotation;
            _animHand  = _hand.localRotation;
        }
        _upper.localRotation = _animUpper;
        _lower.localRotation = _animLower;
        _hand.localRotation  = _animHand;
        if (_clavicle != null && _hasWritten) _clavicle.localRotation = _animClav;

        float w = Weight;
        float t = Time.time - _modeStart;
        bool throwing = _mode == Mode.Throw, picking = _mode == Mode.Pickup;
        // Retro look during the actions: time and weight only advance on the body's step frames (arm and body step together).
        bool stepFresh = false;
        if (throwing || picking)
        {
            if (!_stepInit || StepThisFrame())
            {
                _lastStepTime = _stepInit ? _lastStepTime : Time.time;
                _stepT = t; _stepW = w; _stepInit = true; stepFresh = true;
            }
            t = _stepT; w = _stepW;
        }
        if (w <= 0f) { _hasWritten = false; _smoothInit = false; _shoulderInitDone = false; _shSmoothInit = false; _poleInit = false; _frozenValid = false; return; }

        // 2. Smooth the animated hand in the chest frame (camera position + chest orientation relative to bind).
        //    In this frame the hand only moves with the arm animation: the head-look IK leaning/twisting the spine
        //    (it lags the mouse) and the stepped body cancel out. Smoothing it in full camera space instead made it
        //    trail every pitch/yaw change and drift up the screen. Frozen during a throw (the spline below drives it).
        Transform  cam   = _camera.transform;
        Quaternion body  = _chest != null ? _chest.rotation * _chestBindInv : Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
        Quaternion invBody = Quaternion.Inverse(body);
        // Reference point for the chest frame: the eye as if it were rigid with the chest (neck + eye offset rotated by the
        // chest's lean). The real camera keeps the eye offset in the character's upright axes, so a forward-leaning jog or a
        // bowed crouch would otherwise shift the camera->hand vector in the chest frame (UAL gaits lean ~15 deg: the lantern
        // jumped ~0.16 in viewport y). With no lean this equals the camera position, i.e. the previous behaviour.
        Vector3 refPos = cam.position;
        if (_neck != null && _neckCam != null && _chest != null) refPos = _neck.position + body * _neckCam.StandingEyeOffset;   // standing eye even when crouched: keeps the lantern framing calibrated
        Vector3    rawPos = invBody * (_hand.position - refPos);
        Quaternion rawRot = invBody * _hand.rotation;
        if (!_smoothInit)
        {
            _smoothPos = rawPos; _smoothVel = Vector3.zero; _smoothRot = rawRot; _smoothInit = true;
        }
        if (picking)
        {
            // Stepped: take the animated hand as is on step frames (the hold after the pick-up resumes smoothing from there).
            if (stepFresh) { _smoothPos = rawPos; _smoothRot = rawRot; _smoothVel = Vector3.zero; }
        }
        else if (!throwing || !_frozenValid)
        {
            _smoothPos = Vector3.SmoothDamp(_smoothPos, rawPos, ref _smoothVel, _handSmoothTime);
            _smoothRot = Quaternion.Slerp(_smoothRot, rawRot, 1f - Mathf.Exp(-_handRotationSharpness * Time.deltaTime));
        }

        // Place it in the view frame (camera yaw + clamped pitch) so it stays put on screen; the rotation only
        // follows part of the pitch (_pitchTiltFollow) so the torch stays nearly upright.
        Quaternion view = ViewFrame(cam, out Quaternion level, out float pitch);

        // Glide the shoulder joint: smooth it in the view frame and turn the collarbone toward the smoothed point.
        if (_clavicle != null && _shoulderSmoothTime > 0f)
        {
            Vector3 shRaw = Quaternion.Inverse(view) * (_upper.position - cam.position);
            if (!_shSmoothInit) { _shSmooth = shRaw; _shSmoothVel = Vector3.zero; _shSmoothInit = true; }
            if (!(throwing || picking))
                _shSmooth = Vector3.SmoothDamp(_shSmooth, shRaw, ref _shSmoothVel, _shoulderSmoothTime);
            else if (stepFresh)   // stepped: advance by the whole step on step frames only
                _shSmooth = Vector3.SmoothDamp(_shSmooth, shRaw, ref _shSmoothVel, _shoulderSmoothTime, Mathf.Infinity,
                                               Mathf.Max(1e-3f, Time.time - _lastStepTime));
            Vector3 c = _clavicle.position;
            Quaternion q = Quaternion.FromToRotation(_upper.position - c, cam.position + view * _shSmooth - c);
            q.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            ang = Mathf.Clamp(ang, -_maxClavicleAngle, _maxClavicleAngle) * w;
            _clavicle.rotation = Quaternion.AngleAxis(ang, axis) * _clavicle.rotation;
        }

        Vector3    held      = _smoothPos + _viewOffset;
        // Gait: the hand rides along with part of the shoulder's motion around its slow baseline, so the arm swings as a whole
        // instead of the elbow alone taking up the running torso twist.
        Vector3 shoulderView = Quaternion.Inverse(view) * (_upper.position - cam.position);
        if (!_shoulderInitDone) { _shoulderBase = shoulderView; _shoulderBaseVel = Vector3.zero; _shoulderInitDone = true; }
        _shoulderBase = Vector3.SmoothDamp(_shoulderBase, shoulderView, ref _shoulderBaseVel, _shoulderBaselineTime);
        held += (shoulderView - _shoulderBase) * _shoulderFollow;
        held.x *= Mathf.Clamp(_camera.aspect / _referenceAspect, 0.5f, 1f);
        if (picking && (stepFresh || !_startValid)) _stepHeld = held;

        Quaternion tilt     = level * Quaternion.Euler(pitch * _pitchTiltFollow, 0f, 0f);
        Quaternion extraRot = Quaternion.identity;
        float hintThrow = 0f, reach = _comfortReach;
        Vector3 targetPos;

        if (throwing)
        {
            // Frozen hold + the spline at the stepped action time.
            if (!_frozenValid) { _frozenHeld = held; _frozenValid = true; }
            Vector4 k = ThrowSpline(t);
            targetPos = cam.position + view * (_frozenHeld + (Vector3)k);
            extraRot  = Quaternion.AngleAxis(k.w, Vector3.right);
            hintThrow = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TorchActionTimings.ThrowWindUpHold, TorchActionTimings.ThrowReleasePose, t));
            reach = _actionReach;
        }
        else if (picking)
        {
            Vector3 holdPos = cam.position + view * _stepHeld;
            if (!_grabbed && _pickupTorch != null)
            {
                // Reach: from where the hand was when E was pressed (kept in the view frame, not the stepped clip hand) to the
                // torch handle; the fist centre lands on the grip point.
                if (!_startValid) { _startView = Quaternion.Inverse(view) * (_hand.position - cam.position); _startValid = true; }
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / TorchActionTimings.PickupGrab));
                Vector3 wrist = _carrier.GripPointWorld(_pickupTorch) - _hand.rotation * _gripOffset;
                // Out of reach (floor, pedestal): aim at the reachable point on the way, so the hand eases to a stop there
                // instead of slamming into the IK's soft reach limit mid-motion (the torch stays put; it pops in at the end).
                Vector3 toWrist = wrist - _upper.position;
                float armLen = Vector3.Distance(_upper.position, _lower.position) + Vector3.Distance(_lower.position, _hand.position);
                float maxD = armLen * (Mathf.Min(_actionReach, _maxReach) - 0.08f);
                if (toWrist.magnitude > maxD) wrist = _upper.position + toWrist.normalized * maxD;
                targetPos = Vector3.Lerp(cam.position + view * _startView, wrist, a);
            }
            else
            {
                // Come back (fist closed, empty): from where the hand grabbed (kept in the view frame) into the normal hold.
                float b = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_grabT, TorchActionTimings.PickupDuration, t));
                targetPos = Vector3.Lerp(cam.position + view * _grabView, holdPos, b);
            }
            reach = Mathf.Lerp(_actionReach, _comfortReach, Mathf.InverseLerp(TorchActionTimings.PickupGrab, TorchActionTimings.PickupDuration, t));
        }
        else targetPos = cam.position + view * held;

        targetPos = Vector3.Lerp(_hand.position, targetPos, w);
        Quaternion targetRot = Quaternion.Slerp(_hand.rotation, tilt * extraRot * _smoothRot, w);
        bool action = throwing || picking;
        _hintThrow  = hintThrow;
        if (action) { if (stepFresh || _reachLimit < 0f) _reachLimit = reach; }
        else _reachLimit = _reachLimit < 0f ? reach : Mathf.MoveTowards(_reachLimit, reach, Time.deltaTime * 1.5f);
        if (stepFresh) _lastStepTime = Time.time;

        // 3. Two-bone IK to the smoothed target, then set the hand rotation. During an action the elbow direction is only
        //    re-aimed on step frames (no smooth drift between the body's steps).
        SolveTwoBone(targetPos, -1f, smoothPole: !action, freezePole: action && !stepFresh);
        _hand.rotation = targetRot;

        if (_clavicle != null) _wroteClav = _clavicle.localRotation;
        _wroteUpper = _upper.localRotation;
        _wroteLower = _lower.localRotation;
        _wroteHand  = _hand.localRotation;
        _hasWritten = true;
    }

    /// <summary>Local-view throw path: view-frame hand offset (xyz) + torch tilt (w) at <paramref name="t"/> s.</summary>
    private Vector4 ThrowSpline(float t)
    {
        Vector4 f = new(_throwFollow.x, _throwFollow.y, _throwFollow.z, _throwTilt.w);
        return Spline(t, _throwKeys,
                      new Vector4(_throwWindUp.x, _throwWindUp.y, _throwWindUp.z, _throwTilt.x),
                      new Vector4(_throwWindUpHold.x, _throwWindUpHold.y, _throwWindUpHold.z, _throwTilt.y),
                      new Vector4(_throwRelease.x, _throwRelease.y, _throwRelease.z, _throwTilt.z), f, f);
    }

    /// <summary>
    /// Catmull-Rom through zero (t = 0) and the throw keys at wind-up, held wind-up, release pose, follow-through and the end
    /// (<see cref="ThrowKeyTimes"/>); zero velocity at both ends.
    /// </summary>
    private static Vector4 Spline(float t, Vector4[] k, Vector4 windUp, Vector4 windUpHold, Vector4 release, Vector4 follow, Vector4 end)
    {
        k[0] = Vector4.zero; k[1] = windUp; k[2] = windUpHold; k[3] = release; k[4] = follow; k[5] = end;
        float[] T = ThrowKeyTimes;
        int n = T.Length;
        if (t <= 0f) return k[0];
        if (t >= T[n - 1]) return k[n - 1];
        int i = 0;
        while (i < n - 2 && t > T[i + 1]) i++;
        float h = T[i + 1] - T[i], u = (t - T[i]) / h;
        Vector4 m0 = i == 0     ? Vector4.zero : (k[i + 1] - k[i - 1]) / (T[i + 1] - T[i - 1]);
        Vector4 m1 = i + 2 >= n ? Vector4.zero : (k[i + 2] - k[i]) / (T[i + 2] - T[i]);
        float u2 = u * u, u3 = u2 * u;
        return (2f * u3 - 3f * u2 + 1f) * k[i] + (u3 - 2f * u2 + u) * h * m0
             + (-2f * u3 + 3f * u2) * k[i + 1] + (u3 - u2) * h * m1;
    }

    /// <summary>Analytic two-bone IK: elbow stays on the animated bend plane (pole = current elbow).</summary>
    private void SolveTwoBone(Vector3 target, float reachOverride = -1f, bool smoothPole = true, bool freezePole = false)
    {
        Vector3 a = _upper.position, b = _lower.position, c = _hand.position;
        float lab = Vector3.Distance(a, b), lbc = Vector3.Distance(b, c), len = lab + lbc;
        Vector3 toT = target - a;
        Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (c - a).normalized;

        // Keep the reach inside a comfortable band (eased at both ends): never straight (undefined elbow), never folded.
        float reach = reachOverride > 0f ? reachOverride : (_reachLimit > 0f ? _reachLimit : _comfortReach);
        float d = toT.magnitude, hi = len * Mathf.Min(reach, _maxReach), lo = len * _minReach;
        float kneeHi = hi - 0.08f * len, kneeLo = lo + 0.08f * len;
        if (d > kneeHi) d = kneeHi + (hi - kneeHi) * (1f - Mathf.Exp(-(d - kneeHi) / (hi - kneeHi)));
        if (d < kneeLo) d = kneeLo - (kneeLo - lo) * (1f - Mathf.Exp(-(kneeLo - d) / (kneeLo - lo)));
        float lat = Mathf.Clamp(d, Mathf.Abs(lab - lbc) + 1e-4f, hi);

        // Elbow direction: the animated elbow blended toward a stable chest-relative hint (out to the side while cocked for a throw).
        Vector3 hint = _hintThrow > 0f ? Vector3.Slerp(_elbowHint.normalized, _throwElbowHint.normalized, _hintThrow) : _elbowHint;
        Vector3 animPole = Vector3.ProjectOnPlane(b - a, dir);
        Vector3 hintPole = Vector3.ProjectOnPlane(transform.rotation * hint, dir);
        Vector3 want = (animPole.sqrMagnitude > 1e-8f ? animPole.normalized : Vector3.zero) * (1f - _elbowHintWeight)
                     + (hintPole.sqrMagnitude > 1e-8f ? hintPole.normalized : Vector3.zero) * _elbowHintWeight;
        if (want.sqrMagnitude < 1e-8f) want = -transform.up;
        // Smooth the direction in the model's frame (turning the body is not lag), then re-project onto the current plane.
        Vector3 wantLocal = Quaternion.Inverse(transform.rotation) * want.normalized;
        if (!(freezePole && _poleInit))
        {
            if (!smoothPole) _poleInit = false;   // stepped solve: the animated elbow already steps
            if (!_poleInit) { _poleLocal = wantLocal; _poleInit = true; }
            else _poleLocal = Vector3.Slerp(_poleLocal, wantLocal, 1f - Mathf.Exp(-_elbowTurnSharpness * Time.deltaTime)).normalized;
        }
        Vector3 pole = Vector3.ProjectOnPlane(transform.rotation * _poleLocal, dir);
        if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(-transform.up, dir);
        pole.Normalize();

        float x = (lab * lab - lbc * lbc + lat * lat) / (2f * lat);
        float h = Mathf.Sqrt(Mathf.Max(0f, lab * lab - x * x));
        Vector3 elbow = a + dir * x + pole * h;

        _upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * _upper.rotation;
        Vector3 b2 = _lower.position, c2 = _hand.position;
        _lower.rotation = Quaternion.FromToRotation(c2 - b2, (a + dir * lat) - b2) * _lower.rotation;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying && _carrier != null && _layer >= 0 && _hand != null)
            _carrier.SetHandAnchor(_hand, _gripOffset, Quaternion.Euler(_gripEuler)); // live-tweak the grip
    }
#endif
}
