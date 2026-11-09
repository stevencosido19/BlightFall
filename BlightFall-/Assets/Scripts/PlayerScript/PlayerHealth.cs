using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour, IDamageable
{
    // Static reference so the UI can easily hook onto the local player
    public static PlayerHealth LocalPlayerInstance { get; private set; }
    public static event Action<PlayerHealth> OnLocalPlayerHealthReady;

    [Header("Max Health per Role")]
    [SerializeField] private int knightHealth = 150;
    [SerializeField] private int arbalistHealth = 100;
    [SerializeField] private int rogueHealth = 80;

    // Synced health variable across the network
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int MaxHealth { get; private set; }
    public CharacterRole PlayerRole { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 1. Calculate MaxHealth for both Client and Server
        PlayerController controller = GetComponent<PlayerController>();
        if (controller != null)
        {
            PlayerRole = controller.Role;
            MaxHealth = PlayerRole switch
            {
                CharacterRole.Knight => knightHealth,
                CharacterRole.Arbalist => arbalistHealth,
                CharacterRole.Rogue => rogueHealth,
                _ => 100
            };
        }

        // 2. Server initializes starting health
        if (IsServer)
        {
            CurrentHealth.Value = MaxHealth;
        }

        // 3. If this is the local player on this device, notify the UI!
        if (IsOwner)
        {
            LocalPlayerInstance = this;
            OnLocalPlayerHealthReady?.Invoke(this);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsOwner && LocalPlayerInstance == this)
        {
            LocalPlayerInstance = null;
        }
    }

    public void TakeDamage(int damage, Vector3 knockbackForce)
    {
        if (!IsServer) return;

        CurrentHealth.Value = Mathf.Max(0, CurrentHealth.Value - damage);
        Debug.Log($"[PlayerHealth] Client {OwnerClientId} took {damage} dmg. HP: {CurrentHealth.Value}/{MaxHealth}");

        if (CurrentHealth.Value <= 0)
        {
            Debug.Log($"[PlayerHealth] Player {OwnerClientId} died!");
        }
    }
}