using UnityEngine;

/// <summary>
/// Hides the local player's own near geometry so the first-person camera (anchored at the
/// neck) doesn't look through the inside of the head/neck, while the rest of the body stays
/// visible for an immersive first-person view.
///
/// Two layers of culling, both scoped to the owner only:
///  • <b>Neck bone scale</b> — collapses the Humanoid <c>Neck</c> bone to a tiny scale so the
///    neck <i>and the head</i> (parented under it) shrink away together. Humanoid animation
///    drives bone rotation, not scale, so this only needs to be set once. Scaling the neck
///    doesn't move its own joint origin, so the camera's neck anchor still works.
///  • <b>Camera radius cull</b> — pushes <c>_CullRadius</c> into the material so the shader
///    discards any fragment within that distance of the camera, removing the upper-chest
///    stub the camera sits inside. Requires the <c>Fluffterror/ToonOutline</c> shader.
///
/// Other players (Phase 4) keep a full body — call <see cref="Apply"/> with <c>false</c> for
/// non-owned characters. Place on the model child (the GameObject with the Animator).
/// </summary>
[RequireComponent(typeof(Animator))]
public class FirstPersonBodyCull : MonoBehaviour
{
    [Tooltip("When true, this is the local player's own body and its near geometry is hidden.")]
    [SerializeField] private bool _isLocalPlayer = true;
    [Tooltip("Scale applied to the neck bone when hidden (the head, parented under it, collapses too). " +
             "Tiny non-zero avoids shader NaNs from a true zero scale.")]
    [SerializeField] private float _hiddenScale = 0.001f;
    [Tooltip("Hide geometry within this distance (m) from the camera, via the material's _CullRadius. " +
             "0 = off. Requires the ToonOutline shader. ~0.3 trims the chest stub the camera sits inside.")]
    [SerializeField] private float _cullRadius = 0.3f;

    private static readonly int CullRadiusID = Shader.PropertyToID("_CullRadius");

    private Animator              _animator;
    private Transform             _neck;
    private Vector3               _originalScale = Vector3.one;
    private Renderer[]            _renderers;
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _animator  = GetComponent<Animator>();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb       = new MaterialPropertyBlock();

        // Collapse the Neck bone so the neck and the head (its child) both vanish.
        // Fall back to the Head bone if the avatar has no dedicated neck joint.
        _neck = _animator.GetBoneTransform(HumanBodyBones.Neck)
             ?? _animator.GetBoneTransform(HumanBodyBones.Head);
        if (_neck == null)
            Debug.LogError("[FirstPersonBodyCull] No Neck/Head bone found — is the avatar set to Humanoid?", this);
        else
            _originalScale = _neck.localScale;
    }

    private void Start() => Apply(_isLocalPlayer);

    /// <summary>Hide the near geometry (local player) or restore the full body (other players).</summary>
    public void Apply(bool isLocalPlayer)
    {
        _isLocalPlayer = isLocalPlayer;

        if (_neck != null)
            _neck.localScale = isLocalPlayer ? Vector3.one * _hiddenScale : _originalScale;

        SetCullRadius(isLocalPlayer ? _cullRadius : 0f);
    }

    private void SetCullRadius(float radius)
    {
        if (_renderers == null) return;
        foreach (Renderer r in _renderers)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(CullRadiusID, radius);
            r.SetPropertyBlock(_mpb);
        }
    }
}
