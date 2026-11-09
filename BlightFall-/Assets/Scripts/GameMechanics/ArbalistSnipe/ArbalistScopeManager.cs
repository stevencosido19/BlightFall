using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ArbalistScopeManager : MonoBehaviour, IDragHandler
{
    [Header("HUD Buttons")]
    [Tooltip("The on-screen button to enter scope mode")]
    [SerializeField] private Button scopeToggleButton;
    [SerializeField] private Button fireButton;
    [SerializeField] private Button exitScopeButton;

    [Header("Scope Overlay")]
    [SerializeField] private GameObject scopePanel;
    [SerializeField] private RectTransform crosshair;
    [SerializeField] private float dragSensitivity = 1.2f;
    [SerializeField] private float maxShootDistance = 50f;
    [SerializeField] private LayerMask targetLayerMask = ~0; // Everything by default

    private PlayerController localArbalist = null;
    private bool isInScope = false;
    private Camera mainCam;

    private void Start()
    {
        mainCam = Camera.main;

        if (scopePanel != null) scopePanel.SetActive(false);

        if (scopeToggleButton != null)
        {
            scopeToggleButton.gameObject.SetActive(false); // Hidden until local Arbalist detected
            scopeToggleButton.onClick.AddListener(EnterScope);
        }

        if (exitScopeButton != null)
            exitScopeButton.onClick.AddListener(ExitScope);

        if (fireButton != null)
            fireButton.onClick.AddListener(FireCrossbow);
    }

    private void Update()
    {
        // Search for local Arbalist if not already found
        if (localArbalist == null)
        {
            FindLocalArbalist();
        }
    }

    private void FindLocalArbalist()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return;

        var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localObj == null) return;

        PlayerController player = localObj.GetComponentInChildren<PlayerController>();
        if (player != null && player.CanUseCrossbow)
        {
            localArbalist = player;
            if (scopeToggleButton != null) scopeToggleButton.gameObject.SetActive(true);
        }
    }

    public void EnterScope()
    {
        if (localArbalist == null) return;

        isInScope = true;
        scopePanel.SetActive(true);

        // Freeze player movement while scoping
        localArbalist.isMovementLocked = true;

        // Reset crosshair to center of screen
        if (crosshair != null)
            crosshair.anchoredPosition = Vector2.zero;
    }

    public void ExitScope()
    {
        isInScope = false;
        scopePanel.SetActive(false);

        // Restore movement
        if (localArbalist != null)
            localArbalist.isMovementLocked = false;
    }

    // Touch drag: swiping anywhere on the screen moves the crosshair
    public void OnDrag(PointerEventData eventData)
    {
        if (!isInScope || crosshair == null) return;

        crosshair.anchoredPosition += eventData.delta * dragSensitivity;

        // Clamp crosshair to keep it visible on the screen
        float halfWidth = Screen.width * 0.45f;
        float halfHeight = Screen.height * 0.45f;
        Vector2 clampedPos = crosshair.anchoredPosition;
        clampedPos.x = Mathf.Clamp(clampedPos.x, -halfWidth, halfWidth);
        clampedPos.y = Mathf.Clamp(clampedPos.y, -halfHeight, halfHeight);
        crosshair.anchoredPosition = clampedPos;
    }

    private void FireCrossbow()
    {
        if (!isInScope || crosshair == null) return;

        if (mainCam == null) mainCam = Camera.main;

        // Cast a 3D ray directly from the crosshair's current screen position
        Vector3 screenPoint = crosshair.position;
        Ray ray = mainCam.ScreenPointToRay(screenPoint);

        Debug.DrawRay(ray.origin, ray.direction * maxShootDistance, Color.red, 2f);

        if (Physics.Raycast(ray, out RaycastHit hit, maxShootDistance, targetLayerMask))
        {
            Debug.Log($"<color=yellow>[Crossbow Shot]</color> Hit: {hit.collider.gameObject.name}");

            // Check if the object hit is an elevated switch
            HighSwitch highSwitch = hit.collider.GetComponentInParent<HighSwitch>();
            if (highSwitch != null)
            {
                highSwitch.ShootSwitchServerRpc();
                ExitScope(); // Auto-exit scope on successful hit
            }
        }
        else
        {
            Debug.Log("[Crossbow Shot] Missed!");
        }
    }
}