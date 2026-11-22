using UnityEngine;

/// <summary>
/// Raises the left arm while the character carries a torch. Blends the Animator layer
/// <c>LeftArm_Torch</c> (Override, left-arm + left-fingers mask, built by
/// <c>Fluffterror > Animation > Build Torch Hold</c>) in on <see cref="TorchCarrier.TorchPickedUp"/>
/// and out on <see cref="TorchCarrier.TorchDropped"/>; every other animation keeps playing on the rest
/// of the body. While the layer exists it hands TorchCarrier the left-hand bone + grip, and TorchCarrier
/// attaches the torch <b>rigidly</b> to the hand (handle in the fist, zero lag).
///
/// <b>First-person smoothing (local view).</b> <see cref="RetroAnimationStepper"/> poses the body only on
/// stepped frames while the camera moves every frame, so the raised hand (and the torch in it) would tick
/// on screen. When <see cref="_smoothLocalView"/> is on, every LateUpdate (after the camera moved, before
/// TorchCarrier) this takes the latest <i>animated</i> hand pose, SmoothDamps it in camera space, and
/// re-solves the arm with an analytic two-bone IK (upper arm / forearm, elbow kept on the animated bend
/// plane) so the hand itself glides every frame. Only the left arm is touched; the body stays stepped.
/// A small camera-space <see cref="_viewOffset"/> frames the lantern in the upper-left of the screen
/// (viewmodel-style); remote viewers see the plain animated pose.
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
    [SerializeField] private Vector3 _gripOffset = new(-0.036f, 0.04f, 0.048f);
    [Tooltip("Rotation from the hand bone's frame to the torch's upright frame (shaft along +Y). " +
             "Default: +Y = the curled-finger loop axis of this 3-finger rig, so the shaft passes through the fist.")]
    [SerializeField] private Vector3 _gripEuler = new(327.4f, 323.1f, 270f);

    [Header("First-person arm smoothing")]
    [Tooltip("Local player only: smooth the raised hand every frame in camera space (two-bone IK) so it " +
             "doesn't tick with the stepped body animation. Off = raw stepped animation (e.g. remote players).")]
    [SerializeField] private bool _smoothLocalView = true;
    [Tooltip("SmoothDamp time (s) of the hand position in camera space.")]
    [SerializeField] private float _handSmoothTime = 0.1f;
    [Tooltip("Exponential smoothing speed of the hand rotation in camera space (higher = snappier).")]
    [SerializeField] private float _handRotationSharpness = 10f;
    [Tooltip("First-person framing (local view only): camera-space offset (m) added to the smoothed hand target so " +
             "the lantern sits upper-left on screen when looking level. Other players see the plain animated pose.")]
    [SerializeField] private Vector3 _viewOffset = new(0.08f, 0.06f, 0.05f);

    private Animator     _animator;
    private TorchCarrier _carrier;
    private Transform    _upper, _lower, _hand;
    private Camera       _camera;
    private int          _layer = -1;
    private float        _blend;     // linear 0..1, eased when applied
    private float        _target;

    // Last animated (stepped) local rotations, and what the IK wrote last frame (to detect a new step).
    private Quaternion _animUpper, _animLower, _animHand;
    private Quaternion _wroteUpper, _wroteLower, _wroteHand;
    private bool       _hasWritten;
    private Vector3    _smoothPos, _smoothVel;   // camera space
    private Quaternion _smoothRot;               // camera space
    private bool       _smoothInit;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _upper    = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _lower    = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        _hand     = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
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

    private void LateUpdate()
    {
        if (!_smoothLocalView || _layer < 0 || _camera == null || _upper == null || _lower == null || _hand == null)
            return;

        // 1. Recover the latest animated pose. On frames the stepper didn't update the Animator the bones
        //    still hold what the IK wrote, so only re-cache when they differ (= a new animated step).
        if (!_hasWritten || _upper.localRotation != _wroteUpper || _lower.localRotation != _wroteLower ||
            _hand.localRotation != _wroteHand)
        {
            _animUpper = _upper.localRotation;
            _animLower = _lower.localRotation;
            _animHand  = _hand.localRotation;
        }
        _upper.localRotation = _animUpper;
        _lower.localRotation = _animLower;
        _hand.localRotation  = _animHand;

        float w = Weight;
        if (w <= 0f) { _hasWritten = false; _smoothInit = false; return; }

        // 2. Smooth the animated hand in camera space (what the player sees).
        Transform cam = _camera.transform;
        Vector3    rawPos = cam.InverseTransformPoint(_hand.position);
        Quaternion rawRot = Quaternion.Inverse(cam.rotation) * _hand.rotation;
        if (!_smoothInit)
        {
            _smoothPos = rawPos; _smoothVel = Vector3.zero; _smoothRot = rawRot; _smoothInit = true;
        }
        _smoothPos = Vector3.SmoothDamp(_smoothPos, rawPos, ref _smoothVel, _handSmoothTime);
        _smoothRot = Quaternion.Slerp(_smoothRot, rawRot, 1f - Mathf.Exp(-_handRotationSharpness * Time.deltaTime));

        Vector3    targetPos = Vector3.Lerp(_hand.position, cam.TransformPoint(_smoothPos + _viewOffset), w);
        Quaternion targetRot = Quaternion.Slerp(_hand.rotation, cam.rotation * _smoothRot, w);

        // 3. Two-bone IK to the smoothed target, then set the hand rotation.
        SolveTwoBone(targetPos);
        _hand.rotation = targetRot;

        _wroteUpper = _upper.localRotation;
        _wroteLower = _lower.localRotation;
        _wroteHand  = _hand.localRotation;
        _hasWritten = true;
    }

    /// <summary>Analytic two-bone IK: elbow stays on the animated bend plane (pole = current elbow).</summary>
    private void SolveTwoBone(Vector3 target)
    {
        Vector3 a = _upper.position, b = _lower.position, c = _hand.position;
        float lab = Vector3.Distance(a, b), lbc = Vector3.Distance(b, c);
        Vector3 toT = target - a;
        float lat = Mathf.Clamp(toT.magnitude, Mathf.Abs(lab - lbc) + 1e-4f, lab + lbc - 1e-4f);
        Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (c - a).normalized;

        Vector3 pole = Vector3.ProjectOnPlane(b - a, dir);
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
