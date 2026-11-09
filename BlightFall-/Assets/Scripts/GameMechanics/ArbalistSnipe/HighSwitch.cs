using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class HighSwitch : NetworkBehaviour
{
    [Header("Target Mechanism")]
    [Tooltip("Drag the door's NetworkObject here. It will be destroyed when shot!")]
    [SerializeField] private NetworkObject doorToDestroy;

    [Header("Switch Visuals (Optional)")]
    [SerializeField] private Renderer switchRenderer;
    [SerializeField] private Color activatedColor = Color.green;

    private NetworkVariable<bool> isTriggered = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Rpc(SendTo.Server)]
    public void ShootSwitchServerRpc()
    {
        // Prevent double activation
        if (isTriggered.Value) return;

        isTriggered.Value = true;
        Debug.Log($"<color=orange>[HighSwitch]</color> {gameObject.name} was hit by an Arbalist bolt!");

        // 1. Destroy the door across the network
        if (doorToDestroy != null)
        {
            Debug.Log($"[HighSwitch] Destroying door: {doorToDestroy.gameObject.name}");
            doorToDestroy.Despawn(true); // Netcode authoritative destroy
        }

        // 2. Visual feedback on all clients
        ActivateVisualsClientRpc();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ActivateVisualsClientRpc()
    {
        if (switchRenderer != null)
        {
            switchRenderer.material.color = activatedColor;
        }

        // Disable collider so it cannot be shot again
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }
}