using System.Collections.Generic;
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
    private bool isLoadingNextStage = false;

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
        isLoadingNextStage = false;

        if (victoryPanel != null)
            victoryPanel.SetActive(true);

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        if (nextStageButton != null)
        {
            nextStageButton.gameObject.SetActive(isHost);
            nextStageButton.interactable = true;
        }

        if (statusText != null)
        {
            statusText.text = isHost ? "STAGE CLEAR!" : "STAGE CLEAR!\nWaiting for Host...";
        }
    }

    private void OnNextStageClicked()
    {
        if (isLoadingNextStage) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            isLoadingNextStage = true;
            if (nextStageButton != null) nextStageButton.interactable = false;

            // Cleanly despawn Stage 1 players BEFORE loading Stage 2
            List<PlayerHealth> snapshot = new List<PlayerHealth>(PlayerHealth.AllPlayers);
            PlayerHealth.AllPlayers.Clear();
            foreach (PlayerHealth p in snapshot)
            {
                if (p != null && p.NetworkObject != null && p.NetworkObject.IsSpawned)
                {
                    p.NetworkObject.Despawn(true);
                }
            }

            Debug.Log($"[VictoryUI] Host loading next stage: {sceneToLoad}");

            NetworkManager.Singleton.SceneManager.LoadScene(
                sceneToLoad,
                UnityEngine.SceneManagement.LoadSceneMode.Single
            );
        }
    }
}