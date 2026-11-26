using UnityEngine;

/// <summary>
/// Per-character water FX driver (auto-added by CharacterBase.Awake). Reads only the transform, the
/// CharacterController velocity and CharacterAudio.FootContact, so it works for remote players too.
///  - foot contact in water: crown splash at the surface (count by walk / sprint / crouch);
///  - entering water: splash scaled by the fall speed + a big ring; wading: small rings while moving;
///  - leaving water: droplets falling off the body for a moment.
/// Distance-culled against Camera.main; all output goes through WaterSplashFX (global budget).
/// </summary>
[DisallowMultipleComponent]
public class WaterSplashEmitter : MonoBehaviour
{
    [SerializeField] private float _minSpeed = 0.3f;
    [SerializeField] private float _stepCooldown = 0.18f;
    [SerializeField] private float _cullDistance = 30f;
    [Header("Step crown (count per contact)")]
    [SerializeField] private int _walkCount = 5;
    [SerializeField] private int _sprintCount = 9;
    [SerializeField] private int _crouchCount = 3;
    [SerializeField] private float _footOffset = 0.12f;
    [Tooltip("Step crown spawns this far ahead of the character (m), so it isn't hidden behind the legs.")]
    [SerializeField] private float _stepForward = 0.3f;
    [Tooltip("Extra lead along the movement direction, in seconds of horizontal velocity (sprint = further ahead).")]
    [SerializeField] private float _stepLead = 0.05f;
    [Header("Enter")]
    [SerializeField] private float _walkInK = 0.15f;
    [SerializeField] private float _impactMin = 2f;
    [SerializeField] private float _impactRange = 7f;
    [SerializeField] private float _bigRippleSize = 2f;
    [Header("Jump out of water")]
    [Tooltip("Horizontal speed (m/s) that gives the biggest take-off splash (k = 1).")]
    [SerializeField] private float _jumpSpeedForMax = 9f;
    [SerializeField] private float _jumpBaseK = 0.2f;
    [SerializeField] private Vector2 _jumpRipple = new Vector2(0.9f, 1.5f);
    [Header("Wading ripples")]
    [SerializeField] private float _smallRippleSize = 1.2f;
    [SerializeField] private float _rippleWalkInterval = 0.4f;
    [SerializeField] private float _rippleSprintInterval = 0.25f;
    [Header("Drips after leaving")]
    [SerializeField] private float _dripDuration = 1.2f;
    [SerializeField] private float _dripInterval = 0.14f;
    [SerializeField] private Vector2 _dripHeight = new Vector2(0.3f, 1.2f);
    [SerializeField] private float _dripRadius = 0.25f;
    [SerializeField] private bool _debugLog;

    private CharacterBase _character;
    private CharacterController _cc;
    private CharacterAudio _audio;
    private Camera _cam;
    private bool _inWater;
    private float _outTimer, _lastStep = -1f, _lastEnter = -1f, _rippleTimer, _dripUntil = -1f, _dripTimer;
    private float _recentFall, _recentFallTime;
    private bool _flip;
    private bool _subscribed, _jumpSubscribed;
    private CharacterMovement _move;

    private void Awake()
    {
        _character = GetComponent<CharacterBase>();
        _cc = GetComponent<CharacterController>();
    }

    private void Start() { Subscribe(); }
    private void OnEnable() { Subscribe(); }
    private void OnDisable()
    {
        if (_subscribed && _audio != null) _audio.FootContact -= OnFootContact;
        _subscribed = false;
        if (_jumpSubscribed && _move != null) _move.Jumped -= OnJumped;
        _jumpSubscribed = false;
    }

    private void OnJumped()
    {
        var fx = WaterSplashFX.Instance;
        if (fx == null || Dead) return;
        var vol = WaterVolume.TryFind(transform.position);
        if (vol == null || Culled()) return;
        float k = Mathf.Clamp01(Mathf.Max(_jumpBaseK, HSpeed() / _jumpSpeedForMax));
        var p = transform.position; p.y = vol.SurfaceY + 0.02f;
        fx.JumpOut(p, k);
        fx.Ripple(p, Mathf.Lerp(_jumpRipple.x, _jumpRipple.y, k));
        _lastStep = Time.time;                      // the take-off replaces the step crown of that frame
        if (_debugLog) Debug.Log("[WaterSplashEmitter] jump-out k=" + k.ToString("0.00"), this);
    }

    private void Subscribe()
    {
        if (!_jumpSubscribed)
        {
            if (_move == null && _character != null) _move = _character.Movement;
            if (_move != null) { _move.Jumped += OnJumped; _jumpSubscribed = true; }
        }
        if (_subscribed) return;
        if (_audio == null) _audio = GetComponent<CharacterAudio>();
        if (_audio == null) return;
        _audio.FootContact += OnFootContact;
        _subscribed = true;
    }

