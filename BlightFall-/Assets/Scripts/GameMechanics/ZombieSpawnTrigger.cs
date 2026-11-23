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
    [Tooltip("Total zombies per wave (split evenly between SpawnPoint 1 and SpawnPoint 2, e.g. 15 = 8 and 7)")]
    [SerializeField] private int hordeSize = 15;
    [Tooltip("Delay between each zombie spawning")]
    [SerializeField] private float spawnInterval = 0.18f;
    [Tooltip("Scatter radius around spawn point so they do not stack")]
    [SerializeField] private float spawnRadius = 2.2f;

    [Header("Dual Spawn Points")]
    [Tooltip("Assign SpawnPoint 1 and SpawnPoint 2 here")]
    [SerializeField] private Transform spawnPoint1;
    [SerializeField] private Transform spawnPoint2;

    [Header("Optional Reward / Clear Barrier")]
    [Tooltip("Optional: A door or barricade to destroy when all waves are defeated")]
    [SerializeField] private NetworkObject barrierToDestroyOnClear;

    private int currentWave = 0;
    private int currentAliveZombies = 0;
    private bool isEncounterActive = false;
    private bool isEncounterCleared = false;
    private bool isSpawningWave = false;

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
        if (!IsServer || isEncounterActive || isEncounterCleared) return;

        if (other.CompareTag("Player") || other.GetComponentInParent<PlayerController>() != null)
        {
            isEncounterActive = true;
            Debug.Log("<color=yellow>[Horde Encounter]</color> Encounter started! Triggering Wave 1.");
            StartCoroutine(SpawnWaveRoutine());
        }
    }

    private void HandleZombieDied()
    {
        if (!isEncounterActive || isEncounterCleared || !IsServer) return;

        currentAliveZombies = Mathf.Max(0, currentAliveZombies - 1);
        Debug.Log($"<color=orange>[Horde]</color> Zombie killed! Active zombies left: {currentAliveZombies}");

        CheckWaveProgress();
    }

    private void CheckWaveProgress()
    {
        if (isSpawningWave || isEncounterCleared) return;

        if (currentAliveZombies <= spawnNextWhenRemaining)
        {
            if (currentWave < totalWaves)
            {
                Debug.Log($"<color=yellow>[Horde]</color> Only {currentAliveZombies} left! Triggering next wave...");
                StartCoroutine(SpawnWaveRoutine());
            }
            else if (currentAliveZombies == 0)
            {
                OnEncounterCleared();
            }
        }
    }

    private IEnumerator SpawnWaveRoutine()
    {
        isSpawningWave = true;
        currentWave++;

        if (currentWave > 1 && delayBeforeNextWave > 0f)
        {
            yield return new WaitForSeconds(delayBeforeNextWave);
        }

        Transform pointA = spawnPoint1 != null ? spawnPoint1 : (spawnPoint2 != null ? spawnPoint2 : transform);
        Transform pointB = spawnPoint2 != null ? spawnPoint2 : pointA;

        int spawnedAt1 = 0;
        int spawnedAt2 = 0;

        for (int i = 0; i < hordeSize; i++)
        {
            // Alternate each zombie between SpawnPoint 1 and SpawnPoint 2 (e.g., 15 -> 8 and 7)
            bool usePoint1 = (i % 2 == 0);
            Transform chosenPoint = usePoint1 ? pointA : pointB;

            Vector2 scatter = Random.insideUnitCircle * spawnRadius;
            Vector3 rawPos = chosenPoint.position + new Vector3(scatter.x, 0f, scatter.y);
            Vector3 safeFloorPos = GetFloorPosition(rawPos);

            if (ZombiePool.Instance != null)
            {
                ZombiePool.Instance.SpawnZombie(safeFloorPos, chosenPoint.rotation);
                currentAliveZombies++;

                if (usePoint1) spawnedAt1++;
                else spawnedAt2++;
            }

            if (spawnInterval > 0f)
            {
                yield return new WaitForSeconds(spawnInterval);
            }
        }

        Debug.Log($"<color=green>[Horde]</color> Wave {currentWave}/{totalWaves} spawned: {spawnedAt1} at {pointA.name}, {spawnedAt2} at {pointB.name}.");

        isSpawningWave = false;
        CheckWaveProgress();
    }

    private Vector3 GetFloorPosition(Vector3 origin)
    {
        Vector3 rayStart = origin + Vector3.up * 3f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * 0.15f;
        }

        return origin + Vector3.up * 0.15f;
    }

    private void OnEncounterCleared()
    {
        if (isEncounterCleared) return;
        isEncounterCleared = true;
        isEncounterActive = false;

        Debug.Log("<color=green>[Horde Encounter]</color> ALL WAVES DEFEATED! Encounter Cleared!");

        if (barrierToDestroyOnClear != null && barrierToDestroyOnClear.IsSpawned)
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