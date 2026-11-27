using UnityEngine;

/// <summary>
/// The dead player's skull keeps living. Added by <see cref="SkeletonDisassembler"/> to the skull piece.
///
/// For the LOCAL player (<see cref="Init"/> with a camera) the first-person camera (with its AudioListener / reverb)
/// leaves the body and orbits the skull: mouse look via <see cref="InputReader.LookEvent"/>, <see cref="_distance"/>
/// from a pivot just above the skull, pulled in front of walls / floor by a sphere cast (dynamic props and the
/// remains themselves are ignored). Space (<see cref="InputReader.JumpEvent"/>) makes the skull roll-hop in the
/// camera's horizontal look direction (impulse + roll spin + random wobble), then waits a random cooldown in
/// <see cref="_cooldownRange"/>. A small IMGUI line shows the cooldown.
/// Without a camera (remote player / dummy) the skull is just a physics prop.
/// </summary>
[DefaultExecutionOrder(200)]   // after everything that could still touch the camera
public class LivingSkull : MonoBehaviour
{
    [Header("Roll (Space)")]
    [Tooltip("Random cooldown after each roll, seconds (min, max).")]
    [SerializeField] private Vector2 _cooldownRange = new(5f, 8f);
    [Tooltip("Delay after death before the first roll is allowed (lets the burst settle).")]
    [SerializeField] private float _initialDelay = 0.6f;
    [Tooltip("Horizontal velocity change of a roll (m/s).")]
    [SerializeField] private float _rollSpeed = 3.4f;
    [Tooltip("Upward velocity change: the little hop (m/s).")]
    [SerializeField] private float _hopSpeed = 2.2f;
    [Tooltip("Angular velocity added around the roll axis (rad/s).")]
    [SerializeField] private float _rollSpin = 14f;
    [Tooltip("Random extra angular velocity for a comedic wobble (rad/s).")]
    [SerializeField] private float _wobble = 6f;
    [Tooltip("Dampings used from the first roll on (the scatter's settle damping would stop the roll dead).")]
    [SerializeField] private float _rollLinearDamping = 0.2f;
    [SerializeField] private float _rollAngularDamping = 0.6f;
    [SerializeField] private SoundDefinition _rollSound;

    [Header("Orbit camera (local player)")]
    [SerializeField] private float _distance = 1.5f;
    [SerializeField] private float _minDistance = 0.3f;
    [Tooltip("Orbit pivot height above the skull centre (m).")]
    [SerializeField] private float _pivotHeight = 0.25f;
    [SerializeField] private float _startPitch = 22f;
    [SerializeField] private Vector2 _pitchLimits = new(-5f, 75f);
    [Tooltip("Sphere-cast radius that keeps the camera out of walls and the floor.")]
    [SerializeField] private float _cameraRadius = 0.18f;
    [Tooltip("Pivot smoothing (s) so the bouncing skull doesn't shake the view.")]
    [SerializeField] private float _pivotSmoothTime = 0.08f;
    [SerializeField] private LayerMask _cameraBlockers = ~0;
    [Tooltip("Fallback mouse sensitivity when the owner has no stats (degrees per mouse delta unit).")]
    [SerializeField] private float _lookSensitivity = 0.15f;

    private Rigidbody     _body;
    private CharacterBase _owner;
    private InputReader   _input;
    private Camera        _cam;
    private Transform     _remainsRoot;

    private float   _yaw, _pitch;
    private Vector2 _look;
    private Vector3 _pivot, _pivotVel;
    private float   _readyAt;
    private float   _camDistance;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    private GUIStyle _style;
    private string   _label;
    private int      _labelTenths = -1;

    public bool   HasCamera         => _cam != null;
    public Camera Camera            => _cam;
    public bool   RollReady         => Time.time >= _readyAt;
    public float  CooldownRemaining => Mathf.Max(0f, _readyAt - Time.time);
    /// <summary>The cooldown picked after the last roll (s).</summary>
    public float  LastCooldown      { get; private set; }
    public int    RollCount         { get; private set; }
    /// <summary>Raised at the end of every roll (SkullBlink squeezes the lids).</summary>
    public event System.Action Rolled;
    public Vector3 LastRollDirection { get; private set; }
    public CharacterBase Owner      => _owner;
    public Vector2 YawPitch         => new(_yaw, _pitch);

    private DeathFXProfile _fxProfile;
    private SkullWisp _wisp;
    private bool  _wasReady;
    private float _flashUntil;

    /// <summary>Init with the death FX profile + wisp (roll / yip / puff / ready tick go through <see cref="DeathFX"/>).</summary>
    public void Init(CharacterBase owner, Camera cam, Transform remainsRoot, SoundDefinition rollSound, DeathFXProfile fx, SkullWisp wisp)
    {
        _fxProfile = fx;
        _wisp = wisp;
        Init(owner, cam, remainsRoot, rollSound);
    }

