using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(UnityEngine.CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public class ZombieAI : NetworkBehaviour, IDamageable
{
    [Header("Zombie Stats")]
    [SerializeField] private int maxHealth = 60;
    [SerializeField] private int attackDamage = 15;
    [SerializeField] private float attackRange = 1.3f;
    [SerializeField] private float attackCooldown = 1.2f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.2f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Crowd Separation (Prevent Stacking)")]
    [Tooltip("Minimum distance zombies try to keep from each other (should be slightly bigger than 2x CharacterController radius)")]
    [SerializeField] private float separationRadius = 1.25f;
    [Tooltip("How strongly zombies push apart horizontally")]
    [SerializeField] private float separationForce = 3.5f;

    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        60,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Event that notifies the wave spawner whenever a zombie is killed on the server
    public static System.Action OnZombieDiedServer;

    // Tracks active zombies for fast separation without FindObjectsByType
    private static readonly List<ZombieAI> ActiveZombies = new List<ZombieAI>();

    public Vector3 FacingDirection { get; private set; } = Vector3.forward;

    private Transform targetPlayer;
    private UnityEngine.CharacterController controller;
    private Vector3 velocity;
    private Vector3 knockbackVelocity;
    private float lastAttackTime = -99f;
    private float searchTimer = 0f;

    private void Awake()
    {
        controller = GetComponent<UnityEngine.CharacterController>();
    }

    private void OnEnable()
    {
        if (!ActiveZombies.Contains(this))
        {
            ActiveZombies.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveZombies.Remove(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!ActiveZombies.Contains(this))
        {
            ActiveZombies.Add(this);
        }

        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;
            velocity = Vector3.zero;
            knockbackVelocity = Vector3.zero;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        ActiveZombies.Remove(this);
    }

    // Called by ZombiePool when reusing this zombie
    public void SafeSpawnReset(Vector3 spawnPosition)
    {
        if (controller == null) controller = GetComponent<UnityEngine.CharacterController>();

        controller.enabled = false;
        transform.position = spawnPosition;
        velocity = Vector3.zero;
        knockbackVelocity = Vector3.zero;
        targetPlayer = null;
        searchTimer = 0f;
        controller.enabled = true;

        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;
        }
    }

    // Alias for compatibility with ZombiePool
    public void ResetForSpawn(Vector3 spawnPosition) => SafeSpawnReset(spawnPosition);

    public void TakeDamage(int damage, Vector3 knockbackForce)
    {
        if (!IsServer) return;

        CurrentHealth.Value -= damage;
        Debug.Log($"<color=red>[ZombieAI]</color> Took {damage} dmg! HP: {CurrentHealth.Value}/{maxHealth}");

        knockbackVelocity = knockbackForce;

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
        if (!IsServer) return;

        // 1. Periodically search for closest alive player
        searchTimer -= Time.deltaTime;
        if (searchTimer <= 0f)
        {
            FindClosestPlayer();
            searchTimer = 0.5f;
        }

        // Calculate horizontal push away from nearby zombies
        Vector3 separation = CalculateSeparationVector() * separationForce;

        // 2. Handle Knockback or Chase + Separation
        if (knockbackVelocity.sqrMagnitude > 0.1f)
        {
            controller.Move((knockbackVelocity + separation * 0.5f) * Time.deltaTime);
            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, Time.deltaTime * 6f);
        }
        else if (targetPlayer != null)
        {
            Vector3 direction = targetPlayer.position - transform.position;
            direction.y = 0f;

            float distanceToPlayer = direction.magnitude;
            Vector3 chaseMove = Vector3.zero;

            if (distanceToPlayer > 0.1f)
            {
                Vector3 moveDir = direction.normalized;
                FacingDirection = moveDir;

                if (distanceToPlayer > attackRange * 0.8f)
                {
                    chaseMove = moveDir * moveSpeed;
                }
            }

            Vector3 totalHorizontalMove = chaseMove + separation;
            totalHorizontalMove.y = 0f;

            if (totalHorizontalMove.sqrMagnitude > 0.001f)
            {
                controller.Move(totalHorizontalMove * Time.deltaTime);
            }

            if (distanceToPlayer <= attackRange)
            {
                TryAttackPlayer();
            }
        }
        else if (separation.sqrMagnitude > 0.001f)
        {
            controller.Move(separation * Time.deltaTime);
        }

        // 3. Apply Gravity with Terminal Velocity Clamp
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
            velocity.y = Mathf.Max(velocity.y, -12f);
        }

        controller.Move(velocity * Time.deltaTime);
    }

    private Vector3 CalculateSeparationVector()
    {
        Vector3 separation = Vector3.zero;
        float radiusSq = separationRadius * separationRadius;

        for (int i = 0; i < ActiveZombies.Count; i++)
        {
            ZombieAI other = ActiveZombies[i];
            if (other == null || other == this || !other.gameObject.activeInHierarchy || !other.IsSpawned)
                continue;

            if (other.CurrentHealth.Value <= 0)
                continue;

            Vector3 diff = transform.position - other.transform.position;
            diff.y = 0f; // Push horizontally ONLY so they never push up onto each other

            float distSq = diff.sqrMagnitude;
            if (distSq < radiusSq)
            {
                if (distSq < 0.0001f)
                {
                    // If spawned on the exact same spot, pick a unique angle to split apart
                    float angle = (Mathf.Abs(GetInstanceID()) % 360) * Mathf.Deg2Rad;
                    diff = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    distSq = 0.01f;
                }

                float dist = Mathf.Sqrt(distSq);
                float strength = 1f - (dist / separationRadius);
                separation += (diff / dist) * strength;
            }
        }

        return separation;
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

            // Skip dead players (Souls) so zombies chase living teammates
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

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position + Vector3.up, separationRadius);
    }
}