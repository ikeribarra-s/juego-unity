using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Death = the skeleton falls apart. Auto-added by <see cref="CharacterBase"/>; listens to <see cref="CharacterVitals.Died"/>.
///
/// On death: drops the carried torch / beam-held evidence, bakes every body mesh of the model at the death pose
/// (<see cref="SkinnedMeshRenderer.BakeMesh(Mesh, bool)"/> with scale, so a squashed model bakes correctly; the separate
/// jaw mesh joins the skull), splits the triangles by each vertex's dominant Humanoid bone into the
/// <see cref="BoneGroup"/> pieces (fingers → hand, toes → foot, neck / spine / clavicles → ribcage; groups smaller
/// than <see cref="_minTrianglesPerPiece"/> merge into their parent group), and spawns one physics piece per group
/// under a scene-root <c>Remains_&lt;owner&gt;</c>: pivot recentred, same material, convex MeshCollider, Rigidbody
/// with mass from its bounds, launched outward + up with random spin so the bones land within
/// <see cref="_scatterRadius"/>. The model is hidden and the character's controller / colliders / audio turned off.
///
/// Pieces: <see cref="BonePiece"/> (owner info) + <see cref="BonePickup"/> (glow + E → inventory), except the skull:
/// <see cref="LivingSkull"/>, which takes the camera if this is the local player.
/// All allocation happens in the burst; nothing runs per frame afterwards except physics.
/// </summary>
[DisallowMultipleComponent]
public class SkeletonDisassembler : MonoBehaviour
{
    [SerializeField] private bool _disassembleOnDeath = true;
    [Tooltip("Name shown on this character's bones (\"Pick up Ribcage (X's)\"). Empty = CharacterDefinition.DisplayName, else GameObject name.")]
    [SerializeField] private string _ownerNameOverride = "";

    [Header("Scatter")]
    [Tooltip("Bones land within roughly this radius (m) of the body (flight only; a little rolling is added on top).")]
    [SerializeField] private float _scatterRadius = 1.8f;
    [Tooltip("Shortest landing distance from the body (m).")]
    [SerializeField] private float _minScatter = 0.35f;
    [Tooltip("Upward launch speed range (m/s).")]
    [SerializeField] private Vector2 _upSpeed = new(1.6f, 3.0f);
    [Tooltip("Extra upward pop for the skull (m/s) — it flies a bit higher, comedic.")]
    [SerializeField] private float _skullPop = 1.2f;
    [Tooltip("Max random angular velocity at launch (rad/s).")]
    [SerializeField] private float _spin = 10f;
    [Tooltip("Random spread around the outward direction (degrees).")]
    [SerializeField] private float _directionJitter = 35f;

    [Header("Physics")]
    [Tooltip("kg per m³ of each piece's bounding box (bones are mostly air).")]
    [SerializeField] private float _density = 14f;
    [SerializeField] private Vector2 _massRange = new(0.15f, 2.5f);
    [SerializeField] private float _linearDamping = 0.1f;
    [SerializeField] private float _angularDamping = 0.8f;
    [Tooltip("Dampings switched on at a piece's first ground contact, so it stops close to where it lands (rolling eats the radius otherwise).")]
    [SerializeField] private float _settleLinearDamping = 1.2f;
    [SerializeField] private float _settleAngularDamping = 2f;
    [SerializeField, Range(0f, 1f)] private float _friction = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _bounciness = 0.2f;
    [Tooltip("Pieces ignore each other for this long after the burst (they spawn overlapping at the joints). 0 = never collide with each other.")]
    [SerializeField] private float _selfCollisionDelay = 0.6f;
    [Tooltip("Groups with fewer triangles merge into their parent group (hand → forearm …).")]
    [SerializeField] private int _minTrianglesPerPiece = 8;

    [Header("Death FX (Docs/Design/DeathFX.md)")]
    [Tooltip("Shared profile; empty = DeathFX.Instance.Profile (characters get this component at runtime, so the scene DeathFX normally supplies it).")]
    [SerializeField] private DeathFXProfile _fx;

    [Header("Audio")]
    [Tooltip("Collapse + clatter + skull roll + bone pick-up (SD_BoneClack).")]
    [SerializeField] private SoundDefinition _boneSound;
    [SerializeField] private float _impactSoundSpeed = 1.6f;
    [SerializeField] private float _impactSoundInterval = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool _log = true;

    /// <summary>Fired after the pieces were spawned.</summary>
    public event Action<SkeletonDisassembler> Disassembled;

