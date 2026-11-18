using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ZombieSpawnTrigger : NetworkBehaviour
{
    [Header("Wave Settings")]
    [Tooltip("Total number of waves to spawn (e.g. 3, 5, or more)")]
    [SerializeField] private int totalWaves = 4;
    [Tooltip("Spawn the next wave as soon as alive zombies drop to this number")]
    [SerializeField] private int spawnNextWhenRemaining = 2;
    [Tooltip("Small delay before the next wave starts pouring in")]
    [SerializeField] private float delayBeforeNextWave = 1.0f;

    [Header("Horde Settings")]
    [Tooltip("Zombies per wave")]
    [SerializeField] private int hordeSize = 10;
    [Tooltip("Delay between each zombie spawning")]
    [SerializeField] private float spawnInterval = 0.18f;
    [Tooltip("Scatter radius around spawn point so they do not stack")]
    [SerializeField] private float spawnRadius = 2.2f;

    [Header("Alternating Spawn Points")]
    [Tooltip("Assign SpawnPoint 1 and SpawnPoint 2 here")]
    [SerializeField] private Transform spawnPoint1;
    [SerializeField] private Transform spawnPoint2;

    [Header("Optional Reward / Clear Barrier")]
    [Tooltip("Optional: A door or barricade to destroy when all waves are defeated")]
    [SerializeField] private NetworkObject barrierToDestroyOnClear;

    private int currentWave = 0;
    private int currentAliveZombies = 0;
    private bool isEncounterActive = false;
    private bool isSpawningWave = false;
    private bool currentSpawnToggle = false; // false = Point 1, true = Point 2

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            ZombieAI.OnZombieDiedServer += HandleZombieDied;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer)
        {
            ZombieAI.OnZombieDiedServer -= HandleZombieDied;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || isEncounterActive) return;

        if (other.CompareTag("Player") || other.GetComponentInParent<PlayerController>() != null)
        {
            isEncounterActive = true;
            Debug.Log("<color=yellow>[Horde Encounter]</color> Encounter started! Triggering Wave 1.");
            StartCoroutine(SpawnWaveRoutine());
        }
    }

    private void HandleZombieDied()
    {
        if (!isEncounterActive || !IsServer) return;

        currentAliveZombies = Mathf.Max(0, currentAliveZombies - 1);
        Debug.Log($"<color=orange>[Horde]</color> Zombie killed! Active zombies left: {currentAliveZombies}");

        // Check if only 2 zombies remain to trigger the next wave
        if (!isSpawningWave && currentAliveZombies <= spawnNextWhenRemaining)
        {
            if (currentWave < totalWaves)
            {
                Debug.Log($"<color=yellow>[Horde]</color> Only {currentAliveZombies} left! Triggering next wave...");
                StartCoroutine(SpawnWaveRoutine());
            }
            else if (currentAliveZombies == 0)
            {
                // All waves and all zombies defeated!
                OnEncounterCleared();
            }
        }
    }

    private IEnumerator SpawnWaveRoutine()
    {
        isSpawningWave = true;
        currentWave++;

        // Brief delay before wave starts
        if (currentWave > 1 && delayBeforeNextWave > 0f)
        {
            yield return new WaitForSeconds(delayBeforeNextWave);
        }

        // Alternates: Wave 1 = Point 1, Wave 2 = Point 2, Wave 3 = Point 1, etc.
        Transform activeSpawnPoint = currentSpawnToggle ? spawnPoint2 : spawnPoint1;
        currentSpawnToggle = !currentSpawnToggle; // Toggle for next wave

        // Fallback if one point is unassigned
        if (activeSpawnPoint == null) activeSpawnPoint = transform;

        Debug.Log($"<color=green>[Horde]</color> Wave {currentWave}/{totalWaves} spawning at {activeSpawnPoint.name}!");

        for (int i = 0; i < hordeSize; i++)
        {
            // Scatter so they don't stack on each other
            Vector2 scatter = Random.insideUnitCircle * spawnRadius;
            Vector3 rawPos = activeSpawnPoint.position + new Vector3(scatter.x, 0f, scatter.y);
            Vector3 safeFloorPos = GetFloorPosition(rawPos);

            if (ZombiePool.Instance != null)
            {
                ZombiePool.Instance.SpawnZombie(safeFloorPos, activeSpawnPoint.rotation);
                currentAliveZombies++;
            }

            if (spawnInterval > 0f)
            {
                yield return new WaitForSeconds(spawnInterval);
            }
        }

        isSpawningWave = false;
    }

    private Vector3 GetFloorPosition(Vector3 origin)
    {
        Vector3 rayStart = origin + Vector3.up * 3f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 10f))
        {
            return hit.point + Vector3.up * 0.15f;
        }

        return origin + Vector3.up * 0.15f;
    }

    private void OnEncounterCleared()
    {
        Debug.Log("<color=green>[Horde Encounter]</color> ALL WAVES DEFEATED! Encounter Cleared!");

        if (barrierToDestroyOnClear != null)
        {
            barrierToDestroyOnClear.Despawn(true);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (spawnPoint1 != null) Gizmos.DrawWireSphere(spawnPoint1.position, spawnRadius);
        Gizmos.color = Color.magenta;
        if (spawnPoint2 != null) Gizmos.DrawWireSphere(spawnPoint2.position, spawnRadius);
    }
}