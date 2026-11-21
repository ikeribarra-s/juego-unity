using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A simple roaming creature that hunts players. Two states:
///   • <b>Patrol</b> — wander to random reachable points, pausing briefly at each.
///   • <b>Chase</b>  — once it sees a living player (within <see cref="_sightRange"/>, inside its FOV,
///     with clear line of sight past the <see cref="_sightBlockers"/> walls), it runs them down and
///     <see cref="IKillable.Kill"/>s them on contact (<see cref="_killRange"/>). It keeps chasing for
///     <see cref="_loseSightTime"/> after losing sight, then gives up and resumes patrolling.
///
/// Movement is delegated entirely to a <see cref="NavMeshAgent"/> on the same GameObject, so it needs
/// a baked NavMesh (see <see cref="EnemySpawner"/>, which bakes one at runtime over the procedural
/// dungeon). All randomness is gameplay-only (<see cref="Random"/>), unrelated to the seeded generator.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class PatrolEnemy : MonoBehaviour
{
    private enum State { Patrol, Chase }

    [Header("Speed")]
    [SerializeField] private float _patrolSpeed = 2f;
    [SerializeField] private float _chaseSpeed  = 4.5f;

    [Header("Patrol")]
    [Tooltip("How far the next wander point can be from the current position.")]
    [SerializeField] private float _patrolPointRadius = 18f;
    [Tooltip("Distance at which a patrol point counts as reached.")]
    [SerializeField] private float _waypointTolerance = 1.2f;
    [Tooltip("Seconds paused at each patrol point before choosing the next.")]
    [SerializeField] private float _idleAtPoint = 1f;

    [Header("Detection")]
    [SerializeField] private float _sightRange = 12f;
    [Tooltip("Field-of-view cone (degrees). 360 = senses all around.")]
    [SerializeField] private float _fieldOfView = 200f;
    [SerializeField] private float _eyeHeight = 1.6f;
    [Tooltip("Keeps chasing this long after losing line of sight before giving up.")]
    [SerializeField] private float _loseSightTime = 4f;
    [Tooltip("Layers that block sight (walls). Auto-set to the 'Walls' layer if left empty.")]
    [SerializeField] private LayerMask _sightBlockers;

    [Header("Kill")]
    [SerializeField] private float _killRange = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool _debug;

    private NavMeshAgent _agent;
    private State        _state = State.Patrol;
    private CharacterBase _target;
    private float _idleTimer;
    private float _lostTimer;
    private float _retargetTimer;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (_sightBlockers == 0) _sightBlockers = LayerMask.GetMask("Walls");
    }

    private void Start()
    {
        _agent.speed = _patrolSpeed;
        PickNewPatrolPoint();
    }

    private void Update()
    {
        if (!_agent.isOnNavMesh) return;   // not placed yet / off the mesh

        AcquireTarget();

        switch (_state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Chase:  TickChase();  break;
        }
    }

    // ── Patrol ──────────────────────────────────────────────────────────────
    private void TickPatrol()
    {
        _agent.speed = _patrolSpeed;

        if (_target != null && CanSee(_target)) { EnterChase(); return; }

        if (!_agent.pathPending && _agent.remainingDistance <= _waypointTolerance)
        {
            _idleTimer -= Time.deltaTime;
            if (_idleTimer <= 0f) PickNewPatrolPoint();
        }
    }

    private void PickNewPatrolPoint()
    {
        _idleTimer = _idleAtPoint;
        for (int i = 0; i < 16; i++)
        {
            Vector3 candidate = transform.position + Random.insideUnitSphere * _patrolPointRadius;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                _agent.SetDestination(hit.position);
                return;
            }
        }
    }

    // ── Chase ───────────────────────────────────────────────────────────────
    private void EnterChase()
    {
        _state = State.Chase;
        _lostTimer = _loseSightTime;
        if (_debug) Debug.Log("[PatrolEnemy] target spotted — chasing.", this);
    }

    private void EnterPatrol()
    {
        _state = State.Patrol;
        if (_debug) Debug.Log("[PatrolEnemy] lost target — patrolling.", this);
        PickNewPatrolPoint();
    }

    private void TickChase()
    {
        _agent.speed = _chaseSpeed;

        if (_target == null || _target.Vitals == null || _target.Vitals.IsDead) { EnterPatrol(); return; }

        _agent.SetDestination(_target.transform.position);

        if (Vector3.Distance(transform.position, _target.transform.position) <= _killRange)
        {
            _target.Vitals.Kill(gameObject);
            EnterPatrol();
            return;
        }

        if (CanSee(_target)) _lostTimer = _loseSightTime;
        else
        {
            _lostTimer -= Time.deltaTime;
            if (_lostTimer <= 0f) EnterPatrol();
        }
    }

    // ── Targeting & senses ──────────────────────────────────────────────────
    private void AcquireTarget()
    {
        if (_target != null && _target.Vitals != null && !_target.Vitals.IsDead) return;

        _retargetTimer -= Time.deltaTime;
        if (_retargetTimer > 0f) return;
        _retargetTimer = 0.25f;
        _target = FindNearestAlivePlayer();
    }

    private CharacterBase FindNearestAlivePlayer()
    {
        var players = FindObjectsByType<CharacterBase>(FindObjectsSortMode.None);
        CharacterBase best = null;
        float bestSq = float.MaxValue;
        foreach (CharacterBase p in players)
        {
            if (p.Vitals != null && p.Vitals.IsDead) continue;
            float sq = (p.transform.position - transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = p; }
        }
        return best;
    }

    private bool CanSee(CharacterBase t)
    {
        if (t == null || (t.Vitals != null && t.Vitals.IsDead)) return false;

        Vector3 eye     = transform.position + Vector3.up * _eyeHeight;
        Vector3 toChest = (t.transform.position + Vector3.up * 1.4f) - eye;

        if (toChest.magnitude > _sightRange) return false;

        Vector3 flat = new(toChest.x, 0f, toChest.z);
        if (flat.sqrMagnitude > 0.001f &&
            Vector3.Angle(transform.forward, flat) > _fieldOfView * 0.5f) return false;

        // A wall between us blocks sight; nothing else is on the blockers mask.
        if (Physics.Raycast(eye, toChest.normalized, toChest.magnitude, _sightBlockers)) return false;

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!_debug) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _sightRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _killRange);
    }
}
