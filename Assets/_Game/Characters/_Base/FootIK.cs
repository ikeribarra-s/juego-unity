using UnityEngine;

/// <summary>
/// Humanoid foot IK for stairs, slopes and ledges. Lives on the model child (Animator +
/// AnimationHandler). Runs in <c>OnAnimatorIK</c> of the Base Layer (IK Pass on), so it is evaluated
/// exactly when <see cref="RetroAnimationStepper"/> steps the Animator: the feet are stepped with the
/// body (consistent retro look) while the per-foot offset is smoothed between steps (no popping).
///
/// Per foot: raycast down at the animated foot's XZ, measure the ground height relative to the
/// character's flat plane (the CharacterController base), and shift the animated foot by that delta.
/// The shift fades out as the animated foot lifts off the floor (swinging feet keep their arc, planted
/// feet stick to the surface), and the foot rotates toward the surface normal (clamped). The hips drop
/// by part of the lowest foot delta so the far foot can reach down. Off while airborne.
/// </summary>
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(40)]
public class FootIK : MonoBehaviour
{
    [SerializeField] private bool _enabled = true;
    [Tooltip("Master blend, 0 = pure animation.")]
    [SerializeField, Range(0f, 1f)] private float _weight = 1f;

    [Header("Ground probe")]
    [Tooltip("Raycast starts this far above the character's feet plane.")]
    [SerializeField] private float _probeUp = 0.7f;
    [Tooltip("Raycast length from the start (reaches this far below the feet plane + probeUp).")]
    [SerializeField] private float _probeLength = 1.5f;
    [SerializeField] private LayerMask _groundMask = ~0;
    [Tooltip("Largest height difference (m) a foot will follow, up or down.")]
    [SerializeField] private float _maxStep = 0.45f;
    [Tooltip("Extra height (m) added to the ankle (sole thickness / tuning).")]
    [SerializeField] private float _ankleOffset = 0f;

    [Header("Planted vs swinging")]
    [Tooltip("Animated sole height (m above feet plane) below which a foot counts as fully planted.")]
    [SerializeField] private float _plantedHeight = 0.05f;
    [Tooltip("Animated sole height at which the ground delta has faded completely (swing).")]
    [SerializeField] private float _swingHeight = 0.30f;

    [Header("Rotation")]
    [SerializeField, Range(0f, 60f)] private float _maxTilt = 35f;
    [SerializeField, Range(0f, 1f)] private float _rotationWeight = 1f;

    [Header("Hips")]
    [Tooltip("Fraction of the lowest foot delta applied to the hips (never raises).")]
    [SerializeField, Range(0f, 1f)] private float _pelvisFollow = 0.9f;
    [SerializeField] private float _maxPelvisDrop = 0.3f;

    [Header("Smoothing")]
    [SerializeField] private float _footSmoothTime = 0.04f;
    [SerializeField] private float _pelvisSmoothTime = 0.12f;
    [SerializeField] private float _tiltSmoothTime = 0.08f;
    [Tooltip("Fade time (s) of the whole effect when leaving/entering the ground.")]
    [SerializeField] private float _groundedFade = 0.1f;
    [Tooltip("Planar speed (m/s) above which the effect fades out (0 = never). Sprint jitter guard.")]
    [SerializeField] private float _fadeSprintAbove = 0f;

    [Header("Foot lock (no sliding between body steps)")]
    [Tooltip("The Animator only evaluates on RetroAnimationStepper steps, but the model moves every frame, so a planted " +
             "foot would slide with the body between steps and snap back on the next one. With foot lock a planted foot is " +
             "pinned in world space and the two leg bones are re-solved (analytic two-bone IK) EVERY frame from the last " +
             "stepped pose. Upper body stays stepped.")]
    [SerializeField] private bool _footLock = true;
    [Tooltip("Planted-ness above which a foot is locked.")]
    [SerializeField, Range(0.5f, 1f)] private float _lockPlanted = 0.85f;
    [Tooltip("Planted-ness below which the lock is released (swing starts).")]
    [SerializeField, Range(0f, 0.9f)] private float _unlockPlanted = 0.5f;
    [Tooltip("Horizontal distance (m) between locked spot and the animated foot beyond which the lock re-plants.")]
    [SerializeField] private float _maxLockStretch = 0.45f;
    [Tooltip("A foot is stance (lockable) while its animated world speed is below 0.3 m/s + this fraction of the body speed.")]
    [SerializeField, Range(0f, 1f)] private float _stanceSpeedFactor = 0.35f;

