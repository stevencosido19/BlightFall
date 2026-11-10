using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public enum LockableType
{
    Chest,
    Door,       // Standard lockpick minigame door
    KeyDoor     // Opens automatically when Rogue is near with matching Key ID
}

[RequireComponent(typeof(NetworkObject))]
public class LockedChestOrDoor : NetworkBehaviour
{
    [Header("Lock Settings")]
    [Tooltip("Choose Chest, standard Door, or KeyDoor")]
    [SerializeField] private LockableType lockType = LockableType.Door;
    [SerializeField] private float interactionRadius = 4.5f; // Generous radius so colliders don't block it

    [Header("Key Mechanics")]
    [Tooltip("If this is a Chest, does it award a key?")]
    [SerializeField] private bool givesKey = false;
    [Tooltip("Key ID given by this chest OR Key ID needed to open this door")]
    [SerializeField] private int keyId = 1;

    [Header("Mobile Interaction Button")]
    [Tooltip("Assign the on-screen 'Pick Lock' button from your Canvas")]
    [SerializeField] private Button mobileInteractButton;

    private NetworkVariable<bool> isLocked = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Prevents race conditions while waiting for server response
    private bool isUnlockedLocal = false;
    private bool isButtonVisible = false;

    private void Start()
    {
        if (mobileInteractButton != null)
        {
            mobileInteractButton.gameObject.SetActive(false);
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        HideButton();
    }

    private void Update()
    {
        // If already unlocked locally or on server, do nothing
        if (!isLocked.Value || isUnlockedLocal) return;

        CheckLocalRogueInteraction();
    }

    private void CheckLocalRogueInteraction()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

        var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localObj == null) return;

        PlayerController player = localObj.GetComponentInChildren<PlayerController>();
        if (player == null || !player.CanPickLocks) return;

        float distance = GetHorizontalDistance(transform.position, player.transform.position);

        // --- ROGUE IS IN RANGE ---
        if (distance <= interactionRadius)
        {
            // CASE 1: KEY DOOR (Automatic unlock with key)
            if (lockType == LockableType.KeyDoor)
            {
                if (player.CurrentKeyId.Value == keyId)
                {
                    isUnlockedLocal = true; // Prevents calling multiple RPCs
                    Debug.Log($"<color=green>[KeyDoor]</color> Rogue has Key #{keyId}! Destroying door...");
                    OpenKeyDoorServerRpc(NetworkManager.Singleton.LocalClientId);
                }
                return;
            }

            // CASE 2: CHEST OR PICKABLE DOOR (Show Minigame button)
            if (!isButtonVisible && mobileInteractButton != null)
            {
                ShowButton();
            }
        }
        // --- ROGUE WALKED AWAY ---
        else
        {
            if (isButtonVisible)
            {
                HideButton();
                if (LockpickMinigame.Instance != null)
                    LockpickMinigame.Instance.CloseMinigame();
            }
        }
    }

    private void ShowButton()
    {
        if (mobileInteractButton == null) return;

        isButtonVisible = true;
        mobileInteractButton.onClick.RemoveAllListeners();
        mobileInteractButton.onClick.AddListener(OnMobileButtonPressed);
        mobileInteractButton.gameObject.SetActive(true);
    }

    private void HideButton()
    {
        if (mobileInteractButton == null) return;

        isButtonVisible = false;
        mobileInteractButton.onClick.RemoveAllListeners();
        mobileInteractButton.gameObject.SetActive(false);
    }

    private void OnMobileButtonPressed()
    {
        if (isLocked.Value && !isUnlockedLocal)
        {
            if (LockpickMinigame.Instance != null)
            {
                LockpickMinigame.Instance.StartLockpicking(OnMinigameSuccess);
            }
        }
    }

    private void OnMinigameSuccess()
    {
        // 1. Immediately kill the button locally so it CANNOT stay on screen
        isUnlockedLocal = true;
        HideButton();

        // 2. Notify the server who unlocked it
        ulong localId = NetworkManager.Singleton.LocalClientId;
        UnlockServerRpc(localId);
    }

    [Rpc(SendTo.Server)]
    public void UnlockServerRpc(ulong interactorClientId)
    {
        isLocked.Value = false;

        if (lockType == LockableType.Door)
        {
            GetComponent<NetworkObject>().Despawn(true);
        }
        else // Chest
        {
            OpenChestClientRpc();

            // Give the key to the Rogue on the server
            if (givesKey)
            {
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(interactorClientId, out var client))
                {
                    var player = client.PlayerObject.GetComponentInChildren<PlayerController>();
                    if (player != null)
                    {
                        player.CurrentKeyId.Value = keyId;
                        Debug.Log($"<color=yellow>[Chest]</color> Server awarded Key #{keyId} to Client {interactorClientId}!");
                    }
                }
            }
        }
    }

    [Rpc(SendTo.Server)]
    public void OpenKeyDoorServerRpc(ulong interactorClientId)
    {
        isLocked.Value = false;

        // Consume the key from the player's inventory
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(interactorClientId, out var client))
        {
            var player = client.PlayerObject.GetComponentInChildren<PlayerController>();
            if (player != null)
            {
                player.CurrentKeyId.Value = 0; // Key consumed!
            }
        }

        Debug.Log($"<color=green>[KeyDoor]</color> Door #{keyId} destroyed with key!");
        GetComponent<NetworkObject>().Despawn(true); // Netcode authoritative destroy
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OpenChestClientRpc()
    {
        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null) renderer.material.color = Color.green;
    }

    private float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0;
        b.y = 0;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = (lockType == LockableType.Chest) ? Color.yellow : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}