    public bool IsDisassembled { get; private set; }
    public IReadOnlyList<BonePiece> Pieces => _pieces;
    public BonePiece Skull { get; private set; }
    public LivingSkull LivingSkull { get; private set; }
    public Transform RemainsRoot { get; private set; }
    /// <summary>Horizontal centre of the burst (the pelvis at death).</summary>
    public Vector3 BurstCenter { get; private set; }
    public float ScatterRadius => _scatterRadius;
    public string OwnerName => ResolveOwnerName();

    private DeathFXProfile Fx => _fx != null ? _fx : (DeathFX.Instance != null ? DeathFX.Instance.Profile : null);

    private CharacterBase _character;
    private CharacterVitals _vitals;
    private readonly List<BonePiece> _pieces = new();
    private static PhysicsMaterial s_material;

    // ── Lifecycle ───────────────────────────────────────────────────────────
    private void Awake() => _character = GetComponent<CharacterBase>();

    // Vitals is auto-added in CharacterBase.Awake, which may run after ours.
    private void Start()
    {
        _vitals = _character != null ? _character.Vitals : GetComponent<CharacterVitals>();
        if (_vitals != null) _vitals.Died += OnDied;
    }

    private void OnDestroy()
    {
        if (_vitals != null) _vitals.Died -= OnDied;
    }

    private void OnDied()
    {
        if (_disassembleOnDeath && isActiveAndEnabled) Disassemble();
    }

    public void SetOwnerNameOverride(string n) => _ownerNameOverride = n;
    public void SetBoneSound(SoundDefinition s) => _boneSound = s;

    private string ResolveOwnerName()
    {
        if (!string.IsNullOrWhiteSpace(_ownerNameOverride)) return _ownerNameOverride;
        if (_character != null && _character.Definition != null && !string.IsNullOrWhiteSpace(_character.Definition.DisplayName))
            return _character.Definition.DisplayName;
        return name;
    }

    // ── The burst ───────────────────────────────────────────────────────────
    private struct Source
    {
        public Vector3[] Positions;   // world
        public Vector3[] Normals;     // world
        public Vector2[] Uvs;
        public int[][]   SubTriangles;
        public int[]     SubMaterial; // index into materials list
        public BoneGroup[] VertexGroup;
    }

    /// <summary>Fall apart now (normally called by <see cref="CharacterVitals.Died"/>). Safe to call once.</summary>
    public void Disassemble()
    {
        if (IsDisassembled) return;
        IsDisassembled = true;

        // 1. Let go of everything (the torch must leave the hand before the model is hidden).
        if (_character != null)
        {
            if (_character.Grabber != null) _character.Grabber.Release();
            if (_character.TorchCarrier != null) { _character.TorchCarrier.CancelAction(); _character.TorchCarrier.Drop(); }
        }

        Animator animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman)
        {
            Debug.LogWarning($"[SkeletonDisassembler] '{name}' has no Humanoid Animator — nothing to disassemble.", this);
            return;
        }

