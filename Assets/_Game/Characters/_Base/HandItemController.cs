using UnityEngine;

/// <summary>
/// Uses the usable pocket item (<see cref="IUsableItem"/>, the salt bag): holding F (<c>InputReader.UseInventoryStarted/CanceledEvent</c>)
/// starts the use, and after <see cref="_pourTime"/> <see cref="IUsableItem.UseTick"/> runs every frame (the pour). Used-up items
/// are removed from the pockets and destroyed. Auto-added by <see cref="CharacterBase"/>.
///
/// No arm animation and no bag in the hand (removed 2026-11-28): the salt leaves from <see cref="MouthWorld"/>, a fixed point in front
/// of the character's chest, and lands on <see cref="TryGetPourTarget"/>. <see cref="IUsableItem.HeldPrefab"/> is not shown.
/// </summary>
public class HandItemController : MonoBehaviour
{
    [Tooltip("Where the salt leaves from, in the character's local space (x right, y up, z forward; metres from the feet).")]
    [SerializeField] private Vector3 _mouthOffset = new(0.1f, 1.4f, 0.55f);
    [Tooltip("Seconds between pressing F and the salt starting to flow.")]
    [SerializeField] private float _pourTime = 0.30f;

    [Header("Pour target")]
    [Tooltip("The salt lands where the crosshair meets the floor, kept between these horizontal distances from the player (m).")]
    [SerializeField] private Vector2 _targetRange = new(0.5f, 1.4f);
    [SerializeField] private float _reachRay = 6f;

    // ── state ──
    private CharacterBase _character;
    private Camera _camera;
    private IUsableItem _usable;
    private IInventoryItem _usableItem;
    private bool _using, _dead;
    private float _pour;
    private AudioSource _loop;
    private SoundDefinition _loopDef;
    private float _loopVolume;
    private bool _loopOn;

    // ── public API for items ──
    public CharacterBase Owner => _character;
    public bool IsUsing => _using;
    public IUsableItem Usable => _usable;
    /// <summary>0..1 how far into the pour start-up delay (smoothed).</summary>
    public float PourBlend => Mathf.SmoothStep(0f, 1f, _pour);
    public bool PourReady => _using && _pour > 0.85f;
    /// <summary>Where the salt stream starts, in world space.</summary>
    public Vector3 MouthWorld => transform.TransformPoint(_mouthOffset);
    public bool LocalView => _camera != null && _camera.enabled;

    private void Awake()
    {
        _character = GetComponent<CharacterBase>();
        _camera = GetComponentInChildren<Camera>(true);
        _loop = gameObject.AddComponent<AudioSource>();
        _loop.playOnAwake = false; _loop.loop = true; _loop.spatialBlend = 1f; _loop.rolloffMode = AudioRolloffMode.Logarithmic;
        _loop.dopplerLevel = 0f;
    }

    private void OnEnable()
    {
        if (_character == null) return;
        if (_character.Input != null)
        {
            _character.Input.UseInventoryStartedEvent  += StartUse;
            _character.Input.UseInventoryCanceledEvent += StopUse;
        }
        if (_character.Inventory != null) _character.Inventory.Changed += Refresh;
        if (_character.Vitals != null) _character.Vitals.Died += OnDied;
        Refresh();
    }

    private void OnDisable()
    {
        if (_character == null) return;
        if (_character.Input != null)
        {
            _character.Input.UseInventoryStartedEvent  -= StartUse;
            _character.Input.UseInventoryCanceledEvent -= StopUse;
        }
        if (_character.Inventory != null) _character.Inventory.Changed -= Refresh;
        if (_character.Vitals != null) _character.Vitals.Died -= OnDied;
        StopUse();
        if (_loop != null) _loop.Stop();
    }

    private void OnDied()
    {
        _dead = true;
        StopUse();
        if (_loop != null) _loop.Stop();
        enabled = false;
    }

    // ── input ──
    public void StartUse()
    {
        if (_dead || _usable == null || !_usable.CanUse) return;
        _using = true;
    }

