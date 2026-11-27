using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hides the local player's own near geometry so the first-person camera (anchored at the
/// neck) doesn't look through the inside of the head/neck, while the rest of the body stays
/// visible for an immersive first-person view — and the head still CASTS ITS SHADOW.
///
/// Both kinds of culling are shader-side (<c>Fluffterror/ToonOutline</c>) and only run in the
/// camera-visible passes (Outline, ForwardLit, DepthOnly), never in the ShadowCaster pass:
///  • <b>Head mask</b> — on the local player, every renderer's mesh is replaced by a runtime copy whose
///    UV3.x holds each vertex's skin weight on the Neck bone (or Head, see _hideNeckToo) and its children (skull, the separate jaw
///    mesh under Head/JawPivot counts as 1). <c>_HideHead</c> = 1 then discards exactly those vertices on
///    screen: no geometric tuning, the shoulders / torch hand are never cut, and shadows keep the head.
///  • <b>Camera radius cull</b> — <c>_CullRadius</c> discards anything within that distance of the camera,
///    removing the upper-chest stub the camera sits inside.
///
/// Needs Read/Write enabled on the model FBX (Skeleton.fbx, Skeleton_Jaw.fbx) to read bone weights.
/// The old approach — collapsing the Humanoid <c>Neck</c> bone to scale 0.001 — removed the head from
/// every pass including shadows; it remains as a fallback (<see cref="_collapseNeckFallback"/>, default off,
/// also used automatically if a mesh isn't readable).
///
/// Other players (Phase 4) keep a full body — call <see cref="Apply"/> with <c>false</c> for
/// non-owned characters. Place on the model child (the GameObject with the Animator).
/// </summary>
[RequireComponent(typeof(Animator))]
public class FirstPersonBodyCull : MonoBehaviour
{
    [Tooltip("When true, this is the local player's own body and its near geometry is hidden.")]
    [SerializeField] private bool _isLocalPlayer = true;

    [Tooltip("Hide geometry within this distance (m) from the camera, via the material's _CullRadius. " +
             "0 = off. Requires the ToonOutline shader. ~0.3 trims the chest stub the camera sits inside.")]
    [SerializeField] private float _cullRadius = 0.3f;

    [Tooltip("Hide the neck as well as the head (mask rooted at the Neck bone). The neck rises into view when " +
             "looking down from the neck-anchored camera, so this is on by default — same coverage as the old neck collapse.")]
    [SerializeField] private bool _hideNeckToo = true;

    [Header("Fallback")]
    [Tooltip("Old method: collapse the Neck bone (head vanishes from shadows too). Use only for non-ToonOutline materials.")]
    [SerializeField] private bool _collapseNeckFallback = false;
    [SerializeField] private float _hiddenScale = 0.001f;

    private static readonly int CullRadiusID = Shader.PropertyToID("_CullRadius");
    private static readonly int HideHeadID   = Shader.PropertyToID("_HideHead");
    private const int MaskChannel = 3; // UV3

    private Animator              _animator;
    private Transform             _neck;
    private Transform             _head;
    private Transform             _maskRoot;
    private Vector3               _originalNeckScale = Vector3.one;
    private Renderer[]            _renderers;
    private MaterialPropertyBlock _mpb;
    private bool                  _maskReady;
    private bool                  _neckCollapsed;
    private readonly List<(Renderer r, Mesh original, Mesh copy)> _maskedMeshes = new();

    /// <summary>True when the head is hidden with the shader mask (shadow kept), false if it fell back to the neck collapse.</summary>
    public bool UsingHeadMask => _maskReady && !_neckCollapsed;

    private void Awake()
    {
        _animator  = GetComponent<Animator>();
        _renderers = GetComponentsInChildren<Renderer>(true); // body + the separate jaw mesh under Head
        _mpb       = new MaterialPropertyBlock();

        _head = _animator.GetBoneTransform(HumanBodyBones.Head);
        _neck = _animator.GetBoneTransform(HumanBodyBones.Neck) ?? _head;
        if (_head == null)
            Debug.LogError("[FirstPersonBodyCull] No Head bone found — is the avatar set to Humanoid?", this);
        if (_neck != null) _originalNeckScale = _neck.localScale;
        _maskRoot = _hideNeckToo ? _neck : _head;
    }

