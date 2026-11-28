using UnityEngine;

/// <summary>
/// Makes a <see cref="BonePiece"/> pickable: E puts it into the looker's <see cref="InventorySystem"/> (the piece is
/// deactivated, not destroyed, so it can be dropped / used for a revive later). While the player looks at it
/// (<see cref="IHighlightable"/>, toggled by <see cref="InteractionSystem"/>) it glows: warm HDR emission on the
/// ToonOutline material through a MaterialPropertyBlock (blooms), with a gentle pulse; fades in/out.
/// Prompts are cached strings (<see cref="IContextualPrompt"/> is re-read every frame), so looking costs no allocations.
/// </summary>
[RequireComponent(typeof(BonePiece))]
public class BonePickup : MonoBehaviour, IInteractable, IHighlightable, IContextualPrompt, IInventoryItem, IDroppable, IAimAssistTarget
{
    [Header("Targeting")]
    [Tooltip("Circular zone (m) around the crosshair in which this bone can be targeted, so thin bones on the floor " +
             "don't need a pixel-perfect aim. InteractionSystem checks view blockers first.")]
    [SerializeField, Min(0f)] private float _assistRadius = 0.45f;

    [Header("Glow")]
    [SerializeField, ColorUsage(false, true)] private Color _glowColor = new(1f, 0.78f, 0.48f, 1f);
    [SerializeField] private float _glowIntensity = 0.9f;
    [Tooltip("± intensity of the pulse, fraction of _glowIntensity.")]
    [SerializeField, Range(0f, 1f)] private float _pulseAmount = 0.35f;
    [SerializeField] private float _pulseSpeed = 4f;
    [SerializeField] private float _fadeTime = 0.12f;
    [SerializeField] private SoundDefinition _pickupSound;

    private static readonly int EmissionColorID     = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityID = Shader.PropertyToID("_EmissionIntensity");
    private static readonly int HideHeadID          = Shader.PropertyToID("_HideHead");
    private static readonly int CullRadiusID        = Shader.PropertyToID("_CullRadius");

    private BonePiece _piece;
    private MaterialPropertyBlock _mpb;
    private bool  _highlighted;
    private float _glow;            // 0..1 fade
    private string _prompt, _fullPrompt, _subtitle;
    private Sprite _icon;

    public float AssistRadius => _assistRadius;
    public BonePiece Piece => _piece;
    public bool IsHighlighted => _highlighted;
    /// <summary>Current emission intensity written to the renderer (0 when not glowing).</summary>
    public float CurrentEmission { get; private set; }
    /// <summary>Who picked it up (null while it lies in the world).</summary>
    public CharacterBase Holder { get; private set; }

    // IInventoryItem
    public string DisplayName => _piece != null ? _piece.BoneName : name;
    public string Subtitle    => _subtitle;
    public Sprite Icon        => _icon;

    public void Init(SoundDefinition pickupSound, Sprite icon = null)
    {
        _icon = icon;
        _piece = GetComponent<BonePiece>();
        _pickupSound = pickupSound;
        _subtitle   = $"{_piece.OwnerName}'s";
        _prompt     = $"Press E to pick up {_piece.BoneName} ({_subtitle})";
        _fullPrompt = $"{_piece.BoneName} ({_subtitle}) - inventory full";
        _mpb = new MaterialPropertyBlock();
        Apply(0f);
        enabled = false;   // Update only runs while glowing / fading
    }

    // ── IInteractable ───────────────────────────────────────────────────────
    public string GetPrompt() => _prompt;
    public string GetPrompt(CharacterBase interactor) =>
        interactor != null && interactor.Inventory != null && interactor.Inventory.IsFull ? _fullPrompt : _prompt;

    public void Interact(CharacterBase interactor)
    {
        if (interactor == null || interactor.Inventory == null || Holder != null) return;
        if (!interactor.Inventory.TryAdd(this)) return;   // full

        Holder = interactor;
        if (_pickupSound != null && SoundManager.Instance != null) SoundManager.Instance.Play(_pickupSound, transform.position);
        SetHighlighted(false);
        _glow = 0f; Apply(0f);
        gameObject.SetActive(false);   // kept (not destroyed) — Phase 2 drop / revive
        Debug.Log($"[BonePickup] {interactor.name} picked up {DisplayName} ({_subtitle}, owner id {_piece.OwnerId}).", this);
    }

    /// <summary>Back into the world (<see cref="InventorySystem.TryDrop"/>): re-activated at the drop pose with the toss velocity.</summary>
    public void Drop(CharacterBase dropper, Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        Holder = null;
        transform.SetPositionAndRotation(position, rotation);
        gameObject.SetActive(true);
        if (_piece != null && _piece.Body != null)
        {
            _piece.Body.linearVelocity = velocity;
            _piece.Body.angularVelocity = Random.insideUnitSphere * 3f;
            _piece.Body.WakeUp();
        }
        Debug.Log($"[BonePickup] {dropper.name} dropped {DisplayName} ({_subtitle}).", this);
    }

    // ── IHighlightable ──────────────────────────────────────────────────────
    public void SetHighlighted(bool on)
    {
        _highlighted = on;
        if (on || _glow > 0f) enabled = true;
    }

    private void Update()
    {
        float step = _fadeTime > 0f ? Time.deltaTime / _fadeTime : 1f;
        _glow = Mathf.MoveTowards(_glow, _highlighted ? 1f : 0f, step);
        float pulse = 1f + _pulseAmount * Mathf.Sin(Time.time * _pulseSpeed);
        Apply(_glow * _glowIntensity * pulse);
        if (!_highlighted && _glow <= 0f) enabled = false;
    }

    private void Apply(float intensity)
    {
        if (_piece == null || _piece.Renderer == null) return;
        CurrentEmission = intensity;
        _mpb.SetColor(EmissionColorID, _glowColor);
        _mpb.SetFloat(EmissionIntensityID, intensity);
        _mpb.SetFloat(HideHeadID, 0f);      // never inherit the first-person head cull
        _mpb.SetFloat(CullRadiusID, 0f);
        _piece.Renderer.SetPropertyBlock(_mpb);
    }
}
