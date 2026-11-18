using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ZombiePool : NetworkBehaviour
{
    public static ZombiePool Instance;

    [Header("Pool Settings")]
    [SerializeField] private GameObject zombiePrefab;
    [SerializeField] private int initialPoolSize = 15;

    private Queue<NetworkObject> availableZombies = new Queue<NetworkObject>();
    private static readonly Vector3 holdingArea = new Vector3(0f, -999f, 0f);

    public override void OnNetworkSpawn()
    {
        // Set instance on both server and clients
        Instance = this;

        if (IsServer)
        {
            InitializePool();
        }
    }

    private void InitializePool()
    {
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewZombieForPool();
        }
    }

    private NetworkObject CreateNewZombieForPool()
    {
        GameObject obj = Instantiate(zombiePrefab, holdingArea, Quaternion.identity);
        NetworkObject netObj = obj.GetComponent<NetworkObject>();
        netObj.Spawn(); // Spawns network object

        SetZombieState(netObj, false, holdingArea);
        availableZombies.Enqueue(netObj);
        return netObj;
    }

    public void SpawnZombie(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        NetworkObject zombie = null;

        // Find an active pooled object that wasn't destroyed
        while (availableZombies.Count > 0)
        {
            var candidate = availableZombies.Dequeue();
            if (candidate != null && candidate.IsSpawned)
            {
                zombie = candidate;
                break;
            }
        }

        // Expand pool dynamically if we run out
        if (zombie == null)
        {
            zombie = CreateNewZombieForPool();
            availableZombies.Dequeue();
        }

        // 1. Safely position and enable on Server
        SetZombieState(zombie, true, position, rotation);

        // 2. Reset health and clear residual physics velocity
        var ai = zombie.GetComponent<ZombieAI>();
        if (ai != null)
        {
            ai.ResetForSpawn(position);
        }

        // 3. Sync position and activation to all clients
        SyncZombieStateClientRpc(zombie.NetworkObjectId, position, rotation, true);
    }

    public void ReturnToPool(NetworkObject zombie)
    {
        if (!IsServer || zombie == null) return;

        // Move to holding area and disable
        SetZombieState(zombie, false, holdingArea);

        SyncZombieStateClientRpc(zombie.NetworkObjectId, holdingArea, Quaternion.identity, false);

        availableZombies.Enqueue(zombie);
    }

    // Safely handles CharacterController teleportation so it never falls through the floor
    private void SetZombieState(NetworkObject zombie, bool isActive, Vector3 pos, Quaternion rot = default)
    {
        var cc = zombie.GetComponent<CharacterController>();

        // CRITICAL: Must disable CharacterController before moving transform
        if (cc != null) cc.enabled = false;

        zombie.transform.position = pos;
        if (rot != default) zombie.transform.rotation = rot;

        // Toggle standard colliders
        foreach (var col in zombie.GetComponentsInChildren<Collider>())
        {
            if (col is not CharacterController) col.enabled = isActive;
        }

        // Toggle visuals
        foreach (var rend in zombie.GetComponentsInChildren<SpriteRenderer>()) rend.enabled = isActive;
        foreach (var rend in zombie.GetComponentsInChildren<MeshRenderer>()) rend.enabled = isActive;

        var ai = zombie.GetComponent<ZombieAI>();
        if (ai != null) ai.enabled = isActive;

        // Re-enable CharacterController only after position is settled
        if (cc != null) cc.enabled = isActive;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncZombieStateClientRpc(ulong networkObjectId, Vector3 pos, Quaternion rot, bool isActive)
    {
        // Server already handled this locally
        if (IsServer) return;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            SetZombieState(netObj, isActive, pos, rot);
        }
    }
}