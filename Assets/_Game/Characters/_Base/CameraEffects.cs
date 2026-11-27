using UnityEngine;

public class CameraEffects : MonoBehaviour
{
    [SerializeField] private CharacterBase _character;

    private void Awake()
    {
        if (_character == null)
            _character = GetComponentInParent<CharacterBase>();
    }

    [Header("Head Bob")]
    [SerializeField] private float _bobFrequency  = 8f;    // cycles per second at full speed
    [SerializeField] private float _bobAmplitudeY = 0.03f; // vertical travel
    [SerializeField] private float _bobAmplitudeX = 0.015f;// side-to-side travel
    [SerializeField] private float _bobSmoothing  = 14f;

    [Header("Tilt")]
    [SerializeField] private float _maxTilt   = 4f;  // degrees on Z axis
    [SerializeField] private float _tiltSpeed = 8f;

    private float   _bobTimer;
    private Vector3 _targetBobOffset;
    private Vector3 _bobCurrent;
    private float   _currentTilt;

    /// <summary>Smoothed head-bob offset in the camera parent's local space (CameraRoot).</summary>
    public Vector3 BobOffset => _bobCurrent;

    /// <summary>
    /// Set by <see cref="NeckFollowCamera"/>, which owns the camera position (it adds <see cref="BobOffset"/> itself).
    /// Writing <c>localPosition</c> here as well dragged the camera toward CameraRoot every Update, and the neck
    /// smoothing started from that dragged spot each frame -> frame-time-dependent vertical jitter at idle.
    /// </summary>
    public bool PositionDrivenExternally { get; set; }

    private void Update()
    {
        if (_character == null) return;

        float speed = _character.Controller.velocity.magnitude;
        HandleBob(speed);
        HandleTilt();
    }

    private void HandleBob(float speed)
    {
        bool shouldBob = speed > 0.15f && _character.Controller.isGrounded;

        if (shouldBob)
        {
            float speedRatio = speed / Mathf.Max(_character.Stats.MoveSpeed, 0.01f);
            _bobTimer += Time.deltaTime * _bobFrequency * speedRatio;

            _targetBobOffset = new Vector3(
                Mathf.Sin(_bobTimer * 0.5f) * _bobAmplitudeX * speedRatio,
                Mathf.Sin(_bobTimer)        * _bobAmplitudeY * speedRatio,
                0f
            );
        }
        else
        {
            _targetBobOffset = Vector3.zero;
        }

        _bobCurrent = Vector3.Lerp(_bobCurrent, _targetBobOffset, Mathf.Clamp01(Time.deltaTime * _bobSmoothing));
        if (!PositionDrivenExternally)
            transform.localPosition = _bobCurrent;
    }

    private void HandleTilt()
    {
        // Measure lateral velocity in the character's local space
        Vector3 localVel       = _character.transform.InverseTransformDirection(_character.Controller.velocity);
        float normalizedLateral = localVel.x / Mathf.Max(_character.Stats.MoveSpeed, 0.01f);
        float targetTilt        = -normalizedLateral * _maxTilt;

        _currentTilt = Mathf.Lerp(_currentTilt, targetTilt, Time.deltaTime * _tiltSpeed);

        transform.localRotation = Quaternion.Euler(0f, 0f, _currentTilt);
    }
}
