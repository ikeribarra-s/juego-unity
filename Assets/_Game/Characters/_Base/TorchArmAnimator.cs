using UnityEngine;

/// <summary>
/// Raises the left arm while the character carries a torch. Blends the Animator layer
/// <c>LeftArm_Torch</c> (Override, left-arm + left-fingers mask, built by
/// <c>Fluffterror > Animation > Build Torch Hold</c>) in on <see cref="TorchCarrier.TorchPickedUp"/>
/// and out on <see cref="TorchCarrier.TorchDropped"/>; every other animation keeps playing on the rest
/// of the body. While the layer exists it hands TorchCarrier the left-hand bone + grip, and TorchCarrier
/// attaches the torch <b>rigidly</b> to the hand (handle in the fist, zero lag).
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
/// The IK reach is soft-limited (<see cref="_maxReach"/>) so the elbow never snaps straight (the straight-arm
/// singularity made the elbow wave when the target crossed the arm's reach). Only the left arm is touched;
/// the body stays stepped. A small <see cref="_viewOffset"/> fine-tunes the framing; remote viewers see the
/// plain animated pose (which itself already raises the lantern forward-left at head height).
/// The IK blends in with the layer weight. Turn it off for remote (non-owner) characters.
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
        _neckCam  = GetComponent<NeckFollowCamera>();
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

    private void Subscribe()
    {
        if (_carrier == null) return;
        Unsubscribe(); // idempotent: Start and OnEnable may both run
        _carrier.TorchPickedUp += OnPickedUp;
        _carrier.TorchDropped  += OnDropped;
        if (_layer >= 0 && _hand != null) _carrier.SetHandAnchor(_hand, _gripOffset, Quaternion.Euler(_gripEuler));
        _target = _carrier.HasTorch ? 1f : 0f;   // torch may already be held at start
    }

    private void Unsubscribe()
    {
        if (_carrier == null) return;
        _carrier.TorchPickedUp -= OnPickedUp;
        _carrier.TorchDropped  -= OnDropped;
        _carrier.SetHandAnchor(null, Vector3.zero, Quaternion.identity);
    }

    private void OnPickedUp(Torch _) => _target = 1f;
    private void OnDropped(Torch _)  => _target = 0f;

    private void Update()
    {
        if (_layer < 0) return;
        _blend = _blendTime > 0f ? Mathf.MoveTowards(_blend, _target, Time.deltaTime / _blendTime) : _target;
        _animator.SetLayerWeight(_layer, Weight);
    }

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

    private void LateUpdate()
    {
        if (!_smoothLocalView || _layer < 0 || _camera == null || _upper == null || _lower == null || _hand == null)
            return;

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
        if (w <= 0f) { _hasWritten = false; _smoothInit = false; _shoulderInitDone = false; _shSmoothInit = false; _poleInit = false; return; }

        // 2. Smooth the animated hand in the chest frame (camera position + chest orientation relative to bind).
        //    In this frame the hand only moves with the arm animation: the head-look IK leaning/twisting the spine
        //    (it lags the mouse) and the stepped body cancel out. Smoothing it in full camera space instead made it
        //    trail every pitch/yaw change and drift up the screen.
        Transform  cam   = _camera.transform;
        Quaternion level = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
        Quaternion body  = _chest != null ? _chest.rotation * _chestBindInv : level;
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
        _smoothPos = Vector3.SmoothDamp(_smoothPos, rawPos, ref _smoothVel, _handSmoothTime);
        _smoothRot = Quaternion.Slerp(_smoothRot, rawRot, 1f - Mathf.Exp(-_handRotationSharpness * Time.deltaTime));

        // Place it in the view frame (camera yaw + clamped pitch) so it stays put on screen; the rotation only
        // follows part of the pitch (_pitchTiltFollow) so the torch stays nearly upright.
        Vector3 fwd   = Quaternion.Inverse(level) * cam.forward;
        float   pitch = -Mathf.Atan2(fwd.y, new Vector2(fwd.x, fwd.z).magnitude) * Mathf.Rad2Deg; // + = looking down
        pitch = Mathf.Clamp(pitch, -_pitchFollowLimit, _pitchFollowLimit) * _pitchFollow;
        Quaternion view = level * Quaternion.Euler(pitch, 0f, 0f);

        // Glide the shoulder joint: smooth it in the view frame and turn the collarbone toward the smoothed point.
        if (_clavicle != null && _shoulderSmoothTime > 0f)
        {
            Vector3 shRaw = Quaternion.Inverse(view) * (_upper.position - cam.position);
            if (!_shSmoothInit) { _shSmooth = shRaw; _shSmoothVel = Vector3.zero; _shSmoothInit = true; }
            _shSmooth = Vector3.SmoothDamp(_shSmooth, shRaw, ref _shSmoothVel, _shoulderSmoothTime);
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
        Vector3    targetPos = Vector3.Lerp(_hand.position, cam.position + view * held, w);
        Quaternion tilt      = level * Quaternion.Euler(pitch * _pitchTiltFollow, 0f, 0f);
        Quaternion targetRot = Quaternion.Slerp(_hand.rotation, tilt * _smoothRot, w);

        // 3. Two-bone IK to the smoothed target, then set the hand rotation.
        SolveTwoBone(targetPos);
        _hand.rotation = targetRot;

        if (_clavicle != null) _wroteClav = _clavicle.localRotation;
        _wroteUpper = _upper.localRotation;
        _wroteLower = _lower.localRotation;
        _wroteHand  = _hand.localRotation;
        _hasWritten = true;
    }

    /// <summary>Analytic two-bone IK: elbow stays on the animated bend plane (pole = current elbow).</summary>
    private void SolveTwoBone(Vector3 target)
    {
        Vector3 a = _upper.position, b = _lower.position, c = _hand.position;
        float lab = Vector3.Distance(a, b), lbc = Vector3.Distance(b, c), len = lab + lbc;
        Vector3 toT = target - a;
        Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (c - a).normalized;

        // Keep the reach inside a comfortable band (eased at both ends): never straight (undefined elbow), never folded.
        float d = toT.magnitude, hi = len * Mathf.Min(_comfortReach, _maxReach), lo = len * _minReach;
        float kneeHi = hi - 0.08f * len, kneeLo = lo + 0.08f * len;
        if (d > kneeHi) d = kneeHi + (hi - kneeHi) * (1f - Mathf.Exp(-(d - kneeHi) / (hi - kneeHi)));
        if (d < kneeLo) d = kneeLo - (kneeLo - lo) * (1f - Mathf.Exp(-(kneeLo - d) / (kneeLo - lo)));
        float lat = Mathf.Clamp(d, Mathf.Abs(lab - lbc) + 1e-4f, hi);

        // Elbow direction: the animated elbow blended toward a stable chest-relative hint.
        Vector3 animPole = Vector3.ProjectOnPlane(b - a, dir);
        Vector3 hintPole = Vector3.ProjectOnPlane(transform.rotation * _elbowHint, dir);
        Vector3 want = (animPole.sqrMagnitude > 1e-8f ? animPole.normalized : Vector3.zero) * (1f - _elbowHintWeight)
                     + (hintPole.sqrMagnitude > 1e-8f ? hintPole.normalized : Vector3.zero) * _elbowHintWeight;
        if (want.sqrMagnitude < 1e-8f) want = -transform.up;
        // Smooth the direction in the model's frame (turning the body is not lag), then re-project onto the current plane.
        Vector3 wantLocal = Quaternion.Inverse(transform.rotation) * want.normalized;
        if (!_poleInit) { _poleLocal = wantLocal; _poleInit = true; }
        _poleLocal = Vector3.Slerp(_poleLocal, wantLocal, 1f - Mathf.Exp(-_elbowTurnSharpness * Time.deltaTime)).normalized;
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
