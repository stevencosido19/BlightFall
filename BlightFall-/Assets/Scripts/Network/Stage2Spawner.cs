using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
public class Stage2Spawner : NetworkBehaviour
{
    [Header("Character Prefabs")]
    [SerializeField] private GameObject knightPrefab;
    [SerializeField] private GameObject arbalistPrefab;
    [SerializeField] private GameObject roguePrefab;

    [Header("Stage 2 Spawn Points")]
    [SerializeField] private Transform[] spawnPoints;

    private bool hasSpawnedForStage = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        hasSpawnedForStage = false;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
        }

        base.OnNetworkDespawn();
    }

    private void HandleLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer || hasSpawnedForStage) return;

        hasSpawnedForStage = true;
        Debug.Log($"<color=green>[Stage2Spawner]</color> Stage '{sceneName}' finished loading for {clientsCompleted.Count} player(s) (Timed out: {clientsTimedOut.Count}). Spawning characters...");

        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        for (int i = 0; i < clientIds.Count; i++)
        {
            SpawnPlayerForStage2(clientIds[i], i);
        }
    }

    private void SpawnPlayerForStage2(ulong clientId, int spawnOrderIndex)
    {
        // 1. Clean up leftover Stage 1 player object if it survived scene transition
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient netClient))
        {
            if (netClient.PlayerObject != null && netClient.PlayerObject.IsSpawned)
            {
                netClient.PlayerObject.Despawn(true);
            }
        }

        // 2. Get the role this player picked in the Lobby
        PlayerRole assignedRole = LobbyManager.GetAssignedRole(clientId);

        GameObject prefabToSpawn = assignedRole switch
        {
            PlayerRole.Knight => knightPrefab,
            PlayerRole.Arbalist => arbalistPrefab,
            PlayerRole.Rogue => roguePrefab,
            _ => knightPrefab
        };

        if (prefabToSpawn == null)
        {
            Debug.LogError($"[Stage2Spawner] Missing prefab for role {assignedRole}!");
            return;
        }

        // 3. Pick a Stage 2 spawn point sequentially so players don't overlap
        Vector3 spawnPos = Vector3.up * 0.5f;
        Quaternion spawnRot = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int index = spawnOrderIndex % spawnPoints.Length;
            if (spawnPoints[index] != null)
            {
                spawnPos = spawnPoints[index].position;
                spawnRot = spawnPoints[index].rotation;
            }
        }

        spawnPos = GetSafeFloorPosition(spawnPos);

        // 4. Instantiate and spawn with destroyWithScene = true
        GameObject playerInstance = Instantiate(prefabToSpawn, spawnPos, spawnRot);
        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(clientId, true);

        // 5. Ensure movement is unlocked and teleport client to Stage 2 spawn point
        PlayerController controller = playerInstance.GetComponent<PlayerController>();
        if (controller != null)
        {
            controller.isMovementLocked = false;
            controller.TeleportToSpawnRpc(spawnPos);
        }

        Debug.Log($"<color=green>[Stage2Spawner]</color> Spawned {assignedRole} for Client {clientId} at {spawnPos}");
    }

    private Vector3 GetSafeFloorPosition(Vector3 origin)
    {
        Vector3 rayStart = origin + Vector3.up * 3f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 15f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * 0.25f;
        }
        return origin + Vector3.up * 0.5f;
    }
}