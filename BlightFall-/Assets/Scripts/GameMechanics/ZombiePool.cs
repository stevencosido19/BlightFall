using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ZombiePool : NetworkBehaviour
{
    public static ZombiePool Instance;

    [Header("Pool Settings")]
    [SerializeField] private GameObject zombiePrefab;
    [SerializeField] private int initialPoolSize = 10;

    private Queue<NetworkObject> availableZombies = new Queue<NetworkObject>();
    private Vector3 holdingArea = new Vector3(0f, -999f, 0f);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Instance = this;
            InitializePool();
        }
    }

    private void InitializePool()
    {
        for (int i = 0; i < initialPoolSize; i++)
        {
            GameObject obj = Instantiate(zombiePrefab, holdingArea, Quaternion.identity);
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            netObj.Spawn(); // Spawns the network object across all clients
            SetZombieActiveState(netObj, false);
            availableZombies.Enqueue(netObj);
        }
    }

    public void SpawnZombie(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        NetworkObject zombie;
        if (availableZombies.Count > 0)
        {
            zombie = availableZombies.Dequeue();
        }
        else
        {
            // Dynamic expansion if pool runs out
            GameObject obj = Instantiate(zombiePrefab, holdingArea, Quaternion.identity);
            zombie = obj.GetComponent<NetworkObject>();
            zombie.Spawn();
        }

        // Reposition and activate across network
        zombie.transform.position = position;
        zombie.transform.rotation = rotation;
        SetZombieActiveState(zombie, true);

        SyncPositionClientRpc(zombie.NetworkObjectId, position, true);
    }

    public void ReturnToPool(NetworkObject zombie)
    {
        if (!IsServer) return;

        zombie.transform.position = holdingArea;
        SetZombieActiveState(zombie, false);

        SyncPositionClientRpc(zombie.NetworkObjectId, holdingArea, false);

        availableZombies.Enqueue(zombie);
    }

    private void SetZombieActiveState(NetworkObject zombie, bool isActive)
    {
        foreach (var col in zombie.GetComponentsInChildren<Collider>()) col.enabled = isActive;
        foreach (var rend in zombie.GetComponentsInChildren<SpriteRenderer>()) rend.enabled = isActive;

        var ai = zombie.GetComponent<ZombieAI>();
        if (ai != null) ai.enabled = isActive;
    }

    [ClientRpc]
    private void SyncPositionClientRpc(ulong networkObjectId, Vector3 pos, bool isActive)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            netObj.transform.position = pos;

            foreach (var col in netObj.GetComponentsInChildren<Collider>()) col.enabled = isActive;
            foreach (var rend in netObj.GetComponentsInChildren<SpriteRenderer>()) rend.enabled = isActive;

            var ai = netObj.GetComponent<ZombieAI>();
            if (ai != null) ai.enabled = isActive;
        }
    }
}