    private bool Dead { get { return _character != null && _character.Vitals != null && _character.Vitals.IsDead; } }

    private bool Culled()
    {
        if (_cam == null) _cam = Camera.main;
        return _cam != null && (_cam.transform.position - transform.position).sqrMagnitude > _cullDistance * _cullDistance;
    }

    private float HSpeed()
    {
        Vector3 v = _cc != null ? _cc.velocity : Vector3.zero;
        return Mathf.Sqrt(v.x * v.x + v.z * v.z);
    }

    private Vector3 FootPos(float surfaceY)
    {
        Vector3 p = transform.position;
        _flip = !_flip;
        Vector3 v = _cc != null ? _cc.velocity : Vector3.zero;
        v.y = 0f;
        Vector3 off = transform.right * (_flip ? _footOffset : -_footOffset)
                    + transform.forward * _stepForward + v * _stepLead;
        return new Vector3(p.x + off.x, surfaceY + 0.02f, p.z + off.z);
    }

    private void OnFootContact()
    {
        var fx = WaterSplashFX.Instance;
        if (fx == null || _cc == null || Dead || !_cc.isGrounded || Time.time - _lastStep < _stepCooldown) return;
        if (HSpeed() < _minSpeed) return;
        var vol = WaterVolume.TryFind(transform.position);
        if (vol == null || Culled()) return;
        _lastStep = Time.time;

        var m = _character != null ? _character.Movement : null;
        bool sprint = m != null && m.IsSprinting, crouch = m != null && m.IsCrouching;
        int n = sprint ? _sprintCount : crouch ? _crouchCount : _walkCount;
        float size = sprint ? 1.25f : crouch ? 0.7f : 1f;
        float speed = sprint ? 1.3f : 1f;
        fx.Step(FootPos(vol.SurfaceY), n, size, speed);
    }

    private void Update()
    {
        var fx = WaterSplashFX.Instance;
        if (fx == null || _cc == null) return;

        // recent downward speed (the controller's velocity is already ~0 on the frame it lands)
        float fall = Mathf.Max(0f, -_cc.velocity.y);
        if (fall >= _recentFall || Time.time - _recentFallTime > 0.25f) { _recentFall = fall; _recentFallTime = Time.time; }

        var vol = WaterVolume.TryFind(transform.position);
        bool now = vol != null && !Dead;
        float dt = Time.deltaTime;

        if (now)
        {
            _outTimer = 0f;
            if (!_inWater)
            {
                _inWater = true; _dripUntil = -1f;
                if (Time.time - _lastEnter > 0.3f && !Culled())
                {
                    _lastEnter = Time.time;
                    float k = Mathf.Max(_walkInK, Mathf.Clamp01((_recentFall - _impactMin) / _impactRange));
                    var p = transform.position; p.y = vol.SurfaceY + 0.02f;
                    fx.Enter(p, k);
                    fx.Ripple(p, Mathf.Lerp(1.2f, _bigRippleSize, k));
                    _rippleTimer = 0.2f;
                    if (_debugLog) Debug.Log("[WaterSplashEmitter] enter fall=" + _recentFall.ToString("0.0") + " k=" + k.ToString("0.00"), this);
                }
            }
            else if (HSpeed() >= _minSpeed && _cc.isGrounded)
            {
                _rippleTimer -= dt;
                if (_rippleTimer <= 0f)
                {
                    var m = _character != null ? _character.Movement : null;
                    _rippleTimer = m != null && m.IsSprinting ? _rippleSprintInterval : _rippleWalkInterval;
                    if (!Culled()) fx.Ripple(FootPos(vol.SurfaceY), _smallRippleSize);
                }
            }
            else _rippleTimer = 0.1f;
        }
        else if (_inWater)
        {
            _outTimer += dt;                       // debounce the edge so a lip step doesn't spam exit/enter
            if (_outTimer > 0.15f)
            {
                _inWater = false; _dripUntil = Time.time + _dripDuration; _dripTimer = 0f;
            }
        }

        if (_dripUntil > 0f)
        {
            if (Time.time >= _dripUntil || Dead) _dripUntil = -1f;
            else
            {
                _dripTimer -= dt;
                if (_dripTimer <= 0f)
                {
                    _dripTimer = _dripInterval;
                    if (!Culled())
                    {
                        Vector2 c = Random.insideUnitCircle * _dripRadius;
                        fx.Drip(transform.position + new Vector3(c.x, Random.Range(_dripHeight.x, _dripHeight.y), c.y));
                    }
                }
            }
        }
    }
}
