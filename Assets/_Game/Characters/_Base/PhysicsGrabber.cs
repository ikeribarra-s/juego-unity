using System;
using UnityEngine;

/// <summary>
/// R.E.P.O.-style physics carry. Hold Interact while aiming at an EvidencePickup
/// to suspend it in front of the camera with a spring-damper force and a visible
/// beam. Release Interact to let go gently; press DropItem to throw.
/// </summary>
public class PhysicsGrabber : MonoBehaviour
{
    [Header("Hold")]
    [SerializeField] private float _holdDistance   = 1.8f;
    [SerializeField] private float _springStrength = 140f;
    [SerializeField] private float _damping        = 11f;
    [SerializeField] private float _angularDamping = 6f;
    [Tooltip("Grip breaks if the item is dragged further than this from the hold point")]
    [SerializeField] private float _breakDistance  = 3.5f;
    [Tooltip("Rigidbodies heavier than this sag and respond sluggishly")]
    [SerializeField] private float _maxLiftMass    = 8f;

    [Header("Throw")]
    [SerializeField] private float _throwSpeed = 8f;

    [Header("Beam")]
    [SerializeField] private Material _beamMaterial;   // optional — defaults to Sprites/Default
    [SerializeField] private Color    _beamColor       = new(0.45f, 0.9f, 1f, 0.9f);
    [SerializeField] private float    _beamWidth       = 0.035f;
    [SerializeField] private float    _beamWobble      = 0.05f;
    [SerializeField] private Vector3  _beamOriginOffset = new(0.25f, -0.25f, 0.35f); // camera-local "hand"

    private const int BeamSegments = 12;

    private CharacterBase _character;
    private Camera        _cam;
    private LineRenderer  _beam;

    private EvidencePickup _held;
    private Rigidbody      _heldRb;
    private bool           _heldUsedGravity;
    private float          _liftFactor = 1f;   // 1 = light item, → 0 = at/over max lift mass

    public EvidencePickup Held      => _held;
    public bool           IsHolding => _held != null;

    public event Action<EvidencePickup> ItemGrabbed;
    public event Action<EvidencePickup> ItemReleased;

    private void Awake()
    {
        _character = GetComponent<CharacterBase>();
        CreateBeam();
    }

    private void Start()
    {
        _cam = GetComponentInChildren<Camera>();
        if (_cam == null)
            Debug.LogError("[PhysicsGrabber] No Camera found in children. Attach a Camera to CameraRoot.");
    }

    private void OnEnable()
    {
        _character.Input.InteractCanceledEvent += OnInteractCanceled;
        _character.Input.DropItemEvent         += OnDropItem;
    }

    private void OnDisable()
    {
        _character.Input.InteractCanceledEvent -= OnInteractCanceled;
        _character.Input.DropItemEvent         -= OnDropItem;
        Release();
    }

    public bool TryGrab(EvidencePickup pickup)
    {
        if (IsHolding || pickup == null || _cam == null) return false;

        _heldRb = pickup.Body;
        if (_heldRb == null) return false;

        _held            = pickup;
        _heldUsedGravity = _heldRb.useGravity;
        _heldRb.useGravity = false;
        _liftFactor      = Mathf.Clamp01(_maxLiftMass / Mathf.Max(_heldRb.mass, 0.0001f));

        pickup.OnGrabbed(_character);
        ItemGrabbed?.Invoke(pickup);
        return true;
    }

    private void OnInteractCanceled() => Release();

    private void OnDropItem() => Release(_cam != null ? _cam.transform.forward * _throwSpeed * _liftFactor : Vector3.zero);

    public void Release() => Release(Vector3.zero);

    private void Release(Vector3 impulse)
    {
        if (!IsHolding) return;

        var item = _held;
        var rb   = _heldRb;

        rb.useGravity = _heldUsedGravity;
        item.OnReleased();
        Clear();

        if (impulse != Vector3.zero)
            rb.AddForce(impulse, ForceMode.VelocityChange);

        ItemReleased?.Invoke(item);
    }

