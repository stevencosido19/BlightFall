using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LockpickMinigame : MonoBehaviour, IDragHandler
{
    public static LockpickMinigame Instance { get; private set; }

    [Header("UI Panels")]
    [SerializeField] private GameObject minigamePanel;
    [SerializeField] private RectTransform pickPivot;      
    [SerializeField] private RectTransform lockCylinder;   

    [Header("Mobile Controls")]
    [SerializeField] private LockTurnButton turnButton;   
    [SerializeField] private Button closeButton;           

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

       
        sweetSpotAngle = UnityEngine.Random.Range(-80f, 80f);

        UpdateRotations();
        minigamePanel.SetActive(true);
    }

    public void CloseMinigame()
    {
        minigamePanel.SetActive(false);
    }

    
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

        
        isTurning = (turnButton != null && turnButton.IsPressed);

#if UNITY_EDITOR
       
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            (UnityEngine.InputSystem.Keyboard.current.dKey.isPressed || UnityEngine.InputSystem.Keyboard.current.spaceKey.isPressed))
        {
            isTurning = true;
        }
#endif

        
        float angleDifference = Mathf.Abs(currentPickAngle - sweetSpotAngle);
        float maxTurnAngle;

        if (angleDifference <= sweetSpotTolerance)
        {
            maxTurnAngle = 90f; 
        }
        else
        {
            float proximity = Mathf.Clamp01(1f - (angleDifference / 90f));
            maxTurnAngle = Mathf.Lerp(0f, 75f, proximity * proximity);
        }

        
        if (isTurning)
        {
            currentLockAngle = Mathf.MoveTowards(currentLockAngle, maxTurnAngle, turnSpeed * Time.deltaTime);

           
            if (currentLockAngle >= maxTurnAngle - 0.5f && currentLockAngle < 90f)
            {
                float shake = UnityEngine.Random.Range(-2.5f, 2.5f);
                pickPivot.localEulerAngles = new Vector3(0, 0, currentPickAngle + shake);
            }
        }
        else
        {
           
            currentLockAngle = Mathf.MoveTowards(currentLockAngle, 0f, turnSpeed * 1.5f * Time.deltaTime);
        }

        UpdateRotations();

       
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