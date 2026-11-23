using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkObject))]
public class ZombieAI : NetworkBehaviour, IDamageable
{
    [Header("Zombie Stats")]
    [SerializeField] private int maxHealth = 60;
    [SerializeField] private int attackDamage = 15;
    [SerializeField] private float attackRange = 1.4f;
    [SerializeField] private float attackCooldown = 1.2f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.2f;
    [SerializeField] private float pathUpdateInterval = 0.2f;

    [Header("Optional 2D Sprite Flip")]
    [SerializeField] private SpriteRenderer zombieSprite;

    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        60,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsActiveInPool = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public static System.Action OnZombieDiedServer;

    public Vector3 FacingDirection { get; private set; } = Vector3.forward;

    private Transform targetPlayer;
    private NavMeshAgent agent;
    private Vector3 knockbackVelocity;
    private float lastAttackTime = -99f;
    private float searchTimer = 0f;
    private float pathTimer = 0f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            // Keep 2D sprite upright—do NOT let NavMeshAgent rotate the transform!
            agent.updateRotation = false;
            agent.speed = moveSpeed;
            agent.stoppingDistance = attackRange * 0.75f;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Only the Server runs NavMeshAgent; clients receive positions from NetworkTransform
        if (!IsServer && agent != null)
        {
            agent.enabled = false;
        }

        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;
            knockbackVelocity = Vector3.zero;
        }

        IsActiveInPool.OnValueChanged += OnPoolStateChanged;
        ApplyPoolVisuals(IsActiveInPool.Value);

        if (!IsServer)
        {
            Debug.Log($"<color=cyan>[ZombieClient]</color> OnNetworkSpawn ID={NetworkObjectId} | Active={IsActiveInPool.Value} | Pos={transform.position}");
        }
    }

    public override void OnNetworkDespawn()
    {
        IsActiveInPool.OnValueChanged -= OnPoolStateChanged;
        base.OnNetworkDespawn();
    }

    private void OnPoolStateChanged(bool previous, bool current)
    {
        ApplyPoolVisuals(current);

        if (!IsServer)
        {
            Debug.Log($"<color=lime>[ZombieClient]</color> ID={NetworkObjectId} IsActiveInPool: {previous} -> {current} | Pos={transform.position}");
        }
    }

    public void ApplyPoolVisuals(bool isActive)
    {
        foreach (var col in GetComponentsInChildren<Collider>(true))
        {
            if (col is not CharacterController)
            {
                col.enabled = isActive;
            }
        }

        var sprites = GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var rend in sprites)
        {
            rend.enabled = isActive;
        }

        var meshes = GetComponentsInChildren<MeshRenderer>(true);
        foreach (var rend in meshes)
        {
            rend.enabled = isActive;
        }

        if (!IsServer && isActive)
        {
            Debug.Log($"<color=yellow>[ZombieClient]</color> ApplyPoolVisuals(TRUE) on ID={NetworkObjectId} | Sprites={sprites.Length} | Meshes={meshes.Length} | Pos={transform.position}");
        }
    }

    public void SafeSpawnReset(Vector3 spawnPosition)
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();

        knockbackVelocity = Vector3.zero;
        targetPlayer = null;
        searchTimer = 0f;
        pathTimer = 0f;

        if (IsServer && agent != null)
        {
            if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                spawnPosition = hit.position;
            }

            transform.position = spawnPosition;
            agent.enabled = true;
            agent.Warp(spawnPosition);
            agent.isStopped = false;
            agent.speed = moveSpeed;
            agent.stoppingDistance = attackRange * 0.75f;

            CurrentHealth.Value = maxHealth;
            IsActiveInPool.Value = true;
        }
    }

    public void ResetForSpawn(Vector3 spawnPosition) => SafeSpawnReset(spawnPosition);

    public void TakeDamage(int damage, Vector3 knockbackForce)
    {
        if (!IsServer || !IsActiveInPool.Value) return;

        CurrentHealth.Value -= damage;
        Debug.Log($"<color=red>[ZombieAI]</color> Took {damage} dmg! HP: {CurrentHealth.Value}/{maxHealth}");

        knockbackVelocity = knockbackForce;
        knockbackVelocity.y = 0f;

        if (CurrentHealth.Value <= 0)
        {
            Debug.Log("<color=red>[ZombieAI]</color> Zombie died! Returning to pool.");
            OnZombieDiedServer?.Invoke();

            if (ZombiePool.Instance != null)
            {
                ZombiePool.Instance.ReturnToPool(GetComponent<NetworkObject>());
            }
            else
            {
                GetComponent<NetworkObject>().Despawn(true);
            }
        }
    }

    private void Update()
    {
        if (!IsServer || !IsActiveInPool.Value || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        // 1. Search for closest alive player
        searchTimer -= Time.deltaTime;
        if (searchTimer <= 0f)
        {
            FindClosestPlayer();
            searchTimer = 0.4f;
        }

        // 2. Handle Knockback (stays inside NavMesh walls)
        if (knockbackVelocity.sqrMagnitude > 0.1f)
        {
            agent.isStopped = true;
            agent.Move(knockbackVelocity * Time.deltaTime);
            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, Time.deltaTime * 6f);
            return;
        }

        if (agent.isStopped)
        {
            agent.isStopped = false;
        }

        // 3. Pathfind & Attack
        if (targetPlayer != null)
        {
            pathTimer -= Time.deltaTime;
            if (pathTimer <= 0f)
            {
                agent.SetDestination(targetPlayer.position);
                pathTimer = pathUpdateInterval;
            }

            // Update FacingDirection for Rogue backstab crits & optional sprite flip
            Vector3 moveVel = agent.velocity;
            moveVel.y = 0f;
            if (moveVel.sqrMagnitude > 0.05f)
            {
                FacingDirection = moveVel.normalized;
            }
            else
            {
                Vector3 toPlayer = targetPlayer.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    FacingDirection = toPlayer.normalized;
                }
            }

            if (zombieSprite != null && Mathf.Abs(FacingDirection.x) > 0.05f)
            {
                zombieSprite.flipX = FacingDirection.x < 0f;
            }

            float distToPlayer = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(targetPlayer.position.x, 0f, targetPlayer.position.z)
            );

            if (distToPlayer <= attackRange)
            {
                TryAttackPlayer();
            }
        }
    }

    private void TryAttackPlayer()
    {
        if (Time.time < lastAttackTime + attackCooldown) return;

        lastAttackTime = Time.time;

        IDamageable player = targetPlayer.GetComponentInParent<IDamageable>();
        if (player != null)
        {
            Vector3 pushBack = (targetPlayer.position - transform.position).normalized * 4f;
            player.TakeDamage(attackDamage, pushBack);
            Debug.Log($"<color=orange>[ZombieAI]</color> Attacked player for {attackDamage} damage!");
        }
    }

    private void FindClosestPlayer()
    {
        PlayerController[] players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        float shortestDistance = Mathf.Infinity;
        Transform closest = null;

        foreach (PlayerController player in players)
        {
            if (player == null) continue;

            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead.Value) continue;

            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance < shortestDistance)
            {
                shortestDistance = distance;
                closest = player.transform;
            }
        }

        targetPlayer = closest;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up, attackRange);
    }
}