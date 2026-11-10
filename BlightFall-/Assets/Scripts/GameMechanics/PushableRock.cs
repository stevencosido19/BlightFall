using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(NetworkObject))]
public class PushableRock : NetworkBehaviour
{
    [Header("Push Settings")]
    [Tooltip("Number of button taps required to trigger one push slide (Set to 1 for instant push)")]
    [SerializeField] private int tapsRequired = 3;
    [SerializeField] private float pushDistance = 2.5f;       // Distance the rock slides per push
    [SerializeField] private float pushSpeed = 3.5f;          // Slide speed
    [SerializeField] private float interactionRadius = 2.5f;  // Detection range

    [Header("Mobile UI")]
    [Tooltip("Drag the on-screen 'PUSH' button from your Canvas")]
    [SerializeField] private Button mobilePushButton;

    // Synced state: prevents pushing while the rock is already sliding
    private NetworkVariable<bool> isRockSliding = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private int currentTaps = 0;
    private PlayerController nearbyKnight = null;
    private bool isButtonBound = false;

    private void Start()
    {
        // Auto-find button if not assigned in Inspector
        if (mobilePushButton == null)
        {
            GameObject btnObj = GameObject.FindWithTag("PushButton");
            if (btnObj != null)
            {
                mobilePushButton = btnObj.GetComponent<Button>();
            }
        }

        if (mobilePushButton != null)
        {
            mobilePushButton.gameObject.SetActive(false);
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        HideButton();
    }

    private void Update()
    {
        FindLocalKnight();

#if UNITY_EDITOR
        if (nearbyKnight != null && !isRockSliding.Value)
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                OnPushTapped();
            }
        }
#endif
    }

    private void FindLocalKnight()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

        var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localObj == null) return;

        PlayerController player = localObj.GetComponentInChildren<PlayerController>();
        if (player == null || !player.CanPushRocks) return;

        float distance = GetHorizontalDistance(transform.position, player.transform.position);

        // --- KNIGHT IN RANGE ---
        if (distance <= interactionRadius)
        {
            nearbyKnight = player;

            if (!isButtonBound && !isRockSliding.Value && mobilePushButton != null)
            {
                ShowButton();
            }
        }
        // --- KNIGHT WALKED AWAY ---
        else
        {
            if (nearbyKnight != null)
            {
                nearbyKnight = null;
                currentTaps = 0;
                HideButton();
            }
        }
    }

    private void ShowButton()
    {
        if (mobilePushButton == null) return;

        isButtonBound = true;
        mobilePushButton.onClick.RemoveAllListeners();
        mobilePushButton.onClick.AddListener(OnPushTapped);
        mobilePushButton.gameObject.SetActive(true);
    }

    private void HideButton()
    {
        if (mobilePushButton == null) return;

        isButtonBound = false;
        mobilePushButton.onClick.RemoveAllListeners();
        mobilePushButton.gameObject.SetActive(false);
    }

    public void OnPushTapped()
    {
        if (nearbyKnight == null || isRockSliding.Value) return;

        currentTaps++;
        Debug.Log($"<color=yellow>[PushableRock]</color> Taps: {currentTaps}/{tapsRequired}");

        if (currentTaps >= tapsRequired)
        {
            currentTaps = 0;

            Vector3 pushDir = (transform.position - nearbyKnight.transform.position).normalized;
            pushDir.y = 0f;

            HideButton();

            // Ask server to execute the push
            PushRockServerRpc(pushDir.normalized);
        }
    }

    // 1. Server authoritatively calculates the target position
    [Rpc(SendTo.Server)]
    public void PushRockServerRpc(Vector3 pushDirection)
    {
        if (isRockSliding.Value) return;

        isRockSliding.Value = true;
        Vector3 targetPos = transform.position + (pushDirection * pushDistance);

        // 2. Broadcast to ALL clients and host to slide the rock visually!
        StartSlideClientRpc(targetPos);
    }

    // 3. Runs on ALL players' devices so the rock moves on everyone's screen
    [Rpc(SendTo.ClientsAndHost)]
    private void StartSlideClientRpc(Vector3 targetPosition)
    {
        StartCoroutine(MoveRockRoutine(targetPosition));
    }

    private IEnumerator MoveRockRoutine(Vector3 targetPosition)
    {
        while (Vector3.Distance(transform.position, targetPosition) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, pushSpeed * Time.deltaTime);
            yield return null;
        }

        transform.position = targetPosition;

        // Only the server resets the network state
        if (IsServer)
        {
            isRockSliding.Value = false;
        }
    }

    private float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}