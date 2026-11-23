using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
public class ZombiePool : NetworkBehaviour
{
    public static ZombiePool Instance;

    [Header("Pool Settings")]
    [SerializeField] private GameObject zombiePrefab;
    [SerializeField] private int initialPoolSize = 15;
    [Tooltip("How many pooled zombies to create per frame during startup (prevents packet queue overflow)")]
    [SerializeField] private int prewarmPerFrame = 1;
    [Tooltip("Delay after scene load so players finish spawning before zombies prewarm")]
    [SerializeField] private float prewarmStartDelay = 0.5f;

    private Queue<NetworkObject> availableZombies = new Queue<NetworkObject>();
    private static readonly Vector3 holdingArea = new Vector3(0f, -999f, 0f);
    private bool hasStartedPoolInit = false;
    private Coroutine initRoutine;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;

        if (!IsServer) return;

        hasStartedPoolInit = false;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (initRoutine != null)
        {
            StopCoroutine(initRoutine);
            initRoutine = null;
        }

        if (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
        }

        if (Instance == this)
        {
            Instance = null;
        }

        availableZombies.Clear();
        base.OnNetworkDespawn();
    }

    private void HandleLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer || hasStartedPoolInit) return;
        hasStartedPoolInit = true;
        initRoutine = StartCoroutine(InitializePoolRoutine());
    }

    private IEnumerator InitializePoolRoutine()
    {
        if (prewarmStartDelay > 0f)
        {
            yield return new WaitForSeconds(prewarmStartDelay);
        }

        availableZombies.Clear();

        for (int i = 0; i < initialPoolSize; i++)
        {
            if (!IsSpawned) yield break;

            CreateNewZombieForPool();

            if ((i + 1) % Mathf.Max(1, prewarmPerFrame) == 0)
            {
                yield return null; // Spread network spawns across frames
            }
        }

        initRoutine = null;
    }

    private NetworkObject CreateNewZombieForPool()
    {
        GameObject obj = Instantiate(zombiePrefab, holdingArea, Quaternion.identity);

        var agent = obj.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        NetworkObject netObj = obj.GetComponent<NetworkObject>();
        netObj.Spawn(true);

        var ai = obj.GetComponent<ZombieAI>();
        if (ai != null)
        {
            ai.ApplyPoolVisuals(false);
        }

        availableZombies.Enqueue(netObj);
        return netObj;
    }

    public void SpawnZombie(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        if (!IsSpawned)
        {
            Debug.LogError("[ZombiePool] SpawnZombie failed because ZombiePool is NOT spawned!");
            return;
        }

        if (!hasStartedPoolInit)
        {
            hasStartedPoolInit = true;
            initRoutine = StartCoroutine(InitializePoolRoutine());
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

        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            position = hit.position;
        }

        var ai = zombie.GetComponent<ZombieAI>();
        if (ai != null)
        {
            ai.SafeSpawnReset(position);
            ai.ApplyPoolVisuals(true);
        }

        TeleportZombie(zombie, position, rotation);
        SyncZombieStateClientRpc(zombie.NetworkObjectId, position, rotation, true);
    }

    public void ReturnToPool(NetworkObject zombie)
    {
        if (!IsServer || !IsSpawned || zombie == null || !zombie.IsSpawned) return;

        SetZombieInactive(zombie);
        SyncZombieStateClientRpc(zombie.NetworkObjectId, holdingArea, Quaternion.identity, false);
        availableZombies.Enqueue(zombie);
    }

    private void SetZombieInactive(NetworkObject zombie)
    {
        var agent = zombie.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        var ai = zombie.GetComponent<ZombieAI>();
        if (ai != null)
        {
            ai.IsActiveInPool.Value = false;
            ai.ApplyPoolVisuals(false);
        }

        TeleportZombie(zombie, holdingArea, Quaternion.identity);
    }

    private void TeleportZombie(NetworkObject zombie, Vector3 pos, Quaternion rot)
    {
        var netTransform = zombie.GetComponent<NetworkTransform>();
        if (netTransform != null && netTransform.CanCommitToTransform)
        {
            netTransform.Teleport(pos, rot, zombie.transform.localScale);
        }
        else
        {
            zombie.transform.SetPositionAndRotation(pos, rot);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncZombieStateClientRpc(ulong networkObjectId, Vector3 pos, Quaternion rot, bool isActive)
    {
        if (IsServer) return;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            netObj.transform.SetPositionAndRotation(pos, rot);

            var ai = netObj.GetComponent<ZombieAI>();
            if (ai != null)
            {
                ai.ApplyPoolVisuals(isActive);
            }
        }
        else
        {
            Debug.LogWarning($"[ZombieClient] SyncZombieStateClientRpc could NOT find Zombie ID={networkObjectId} (isActive={isActive})!");
        }
    }
}