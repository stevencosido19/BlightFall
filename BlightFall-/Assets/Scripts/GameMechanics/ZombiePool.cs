using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
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
        base.OnNetworkSpawn();

        // Set instance on both server and clients for THIS scene
        Instance = this;

        if (IsServer)
        {
            InitializePool();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (Instance == this)
        {
            Instance = null;
        }

        availableZombies.Clear();
    }

    private void InitializePool()
    {
        availableZombies.Clear();

        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewZombieForPool();
        }
    }

    private NetworkObject CreateNewZombieForPool()
    {
        GameObject obj = Instantiate(zombiePrefab, holdingArea, Quaternion.identity);
        NetworkObject netObj = obj.GetComponent<NetworkObject>();

        // destroyWithScene = true ensures Stage 1's pooled zombies clean up when loading Stage 2!
        netObj.Spawn(true);

        SetZombieState(netObj, false, holdingArea);
        availableZombies.Enqueue(netObj);
        return netObj;
    }

    public void SpawnZombie(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        if (!IsSpawned)
        {
            Debug.LogError("[ZombiePool] SpawnZombie failed because ZombiePool is NOT spawned! Make sure Stage 2 has its own ZombiePool with a NetworkObject (and is NOT inside a DontDestroyOnLoad object).");
            return;
        }

        NetworkObject zombie = null;

        while (availableZombies.Count > 0)
        {
            var candidate = availableZombies.Dequeue();
            if (candidate != null && candidate.IsSpawned)
            {
                zombie = candidate;
                break;
            }
        }

        if (zombie == null)
        {
            zombie = CreateNewZombieForPool();
            availableZombies.Dequeue();
        }

        SetZombieState(zombie, true, position, rotation);

        var ai = zombie.GetComponent<ZombieAI>();
        if (ai != null)
        {
            ai.ResetForSpawn(position);
        }

        SyncZombieStateClientRpc(zombie.NetworkObjectId, position, rotation, true);
    }

    public void ReturnToPool(NetworkObject zombie)
    {
        if (!IsServer || !IsSpawned || zombie == null || !zombie.IsSpawned) return;

        SetZombieState(zombie, false, holdingArea);
        SyncZombieStateClientRpc(zombie.NetworkObjectId, holdingArea, Quaternion.identity, false);

        availableZombies.Enqueue(zombie);
    }

    private void SetZombieState(NetworkObject zombie, bool isActive, Vector3 pos, Quaternion rot = default)
    {
        var cc = zombie.GetComponent<CharacterController>();

        if (cc != null) cc.enabled = false;

        zombie.transform.position = pos;
        if (rot != default) zombie.transform.rotation = rot;

        foreach (var col in zombie.GetComponentsInChildren<Collider>())
        {
            if (col is not CharacterController) col.enabled = isActive;
        }

        foreach (var rend in zombie.GetComponentsInChildren<SpriteRenderer>()) rend.enabled = isActive;
        foreach (var rend in zombie.GetComponentsInChildren<MeshRenderer>()) rend.enabled = isActive;

        var ai = zombie.GetComponent<ZombieAI>();
        if (ai != null) ai.enabled = isActive;

        if (cc != null) cc.enabled = isActive;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncZombieStateClientRpc(ulong networkObjectId, Vector3 pos, Quaternion rot, bool isActive)
    {
        if (IsServer) return;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            SetZombieState(netObj, isActive, pos, rot);
        }
    }
}