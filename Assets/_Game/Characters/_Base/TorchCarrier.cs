using System;
using UnityEngine;

/// <summary>
/// Lets a character carry a single <see cref="Torch"/> at a time. Auto-added by
/// <see cref="CharacterBase"/> (like PhysicsGrabber / CharacterVitals) and exposed as
/// <see cref="CharacterBase.TorchCarrier"/>.
///
/// On pickup the torch is parented to the hold anchor (the player camera by default, so it stays in
/// view) at a fixed front-left offset. Its rotation is re-applied every LateUpdate so it stays
/// <b>upright</b> (flame up, <see cref="Torch.UprightRotation"/>) and only follows the character's yaw,
/// not the camera pitch. Its colliders are disabled while carried so the interaction ray doesn't keep
/// hitting it and it doesn't bump the world. A second pickup is refused until the current torch is
/// thrown (DropItem / G) — but only when <see cref="PhysicsGrabber"/> isn't beam-holding evidence,
/// which keeps priority for G.
///
/// If an animated hand is registered via <see cref="SetHandAnchor"/> (done by <see cref="TorchArmAnimator"/>
/// on the model), the torch is attached <b>rigidly</b> to that hand every LateUpdate (grip offset +
/// grip rotation in hand space, handle inside the fist) instead of the camera front-left slot — zero lag
/// between hand and torch. Runs late (<c>DefaultExecutionOrder(100)</c>) so the camera has moved and
/// TorchArmAnimator (order 50) has already applied its smoothed first-person arm IK.
///
/// <b>Animated pick-up / throw.</b> When an animator drives the actions (<see cref="AnimatedActions"/>, set by
/// TorchArmAnimator when the Animator has the TorchPickup / TorchThrow states) both wait for the animation:
/// E starts a pick-up (<see cref="PickupStarted"/>; the torch is reserved so nobody else takes it and stays exactly where it
/// is while the arm reaches) and the torch pops into the hand on <see cref="CompletePickup"/> (end of the pick-up clip, arm
/// back in the hold pose: attached and scaled in one frame, <see cref="TorchPickedUp"/> fires then); G starts a
/// throw (<see cref="ThrowStarted"/>) and the torch only leaves the hand on <see cref="ReleaseThrow"/> (the release
/// frame), camera-forward as before, growing back to its world size in flight. Each has a fallback timer
/// (<see cref="TorchActionTimings"/> + <see cref="_animFallback"/>), so nothing can get stuck. Until the clip ends a
/// second pick-up or throw is refused. Death / disabling cancels: a pending pick-up stays where it was, a torch
/// mid-throw drops from the hand.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class TorchCarrier : MonoBehaviour
{
    [Tooltip("Where the carried torch attaches. Empty = the player camera (found in children at runtime).")]
    [SerializeField] private Transform _holdAnchor;
    [Tooltip("Local position of the held torch relative to the anchor (front, slightly left & down).")]
    [SerializeField] private Vector3 _holdLocalPosition = new(-0.35f, -0.25f, 0.6f);
    [Tooltip("Extra tilt (euler) applied on top of the torch's upright rotation — e.g. a slight lean.")]
    [SerializeField] private Vector3 _holdLocalEuler = Vector3.zero;
    [Tooltip("Keep the torch vertical in world space (yaw only) instead of pitching with the camera.")]
    [SerializeField] private bool _keepUpright = true;
    [Tooltip("Override the held torch's scale (useful if the wall scale is too big/small in-hand).")]
    [SerializeField] private bool _overrideHoldScale = false;
    [SerializeField] private Vector3 _holdLocalScale = Vector3.one;
    [Tooltip("When held in an animated hand: offset of the torch pivot from the grip point, in the torch's " +
             "upright frame (y = along the shaft, flame-up). Positive y puts the fist on the handle below the pivot.")]
    [SerializeField] private Vector3 _handHoldOffset = new(0f, 0.19f, 0f);
    [Tooltip("When held in an animated hand (and Override Hold Scale is off): multiplier on the torch's " +
             "world scale. The torch prefab is ~1.9 m tall — far too big for a fist.")]
    [SerializeField] private float _handHoldScale = 0.3f;

    [Header("Throw (G)")]
    [SerializeField] private float _throwSpeed = 6f;
    [Tooltip("Extra upward velocity so the throw arcs instead of skimming the floor.")]
    [SerializeField] private float _throwUpward = 1.5f;
    [Tooltip("Tumble (rad/s) around the camera's right axis, top forward — an overhand throw flips the torch end over end.")]
    [SerializeField] private float _throwSpin = 1.75f;
    [Tooltip("Fraction of the thrower's horizontal velocity added to the throw. 0 = camera-forward speed only (sprinting at 9 m/s " +
             "then overtakes the 6 m/s torch, which can pass through the camera); 1 = physically inherited.")]
    [SerializeField, Range(0f, 1f)] private float _inheritCarrierVelocity = 0f;

    [Header("Animated pick-up / throw (TorchArmAnimator)")]
    [Tooltip("If the animator never reports the pick-up end / release frame, force it this long (s) after the expected time.")]
    [SerializeField] private float _animFallback = 0.25f;
    [Tooltip("Seconds a thrown torch takes to grow from the in-hand size back to its world size.")]
    [SerializeField] private float _releaseGrowTime = 0.15f;

    public enum TorchAction { None, PickingUp, Throwing, Recovering }

    private CharacterBase _character;
    private Torch _carried;
    private Vector3 _originalScale = Vector3.one;
    private bool _scaleChanged;
    private RigidbodyInterpolation _savedInterpolation; // a thrown torch has an interpolated body
    private Transform _hand;          // optional animated hand (TorchArmAnimator)
    private Vector3 _handGripOffset;              // grip point in the hand bone's axes (metres)
    private Quaternion _handGripRotation = Quaternion.identity; // hand frame -> torch upright frame

    private Torch      _pending;                  // reserved, waiting for the grab frame
    private bool       _pendingHadBody, _pendingWasKinematic;
    private float      _actionStart;
    private float      _actionEnd;                // Recovering lasts until the clip ends
    private Torch      _growTorch;
    private Vector3    _growFrom, _growTo;
    private float      _growStart;

    public Torch Carried  => _carried;
    public bool  HasTorch => _carried != null;
    /// <summary>The torch being reached for (reserved, not in the hand yet).</summary>
    public Torch PendingTorch => _pending;
    public TorchAction Action { get; private set; }
    /// <summary>A pick-up or throw animation is playing: further pick-ups / throws are refused.</summary>
    public bool IsBusy => Action != TorchAction.None;
    /// <summary>Seconds since the current action started.</summary>
    public float ActionTime => Time.time - _actionStart;
    /// <summary>Set by the animator that plays the pick-up / throw clips. Off = instant pick-up / throw (old behaviour).</summary>
    public bool AnimatedActions { get; set; }

    public event Action<Torch> TorchPickedUp;
    public event Action<Torch> TorchDropped;
    /// <summary>Fired after <see cref="TorchDropped"/> when the release was a throw (G), not a plain drop.</summary>
    public event Action<Torch> TorchThrown;
    /// <summary>E accepted: the hand starts reaching for the torch (it pops into the hand later, on <see cref="TorchPickedUp"/>).</summary>
    public event Action<Torch> PickupStarted;
    /// <summary>G accepted: the throw animation starts (the torch leaves the hand later, on <see cref="TorchThrown"/>).</summary>
    public event Action<Torch> ThrowStarted;
    /// <summary>A pick-up or throw was abandoned before its grab / release frame (death, disable, torch gone).</summary>
    public event Action ActionCancelled;

    private void Awake()
    {
        _character = GetComponent<CharacterBase>();
        // Keeps the held light off the local first-person viewmodel (no white blow-out), see HeldTorchLighting.
        if (GetComponent<HeldTorchLighting>() == null) gameObject.AddComponent<HeldTorchLighting>();
    }

    private void OnEnable()
    {
        if (_character == null) _character = GetComponent<CharacterBase>();
        if (_character != null && _character.Input != null)
            _character.Input.DropItemEvent += OnDropItem;
    }

    // Vitals is auto-added in CharacterBase.Awake, which may run after ours.
    private void Start()
    {
        if (_character != null && _character.Vitals != null) _character.Vitals.Died += OnDied;
    }

    private void OnDisable()
    {
        if (_character != null && _character.Input != null)
            _character.Input.DropItemEvent -= OnDropItem;
        CancelAction();
    }

    private void OnDestroy()
    {
        if (_character != null && _character.Vitals != null) _character.Vitals.Died -= OnDied;
    }

    private void OnDied() => CancelAction();

    /// <summary>
    /// Pick up <paramref name="torch"/>. Returns false if hands are already full (one at a time) or an action is playing.
    /// With <see cref="AnimatedActions"/> the torch is only reserved here (it does not move) and pops into the hand at the end
    /// of the pick-up animation (<see cref="CompletePickup"/>); <paramref name="instant"/> skips the animation.
    /// </summary>
    public bool TryCarry(Torch torch, bool instant = false)
    {
        if (torch == null || _carried != null || IsBusy) return false;
        if (instant || !AnimatedActions || !isActiveAndEnabled)
        {
            Attach(torch);
            return true;
        }

        _pending = torch;
        _pendingHadBody = torch.TryGetComponent(out Rigidbody body);
        _pendingWasKinematic = _pendingHadBody && body.isKinematic;
        torch.SetCarried(true);   // reserved: no prompt, nobody else can take it, a rolling torch holds still
        StartAction(TorchAction.PickingUp);
        PickupStarted?.Invoke(torch);
        return true;
    }

    /// <summary>End of the pick-up animation (arm in the hold pose): the reserved torch pops into the hand in one frame.</summary>
    public void CompletePickup()
    {
        if (Action != TorchAction.PickingUp) return;
        Torch torch = _pending;
        if (torch == null || !torch.gameObject.activeInHierarchy) { CancelAction(); return; }
        _pending = null;
        Action = TorchAction.None;
        Attach(torch);
    }

    /// <summary>Release frame of the throw animation: the torch leaves the hand, camera-forward.</summary>
    public void ReleaseThrow()
    {
        if (Action != TorchAction.Throwing) return;
        Action     = TorchAction.Recovering;          // follow-through: no pick-up / throw until the clip ends
        _actionEnd = _actionStart + TorchActionTimings.ThrowDuration;
        ThrowNow();
    }

    /// <summary>Abandon the current action: a pending pick-up stays where it was, a torch mid-throw drops from the hand.</summary>
    public void CancelAction()
    {
        TorchAction was = Action;
        Action = TorchAction.None;
        if (was == TorchAction.PickingUp && _pending != null)
        {
            _pending.SetCarried(false);
            if (_pendingHadBody && !_pendingWasKinematic && _pending.TryGetComponent(out Rigidbody body)) body.isKinematic = false;
        }
        _pending = null;
        if (was == TorchAction.Throwing && _carried != null) Release(Vector3.zero, grow: false);
        if (was == TorchAction.PickingUp || was == TorchAction.Throwing) ActionCancelled?.Invoke();
    }

    private void StartAction(TorchAction action)
    {
        Action = action;
        _actionStart = Time.time;
    }

    private void Attach(Torch torch)
    {
        Transform t = torch.transform;
        _carried = torch;
        torch.SetCarried(true);
        // A previously thrown torch carries an *interpolated* (now kinematic) Rigidbody. Interpolation would
        // keep writing its stale physics pose into the transform we drive every LateUpdate — switch it off.
        if (torch.TryGetComponent(out Rigidbody body))
        {
            _savedInterpolation = body.interpolation;
            body.interpolation  = RigidbodyInterpolation.None;
        }
        SetColliders(torch.gameObject, enabled: false);
        if (_growTorch == torch) _growTorch = null;

        _originalScale = t.localScale;                 // world (spawn) scale, restored on release
        Vector3 heldBase = torch.BaseScale;            // held size derives from the prefab scale, not the spawn scale
        t.SetParent(ResolveAnchor(), worldPositionStays: false);
        t.localPosition = _holdLocalPosition;
        _scaleChanged = _overrideHoldScale || _hand != null;
        if (_overrideHoldScale) t.localScale = _holdLocalScale;
        else if (_hand != null) t.localScale = heldBase * _handHoldScale;
        ApplyHoldPose();

        TorchPickedUp?.Invoke(torch);
    }

    /// <summary>Where the fist closes on <paramref name="torch"/> as it stands now (its handle), in world space.</summary>
    public Vector3 GripPointWorld(Torch torch)
    {
        Transform t = torch.transform;
        Quaternion frame = t.rotation * Quaternion.Inverse(torch.UprightRotation) * Quaternion.Inverse(Quaternion.Euler(_holdLocalEuler));
        float held  = Mathf.Max(1e-4f, torch.BaseScale.x * _handHoldScale);
        float ratio = _overrideHoldScale ? 1f : t.lossyScale.x / held;
        return t.position - frame * (_handHoldOffset * ratio);
    }

    /// <summary>
    /// Hold the torch rigidly in an animated hand instead of the camera slot. <paramref name="gripOffset"/>
    /// is the fist centre in metres along the hand bone's axes; <paramref name="gripRotation"/> turns the hand
    /// frame into the torch's upright frame (shaft along +Y). Pass <c>null</c> to go back to the camera slot.
    /// </summary>
    public void SetHandAnchor(Transform hand, Vector3 gripOffset, Quaternion gripRotation)
    {
        _hand = hand;
        _handGripOffset = gripOffset;
        _handGripRotation = gripRotation;
    }

    /// <summary>Let go of the carried torch with no throw impulse (falls from the hand). Cancels a throw in progress.</summary>
    public void Drop()
    {
        if (Action == TorchAction.Throwing) Action = TorchAction.None;
        Release(Vector3.zero, grow: false);
    }

    /// <summary>
    /// Throw the carried torch forward from the camera. With <see cref="AnimatedActions"/> this starts the throw
    /// animation and the torch leaves the hand on its release frame (<see cref="ReleaseThrow"/>).
    /// Returns false when there is nothing to throw or an action is already playing.
    /// </summary>
    public bool Throw(bool instant = false)
    {
        if (_carried == null || IsBusy) return false;
        if (instant || !AnimatedActions || !isActiveAndEnabled) { ThrowNow(); return true; }
        StartAction(TorchAction.Throwing);
        ThrowStarted?.Invoke(_carried);
        return true;
    }

    private void ThrowNow()
    {
        Torch torch = _carried;
        if (torch == null) return;
        if (_hand != null) ApplyHoldPose();   // leave from the hand as it is now (the animator calls this once the arm pose is final)
        Transform anchor = ResolveAnchor();
        Vector3 inherit = Vector3.zero;
        if (_inheritCarrierVelocity > 0f && _character != null && _character.Controller != null)
            inherit = Vector3.ProjectOnPlane(_character.Controller.velocity, Vector3.up) * _inheritCarrierVelocity;
        Release(anchor.forward * _throwSpeed + Vector3.up * _throwUpward + inherit, grow: true);
        if (_throwSpin != 0f && torch.TryGetComponent(out Rigidbody body)) body.angularVelocity = anchor.right * _throwSpin;
        TorchThrown?.Invoke(torch);
    }

    private void Release(Vector3 velocity, bool grow)
    {
        if (_carried == null) return;

        Torch torch = _carried;
        Transform t = torch.transform;
        t.SetParent(null, worldPositionStays: true);   // keeps the in-hand size for now
        if (_scaleChanged)
        {
            if (grow && _releaseGrowTime > 0f)
            {
                _growTorch = torch; _growFrom = t.localScale; _growTo = _originalScale; _growStart = Time.time;
            }
            else t.localScale = _originalScale;
        }
        SetColliders(torch.gameObject, enabled: true);
        IgnoreOwnColliders(torch.gameObject);
        torch.SetCarried(false);
        bool hadBody = torch.TryGetComponent(out Rigidbody body);
        torch.Launch(velocity);
        if (hadBody) body.interpolation = _savedInterpolation; // Launch adds an interpolated body on first throw

        _carried = null;

        TorchDropped?.Invoke(torch);
    }

    private void OnDropItem()
    {
        // Evidence on the beam has priority for G. LastReleaseFrame covers the case where the grabber's
        // handler ran first this frame and already threw its item — G must not throw both.
        PhysicsGrabber grabber = _character != null ? _character.Grabber : null;
        if (grabber != null && (grabber.IsHolding || grabber.LastReleaseFrame == Time.frameCount)) return;
        Throw();
    }

    private void Update()
    {
        // Fallback timers: the animator normally calls CompletePickup / ReleaseThrow on the exact frame.
        switch (Action)
        {
            case TorchAction.PickingUp:
                if (_pending == null || !_pending.gameObject.activeInHierarchy) CancelAction();
                else if (!AnimatedActions || ActionTime >= TorchActionTimings.PickupDuration + _animFallback) CompletePickup();
                break;
            case TorchAction.Throwing:
                if (_carried == null) Action = TorchAction.None;
                else if (!AnimatedActions || ActionTime >= TorchActionTimings.ThrowRelease + _animFallback) ReleaseThrow();
                break;
            case TorchAction.Recovering:
                if (Time.time >= _actionEnd) Action = TorchAction.None;
                break;
        }

        if (_growTorch != null)
        {
            float k = _releaseGrowTime > 0f ? (Time.time - _growStart) / _releaseGrowTime : 1f;
            if (_growTorch.IsCarried) _growTorch = null;
            else if (k >= 1f) { _growTorch.transform.localScale = _growTo; _growTorch = null; }
            else _growTorch.transform.localScale = Vector3.Lerp(_growFrom, _growTo, 1f - (1f - k) * (1f - k));
        }
    }

    private void LateUpdate()
    {
        if (_carried != null) ApplyHoldPose();
    }

    private void ApplyHoldPose()
    {
        Transform t = _carried.transform;
        if (_hand != null)
        {
            // Rigid: the torch is a fixed part of the hand (no smoothing here — TorchArmAnimator smooths the arm).
            Quaternion frame = _hand.rotation * _handGripRotation;            // torch upright frame
            t.rotation = frame * Quaternion.Euler(_holdLocalEuler) * _carried.UprightRotation;
            t.position = _hand.position + _hand.rotation * _handGripOffset + frame * _handHoldOffset;
            return;
        }

        Quaternion upright = Quaternion.Euler(_holdLocalEuler) * _carried.UprightRotation;
        if (_keepUpright)
            t.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * upright;
        else
            t.localRotation = upright;
    }

    /// <summary>The thrown torch spawns inside/near the player's capsule — don't let it collide with its thrower.</summary>
    private void IgnoreOwnColliders(GameObject torch)
    {
        Collider[] own = GetComponentsInChildren<Collider>();
        foreach (Collider tc in torch.GetComponentsInChildren<Collider>())
            foreach (Collider oc in own)
                if (oc != tc) Physics.IgnoreCollision(tc, oc, true);
    }

    private Transform ResolveAnchor()
    {
        if (_holdAnchor != null) return _holdAnchor;
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) _holdAnchor = cam.transform;
        else if (_character != null && _character.CameraRoot != null) _holdAnchor = _character.CameraRoot;
        else _holdAnchor = transform;
        return _holdAnchor;
    }

    private static void SetColliders(GameObject go, bool enabled)
    {
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            c.enabled = enabled;
    }
}