        // Bone → group via the Humanoid map (walk up for unmapped bones such as finger tips / JawPivot).
        var humanMap = new Dictionary<Transform, BoneGroup>(64);
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            Transform t = animator.GetBoneTransform((HumanBodyBones)i);
            if (t != null) humanMap[t] = BoneGroups.FromHumanBone((HumanBodyBones)i);
        }
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);

        // 2. Bake the death pose.
        var materials = new List<Material>(2);
        var sources = new List<Source>(2);
        var bakeMesh = new Mesh();
        foreach (Renderer r in animator.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || r.GetComponentInParent<Torch>() != null || r.GetComponent<SkullBlink>() != null) continue;   // lids ride on the skull instead
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                sources.Add(BakeSkinned(smr, bakeMesh, humanMap, materials));
            else if (r is MeshRenderer mr && mr.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null && mf.sharedMesh.isReadable)
                sources.Add(BakeStatic(mr, mf.sharedMesh, GroupOf(mr.transform, humanMap), materials));
        }
        Destroy(bakeMesh);
        if (sources.Count == 0)
        {
            Debug.LogWarning($"[SkeletonDisassembler] '{name}': no readable body meshes.", this);
            return;
        }

        // 3. Triangle → group (majority of its vertices), with small groups merged into their parent.
        var triGroups = new List<BoneGroup[]>[sources.Count];
        var triCount = new int[BoneGroups.Count];
        for (int s = 0; s < sources.Count; s++)
        {
            Source src = sources[s];
            triGroups[s] = new List<BoneGroup[]>(src.SubTriangles.Length);
            for (int sm = 0; sm < src.SubTriangles.Length; sm++)
            {
                int[] tris = src.SubTriangles[sm];
                var g = new BoneGroup[tris.Length / 3];
                for (int t = 0; t < g.Length; t++)
                {
                    BoneGroup a = src.VertexGroup[tris[t * 3]], b = src.VertexGroup[tris[t * 3 + 1]], c = src.VertexGroup[tris[t * 3 + 2]];
                    g[t] = (b == c && a != b) ? b : a;
                    triCount[(int)g[t]]++;
                }
                triGroups[s].Add(g);
            }
        }
        var remap = new BoneGroup[BoneGroups.Count];
        for (int i = 0; i < remap.Length; i++)
        {
            BoneGroup g = (BoneGroup)i;
            int guard = 0;
            while (g != BoneGroup.Pelvis && triCount[(int)g] < _minTrianglesPerPiece && guard++ < 8) g = BoneGroups.Parent(g);
            remap[i] = g;
        }

        // 4. Hide the body, stop the character.
        BurstCenter = hips != null ? hips.position : transform.position + Vector3.up;
        Camera localCam = FindLocalCamera();
        // Lids ride on the skull: detach them before the model GameObject is deactivated (SkullBlink.md 7b).
        var blinks = animator.GetComponentsInChildren<SkullBlink>(true);
        foreach (SkullBlink b in blinks) b.DetachForDeath();
        HideBody(animator);

        string ownerName = ResolveOwnerName();
        // Phase 4 (NGO): use the NetworkObjectId / owner client id instead of the session-local instance id.
        ulong ownerId = EntityId.ToULong(_character != null ? _character.GetEntityId() : GetEntityId());
        var root = new GameObject($"Remains_{ownerName}");
        root.transform.position = new Vector3(BurstCenter.x, transform.position.y, BurstCenter.z);
        RemainsRoot = root.transform;

        float groundY = transform.position.y;
        if (Physics.Raycast(BurstCenter, Vector3.down, out RaycastHit gh, 4f, ~0, QueryTriggerInteraction.Ignore)) groundY = gh.point.y;

        // 5. Build one piece per group.
        var pos = new List<Vector3>(2048);
        var nrm = new List<Vector3>(2048);
        var uv  = new List<Vector2>(2048);
        var idx = new List<int>[materials.Count];
        for (int m = 0; m < idx.Length; m++) idx[m] = new List<int>(4096);
        var vmap = new List<int[]>(sources.Count);
        foreach (Source src in sources) vmap.Add(new int[src.Positions.Length]);

        for (int gi = 0; gi < BoneGroups.Count; gi++)
        {
            var group = (BoneGroup)gi;
            pos.Clear(); nrm.Clear(); uv.Clear();
            foreach (var l in idx) l.Clear();

            for (int s = 0; s < sources.Count; s++)
            {
                Source src = sources[s];
                int[] map = vmap[s];
                for (int i = 0; i < map.Length; i++) map[i] = -1;
                for (int sm = 0; sm < src.SubTriangles.Length; sm++)
                {
                    int[] tris = src.SubTriangles[sm];
                    BoneGroup[] tg = triGroups[s][sm];
                    List<int> dst = idx[src.SubMaterial[sm]];
                    for (int t = 0; t < tg.Length; t++)
                    {
                        if (remap[(int)tg[t]] != group) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int v = tris[t * 3 + k];
                            if (map[v] < 0)
                            {
                                map[v] = pos.Count;
                                pos.Add(src.Positions[v]);
                                nrm.Add(src.Normals[v]);
                                uv.Add(src.Uvs != null && v < src.Uvs.Length ? src.Uvs[v] : Vector2.zero);
                            }
                            dst.Add(map[v]);
                        }
                    }
                }
            }
            if (pos.Count < 4) continue;

            BonePiece piece = BuildPiece(group, pos, nrm, uv, idx, materials, root.transform, ownerName, ownerId, groundY);
            if (piece != null) _pieces.Add(piece);
        }

        // Neighbouring pieces spawn interpenetrating at the joints; PhysX depenetration would bat the light ones
        // (hands, feet) metres away. They ignore each other during the flight, then collide again unless still overlapping.
        SetSelfCollision(false, false);
        if (_selfCollisionDelay > 0f) Invoke(nameof(RestoreSelfCollision), _selfCollisionDelay);

        DeathFXProfile fx = Fx;
        if (fx != null && DeathFX.Instance != null) DeathFX.Instance.BurstAt(BurstCenter, fx, localCam != null);
        else if (_boneSound != null && SoundManager.Instance != null) SoundManager.Instance.Play(_boneSound, BurstCenter);

        // 6. The living skull (+ its wisp / eye light; local player also gets the post FX).
        if (Skull != null)
        {
            SkullWisp wisp = null;
            if (fx != null)
            {
                wisp = Skull.gameObject.AddComponent<SkullWisp>();
                wisp.Init(fx, DeathFX.Instance != null ? DeathFX.Instance.WispPrefab : null, 0.6f);
            }
            LivingSkull = Skull.gameObject.AddComponent<LivingSkull>();
            LivingSkull.Init(_character, localCam, root.transform, _boneSound, fx, wisp);
            foreach (SkullBlink b in blinks) { if (b == null) continue; b.Attach(LivingSkull, Skull); b.AttachToSkull(Skull.transform); }
        }
        else
        {
            foreach (SkullBlink b in blinks) if (b != null) Destroy(b.gameObject);
            if (blinks.Length > 0) Debug.LogWarning("[SkeletonDisassembler] No skull piece: lids removed.", this);
        }
        if (fx != null && localCam != null)
        {
            var local = gameObject.GetComponent<DeathFXLocal>();
            if (local == null) local = gameObject.AddComponent<DeathFXLocal>();
            local.Init(fx);
        }

        if (_log)
            Debug.Log($"[SkeletonDisassembler] '{name}' fell apart into {_pieces.Count} pieces (owner '{ownerName}', id {ownerId})" +
                      (localCam != null ? " — local player, skull camera on." : "."), this);
        Disassembled?.Invoke(this);
    }

    private Camera FindLocalCamera()
    {
        Camera cam = GetComponentInChildren<Camera>();
        return cam != null && cam.isActiveAndEnabled ? cam : null;
    }

    private void HideBody(Animator animator)
    {
        // Everything on the model (renderers, Animator, IK, first-person camera follow, torch arm …) goes with it.
        animator.gameObject.SetActive(false);
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;   // CharacterController included
        if (_character != null)
        {
            if (_character.Grabber != null) _character.Grabber.enabled = false;
            if (_character.TorchCarrier != null) _character.TorchCarrier.enabled = false;   // OnDisable restores HeldTorchLighting
        }
        if (TryGetComponent(out HeldTorchLighting htl)) htl.enabled = false;
        if (TryGetComponent(out CharacterAudio audio)) audio.enabled = false;
    }

    private BonePiece BuildPiece(BoneGroup group, List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv, List<int>[] idx,
                                 List<Material> materials, Transform root, string ownerName, ulong ownerId, float groundY)
    {
        // Recentre the pivot on the bounds centre.
        Vector3 min = pos[0], max = pos[0];
        for (int i = 1; i < pos.Count; i++) { min = Vector3.Min(min, pos[i]); max = Vector3.Max(max, pos[i]); }
        Vector3 center = (min + max) * 0.5f;
        for (int i = 0; i < pos.Count; i++) pos[i] -= center;

        var mesh = new Mesh { name = $"Bone_{group}" };
        mesh.SetVertices(pos);
        mesh.SetNormals(nrm);
        mesh.SetUVs(0, uv);
        var usedMats = new List<Material>(materials.Count);
        int sub = 0;
        for (int m = 0; m < idx.Length; m++) if (idx[m].Count > 0) sub++;
        mesh.subMeshCount = sub;
        sub = 0;
        for (int m = 0; m < idx.Length; m++)
        {
            if (idx[m].Count == 0) continue;
            mesh.SetTriangles(idx[m], sub++, false);
            usedMats.Add(materials[m]);
        }
        mesh.RecalculateBounds();

        var go = new GameObject($"Bone_{group}");
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(center, Quaternion.identity);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = usedMats.ToArray();
        mr.shadowCastingMode = ShadowCastingMode.On;

        // PhysX convex hulls are capped at 255 polygons (the skull's hull is bigger and logs a warning every death):
        // dense pieces get a proxy of their support points in 48 directions instead. Set the mesh BEFORE 'convex':
        // AddComponent auto-assigns the MeshFilter's dense mesh, and turning convex on first would cook that one.
        Mesh hull = mesh.triangles.Length / 3 > 250 ? BuildHullProxy(pos, group) : mesh;
        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = hull;
        col.convex = true;
        col.sharedMaterial = SharedPhysicsMaterial();

        Vector3 size = max - min;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = Mathf.Clamp(size.x * size.y * size.z * _density, _massRange.x, _massRange.y);
        rb.linearDamping = _linearDamping;
        rb.angularDamping = _angularDamping;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.maxAngularVelocity = 30f;

        Launch(rb, group, center, groundY);

        var piece = go.AddComponent<BonePiece>();
        piece.Init(_character, ownerName, ownerId, group, rb, mr, _boneSound, _impactSoundSpeed, _impactSoundInterval);
        piece.SetSettleDamping(_settleLinearDamping, _settleAngularDamping);

        if (group == BoneGroup.Skull)
        {
            Skull = piece;
            // The skull is never in an inventory, but still must not inherit the first-person head cull.
            var mpb = new MaterialPropertyBlock();
            mpb.SetFloat("_HideHead", 0f); mpb.SetFloat("_CullRadius", 0f); mpb.SetFloat("_EmissionIntensity", 0f);
            mr.SetPropertyBlock(mpb);
        }
        else
        {
            go.AddComponent<BonePickup>().Init(Fx != null && Fx.BonePickup != null ? Fx.BonePickup : _boneSound);
        }
        return piece;
    }

    private void RestoreSelfCollision() => SetSelfCollision(true, true);

    /// <summary>Toggle collisions between this skeleton's pieces. With <paramref name="skipOverlapping"/>, pairs that still
    /// overlap stay ignored (re-enabling them would launch them apart).</summary>
    private void SetSelfCollision(bool collide, bool skipOverlapping)
    {
        for (int i = 0; i < _pieces.Count; i++)
        {
            if (_pieces[i] == null || !_pieces[i].gameObject.activeInHierarchy || !_pieces[i].TryGetComponent(out Collider a)) continue;
            for (int j = i + 1; j < _pieces.Count; j++)
            {
                if (_pieces[j] == null || !_pieces[j].gameObject.activeInHierarchy || !_pieces[j].TryGetComponent(out Collider b)) continue;
                bool ignore = !collide;
                if (collide && skipOverlapping && Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                                                                               b, b.transform.position, b.transform.rotation, out _, out _))
                    ignore = true;
                Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }

    private static Mesh BuildHullProxy(List<Vector3> pos, BoneGroup group)
    {
        const int N = 48;
        var picked = new List<Vector3>(N);
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < N; i++)
        {
            float y = 1f - (i + 0.5f) * 2f / N, r = Mathf.Sqrt(1f - y * y), a = golden * i;
            var dir = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            int best = 0; float bd = float.MinValue;
            for (int v = 0; v < pos.Count; v++) { float d = Vector3.Dot(pos[v], dir); if (d > bd) { bd = d; best = v; } }
            if (!picked.Contains(pos[best])) picked.Add(pos[best]);
        }
        var tris = new List<int>((picked.Count - 2) * 3);
        for (int i = 1; i < picked.Count - 1; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }   // only the points matter for the hull
        var m = new Mesh { name = $"BoneHull_{group}" };
        m.SetVertices(picked);
        m.SetTriangles(tris, 0);
        return m;
    }

    private void Launch(Rigidbody rb, BoneGroup group, Vector3 center, float groundY)
    {
        Vector3 outward = center - BurstCenter;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0225f) // pieces on the body axis (skull, ribcage, pelvis): any direction
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle.normalized;
            outward = new Vector3(r.x, 0f, r.y);
        }
        outward = Quaternion.Euler(0f, UnityEngine.Random.Range(-_directionJitter, _directionJitter), 0f) * outward.normalized;

        // Pick a landing distance and solve the horizontal speed from the flight time to the ground.
        float vy = UnityEngine.Random.Range(_upSpeed.x, _upSpeed.y) + (group == BoneGroup.Skull ? _skullPop : 0f);
        float g = Mathf.Max(0.1f, -Physics.gravity.y);
        float h = Mathf.Max(0f, center.y - groundY);
        float flight = (vy + Mathf.Sqrt(vy * vy + 2f * g * h)) / g;
        float d = UnityEngine.Random.Range(_minScatter, Mathf.Max(_minScatter, _scatterRadius * 0.8f));
        float vh = d / Mathf.Max(0.2f, flight);

        rb.linearVelocity = outward * vh + Vector3.up * vy;
        rb.angularVelocity = UnityEngine.Random.insideUnitSphere * _spin;
    }

    private PhysicsMaterial SharedPhysicsMaterial()
    {
        if (s_material == null)
        {
            s_material = new PhysicsMaterial("BonePieces")
            {
                dynamicFriction = _friction,
                staticFriction  = Mathf.Min(1f, _friction + 0.1f),
                bounciness      = _bounciness,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine   = PhysicsMaterialCombine.Average,
            };
        }
        return s_material;
    }

    // ── Baking helpers ──────────────────────────────────────────────────────
    private static BoneGroup GroupOf(Transform t, Dictionary<Transform, BoneGroup> map)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (map.TryGetValue(p, out BoneGroup g)) return g;
        return BoneGroup.Ribcage;
    }

    private static int MaterialIndex(Material m, List<Material> materials)
    {
        int i = materials.IndexOf(m);
        if (i >= 0) return i;
        materials.Add(m);
        return materials.Count - 1;
    }

    private static Source BakeSkinned(SkinnedMeshRenderer smr, Mesh bake, Dictionary<Transform, BoneGroup> map, List<Material> materials)
    {
        // useScale: true → vertices exclude the renderer's scale, so localToWorldMatrix puts them in the world
        // exactly where they are drawn (incl. a CartoonAnimator squash). Verified 2026-11-27.
        smr.BakeMesh(bake, true);
        Matrix4x4 l2w = smr.transform.localToWorldMatrix;
        Matrix4x4 n2w = l2w.inverse.transpose;

        Mesh shared = smr.sharedMesh;
        Transform[] bones = smr.bones;
        var boneGroup = new BoneGroup[bones.Length];
        for (int i = 0; i < bones.Length; i++) boneGroup[i] = bones[i] != null ? GroupOf(bones[i], map) : BoneGroup.Ribcage;

        BoneWeight[] w = shared.boneWeights;
        Vector3[] v = bake.vertices;
        Vector3[] n = bake.normals;
        var src = new Source
        {
            Positions = new Vector3[v.Length],
            Normals = new Vector3[v.Length],
            Uvs = shared.uv,
            VertexGroup = new BoneGroup[v.Length],
        };
        for (int i = 0; i < v.Length; i++)
        {
            src.Positions[i] = l2w.MultiplyPoint3x4(v[i]);
            src.Normals[i] = i < n.Length ? n2w.MultiplyVector(n[i]).normalized : Vector3.up;
            if (i < w.Length)
            {
                BoneWeight b = w[i];
                int best = b.boneIndex0; float bw = b.weight0;
                if (b.weight1 > bw) { best = b.boneIndex1; bw = b.weight1; }
                if (b.weight2 > bw) { best = b.boneIndex2; bw = b.weight2; }
                if (b.weight3 > bw) { best = b.boneIndex3; }
                src.VertexGroup[i] = best >= 0 && best < boneGroup.Length ? boneGroup[best] : BoneGroup.Ribcage;
            }
            else src.VertexGroup[i] = BoneGroup.Ribcage;
        }
        FillSubmeshes(ref src, shared, smr.sharedMaterials, materials);
        return src;
    }

    private static Source BakeStatic(MeshRenderer mr, Mesh mesh, BoneGroup group, List<Material> materials)
    {
        Matrix4x4 l2w = mr.transform.localToWorldMatrix;
        Matrix4x4 n2w = l2w.inverse.transpose;
        Vector3[] v = mesh.vertices;
        Vector3[] n = mesh.normals;
        var src = new Source
        {
            Positions = new Vector3[v.Length],
            Normals = new Vector3[v.Length],
            Uvs = mesh.uv,
            VertexGroup = new BoneGroup[v.Length],
        };
        for (int i = 0; i < v.Length; i++)
        {
            src.Positions[i] = l2w.MultiplyPoint3x4(v[i]);
            src.Normals[i] = i < n.Length ? n2w.MultiplyVector(n[i]).normalized : Vector3.up;
            src.VertexGroup[i] = group;
        }
        FillSubmeshes(ref src, mesh, mr.sharedMaterials, materials);
        return src;
    }

    private static void FillSubmeshes(ref Source src, Mesh mesh, Material[] rendererMats, List<Material> materials)
    {
        int count = mesh.subMeshCount;
        src.SubTriangles = new int[count][];
        src.SubMaterial = new int[count];
        for (int sm = 0; sm < count; sm++)
        {
            src.SubTriangles[sm] = mesh.GetTriangles(sm);
            Material m = rendererMats != null && rendererMats.Length > 0 ? rendererMats[Mathf.Min(sm, rendererMats.Length - 1)] : null;
            src.SubMaterial[sm] = MaterialIndex(m, materials);
        }
    }
}
