using UnityEngine;

/// <summary>
/// A wall torch the player can pick up and carry (one at a time). Implements <see cref="IInteractable"/>,
/// so it works with the existing <see cref="InteractionSystem"/> raycast — press E while looking at it.
///
/// Put this on the torch prefab's <b>root</b>, alongside a <see cref="Collider"/> (the interaction ray
/// does <c>hit.collider.GetComponent&lt;IInteractable&gt;()</c>, so they must share the GameObject).
/// Pickup/placement is handled by <see cref="TorchCarrier"/> on the character — this just routes to it.
///
/// Wall torches have no Rigidbody (they stay put). The first time a torch is thrown/dropped
/// (<see cref="Launch"/>) a Rigidbody is added so it falls and tumbles; while carried that body is
/// kinematic. Child lights / <see cref="LightFlicker"/> are untouched, so it keeps burning on the floor.
/// </summary>
[DisallowMultipleComponent]
public class Torch : MonoBehaviour, IInteractable
{
    [SerializeField] private string _prompt = "Press E to take torch";
    [SerializeField] private string _displayName = "Torch";
    [Tooltip("Prompt on a burnt-out torch when the player carries a lit one (E relights it).")]
    [SerializeField] private string _relightPrompt = "Press E to relight";
    [Tooltip("Prompt on a burnt-out torch when the player has no flame to relight it with.")]
    [SerializeField] private string _deadPrompt = "Burnt out";

    [Tooltip("Rotation that stands the model upright (flame +Y). The FBX is Z-up, so the prefab root " +
             "carries -90° X; anything that overwrites the root rotation must re-apply this.")]
    [SerializeField] private Vector3 _uprightEuler = new(-90f, 0f, 0f);

    [Header("World size")]
    [Tooltip("Multiplier on the prefab's scale for torches placed in the world (wall mounts, braziers, dropped/thrown). " +
             "The prefab is ~1.9 m tall; 0.5 gives ~0.95 m. The carried size is independent (TorchCarrier._handHoldScale × the prefab scale).")]
    [SerializeField] private float _spawnScale = 0.5f;
    [SerializeField, HideInInspector] private Vector3 _baseScale;   // prefab localScale before any spawn scaling

    /// <summary>The prefab's own localScale (what the carried size is derived from).</summary>
    public Vector3 BaseScale => _baseScale != Vector3.zero ? _baseScale : transform.localScale;
    /// <summary>The localScale a torch has lying in the world.</summary>
    public Vector3 WorldScale => _baseScale != Vector3.zero ? _baseScale * _spawnScale : transform.localScale;

    /// <summary>Call right after instantiating a torch in the world, BEFORE positioning / WallMount.PushOffWall.</summary>
    public static void ApplySpawnScale(GameObject go)
    {
        if (go == null || !go.TryGetComponent(out Torch t)) return;
        if (t._baseScale == Vector3.zero) t._baseScale = go.transform.localScale;
        go.transform.localScale = t.WorldScale;
    }

    [Header("Thrown physics")]
    [SerializeField] private float _mass = 1f;

    [Header("Floor")]
    [Tooltip("A torch thrown/dropped goes out the first time it lands on a floor (contact normal up). " +
             "Wall torches and the carried torch are never affected; a floor-burnt torch can be picked up (stays Out).")]
    [SerializeField] private bool _extinguishOnFloor = true;

    private Rigidbody _body;
    private TorchStateController _state;
    private bool _armedForFloor;    // set by Launch (a release); cleared by the first floor contact or by pickup
    private bool _burntOnFloor;     // went out on the floor: still pickable, stays Out
    private bool _outNoController;  // fallback when there is no TorchStateController

    public bool       IsCarried   { get; private set; }
    public string     DisplayName => _displayName;
    /// <summary>Model-space correction that makes the torch stand upright (flame up).</summary>
    public Quaternion UprightRotation => Quaternion.Euler(_uprightEuler);