    public void StopUse()
    {
        if (!_using) return;
        _using = false;
        _usable?.UseEnd(_character, this);
        SetPourSound(null, false);
    }

    // ── which item is usable ──
    private void Refresh()
    {
        var u = _character != null && _character.Inventory != null ? _character.Inventory.FindUsable(out _) : null;
        if (u == _usable) return;
        if (_usable != null) StopUse();
        _usable = u;
        _usableItem = null;
        if (u != null) _character.Inventory.FindUsable(out _usableItem);
    }

    // ── loop sound ──
    public void SetPourSound(SoundDefinition def, bool on)
    {
        _loopOn = on && def != null && def.IsValid;
        if (def != null) _loopDef = def;
        if (_loopOn && !_loop.isPlaying)
        {
            _loop.clip = _loopDef.GetClip();
            _loop.outputAudioMixerGroup = _loopDef.MixerGroup;
            _loop.minDistance = _loopDef.MinDistance; _loop.maxDistance = _loopDef.MaxDistance;
            _loop.pitch = _loopDef.GetPitch();
            _loop.volume = 0f; _loopVolume = 0f;
            _loop.Play();
        }
    }

    // ── per frame ──
    private void Update()
    {
        if (_dead) return;

        _pour = Mathf.MoveTowards(_pour, _using ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, _pourTime));

        if (_using && _usable != null && _usable.UseTick(_character, this, Time.deltaTime))
        {
            // used up
            var item = _usableItem; var usable = _usable;
            _using = false;
            usable.UseEnd(_character, this);
            SetPourSound(null, false);
            _character.Inventory.Remove(item);   // -> Changed -> Refresh
            if (item is Component c) Destroy(c.gameObject);
        }

        if (_loop != null && _loop.isPlaying)
        {
            float target = _loopOn && _loopDef != null ? _loopDef.Volume : 0f;
            _loopVolume = Mathf.MoveTowards(_loopVolume, target, Time.deltaTime / 0.12f);
            _loop.volume = _loopVolume;
            if (!_loopOn && _loopVolume <= 0.001f) _loop.Stop();
        }
    }

    /// <summary>
    /// Where the salt lands: the crosshair ray's floor hit, held between <see cref="_targetRange"/> metres from the player
    /// (looking at the horizon or up pours ahead of the feet instead). False when no floor is found.
    /// </summary>
    public bool TryGetPourTarget(out Vector3 pos, out Vector3 normal)
    {
        pos = default; normal = Vector3.up;
        Vector3 origin = _camera != null ? _camera.transform.position : transform.position + Vector3.up * 1.6f;
        Vector3 fwd = _camera != null ? _camera.transform.forward : transform.forward;
        Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up);
        flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : transform.forward;

        Vector3 aim = transform.position + flat * _targetRange.y;
        float nearest = float.MaxValue;
        foreach (var h in Physics.RaycastAll(origin, fwd, _reachRay, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(transform) || h.collider.attachedRigidbody != null) continue;
            if (h.distance < nearest) { nearest = h.distance; aim = h.normal.y > 0.5f ? h.point : transform.position + flat * _targetRange.y; }
        }

        Vector3 delta = aim - transform.position; delta.y = 0f;
        float dist = Mathf.Clamp(delta.magnitude, _targetRange.x, _targetRange.y);
        Vector3 dirFlat = delta.sqrMagnitude > 1e-4f ? delta.normalized : flat;
        Vector3 spot = transform.position + dirFlat * dist;

        var hits = Physics.RaycastAll(spot + Vector3.up * 1.2f, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity; RaycastHit bestHit = default;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform) || h.collider.attachedRigidbody != null) continue;
            if (h.normal.y < 0.5f) continue;
            if (h.point.y > best) { best = h.point.y; bestHit = h; }
        }
        if (best == float.NegativeInfinity) return false;
        pos = bestHit.point; normal = bestHit.normal;
        return true;
    }
}