    public void Init(CharacterBase owner, Camera cam, Transform remainsRoot, SoundDefinition rollSound)
    {
        _body  = GetComponent<Rigidbody>();
        _owner = owner;
        _remainsRoot = remainsRoot;
        if (rollSound != null) _rollSound = rollSound;
        _readyAt = Time.time + _initialDelay;
        if (_body != null) _body.maxAngularVelocity = 40f;
        if (cam == null) return;

        _cam = cam;
        _input = owner != null ? owner.Input : null;
        Vector3 f = cam.transform.forward;
        _yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        _pitch = _startPitch;
        _pivot = transform.position + Vector3.up * _pivotHeight;
        _camDistance = _distance;

        // The camera leaves the dead body (its CameraEffects bob / tilt would fight the orbit).
        if (cam.TryGetComponent(out CameraEffects fx)) fx.enabled = false;
        cam.transform.SetParent(null, true);

        if (_input != null)
        {
            _input.LookEvent += OnLook;
            _input.JumpEvent += OnJump;
        }
        // CharacterMovement.OnDisable unlocked the cursor on death; mouse look needs it back.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void OnDestroy()
    {
        if (_input == null) return;
        _input.LookEvent -= OnLook;
        _input.JumpEvent -= OnJump;
    }

    private void OnLook(Vector2 v) => _look = v;
    private void OnJump() => TryRoll();

    /// <summary>Add look input directly (dev probes); same units as <see cref="InputReader.LookEvent"/>.</summary>
    public void SetLook(float yaw, float pitch) { _yaw = yaw; _pitch = Mathf.Clamp(pitch, _pitchLimits.x, _pitchLimits.y); }

    /// <summary>Roll toward the camera's horizontal look direction. False while on cooldown.</summary>
    public bool TryRoll()
    {
        if (!RollReady || _body == null || !isActiveAndEnabled) return false;
        Vector3 dir = _cam != null ? _cam.transform.forward : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
        dir.Normalize();
        Roll(dir);
        return true;
    }

    private void Roll(Vector3 dir)
    {
        _body.WakeUp();
        _body.linearDamping  = _rollLinearDamping;
        _body.angularDamping = _rollAngularDamping;
        // Cancel most of the current horizontal drift so every roll reads as "toward where I look".
        Vector3 v = _body.linearVelocity;
        _body.linearVelocity = new Vector3(v.x * 0.3f, Mathf.Max(0f, v.y), v.z * 0.3f);
        _body.AddForce(dir * _rollSpeed + Vector3.up * _hopSpeed, ForceMode.VelocityChange);
        Vector3 axis = Vector3.Cross(Vector3.up, dir);
        _body.AddTorque(axis * _rollSpin + Random.insideUnitSphere * _wobble, ForceMode.VelocityChange);

        LastRollDirection = dir;
        LastCooldown = Random.Range(Mathf.Min(_cooldownRange.x, _cooldownRange.y), Mathf.Max(_cooldownRange.x, _cooldownRange.y));
        _readyAt = Time.time + LastCooldown;
        RollCount++;
        if (DeathFX.Instance != null && (_fxProfile != null || DeathFX.Instance.Profile != null))
            DeathFX.Instance.SkullRoll(transform.position, dir, _wisp, _fxProfile);
        else if (_rollSound != null && SoundManager.Instance != null) SoundManager.Instance.Play(_rollSound, transform.position);
        Rolled?.Invoke();
    }

    private void Update()
    {
        // Ready tick: once per cooldown, local owner only (it is a 2D cue for the dead player).
        bool ready = RollReady;
        if (ready && !_wasReady && RollCount > 0 && _cam != null)
        {
            _flashUntil = Time.time + 0.3f;
            if (DeathFX.Instance != null) DeathFX.Instance.SkullReady(_fxProfile);
        }
        _wasReady = ready;
    }

    private void LateUpdate()
    {
        if (_cam == null) return;

        float sens = _owner != null && _owner.Stats != null ? _owner.Stats.LookSensitivity : _lookSensitivity;
        _yaw  += _look.x * sens;
        _pitch = Mathf.Clamp(_pitch - _look.y * sens, _pitchLimits.x, _pitchLimits.y);

        Vector3 target = transform.position + Vector3.up * _pivotHeight;
        _pivot = _pivotSmoothTime > 0f ? Vector3.SmoothDamp(_pivot, target, ref _pivotVel, _pivotSmoothTime) : target;

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 back = rot * Vector3.back;
        float want = _distance;
        int n = Physics.SphereCastNonAlloc(_pivot, _cameraRadius, back, _hits, _distance, _cameraBlockers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];
            Rigidbody rb = h.rigidbody;
            if (rb != null && !rb.isKinematic) continue;                       // props, torches, evidence, bones
            if (_remainsRoot != null && h.transform.IsChildOf(_remainsRoot)) continue;
            if (h.distance <= 0f) continue;                                    // cast started inside it
            if (h.distance < want) want = h.distance;
        }
        want = Mathf.Max(_minDistance, want);
        // Pull in instantly, ease back out.
        _camDistance = want < _camDistance ? want : Mathf.MoveTowards(_camDistance, want, Time.deltaTime * 3f);

        _cam.transform.SetPositionAndRotation(_pivot + back * _camDistance, rot);
    }

    private void OnGUI()
    {
        if (_cam == null) return;
        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };

        float cd = CooldownRemaining;
        int tenths = cd > 0f ? Mathf.CeilToInt(cd * 10f) : 0;
        if (tenths != _labelTenths || _label == null)
        {
            _labelTenths = tenths;
            _label = tenths == 0 ? "SPACE  Skull roll ready" : $"Skull roll in {tenths / 10f:F1} s";
        }

        var rect = new Rect(0f, Screen.height - 170f, Screen.width, 28f);
        Color prev = GUI.color;
        GUI.color = Color.black;
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), _label, _style);
        GUI.color = Time.time < _flashUntil ? new Color(1f, 0.851f, 0.549f) :   // #FFD98C flash on ready
                    tenths == 0 ? new Color(1f, 0.85f, 0.55f) : new Color(0.75f, 0.75f, 0.75f);
        GUI.Label(rect, _label, _style);
        GUI.color = prev;
    }
}
