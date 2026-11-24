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

    [Header("Thrown physics")]
    [SerializeField] private float _mass = 1f;

    private Rigidbody _body;
    private TorchStateController _state;

    public bool       IsCarried   { get; private set; }
    public string     DisplayName => _displayName;
    /// <summary>Model-space correction that makes the torch stand upright (flame up).</summary>
    public Quaternion UprightRotation => Quaternion.Euler(_uprightEuler);

    /// <summary>False while burnt out (see <see cref="TorchStateController"/>).</summary>
    public bool IsLit => State == null || State.IsLit;
    private TorchStateController State => _state != null ? _state : (_state = GetComponent<TorchStateController>());

    public string GetPrompt()
    {
        if (IsCarried) return string.Empty;
        if (IsLit) return _prompt;
        return CarriesLitTorch(FindLocalInteractorHint()) ? _relightPrompt : _deadPrompt;
    }

    public void Interact(CharacterBase interactor)
    {
        if (IsCarried || interactor == null) return;
        if (!IsLit)
        {
            // A dead torch can't be taken; a lit carried torch relights it (Living Map E1).
            if (CarriesLitTorch(interactor)) State.Relight();
            return;
        }
        interactor.TorchCarrier?.TryCarry(this);
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

        _body.isKinematic     = false;
        _body.useGravity      = true;
        _body.linearVelocity  = velocity;
        _body.angularVelocity = Vector3.zero;
    }
}
