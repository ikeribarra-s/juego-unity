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

    private CharacterBase _character;
    private Torch _carried;
    private Vector3 _originalScale = Vector3.one;
    private bool _scaleChanged;
    private RigidbodyInterpolation _savedInterpolation; // a thrown torch has an interpolated body
    private Transform _hand;          // optional animated hand (TorchArmAnimator)
    private Vector3 _handGripOffset;              // grip point in the hand bone's axes (metres)
    private Quaternion _handGripRotation = Quaternion.identity; // hand frame -> torch upright frame

    public Torch Carried  => _carried;
    public bool  HasTorch => _carried != null;

    public event Action<Torch> TorchPickedUp;
    public event Action<Torch> TorchDropped;
    /// <summary>Fired after <see cref="TorchDropped"/> when the release was a throw (G), not a plain drop.</summary>
    public event Action<Torch> TorchThrown;

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

    private void OnDisable()
    {
        if (_character != null && _character.Input != null)
            _character.Input.DropItemEvent -= OnDropItem;
    }

    /// <summary>Pick up <paramref name="torch"/>. Returns false if hands are already full (one at a time).</summary>
    public bool TryCarry(Torch torch)
    {
        if (torch == null || _carried != null) return false;

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

        Transform t = torch.transform;
        _originalScale = t.localScale;
        t.SetParent(ResolveAnchor(), worldPositionStays: false);
        t.localPosition = _holdLocalPosition;
        ApplyHoldPose();
        _scaleChanged = _overrideHoldScale || _hand != null;
        if (_overrideHoldScale) t.localScale = _holdLocalScale;
        else if (_hand != null) t.localScale = _originalScale * _handHoldScale;

        TorchPickedUp?.Invoke(torch);
        return true;
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

    /// <summary>Let go of the carried torch with no throw impulse (falls from the hand).</summary>
    public void Drop() => Release(Vector3.zero);

    /// <summary>Throw the carried torch forward from the camera.</summary>
    public void Throw()
    {
        Torch torch = _carried;
        if (torch == null) return;
        Transform anchor = ResolveAnchor();
        Release(anchor.forward * _throwSpeed + Vector3.up * _throwUpward);
        TorchThrown?.Invoke(torch);
    }

    private void Release(Vector3 velocity)
    {
        if (_carried == null) return;

        Torch torch = _carried;
        Transform t = torch.transform;
        if (_scaleChanged) t.localScale = _originalScale;
        t.SetParent(null, worldPositionStays: true);
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