    private void Clear()
    {
        _held   = null;
        _heldRb = null;
        if (_beam != null) _beam.enabled = false;
    }

    private void FixedUpdate()
    {
        if (!IsHolding) return;

        // Item destroyed/broken out from under us (e.g. Break() while held)
        if (_held.State != EvidencePickup.EvidenceState.Carried || !_held.gameObject.activeInHierarchy)
        {
            Clear();
            return;
        }

        Vector3 holdPoint = GetHoldPoint();
        Vector3 toTarget  = holdPoint - _heldRb.worldCenterOfMass;

        if (toTarget.magnitude > _breakDistance)
        {
            Release();
            return;
        }

        // Spring toward the hold point, damped by velocity. Heavy items get a weaker
        // grip (_liftFactor < 1) and partial gravity back, so they sag and drag.
        Vector3 accel = (toTarget * _springStrength - _heldRb.linearVelocity * _damping) * _liftFactor
                      + Physics.gravity * (1f - _liftFactor);
        _heldRb.AddForce(accel, ForceMode.Acceleration);
        _heldRb.angularVelocity *= Mathf.Clamp01(1f - _angularDamping * Time.fixedDeltaTime);
    }

    private Vector3 GetHoldPoint()
    {
        Vector3 origin = _cam.transform.position;
        Vector3 fwd    = _cam.transform.forward;
        float   dist   = _holdDistance;

        // Pull the hold point in front of walls so the item isn't pushed through geometry
        if (Physics.Raycast(origin, fwd, out RaycastHit hit, _holdDistance, ~0, QueryTriggerInteraction.Ignore)
            && hit.rigidbody != _heldRb)
        {
            dist = Mathf.Max(0.5f, hit.distance - 0.2f);
        }

        return origin + fwd * dist;
    }

    // ── Beam visual ───────────────────────────────────────────────────────────

    private void CreateBeam()
    {
        var go = new GameObject("GrabBeam");
        go.transform.SetParent(transform, false);

        _beam = go.AddComponent<LineRenderer>();
        _beam.positionCount     = BeamSegments;
        _beam.useWorldSpace     = true;
        _beam.startWidth        = _beamWidth;
        _beam.endWidth          = _beamWidth * 0.4f;
        _beam.numCapVertices    = 4;
        _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _beam.receiveShadows    = false;
        _beam.material          = _beamMaterial != null ? _beamMaterial : new Material(Shader.Find("Sprites/Default"));
        _beam.startColor        = _beamColor;
        _beam.endColor          = new Color(_beamColor.r, _beamColor.g, _beamColor.b, _beamColor.a * 0.5f);
        _beam.enabled           = false;
    }

    private void LateUpdate()
    {
        if (!IsHolding || _cam == null)
        {
            if (_beam.enabled) _beam.enabled = false;
            return;
        }

        _beam.enabled = true;

        Vector3 start = _cam.transform.TransformPoint(_beamOriginOffset);
        Vector3 end   = _heldRb.worldCenterOfMass;
        Vector3 dir   = (end - start).normalized;
        Vector3 right = Vector3.Cross(dir, Vector3.up).normalized;
        Vector3 up    = Vector3.Cross(right, dir);

        for (int i = 0; i < BeamSegments; i++)
        {
            float   t   = i / (float)(BeamSegments - 1);
            Vector3 pos = Vector3.Lerp(start, end, t);

            // Perlin wobble, zero at both endpoints so the beam stays anchored
            float amp = Mathf.Sin(t * Mathf.PI) * _beamWobble;
            float n1  = Mathf.PerlinNoise(t * 4f + Time.time * 6f, 0.3f) - 0.5f;
            float n2  = Mathf.PerlinNoise(0.7f, t * 4f + Time.time * 6f) - 0.5f;
            pos += (right * n1 + up * n2) * (2f * amp);

            _beam.SetPosition(i, pos);
        }
    }
}
