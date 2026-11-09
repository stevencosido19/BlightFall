using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public enum LockableType
{
    Chest,
    Door
}

[RequireComponent(typeof(NetworkObject))]
public class LockedChestOrDoor : NetworkBehaviour
{
    [Header("Lock Settings")]
    [Tooltip("Choose whether this object is a Chest or a Door")]
    [SerializeField] private LockableType lockType = LockableType.Door;
    [SerializeField] private float interactionRadius = 3.5f;

    [Header("Mobile Interaction Button")]
    [Tooltip("Assign the on-screen 'Pick Lock' button from your Canvas")]
    [SerializeField] private Button mobileInteractButton;

    // Network variable to sync unlocked status across all connected players
    private NetworkVariable<bool> isLocked = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private PlayerController nearbyRogue = null;

    private void Start()
    {
        if (mobileInteractButton == null)
        {
            Debug.LogError($"[LockedChestOrDoor] 'Mobile Interact Button' is NOT assigned on {gameObject.name} in the Inspector!", this);
            return;
        }

        // Ensure the button starts hidden (DO NOT bind listeners here)
        mobileInteractButton.gameObject.SetActive(false);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();

        if (nearbyRogue != null && mobileInteractButton != null)
        {
            mobileInteractButton.onClick.RemoveAllListeners();
            mobileInteractButton.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        // Stop checking if this object is already unlocked
        if (!isLocked.Value) return;

        FindLocalRogue();
    }

    private void FindLocalRogue()
    {
        // 1. If Rogue walked away from THIS object, release the button
        if (nearbyRogue != null)
        {
            float dist = GetHorizontalDistance(transform.position, nearbyRogue.transform.position);
            if (dist > interactionRadius)
            {
                nearbyRogue = null;
                if (mobileInteractButton != null)
                {
                    mobileInteractButton.onClick.RemoveAllListeners();
                    mobileInteractButton.gameObject.SetActive(false);
                }
                if (LockpickMinigame.Instance != null) LockpickMinigame.Instance.CloseMinigame();
            }
            return;
        }

        // 2. Locate the local player
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

        var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localObj == null) return;

        PlayerController player = localObj.GetComponentInChildren<PlayerController>();
        if (player == null) return;

        // 3. Verify if player is allowed to pick locks
        if (!player.CanPickLocks) return;

        // 4. Check distance
        float horizontalDistance = GetHorizontalDistance(transform.position, player.transform.position);

        if (horizontalDistance <= interactionRadius)
        {
            nearbyRogue = player;

            if (mobileInteractButton != null)
            {
                // CRUCIAL: Bind the button specifically to THIS object (Chest or Door)
                mobileInteractButton.onClick.RemoveAllListeners();
                mobileInteractButton.onClick.AddListener(OnMobileButtonPressed);
                mobileInteractButton.gameObject.SetActive(true);

                Debug.Log($"<color=cyan>[Lockpick]</color> Button re-bound to {gameObject.name} ({lockType})");
            }
        }
    }

    private float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0;
        b.y = 0;
        return Vector3.Distance(a, b);
    }

    private void OnMobileButtonPressed()
    {
        if (nearbyRogue != null && isLocked.Value)
        {
            if (LockpickMinigame.Instance != null)
            {
                LockpickMinigame.Instance.StartLockpicking(OnMinigameSuccess);
            }
            else
            {
                Debug.LogError("[LockedChestOrDoor] LockpickMinigame.Instance is null! Make sure LockpickMinigame script is active in the scene.");
            }
        }
    }

    private void OnMinigameSuccess()
    {
        nearbyRogue = null;

        // Release the button so another door/chest can use it
        if (mobileInteractButton != null)
        {
            mobileInteractButton.onClick.RemoveAllListeners();
            mobileInteractButton.gameObject.SetActive(false);
        }

        UnlockServerRpc();
    }

    [Rpc(SendTo.Server)]
    private void UnlockServerRpc()
    {
        isLocked.Value = false;

        if (lockType == LockableType.Door)
        {
            Debug.Log($"[LockedChestOrDoor] Door picked! Despawning {gameObject.name}.");
            GetComponent<NetworkObject>().Despawn(true);
        }
        else // Chest
        {
            Debug.Log($"[LockedChestOrDoor] Chest picked! Unlocking {gameObject.name}.");
            OpenChestClientRpc();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void OpenChestClientRpc()
    {
        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null) renderer.material.color = Color.green;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = (lockType == LockableType.Door) ? Color.cyan : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}