using UnityEngine;

/// <summary>
/// Body-aware first-person camera. The camera keeps its <b>rotation</b> from the look rig
/// (mouse — driven by CharacterMovement on CameraRoot), but its <b>position</b> is anchored
/// to the animated <c>Neck</c> bone so the view moves naturally with the body's
/// walk / jump / lean animation. The camera is clamped so it never drops below the neck joint.
///
/// Runs in LateUpdate, after the Animator has posed the skeleton. Place on the model child
/// (the GameObject with the Animator) and assign the Camera transform.
/// </summary>
[RequireComponent(typeof(Animator))]
public class NeckFollowCamera : MonoBehaviour
{
    [SerializeField] private Transform _camera;

    [Tooltip("0 = static eye position (no body influence), 1 = fully anchored to the neck bone.")]
    [SerializeField, Range(0f, 1f)] private float _followWeight = 1f;

    [Tooltip("Offset from the neck joint to the eye, expressed in the character's orientation " +
             "(x = right, y = up, z = forward). Lifts the camera from the neck up to eye level.")]
    [SerializeField] private Vector3 _eyeOffset = new Vector3(0f, 0.15f, 0.08f);

    [Tooltip("Smoothing time in seconds. 0 = glued exactly to the neck (snappiest, but shows the " +
             "stepped 24fps motion). ~0.04-0.06 hides the step without feeling heavy. Lower = snappier.")]
    [SerializeField] private float _smoothTime = 0.05f;

    private Animator  _animator;
    private Transform _neck;
    private Transform _body;        // character root — stable orientation for the eye offset
    private Vector3   _restLocalPos;
    private bool      _hasRest;
    private Vector3   _smoothVel;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
        if (_neck == null) _neck = _animator.GetBoneTransform(HumanBodyBones.Head);
        if (_neck == null)
            Debug.LogError("[NeckFollowCamera] No Neck/Head bone — is the avatar Humanoid?", this);

        var character = GetComponentInParent<CharacterBase>();
        _body = character != null ? character.transform : transform.root;

        if (_camera != null && _camera.parent != null)
        {
            _restLocalPos = _camera.localPosition;
            _hasRest = true;
        }
    }

    private void LateUpdate()
    {
        if (_camera == null || _neck == null) return;

        // Where the camera would sit without any body influence (its authored local spot).
        Vector3 staticPos = (_hasRest && _camera.parent != null)
            ? _camera.parent.TransformPoint(_restLocalPos)
            : _camera.position;

        // Neck-anchored eye position: the neck joint plus an offset along the BODY's
        // orientation (not the neck's animated rotation, so the offset stays stable).
        Vector3 neckPos = _neck.position
                        + _body.right   * _eyeOffset.x
                        + _body.up      * _eyeOffset.y
                        + _body.forward * _eyeOffset.z;

        Vector3 target = Vector3.Lerp(staticPos, neckPos, _followWeight);

        // Never let the camera drop below the neck joint.
        if (target.y < _neck.position.y) target.y = _neck.position.y;

        _camera.position = _smoothTime > 0f
            ? Vector3.SmoothDamp(_camera.position, target, ref _smoothVel, _smoothTime)
            : target;
    }
}