    [SerializeField] private bool _debugDraw;

    /// <summary>Last measured world offsets for tests.</summary>
    public float LeftDelta => _foot[0].delta;
    public float RightDelta => _foot[1].delta;
    public float PelvisOffset => _pelvis;
    public bool LeftLocked => _foot[0].locked;
    public bool RightLocked => _foot[1].locked;
    public float GlobalWeight => _global;

    private static readonly int GroundedHash = Animator.StringToHash("Grounded");

    private struct FootState
    {
        public float delta, deltaVel, ground;
        public Vector3 normal, normalVel;
        public bool init;
        public bool locked, hasPrev;
        public Vector3 lockPos, prevAnim;
        public float lockGroundOffset;
    }

    private Transform[] _legBones;           // L upper, lower, foot, R upper, lower, foot
    private readonly Quaternion[] _legCache = new Quaternion[6];
    private bool _legCacheValid;
    private int _stepFrame = -1;
    private Vector3 _prevRoot;
    private bool _hasPrevRoot;

    private Animator _animator;
    private CharacterMovement _movement;
    private CharacterController _controller;
    private Transform _characterRoot;
    private Transform _lFoot, _rFoot;
    private float _ankleHeight;
    private readonly FootState[] _foot = new FootState[2];
    private float _pelvis, _pelvisVel, _global, _globalVel;
    private float _lastIkTime = -1f;
    private readonly RaycastHit[] _hits = new RaycastHit[10];

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _movement = GetComponentInParent<CharacterMovement>();
        _controller = GetComponentInParent<CharacterController>();
        var cb = GetComponentInParent<CharacterBase>();
        _characterRoot = cb != null ? cb.transform : transform.root;
        _lFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        _rFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        // Bind pose stands on the feet plane: ankle height = foot bone above the character root.
        if (_lFoot != null && _rFoot != null)
            _ankleHeight = Mathf.Max(0.02f, (_lFoot.position.y + _rFoot.position.y) * 0.5f - _characterRoot.position.y);
        _foot[0].normal = _foot[1].normal = Vector3.up;
        _legBones = new[]
        {
            _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),  _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),  _lFoot,
            _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), _rFoot,
        };
    }

    // Runs every frame after the (possibly skipped) stepped Animator update. On a step frame the bones hold the
    // freshly solved pose: cache the leg rotations and pin the locked feet where the IK put them. On the frames in
    // between, restore the cached pose (the model has moved with the root) and re-solve the legs so locked feet stay put.
    private void LateUpdate()
    {
        if (_legBones == null || _legBones[0] == null || _legBones[1] == null || _legBones[3] == null || _legBones[4] == null) return;
        bool stepFrame = _stepFrame == Time.frameCount;
        bool any = _foot[0].locked || _foot[1].locked;

        if (stepFrame)
        {
            for (int i = 0; i < 6; i++) _legCache[i] = _legBones[i].localRotation;
            _legCacheValid = true;
            // lockPos is NOT re-read from the solved foot: any solver error would feed back and walk the foot away.
            return;
        }
        if (!any || !_legCacheValid || _global < 0.5f) return;

        for (int i = 0; i < 6; i++) _legBones[i].localRotation = _legCache[i];
        for (int i = 0; i < 2; i++)
            if (_foot[i].locked)
            {
                // XZ stays pinned; height follows the ground under the pinned spot (the CharacterController pops up
                // stairs between body steps, so a world-pinned height would be unreachable for a frame).
                Vector3 t = _foot[i].lockPos;
                Vector3 o = new Vector3(t.x, _characterRoot.position.y + _probeUp, t.z);
                int n = Physics.RaycastNonAlloc(o, Vector3.down, _hits, _probeLength, _groundMask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue, gy = float.NaN;
                for (int h = 0; h < n; h++)
                {
                    var hit = _hits[h];
                    if (hit.collider.transform.IsChildOf(_characterRoot)) continue;
                    var rb = hit.collider.attachedRigidbody;
                    if (rb != null && !rb.isKinematic) continue;
                    if (hit.distance < best) { best = hit.distance; gy = hit.point.y; }
                }
                if (!float.IsNaN(gy)) t.y = gy + _foot[i].lockGroundOffset;
                SolveLeg(_legBones[i * 3], _legBones[i * 3 + 1], _legBones[i * 3 + 2], t);
            }
    }

    private static void SolveLeg(Transform upper, Transform lower, Transform foot, Vector3 target)
    {
        Quaternion footRot = foot.rotation;
        Vector3 a = upper.position, b = lower.position, c = foot.position;
        float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
        if (l1 < 1e-4f || l2 < 1e-4f) return;
        Vector3 toT = target - a;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.005f);
        Vector3 u = toT.normalized;
        Vector3 ac = (c - a).normalized;
        Vector3 pole = (b - a) - ac * Vector3.Dot(b - a, ac);
        pole -= u * Vector3.Dot(pole, u);
        if (pole.sqrMagnitude < 1e-8f) pole = Vector3.Cross(u, Vector3.right);
        pole.Normalize();
        float x = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
        float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - x * x));
        Vector3 knee = a + u * x + pole * h;
        upper.rotation = Quaternion.FromToRotation(b - a, knee - a) * upper.rotation;
        Vector3 k = lower.position;
        lower.rotation = Quaternion.FromToRotation(foot.position - k, (a + u * d) - k) * lower.rotation;
        foot.rotation = footRot;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || _lFoot == null || _rFoot == null) return;

        _stepFrame = Time.frameCount;
        float now = Time.time;
        float dt = _lastIkTime < 0f ? 0.0416f : Mathf.Clamp(now - _lastIkTime, 0.001f, 0.2f);
        _lastIkTime = now;

        float feetY = _characterRoot.position.y;
        Vector3 rp = _characterRoot.position;
        float bodySpeed = _hasPrevRoot ? new Vector2(rp.x - _prevRoot.x, rp.z - _prevRoot.z).magnitude / dt : 0f;
        _prevRoot = rp; _hasPrevRoot = true;

        // Global weight: grounded + master toggle (+ optional sprint fade).
        bool grounded = _animator.GetBool(GroundedHash);
        float target = (_enabled && grounded) ? _weight : 0f;
        // Humanoid IK goals are wrong on a non-uniformly scaled model (crouch squash / landing squash): play the plain animation.
        Vector3 sc = transform.localScale;
        if (Mathf.Abs(sc.y / sc.x - 1f) > 0.03f) { target = 0f; _foot[0].locked = _foot[1].locked = false; }
        if (_fadeSprintAbove > 0f && _movement != null && _movement.PlanarVelocity.magnitude > _fadeSprintAbove) target = 0f;
        _global = Mathf.SmoothDamp(_global, target, ref _globalVel, _groundedFade, Mathf.Infinity, dt);
        if (_global < 0.001f)
        {
            // Let deltas relax so re-landing doesn't start from a stale value.
            for (int i = 0; i < 2; i++) { _foot[i].delta = 0f; _foot[i].deltaVel = 0f; _foot[i].init = false; _foot[i].normal = Vector3.up; _foot[i].locked = false; }
            _pelvis = 0f; _pelvisVel = 0f;
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            return;
        }

        // The character root sits on the floor (CharacterMovement lifts the capsule by its skin width), so the animation's foot
        // plane is simply the root height and flat ground gives a delta of 0 = the plain animated pose.
        float refY = feetY;
        float rootGround = feetY;
        if (ProbeGround(_characterRoot.position, feetY, out RaycastHit rootHit)) rootGround = Mathf.Min(feetY, rootHit.point.y);

        float lowest = 0f;
        for (int i = 0; i < 2; i++)
        {
            Transform bone = i == 0 ? _lFoot : _rFoot;
            AvatarIKGoal goal = i == 0 ? AvatarIKGoal.LeftFoot : AvatarIKGoal.RightFoot;
            Vector3 animPos = bone.position;
            // The IK goal rotation lives in the humanoid goal frame (toes +Z, up +Y), NOT the foot bone's axes: using
            // bone.rotation here pitched the foot down (tiptoe). Start from the animated goal rotation instead.
            Quaternion animRot = _animator.GetIKRotation(goal);

            float rawGround = feetY;
            Vector3 normal = Vector3.up;
            if (ProbeGround(animPos, feetY, out RaycastHit hit))
            {
                rawGround = Mathf.Clamp(hit.point.y, feetY - _maxStep, feetY + _maxStep);
                normal = hit.normal;
            }

            ref FootState f = ref _foot[i];
            // Smooth the ABSOLUTE ground height under the foot (not the delta to the root): when the
            // CharacterController pops up a step the delta is exact at once, only foot-to-foot changes are eased.
            if (!f.init) { f.ground = rawGround; f.normal = normal; f.init = true; }
            f.ground = Mathf.SmoothDamp(f.ground, rawGround, ref f.deltaVel, _footSmoothTime, Mathf.Infinity, dt);
            f.delta = Mathf.Clamp(f.ground - refY, -_maxStep, _maxStep);
            f.normal = Vector3.SmoothDamp(f.normal, normal, ref f.normalVel, _tiltSmoothTime, Mathf.Infinity, dt).normalized;

            // Planted-ness from the animated sole height above the feet plane.
            float sole = animPos.y - _ankleHeight - feetY;
            float planted = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_plantedHeight, _swingHeight, sole));

            Vector3 pos = animPos + Vector3.up * (f.delta * planted + (_ankleOffset * planted));

            // World speed of the ANIMATED foot between the last two steps: a stance foot is ~stationary in the world,
            // a swing foot passing the other leg at ground height is not (height alone would wrongly lock it).
            float footSpeed = 0f;
            if (f.hasPrev) footSpeed = new Vector2(animPos.x - f.prevAnim.x, animPos.z - f.prevAnim.z).magnitude / dt;
            f.prevAnim = animPos; f.hasPrev = true;
            float stanceSpeed = 0.3f + _stanceSpeedFactor * bodySpeed;

            // Foot lock: pin the planted foot's XZ in world space (re-solved per frame in LateUpdate).
            if (_footLock && target > 0f && _global > 0.5f)
            {
                // Ankle height above the ground while locked. Refreshed on EVERY step (not just at lock time): fast gaits lock
                // the foot while it is still ~5 cm up and the clip keeps lowering it, so a frozen offset left the sole floating.
                f.lockGroundOffset = pos.y - f.ground;
                if (f.locked)
                {
                    Vector2 off = new Vector2(f.lockPos.x - animPos.x, f.lockPos.z - animPos.z);
                    if (planted < _unlockPlanted || footSpeed > stanceSpeed * 1.8f || off.magnitude > _maxLockStretch) f.locked = false;
                    else { pos.x = f.lockPos.x; pos.z = f.lockPos.z; }
                }
                if (!f.locked && planted >= _lockPlanted && footSpeed < stanceSpeed) { f.locked = true; f.lockPos = pos; }
            }
            else f.locked = false;
            // Never sink below the surface under the foot (sole at least on the ground when planted).
            Vector3 n = Vector3.Slerp(Vector3.up, f.normal, planted);
            float tilt = Vector3.Angle(Vector3.up, n);
            if (tilt > _maxTilt) n = Vector3.Slerp(Vector3.up, n, _maxTilt / tilt);
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, n) * animRot;

            _animator.SetIKPositionWeight(goal, _global);
            _animator.SetIKPosition(goal, pos);
            _animator.SetIKRotationWeight(goal, _global * _rotationWeight);
            _animator.SetIKRotation(goal, rot);

            lowest = Mathf.Min(lowest, Mathf.Clamp(f.ground - rootGround, -_maxStep, _maxStep) * planted);

            if (_debugDraw) Debug.DrawLine(animPos, pos, Color.green);
        }

        // Hips follow the lowest foot (down only), smoothed.
        float pelvisTarget = Mathf.Clamp(lowest * _pelvisFollow, -_maxPelvisDrop, 0f) * _global;
        _pelvis = Mathf.SmoothDamp(_pelvis, pelvisTarget, ref _pelvisVel, _pelvisSmoothTime, Mathf.Infinity, dt);
        if (Mathf.Abs(_pelvis) > 0.0005f)
            _animator.bodyPosition += Vector3.up * _pelvis;
    }

    private bool ProbeGround(Vector3 footPos, float feetY, out RaycastHit best)
    {
        Vector3 origin = new Vector3(footPos.x, feetY + _probeUp, footPos.z);
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, _probeLength, _groundMask, QueryTriggerInteraction.Ignore);
        best = default;
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i];
            if (h.collider.transform.IsChildOf(_characterRoot)) continue;
            var rb = h.collider.attachedRigidbody;
            if (rb != null && !rb.isKinematic) continue; // props / thrown torches / other players' bodies
            if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
        }
        return found;
    }
}
