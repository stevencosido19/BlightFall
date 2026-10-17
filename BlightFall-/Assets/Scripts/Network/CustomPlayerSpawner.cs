using UnityEngine;
using Unity.Netcode;

public class CustomPlayerSpawner : MonoBehaviour
{
    [Header("Assign your 3 different characters here")]
    public GameObject[] characterPrefabs;

    [Header("Spawn Points (Optional but Recommended)")]
    public Transform[] spawnPoints;

    private void Start()
    {
        // Subscribe to the event that fires whenever a client connects
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        // Unsubscribe to prevent memory leaks
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // ONLY the server handles spawning characters
        if (!NetworkManager.Singleton.IsServer) return;

        // Pick a character based on client ID (0, 1, 2)
        int index = (int)clientId % characterPrefabs.Length;
        GameObject selectedPrefab = characterPrefabs[index];

        // Pick a spawn point (so they don't spawn inside each other)
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform sp = spawnPoints[index % spawnPoints.Length];
            spawnPos = sp.position;
            spawnRot = sp.rotation;
        }

        // 1. Instantiate the GameObject physically into the scene
        GameObject playerInstance = Instantiate(selectedPrefab, spawnPos, spawnRot);

        // 2. Spawn it on the network and assign it to the connecting player!
        playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }
}