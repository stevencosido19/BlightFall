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

    // We cache the players in an array so we don't have to search the whole game every frame
    private CharacterController[] activePlayers = new CharacterController[0];
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
            // This finds every character in the scene, regardless of whether they are a Knight, Arbalist, or Rogue!
            activePlayers = Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
            searchTimer = 0.5f;
        }

        // If no players have spawned yet, do nothing
        if (activePlayers == null || activePlayers.Length == 0) return;

        // 2. Calculate the Average Position and the Bounding Box
        Vector3 averagePosition = Vector3.zero;
        Bounds bounds = new Bounds(activePlayers[0].transform.position, Vector3.zero);

        for (int i = 0; i < activePlayers.Length; i++)
        {
            // Add up all player positions
            averagePosition += activePlayers[i].transform.position;

            // Expand the invisible box to include this player
            bounds.Encapsulate(activePlayers[i].transform.position);
        }

        // Divide by the number of players to get the exact center of the group
        averagePosition /= activePlayers.Length;

        // 3. Move the Camera
        // Target is the group's average center plus your chosen height/angle offset
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