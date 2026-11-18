using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class DefeatScreenUI : MonoBehaviour
{
    public static DefeatScreenUI Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("The panel containing the Defeat text and buttons (kept disabled until everyone dies)")]
    [SerializeField] private GameObject defeatPanel;
    [SerializeField] private Button repeatStageButton;
    [SerializeField] private Button mainMenuButton;

    [Header("HUD to Disable on Defeat")]
    [Tooltip("Drag your HUD panel, Joystick, Health UI, Key UI, etc. here. Make sure DefeatPanel is NOT inside these objects!")]
    [SerializeField] private GameObject[] hudObjectsToDisable;

    [Header("Settings")]
    [Tooltip("Exact name of your Main Menu scene in Build Settings")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [Tooltip("If true, only the Host can click Repeat Stage. If false, any player can trigger a stage restart.")]
    [SerializeField] private bool onlyHostCanRepeat = false;
    [Tooltip("Optional text shown to clients if Only Host Can Repeat is enabled")]
    [SerializeField] private GameObject waitingForHostLabel;

    private bool isActionProcessing = false;

    private void Awake()
    {
        Instance = this;

        if (defeatPanel != null)
        {
            defeatPanel.SetActive(false);
        }

        if (repeatStageButton != null)
        {
            repeatStageButton.onClick.RemoveAllListeners();
            repeatStageButton.onClick.AddListener(OnRepeatStageClicked);
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.RemoveAllListeners();
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ShowDefeatScreen()
    {
        isActionProcessing = false;

        // Disable all gameplay HUD objects
        if (hudObjectsToDisable != null)
        {
            foreach (GameObject hudObj in hudObjectsToDisable)
            {
                if (hudObj == null) continue;

                // Safety check so we don't accidentally disable DefeatPanel itself
                if (defeatPanel != null && (hudObj == defeatPanel || defeatPanel.transform.IsChildOf(hudObj.transform)))
                {
                    Debug.LogWarning($"[DefeatScreenUI] '{hudObj.name}' contains DefeatPanel, so it was not disabled! Move DefeatPanel outside '{hudObj.name}'.");
                    continue;
                }

                hudObj.SetActive(false);
            }
        }

        if (defeatPanel != null)
        {
            defeatPanel.SetActive(true);
        }

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        if (repeatStageButton != null)
        {
            bool canRepeat = !onlyHostCanRepeat || isHost;
            repeatStageButton.gameObject.SetActive(canRepeat);
            repeatStageButton.interactable = true;
        }

        if (waitingForHostLabel != null)
        {
            waitingForHostLabel.SetActive(onlyHostCanRepeat && !isHost);
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.interactable = true;
        }
    }

    private void OnRepeatStageClicked()
    {
        if (isActionProcessing) return;
        isActionProcessing = true;

        if (repeatStageButton != null) repeatStageButton.interactable = false;

        if (PlayerHealth.LocalPlayerInstance != null)
        {
            PlayerHealth.LocalPlayerInstance.RequestRepeatStageServerRpc();
        }
        else
        {
            Debug.LogWarning("[DefeatScreenUI] No LocalPlayerInstance found to restart stage!");
            isActionProcessing = false;
            if (repeatStageButton != null) repeatStageButton.interactable = true;
        }
    }

    private void OnMainMenuClicked()
    {
        if (isActionProcessing) return;
        isActionProcessing = true;

        if (mainMenuButton != null) mainMenuButton.interactable = false;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        PlayerHealth.AllPlayers.Clear();
        SceneManager.LoadScene(mainMenuSceneName);
    }
}