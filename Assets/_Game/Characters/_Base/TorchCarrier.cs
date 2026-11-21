using UnityEngine;

/// <summary>
/// Lets a character carry a single <see cref="Torch"/> at a time. Auto-added by
/// <see cref="CharacterBase"/> (like PhysicsGrabber / CharacterVitals) and exposed as
/// <see cref="CharacterBase.TorchCarrier"/>.
///
/// On pickup the torch is parented to the hold anchor (the player camera by default, so it stays in
/// view) at a fixed front-left offset — for now it just sits there statically (no sway). Its colliders
/// are disabled while carried so the interaction ray doesn't keep hitting it and it doesn't bump the
/// world. A second pickup is refused until the current torch is dropped (<see cref="Drop"/> exists for
/// a future drop key; nothing binds it yet).
/// </summary>
[DisallowMultipleComponent]
public class TorchCarrier : MonoBehaviour
{
    [Tooltip("Where the carried torch attaches. Empty = the player camera (found in children at runtime).")]
    [SerializeField] private Transform _holdAnchor;
    [Tooltip("Local position of the held torch relative to the anchor (front, slightly left & down).")]
    [SerializeField] private Vector3 _holdLocalPosition = new(-0.35f, -0.25f, 0.6f);
    [Tooltip("Local rotation (euler) of the held torch — tweak if the prefab points the wrong way in-hand.")]
    [SerializeField] private Vector3 _holdLocalEuler = Vector3.zero;
    [Tooltip("Override the held torch's scale (useful if the wall scale is too big/small in-hand).")]
    [SerializeField] private bool _overrideHoldScale = false;
    [SerializeField] private Vector3 _holdLocalScale = Vector3.one;

    private CharacterBase _character;
    private Torch _carried;
    private Vector3 _originalScale = Vector3.one;

    public Torch Carried  => _carried;
    public bool  HasTorch => _carried != null;

    private void Awake() => _character = GetComponent<CharacterBase>();

    /// <summary>Pick up <paramref name="torch"/>. Returns false if hands are already full (one at a time).</summary>
    public bool TryCarry(Torch torch)
    {
        if (torch == null || _carried != null) return false;

        _carried = torch;
        torch.SetCarried(true);
        SetColliders(torch.gameObject, enabled: false);

        Transform t = torch.transform;
        _originalScale = t.localScale;
        t.SetParent(ResolveAnchor(), worldPositionStays: false);
        t.localPosition = _holdLocalPosition;
        t.localRotation = Quaternion.Euler(_holdLocalEuler);
        if (_overrideHoldScale) t.localScale = _holdLocalScale;
        return true;
    }

    /// <summary>Put the carried torch down at the player's feet. (Not bound to any input yet.)</summary>
    public void Drop()
    {
        if (_carried == null) return;

        Transform t = _carried.transform;
        if (_overrideHoldScale) t.localScale = _originalScale;
        t.SetParent(null, worldPositionStays: true);
        SetColliders(_carried.gameObject, enabled: true);
        _carried.SetCarried(false);
        _carried = null;
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
