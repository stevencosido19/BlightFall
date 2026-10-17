using Unity.Netcode;
using UnityEngine;

public class GameSpawner : NetworkBehaviour
{
    [Header("Character Prefabs")]
    public GameObject knightPrefab;
    public GameObject arbalistPrefab;
    public GameObject roguePrefab;

    [Header("Spawn Points (Optional)")]
    public Transform[] spawnPoints;

    public override void OnNetworkSpawn()
    {
        // ONLY the server has authority to spawn characters
        if (!IsServer) return;

        // Loop through all players who made it from the lobby to the game
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SpawnPlayerCharacter(clientId);
        }
    }

    private void SpawnPlayerCharacter(ulong clientId)
    {
        // 1. Ask the LobbyManager what role this player picked
        PlayerRole assignedRole = LobbyManager.GetAssignedRole(clientId);

        // 2. Pick the correct prefab based on the role
        GameObject prefabToSpawn = null;

        switch (assignedRole)
        {
            case PlayerRole.Knight:
                prefabToSpawn = knightPrefab;
                break;
            case PlayerRole.Arbalist:
                prefabToSpawn = arbalistPrefab;
                break;
            case PlayerRole.Rogue:
                prefabToSpawn = roguePrefab;
                break;
            default:
                Debug.LogWarning($"Player {clientId} didn't have a role! Defaulting to Knight.");
                prefabToSpawn = knightPrefab; // Fallback
                break;
        }

        if (prefabToSpawn == null) return;

        // 3. Pick a spawn location (so they don't spawn inside each other)
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            // Pick a spawn point based on client ID to spread them out
            int spawnIndex = (int)clientId % spawnPoints.Length;
            spawnPos = spawnPoints[spawnIndex].position;
            spawnRot = spawnPoints[spawnIndex].rotation;
        }

        // 4. Instantiate the character in the server's world
        GameObject playerInstance = Instantiate(prefabToSpawn, spawnPos, spawnRot);

        // 5. Spawn the character over the network and give ownership to the client
        playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

        // 6. Force the client to teleport to the spawn position immediately!
        // (This calls the RPC we added in your CharacterController script)
        playerInstance.GetComponent<CharacterController>().TeleportToSpawnRpc(spawnPos);
    }
}