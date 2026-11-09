using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LockpickMinigame : MonoBehaviour, IDragHandler
{
    public static LockpickMinigame Instance { get; private set; }

    [Header("UI Panels")]
    [SerializeField] private GameObject minigamePanel;
    [SerializeField] private RectTransform pickPivot;      // Pick needle
    [SerializeField] private RectTransform lockCylinder;   // Lock cylinder

    [Header("Mobile Controls")]
    [SerializeField] private LockTurnButton turnButton;    // The on-screen "HOLD TO TURN" button
    [SerializeField] private Button closeButton;           // Top corner "X" button

    [Header("Difficulty Settings")]
    [SerializeField] private float sweetSpotTolerance = 12f;
    [SerializeField] private float turnSpeed = 120f;
    [SerializeField] private float touchSensitivity = 0.2f;

    private float sweetSpotAngle;
    private float currentPickAngle = 0f;
    private float currentLockAngle = 0f;
    private bool isTurning = false;
    private bool isUnlocked = false;

    private Action onUnlockSuccess;

    private void Awake()
    {
        Instance = this;

        if (minigamePanel != null)
            minigamePanel.SetActive(false);

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseMinigame);
    }

    public void StartLockpicking(Action onSuccessCallback)
    {
        onUnlockSuccess = onSuccessCallback;
        isUnlocked = false;
        currentLockAngle = 0f;
        currentPickAngle = 0f;

        // Generate a random sweet spot between -80 and +80 degrees
        sweetSpotAngle = UnityEngine.Random.Range(-80f, 80f);

        UpdateRotations();
        minigamePanel.SetActive(true);
    }

    public void CloseMinigame()
    {
        minigamePanel.SetActive(false);
    }

    // Touch drag handler: swiping finger across the screen rotates the pick
    public void OnDrag(PointerEventData eventData)
    {
        if (isTurning || isUnlocked) return;

        currentPickAngle -= eventData.delta.x * touchSensitivity;
        currentPickAngle = Mathf.Clamp(currentPickAngle, -90f, 90f);
        UpdateRotations();
    }

    private void Update()
    {
        if (minigamePanel == null || !minigamePanel.activeSelf || isUnlocked) return;

        // Check if the player is holding the mobile "TURN" button (or Space/D in editor)
        isTurning = (turnButton != null && turnButton.IsPressed);

#if UNITY_EDITOR
        // Keyboard fallback for testing in editor
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            (UnityEngine.InputSystem.Keyboard.current.dKey.isPressed || UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed))
        {
            isTurning = true;
        }
#endif

        // Calculate how far the lock can turn based on accuracy
        float angleDifference = Mathf.Abs(currentPickAngle - sweetSpotAngle);
        float maxTurnAngle;

        if (angleDifference <= sweetSpotTolerance)
        {
            maxTurnAngle = 90f; // Inside sweet spot -> can open!
        }
        else
        {
            float proximity = Mathf.Clamp01(1f - (angleDifference / 90f));
            maxTurnAngle = Mathf.Lerp(0f, 75f, proximity * proximity);
        }

        // Rotate the cylinder
        if (isTurning)
        {
            currentLockAngle = Mathf.MoveTowards(currentLockAngle, maxTurnAngle, turnSpeed * Time.deltaTime);

            // Shaking if stuck
            if (currentLockAngle >= maxTurnAngle - 0.5f && currentLockAngle < 90f)
            {
                float shake = UnityEngine.Random.Range(-2.5f, 2.5f);
                pickPivot.localEulerAngles = new Vector3(0, 0, currentPickAngle + shake);
            }
        }
        else
        {
            // Spring back when released
            currentLockAngle = Mathf.MoveTowards(currentLockAngle, 0f, turnSpeed * 1.5f * Time.deltaTime);
        }

        UpdateRotations();

        // Success check
        if (currentLockAngle >= 89.9f)
        {
            isUnlocked = true;
            Debug.Log("<color=green>Lock Picked Successfully!</color>");
            CloseMinigame();
            onUnlockSuccess?.Invoke();
        }
    }

    private void UpdateRotations()
    {
        if (lockCylinder != null)
            lockCylinder.localEulerAngles = new Vector3(0, 0, -currentLockAngle);

        if (!isTurning && pickPivot != null)
            pickPivot.localEulerAngles = new Vector3(0, 0, currentPickAngle);
    }
}