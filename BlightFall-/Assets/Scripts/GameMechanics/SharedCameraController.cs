using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SharedCameraController : MonoBehaviour
{
    [Header("Camera Positioning")]
    [Tooltip("The offset of the camera relative to the players (e.g., for a Top-Down view)")]
    public Vector3 offset = new Vector3(0f, 15f, -15f);
    [Tooltip("How smoothly the camera catches up to the players")]
    public float smoothTime = 0.3f;

    [Header("Dynamic Zoom (Keeps everyone on screen)")]
    public bool enableZoom = true;
    [Tooltip("The FOV when players are standing close together")]
    public float minFOV = 40f;
    [Tooltip("The max FOV when players run far away from each other")]
    public float maxFOV = 75f;
    [Tooltip("The maximum distance players can be apart before the camera stops zooming out")]
    public float maxPlayerDistance = 20f;

    private Vector3 velocity;
    private Camera cam;

    // Cached array of PlayerController instead of CharacterController
    private PlayerController[] activePlayers = new PlayerController[0];
    private float searchTimer = 0f;

    private void Start()
    {
        cam = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        // 1. Find players in the scene (Refreshes twice a second to catch newly spawned players)
        searchTimer -= Time.deltaTime;
        if (searchTimer <= 0f)
        {
            activePlayers = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            searchTimer = 0.5f;
        }

        // If no player array exists yet, do nothing
        if (activePlayers == null || activePlayers.Length == 0) return;

        // 2. Calculate the Average Position and Bounding Box (safely ignoring destroyed players)
        Vector3 averagePosition = Vector3.zero;
        Bounds bounds = default;
        bool hasFirstBound = false;
        int validPlayerCount = 0;

        for (int i = 0; i < activePlayers.Length; i++)
        {
            // SKIP null/destroyed players so it never throws MissingReferenceException
            if (activePlayers[i] == null) continue;

            Vector3 playerPos = activePlayers[i].transform.position;
            averagePosition += playerPos;

            // Initialize bounds on the first VALID player found
            if (!hasFirstBound)
            {
                bounds = new Bounds(playerPos, Vector3.zero);
                hasFirstBound = true;
            }
            else
            {
                bounds.Encapsulate(playerPos);
            }

            validPlayerCount++;
        }

        // If all players in the list were destroyed/despawned, stop here
        if (validPlayerCount == 0) return;

        // Divide by the number of ALIVE players to get the center
        averagePosition /= validPlayerCount;

        // 3. Move the Camera
        Vector3 targetPosition = averagePosition + offset;
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime);

        // 4. Handle Zooming
        if (enableZoom && cam != null)
        {
            // Find how far apart players are on the X and Z axis
            float greatestDistance = Mathf.Max(bounds.size.x, bounds.size.z);

            // Calculate how much we should zoom based on that distance
            float zoomPercentage = Mathf.Clamp01(greatestDistance / maxPlayerDistance);

            // Smoothly adjust the Field of View
            float targetFOV = Mathf.Lerp(minFOV, maxFOV, zoomPercentage);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, Time.deltaTime * 3f);
        }
    }
}