    private void Start() => Apply(_isLocalPlayer);

    /// <summary>Choose local / remote before Start (networking: remote copies show the full body).</summary>
    public void SetLocalPlayer(bool isLocal) => _isLocalPlayer = isLocal;

    private void OnDestroy()
    {
        foreach (var (r, original, copy) in _maskedMeshes)
        {
            if (r != null) SetMesh(r, original);
            if (copy != null) Destroy(copy);
        }
        _maskedMeshes.Clear();
    }

    /// <summary>Hide the near geometry (local player) or restore the full body (other players).</summary>
    public void Apply(bool isLocalPlayer)
    {
        _isLocalPlayer = isLocalPlayer;

        if (isLocalPlayer && !_collapseNeckFallback && !_maskReady) _maskReady = BuildHeadMask();

        _neckCollapsed = isLocalPlayer && (_collapseNeckFallback || !_maskReady);
        if (_neck != null)
            _neck.localScale = _neckCollapsed ? Vector3.one * _hiddenScale : _originalNeckScale;

        SetProperties(isLocalPlayer ? _cullRadius : 0f, isLocalPlayer && !_neckCollapsed ? 1f : 0f);
    }

    private void SetProperties(float cullRadius, float hideHead)
    {
        if (_renderers == null) return;
        foreach (Renderer r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(CullRadiusID, cullRadius);
            _mpb.SetFloat(HideHeadID, hideHead);
            r.SetPropertyBlock(_mpb);
        }
    }

    // Bake each vertex's Head(+children) skin weight into UV3.x on a per-instance mesh copy.
    private bool BuildHeadMask()
    {
        if (_maskRoot == null) return false;
        var copies = new List<(Renderer, Mesh, Mesh)>();
        foreach (Renderer r in _renderers)
        {
            Mesh src = GetMesh(r);
            if (src == null) continue;
            if (!src.isReadable)
            {
                Debug.LogWarning($"[FirstPersonBodyCull] Mesh '{src.name}' isn't Read/Write enabled — falling back to the neck collapse (no head shadow).", this);
                foreach (var (_, _, c) in copies) Destroy(c);
                return false;
            }

            float[] mask = new float[src.vertexCount];
            if (r is SkinnedMeshRenderer s0 && s0.bones.Length == 0 && r.transform.IsChildOf(_maskRoot))
            {
                for (int v = 0; v < mask.Length; v++) mask[v] = 1f; // boneless blend-shape mesh under the head (eyelids)
            }
            else if (r is SkinnedMeshRenderer smr)
            {
                Transform[] bones = smr.bones;
                bool[] isHead = new bool[bones.Length];
                for (int i = 0; i < bones.Length; i++) isHead[i] = bones[i] != null && bones[i].IsChildOf(_maskRoot);

                var perVertex = src.GetBonesPerVertex();
                var weights   = src.GetAllBoneWeights();
                int w = 0;
                for (int v = 0; v < perVertex.Length; v++)
                {
                    float sum = 0f;
                    for (int k = 0; k < perVertex[v]; k++, w++)
                        if (isHead[weights[w].boneIndex]) sum += weights[w].weight;
                    mask[v] = sum;
                }
            }
            else if (r.transform.IsChildOf(_maskRoot))
            {
                for (int v = 0; v < mask.Length; v++) mask[v] = 1f; // rigid mesh under the head (jaw)
            }
            else continue;

            var uv = new List<Vector2>(mask.Length);
            for (int v = 0; v < mask.Length; v++) uv.Add(new Vector2(mask[v], 0f));
            Mesh copy = Instantiate(src);
            copy.name = src.name + " (FP head mask)";
            copy.SetUVs(MaskChannel, uv);
            copies.Add((r, src, copy));
        }

        foreach (var entry in copies)
        {
            SetMesh(entry.Item1, entry.Item3);
            _maskedMeshes.Add(entry);
        }
        return copies.Count > 0;
    }

    private static Mesh GetMesh(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
        return r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
    }

    private static void SetMesh(Renderer r, Mesh m)
    {
        if (r is SkinnedMeshRenderer smr) smr.sharedMesh = m;
        else if (r.TryGetComponent(out MeshFilter mf)) mf.sharedMesh = m;
    }
}
