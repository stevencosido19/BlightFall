using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class VictoryUI : MonoBehaviour
{
    public static VictoryUI Instance { get; private set; }

    [Header("UI Panels")]
    [SerializeField] private GameObject victoryPanel;
    [SerializeField] private Button nextStageButton;
    [SerializeField] private TextMeshProUGUI statusText;

    private string sceneToLoad;

    private void Awake()
    {
        Instance = this;

        if (victoryPanel != null)
            victoryPanel.SetActive(false);

        if (nextStageButton != null)
            nextStageButton.onClick.AddListener(OnNextStageClicked);
    }

    public void ShowVictoryPanel(string nextScene)
    {
        sceneToLoad = nextScene;

        if (victoryPanel != null)
            victoryPanel.SetActive(true);

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        // In multiplayer, only the Host has authority to trigger scene transitions
        if (nextStageButton != null)
        {
            nextStageButton.gameObject.SetActive(isHost);
        }

        if (statusText != null)
        {
            statusText.text = isHost ? "STAGE CLEAR!" : "STAGE CLEAR!\nWaiting for Host...";
        }
    }

    private void OnNextStageClicked()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            Debug.Log($"[VictoryUI] Host loading next stage: {sceneToLoad}");

            // Automatically synchronizes and loads the scene for ALL connected players
            NetworkManager.Singleton.SceneManager.LoadScene(
                sceneToLoad,
                UnityEngine.SceneManagement.LoadSceneMode.Single
            );
        }
    }
}