using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
public class GameSpawner : NetworkBehaviour
{
    [Header("Character Prefabs")]
    public GameObject knightPrefab;
    public GameObject arbalistPrefab;
    public GameObject roguePrefab;

    [Header("Spawn Points")]
    public Transform[] spawnPoints;

    private bool hasSpawnedForScene = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        hasSpawnedForScene = false;

        // Wait until ALL clients finish loading this scene before spawning players
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
        if (!IsServer || hasSpawnedForScene) return;

        hasSpawnedForScene = true;
        Debug.Log($"<color=green>[GameSpawner]</color> Scene '{sceneName}' loaded by {clientsCompleted.Count} player(s). TimedOut: {clientsTimedOut.Count}.");

        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        for (int i = 0; i < clientIds.Count; i++)
        {
            SpawnPlayerCharacter(clientIds[i], i);
        }
    }

    private void SpawnPlayerCharacter(ulong clientId, int spawnOrderIndex)
    {
        // Safety cleanup in case an old player object somehow survived
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient netClient))
        {
            if (netClient.PlayerObject != null && netClient.PlayerObject.IsSpawned)
            {
                netClient.PlayerObject.Despawn(true);
            }
        }

        // 1. Ask LobbyManager what role this player picked
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
            Debug.LogError($"[GameSpawner] Missing prefab for role {assignedRole}!");
            return;
        }

        // 2. Pick a sequential spawn point so Client 0 and Client 2 never overlap
        Vector3 spawnPos = Vector3.up * 0.5f;
        Quaternion spawnRot = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int spawnIndex = spawnOrderIndex % spawnPoints.Length;
            if (spawnPoints[spawnIndex] != null)
            {
                spawnPos = spawnPoints[spawnIndex].position;
                spawnRot = spawnPoints[spawnIndex].rotation;
            }
        }

        // Snap safely above the floor collider so no one clips through thin MeshColliders
        spawnPos = GetSafeFloorPosition(spawnPos);

        // 3. Instantiate & spawn with destroyWithScene = true
        GameObject playerInstance = Instantiate(prefabToSpawn, spawnPos, spawnRot);
        playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

        // 4. Unlock movement and teleport owner to spawn position
        PlayerController controller = playerInstance.GetComponent<PlayerController>();
        if (controller != null)
        {
            controller.isMovementLocked = false;
            controller.TeleportToSpawnRpc(spawnPos);
        }

        Debug.Log($"<color=green>[GameSpawner]</color> Spawned {assignedRole} for Client {clientId} at {spawnPos}");
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