using Unity.Netcode;
using UnityEngine;

public class ZombieSpawnTrigger : NetworkBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private int zombiesToSpawn = 3;

    private bool hasTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        // Only the server processes trigger activations
        if (!IsServer || hasTriggered) return;

        if (other.CompareTag("Player"))
        {
            SpawnWaveServer();
            hasTriggered = true;
        }
    }

    private void SpawnWaveServer()
    {
        for (int i = 0; i < zombiesToSpawn; i++)
        {
            Transform spawnPoint = (spawnPoints != null && spawnPoints.Length > 0)
                ? spawnPoints[Random.Range(0, spawnPoints.Length)]
                : transform;

            ZombiePool.Instance.SpawnZombie(spawnPoint.position, spawnPoint.rotation);
        }

        Debug.Log($"Server spawned {zombiesToSpawn} networked zombies from pool!");
    }
}