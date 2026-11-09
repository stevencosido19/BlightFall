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

    // Synced Health across the network
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        60,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Tracks which way the zombie is moving without rotating its GameObject
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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;
        }
    }

    // Called on the Server when hit by the Knight or Rogue
    public void TakeDamage(int damage, Vector3 knockbackForce)
    {
        if (!IsServer) return;

        CurrentHealth.Value -= damage;
        Debug.Log($"<color=red>[ZombieAI]</color> Took {damage} dmg! HP: {CurrentHealth.Value}/{maxHealth}");

        // Apply knockback force
        knockbackVelocity = knockbackForce;

        // Check if dead
        if (CurrentHealth.Value <= 0)
        {
            Debug.Log("<color=red>[ZombieAI]</color> Zombie died! Despawning.");
            GetComponent<NetworkObject>().Despawn(true); // Netcode authoritative destroy
        }
    }

    private void Update()
    {
        // AI logic runs exclusively on the SERVER
        if (!IsServer) return;

        // 1. Periodically update target (every 0.5s) to find the closest player
        searchTimer -= Time.deltaTime;
        if (searchTimer <= 0f)
        {
            FindClosestPlayer();
            searchTimer = 0.5f;
        }

        // 2. Handle Knockback (Pushes the zombie back without rotating it)
        if (knockbackVelocity.sqrMagnitude > 0.1f)
        {
            controller.Move(knockbackVelocity * Time.deltaTime);
            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, Time.deltaTime * 6f);
        }
        else if (targetPlayer != null)
        {
            // 3. Calculate direction towards the closest player
            Vector3 direction = (targetPlayer.position - transform.position);
            direction.y = 0f;

            float distanceToPlayer = direction.magnitude;

            if (distanceToPlayer > 0.1f)
            {
                Vector3 moveDir = direction.normalized;

                // Remember direction WITHOUT rotating transform.rotation
                FacingDirection = moveDir;

                // Stop walking if already within attack range
                if (distanceToPlayer > attackRange * 0.8f)
                {
                    controller.Move(moveDir * moveSpeed * Time.deltaTime);
                }
            }

            // 4. Attack player when in range
            if (distanceToPlayer <= attackRange)
            {
                TryAttackPlayer();
            }
        }

        // 5. Apply Gravity
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        controller.Move(velocity * Time.deltaTime);
    }

    private void TryAttackPlayer()
    {
        if (Time.time < lastAttackTime + attackCooldown) return;

        lastAttackTime = Time.time;

        IDamageable player = targetPlayer.GetComponentInParent<IDamageable>();
        if (player != null)
        {
            // Push player slightly back on hit
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