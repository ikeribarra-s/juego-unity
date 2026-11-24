using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Bakes a NavMesh over the (procedurally generated) dungeon at runtime, then drops one or more
/// <see cref="PatrolEnemy"/> creatures at random reachable points well away from the player.
///
/// The dungeon is built from primitive meshes with colliders, so there's no authored NavMesh — this
/// builds one from the scene's physics colliders via a <see cref="NavMeshSurface"/> (auto-created if
/// none is assigned). Spawn points are sampled off the freshly-baked NavMesh, so the creature always
/// lands somewhere walkable. If no <see cref="_enemyPrefab"/> is given it builds a primitive stand-in
/// (a dark capsule with glowing eyes) — same "works with zero art" approach as the generator's
/// primitive machine, so you can playtest the hunt before there's a model.
///
/// Place this on any scene object (e.g. the same manager GameObject as MissionManager). It runs after
/// a short delay so the level is settled; call <see cref="SpawnWave"/> again after a runtime regen.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("Spawn")]
    [Tooltip("Creature prefab (needs a NavMeshAgent + PatrolEnemy; both are added if missing). " +
             "Leave empty to spawn a primitive stand-in.")]
    [SerializeField] private GameObject _enemyPrefab;
    [SerializeField] private int   _enemyCount = 1;
    [Tooltip("Don't spawn closer than this to the player.")]
    [SerializeField] private float _minDistanceFromPlayer = 25f;
    [Tooltip("Delay before spawning, to let the scene/NavMesh settle.")]
    [SerializeField] private float _spawnDelay = 0.25f;

    [Header("NavMesh")]
    [Tooltip("Surface to bake. Auto-found, or one is created on this object if none exists.")]
    [SerializeField] private NavMeshSurface _surface;
    [Tooltip("Rebuild the NavMesh at runtime before spawning (needed for procedural dungeons).")]
    [SerializeField] private bool _bakeOnStart = true;

    /// <summary>How many creatures the next wave spawns (run modifiers raise it before the first wave).</summary>
    public int EnemyCount { get => _enemyCount; set => _enemyCount = Mathf.Max(0, value); }

    /// <summary>Multiplier on spawned creatures' sight range (Fog modifier). Applied at spawn time.</summary>
    public float SightRangeScale { get; set; } = 1f;

    private void Start() => Invoke(nameof(SpawnWave), _spawnDelay);

    /// <summary>Bake the NavMesh (if enabled) and spawn the wave. Safe to call again after a regen.</summary>
    public void SpawnWave()
    {
        if (_bakeOnStart) BakeNavMesh();

        var player = FindAnyObjectByType<CharacterBase>();
        Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

        for (int i = 0; i < _enemyCount; i++)
        {
            if (TryFindSpawnPoint(playerPos, out Vector3 pos)) SpawnEnemy(pos);
            else Debug.LogWarning("[EnemySpawner] No valid NavMesh spawn point found — is the level generated and baked?", this);
        }
    }

    private void BakeNavMesh()
    {
        if (_surface == null) _surface = FindAnyObjectByType<NavMeshSurface>();
        if (_surface == null)
        {
            _surface = gameObject.AddComponent<NavMeshSurface>();
            _surface.collectObjects = CollectObjects.All;
            _surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;   // dungeon has mesh colliders
        }
        _surface.BuildNavMesh();
    }

    private bool TryFindSpawnPoint(Vector3 playerPos, out Vector3 result)
    {
        result = Vector3.zero;
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.vertices == null || tri.vertices.Length == 0) return false;

        float minSq = _minDistanceFromPlayer * _minDistanceFromPlayer;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            Vector3 v = tri.vertices[Random.Range(0, tri.vertices.Length)] + Random.insideUnitSphere * 3f;
            if (NavMesh.SamplePosition(v, out NavMeshHit hit, 4f, NavMesh.AllAreas)
                && (playerPos - hit.position).sqrMagnitude >= minSq)
            {
                result = hit.position;
                return true;
            }
        }

        // Couldn't satisfy the distance constraint (small map) — fall back to any reachable vertex.
        if (NavMesh.SamplePosition(tri.vertices[Random.Range(0, tri.vertices.Length)],
                                   out NavMeshHit any, 4f, NavMesh.AllAreas))
        {
            result = any.position;
            return true;
        }
        return false;
    }

    private void SpawnEnemy(Vector3 pos)
    {
        GameObject go = _enemyPrefab != null ? Instantiate(_enemyPrefab) : BuildPrimitiveEnemy();
        go.transform.position = pos;

        if (!go.TryGetComponent(out NavMeshAgent agent)) agent = go.AddComponent<NavMeshAgent>();
        if (!go.TryGetComponent(out PatrolEnemy patrol)) patrol = go.AddComponent<PatrolEnemy>();
        patrol.SightRangeScale = SightRangeScale;

        // Warp snaps the agent cleanly onto the mesh (just setting transform can leave it "off mesh").
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            agent.Warp(hit.position);
    }

    // Primitive stand-in: a dark capsule body with two glowing red eyes, a NavMeshAgent and a
    // CapsuleCollider for physical presence. PatrolEnemy drives it.
    private GameObject BuildPrimitiveEnemy()
    {
        var root = new GameObject("PatrolEnemy");

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        Destroy(body.GetComponent<Collider>());
        body.GetComponent<MeshRenderer>().sharedMaterial = LitMaterial(new Color(0.06f, 0.06f, 0.07f), Color.black);

        AddEye(root.transform, new Vector3(0.18f, 1.55f, 0.42f));
        AddEye(root.transform, new Vector3(-0.18f, 1.55f, 0.42f));

        var col = root.AddComponent<CapsuleCollider>();
        col.radius = 0.4f;
        col.height = 2f;
        col.center = new Vector3(0f, 1f, 0f);

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius           = 0.4f;
        agent.height           = 1.9f;
        agent.speed            = 2f;
        agent.angularSpeed     = 360f;
        agent.acceleration     = 12f;
        agent.stoppingDistance = 0.4f;

        root.AddComponent<PatrolEnemy>();
        return root;
    }

    private void AddEye(Transform parent, Vector3 local)
    {
        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        eye.transform.SetParent(parent, false);
        eye.transform.localPosition = local;
        eye.transform.localScale    = Vector3.one * 0.12f;
        Destroy(eye.GetComponent<Collider>());
        eye.GetComponent<MeshRenderer>().sharedMaterial = LitMaterial(Color.red, Color.red * 4f);
    }

    private static Material LitMaterial(Color albedo, Color emission)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { color = albedo };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", albedo);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
        }
        return m;
    }
}