    /// <summary>False while burnt out (see <see cref="TorchStateController"/>).</summary>
    public bool IsLit => State != null ? State.IsLit : !_outNoController;
    private TorchStateController State => _state != null ? _state : (_state = GetComponent<TorchStateController>());

    // A mounted torch (no Rigidbody yet) must never block a walkway — the chunk balconies are 1.4 m wide and a
    // solid lantern collider on the wall left too little room for the 0.8 m CharacterController (bell tower).
    // Its collider is a trigger (the interaction ray still hits triggers); Launch() makes it solid for physics.
    private void Awake()
    {
        if (GetComponent<Rigidbody>() != null) return;
        foreach (var c in GetComponents<Collider>()) c.isTrigger = true;
    }

    public string GetPrompt()
    {
        if (IsCarried) return string.Empty;
        if (IsLit) return _prompt;
        if (CarriesLitTorch(FindLocalInteractorHint())) return _relightPrompt;
        return _burntOnFloor ? _prompt : _deadPrompt;
    }

    public void Interact(CharacterBase interactor)
    {
        if (IsCarried || interactor == null) return;
        // A lit carried torch relights a burnt one, floor or wall (Living Map E1).
        if (!IsLit && CarriesLitTorch(interactor))
        {
            if (State != null) State.Relight();
            return;
        }
        // A wall torch burnt out by the director can't be taken; a floor-burnt one can (stays Out).
        if (!IsLit && !_burntOnFloor) return;
        interactor.TorchCarrier?.TryCarry(this);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!_armedForFloor || IsCarried) return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y <= 0.6f) continue;   // walls / ceilings don't count
            _armedForFloor = false;
            ExtinguishOnFloor();
            return;
        }
    }

    // A grazing first contact (box edge, side normal) can leave the torch resting on the floor without a new
    // Enter, so keep checking while armed. Unarmed torches return immediately.
    private void OnCollisionStay(Collision collision) => OnCollisionEnter(collision);

    private void ExtinguishOnFloor()
    {
        _burntOnFloor = true;
        if (State != null) { State.Extinguish(); return; }   // existing Out fade + "out" sound
        // No TorchStateController: switch the lights and flame/embers off directly, play nothing.
        _outNoController = true;
        foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = false;
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "FlameFlipbook") t.gameObject.SetActive(false);
    }

    private static bool CarriesLitTorch(CharacterBase who) =>
        who != null && who.TorchCarrier != null && who.TorchCarrier.Carried != null && who.TorchCarrier.Carried.IsLit;

    // GetPrompt has no interactor; the prompt is only ever read for the local player.
    private static CharacterBase s_localHint;
    private static CharacterBase FindLocalInteractorHint()
    {
        if (s_localHint == null) s_localHint = FindAnyObjectByType<CharacterBase>();
        return s_localHint;
    }

    /// <summary>Set by <see cref="TorchCarrier"/> when this torch is picked up / put down.</summary>
    public void SetCarried(bool carried)
    {
        IsCarried = carried;
        if (carried) _armedForFloor = false;
        if (carried && _body == null) _body = GetComponent<Rigidbody>();
        if (carried && _body != null)
        {
            if (!_body.isKinematic)
            {
                _body.linearVelocity  = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _body.isKinematic = true;
        }
    }

    /// <summary>Turn on physics (adding a Rigidbody if needed) and fling the torch with <paramref name="velocity"/>.</summary>
    public void Launch(Vector3 velocity)
    {
        if (_body == null) _body = GetComponent<Rigidbody>();
        if (_body == null)
        {
            _body = gameObject.AddComponent<Rigidbody>();
            _body.mass                   = _mass;
            _body.interpolation          = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        foreach (var c in GetComponents<Collider>()) c.isTrigger = false;   // solid once it is a physics prop
        _body.isKinematic     = false;
        _body.useGravity      = true;
        _body.linearVelocity  = velocity;
        _body.angularVelocity = Vector3.zero;
        _armedForFloor        = _extinguishOnFloor;   // a release: the next floor contact puts it out
    }
}
