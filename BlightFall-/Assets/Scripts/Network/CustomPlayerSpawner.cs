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
        
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        
        if (!NetworkManager.Singleton.IsServer) return;

       
        int index = (int)clientId % characterPrefabs.Length;
        GameObject selectedPrefab = characterPrefabs[index];

        
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform sp = spawnPoints[index % spawnPoints.Length];
            spawnPos = sp.position;
            spawnRot = sp.rotation;
        }

        
        GameObject playerInstance = Instantiate(selectedPrefab, spawnPos, spawnRot);

        
        playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }
}