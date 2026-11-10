using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class StageExitZone : NetworkBehaviour
{
    [Header("Next Stage")]
    [Tooltip("Exact name of the scene to load in Build Settings")]
    [SerializeField] private string nextSceneName = "Stage2";

    // Tracks players currently inside the zone on the server
    private HashSet<ulong> playersInZone = new HashSet<ulong>();
    private bool stageCompleted = false;

    private void Awake()
    {
        // Required for CharacterController trigger detection in Unity physics
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || stageCompleted) return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null)
        {
            playersInZone.Add(player.OwnerClientId);
            CheckAllPlayersInZone();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer || stageCompleted) return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null)
        {
            playersInZone.Remove(player.OwnerClientId);
            Debug.Log($"[StageExit] Player {player.OwnerClientId} left exit zone. Count: {playersInZone.Count}/{NetworkManager.Singleton.ConnectedClients.Count}");
        }
    }

    private void CheckAllPlayersInZone()
    {
        int totalConnected = NetworkManager.Singleton.ConnectedClients.Count;

        Debug.Log($"<color=cyan>[StageExit]</color> Players in zone: {playersInZone.Count} / {totalConnected}");

        // Win condition: All connected players are standing in the zone
        if (totalConnected > 0 && playersInZone.Count >= totalConnected)
        {
            stageCompleted = true;
            Debug.Log("<color=green>[StageExit]</color> ALL PLAYERS REACHED THE EXIT! STAGE CLEAR!");

            // Tell all clients to pause and show the victory panel
            TriggerVictoryClientRpc();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void TriggerVictoryClientRpc()
    {
        // 1. Freeze movement for all players
        PlayerController[] allPlayers = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var p in allPlayers)
        {
            p.isMovementLocked = true;
        }

        // 2. Show Victory Panel
        if (VictoryUI.Instance != null)
        {
            VictoryUI.Instance.ShowVictoryPanel(nextSceneName);
        }
    }

    private void OnDrawGizmos()
    {
        // Visual indicator in Scene view
        Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
        Gizmos.DrawCube(transform.position, transform.localScale);
    }
}