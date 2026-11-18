using UnityEngine;

public class CameraObstacleFader : MonoBehaviour
{
    [Header("Detection Settings")]
    [Tooltip("Radius of the detection cylinder between camera and player")]
    [SerializeField] private float castRadius = 0.8f;

    [Tooltip("Select the Layer your walls are on (optional, Leave Everything for default)")]
    [SerializeField] private LayerMask obstacleLayer = ~0;

    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    private void LateUpdate()
    {
        // Finds all players in the scene (compatible with single player and your shared-screen co-op)
        PlayerController[] players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        if (players == null || players.Length == 0) return;

        Vector3 cameraPos = transform.position;

        foreach (PlayerController player in players)
        {
            if (player == null) continue;

            // Target player center (roughly waist/chest height)
            Vector3 playerCenter = player.transform.position + Vector3.up * 1.0f;
            Vector3 direction = (playerCenter - cameraPos).normalized;
            float distance = Vector3.Distance(cameraPos, playerCenter);

            // Cast a thick sphere from the camera to the player
            RaycastHit[] hits = Physics.SphereCastAll(cameraPos, castRadius, direction, distance, obstacleLayer);

            foreach (RaycastHit hit in hits)
            {
                // Ignore the player's own colliders
                if (hit.transform.root == player.transform.root) continue;

                // If hit object is a wall, tell it to fade!
                WallFader fader = hit.collider.GetComponentInParent<WallFader>();
                if (fader != null)
                {
                    fader.NotifyObstructing();
                }
            }

            // Visual debug ray in Scene view
            Debug.DrawLine(cameraPos, playerCenter, Color.cyan);
        }
    }
}