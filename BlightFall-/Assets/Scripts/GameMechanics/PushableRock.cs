using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkObject))]
public class PushableRock : NetworkBehaviour
{
    [Header("Push Settings")]
    [SerializeField] private int clicksRequired = 5;          // Space taps needed to push
    [SerializeField] private float pushDistance = 2.5f;       // Distance the rock slides
    [SerializeField] private float pushSpeed = 3f;            // Slide speed
    [SerializeField] private float interactionRadius = 2.0f;  // Detection range

    private int currentClicks = 0;
    private bool isPushed = false;
    private PlayerController nearbyKnight = null; // Changed to PlayerController

    private void Update()
    {
        // Don't interact if already pushed
        if (isPushed) return;

        // Search for the local Knight player nearby
        FindLocalKnight();

        // If the Knight is close enough, listen for SPACE taps
        if (nearbyKnight != null)
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                currentClicks++;
                Debug.Log($"Knight is pushing rock: {currentClicks}/{clicksRequired}");

                if (currentClicks >= clicksRequired)
                {
                    // Push direction: away from the Knight
                    Vector3 pushDir = (transform.position - nearbyKnight.transform.position).normalized;
                    pushDir.y = 0;

                    // Tell the server to move the rock
                    PushRockServerRpc(pushDir);
                    isPushed = true;
                }
            }
        }
    }

    private void FindLocalKnight()
    {
        // Check if the Knight walked away
        if (nearbyKnight != null)
        {
            float distance = Vector3.Distance(transform.position, nearbyKnight.transform.position);
            if (distance > interactionRadius)
            {
                nearbyKnight = null;
                currentClicks = 0; // Reset taps if player walks away
            }
            return;
        }

        // Search for the local player
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayerObj != null)
            {
                PlayerController player = localPlayerObj.GetComponent<PlayerController>(); // Changed to PlayerController

                // Checks if this player is the Knight
                if (player != null && player.CanPushRocks)
                {
                    float distance = Vector3.Distance(transform.position, player.transform.position);
                    if (distance <= interactionRadius)
                    {
                        nearbyKnight = player;
                    }
                }
            }
        }
    }

    // Runs on server to move the rock authoritatively
    [Rpc(SendTo.Server)]
    private void PushRockServerRpc(Vector3 pushDirection)
    {
        Vector3 targetPos = transform.position + (pushDirection * pushDistance);
        StartCoroutine(MoveRockRoutine(targetPos));
    }

    private IEnumerator MoveRockRoutine(Vector3 targetPosition)
    {
        while (Vector3.Distance(transform.position, targetPosition) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, pushSpeed * Time.deltaTime);
            yield return null;
        }

        transform.position = targetPosition;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}