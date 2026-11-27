using UnityEngine;

/// <summary>
/// One physics piece of a disassembled skeleton (built at runtime by <see cref="SkeletonDisassembler"/>).
/// Knows whose bone it is: <see cref="Owner"/>, <see cref="OwnerName"/>, <see cref="OwnerId"/> and its <see cref="Group"/>.
/// Clatters (<see cref="SoundDefinition"/>, usually SD_BoneClack) on hard impacts. Every piece except the skull also
/// carries a <see cref="BonePickup"/>; the skull carries a <see cref="LivingSkull"/> instead.
/// </summary>
[DisallowMultipleComponent]
public class BonePiece : MonoBehaviour
{
    public BoneGroup     Group     { get; private set; }
    public CharacterBase Owner     { get; private set; }
    /// <summary>Name shown in prompts / HUD ("Base", "Test Dummy").</summary>
    public string        OwnerName { get; private set; }
    /// <summary>
    /// Stable id of the owner for this session (Unity EntityId of the owner's CharacterBase today).
    /// Phase 4 (NGO): replace with the owner's <c>NetworkObjectId</c> / client id so every peer agrees on it.
    /// </summary>
    public ulong         OwnerId   { get; private set; }
    public Rigidbody     Body      { get; private set; }
    public MeshRenderer  Renderer  { get; private set; }
    /// <summary>"Left Femur" etc.</summary>
    public string        BoneName  => BoneGroups.DisplayName(Group);

    private SoundDefinition _impactSound;
    private float _impactSpeed;
    private float _impactInterval;
    private float _nextImpact;
    private float _settleLinear = -1f, _settleAngular;
    private bool  _landed;

    /// <summary>True after the first ground contact (settle damping applied).</summary>
    public bool Landed => _landed;

    public void Init(CharacterBase owner, string ownerName, ulong ownerId, BoneGroup group, Rigidbody body, MeshRenderer renderer,
                     SoundDefinition impactSound, float impactSpeed, float impactInterval)
    {
        Owner = owner; OwnerName = ownerName; OwnerId = ownerId; Group = group;
        Body = body; Renderer = renderer;
        _impactSound = impactSound; _impactSpeed = impactSpeed; _impactInterval = impactInterval;
        _nextImpact = Time.time + 0.05f;
    }

    /// <summary>On the first ground contact switch to these dampings, so pieces stop near where they land instead of rolling away.</summary>
    public void SetSettleDamping(float linear, float angular) { _settleLinear = linear; _settleAngular = angular; }

    private void OnCollisionEnter(Collision c)
    {
        if (!_landed && c.contactCount > 0 && c.GetContact(0).normal.y > 0.5f)
        {
            _landed = true;
            if (_settleLinear >= 0f && Body != null) { Body.linearDamping = _settleLinear; Body.angularDamping = _settleAngular; }
        }
        if (_impactSound == null || SoundManager.Instance == null || Time.time < _nextImpact) return;
        if (c.relativeVelocity.sqrMagnitude < _impactSpeed * _impactSpeed) return;
        _nextImpact = Time.time + _impactInterval;
        // louder with the impact: a light hand ticks, a skull drop thunks (DeathFX.md 3a)
        float v = c.relativeVelocity.magnitude;
        SoundManager.Instance.Play(_impactSound, transform.position, Mathf.Lerp(0.35f, 1f, Mathf.Clamp01((v - _impactSpeed) / 6f)));
    }
}
