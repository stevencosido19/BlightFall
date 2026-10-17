using System.Collections;
using System.Net;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject modeSelectPanel;
    [SerializeField] private GameObject joinPanel;

    [Header("Join Elements")]
    [SerializeField] private TMP_InputField ipInputField;

    [Header("Feedback (optional)")]
    [SerializeField] private TMP_Text statusText;

    [Header("Network Settings")]
    [SerializeField] private ushort port = 7777;
    [SerializeField] private string lobbySceneName = "LobbyScene";

    // Blocks double-clicks that would start two coroutines / two connections.
    private bool isBusy;

    private void Start()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (joinPanel != null) joinPanel.SetActive(false);
        SetStatus(string.Empty);
    }

    private void OnDestroy()
    {
        // This object dies when the scene changes; never leave callbacks pointing at it.
        UnsubscribeClientCallbacks();
    }

    // ---------------- Navigation ----------------

    public void OnPlayButtonClicked()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    public void OnJoinMenuButtonClicked()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (joinPanel != null) joinPanel.SetActive(true);
        SetStatus(string.Empty);
    }

    public void OnJoinBackButtonClicked()
    {
        if (isBusy) return;
        if (joinPanel != null) joinPanel.SetActive(false);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
        SetStatus(string.Empty);
    }

    // ---------------- Host ----------------

    public void OnHostButtonClicked()
    {
        if (isBusy) return;

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is NULL! Please add a NetworkManager object to your scene.");
            SetStatus("Network not available.");
            return;
        }

        StartCoroutine(StartHostRoutine());
    }

    private IEnumerator StartHostRoutine()
    {
        isBusy = true;
        SetStatus("Starting host...");

        NetworkManager nm = NetworkManager.Singleton;
        yield return ShutdownIfRunning(nm);

        if (nm == null)
        {
            SetStatus("Network manager was destroyed.");
            isBusy = false;
            yield break;
        }

        // Listen on all interfaces. With the default (127.0.0.1) only the local machine can connect.
        var transport = nm.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport != null)
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");

        if (!nm.StartHost())
        {
            SetStatus("Failed to start host. Is the port already in use?");
            isBusy = false;
            yield break;
        }

        nm.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        isBusy = false;
    }

    // ---------------- Client ----------------

    public void OnSubmitJoinClicked()
    {
        if (isBusy) return;

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton is NULL! Please add a NetworkManager object to your scene.");
            SetStatus("Network not available.");
            return;
        }

        string targetIP = ipInputField != null ? ipInputField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(targetIP)) targetIP = "127.0.0.1";

        // UnityTransport expects a literal IPv4 address, not a hostname.
        if (!IPAddress.TryParse(targetIP, out IPAddress parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            SetStatus("Enter a valid IPv4 address, e.g. 192.168.1.20");
            return;
        }

        StartCoroutine(StartClientRoutine(targetIP));
    }

    private IEnumerator StartClientRoutine(string targetIP)
    {
        isBusy = true;
        SetStatus($"Connecting to {targetIP}...");

        NetworkManager nm = NetworkManager.Singleton;
        yield return ShutdownIfRunning(nm);

        if (nm == null)
        {
            SetStatus("Network manager was destroyed.");
            isBusy = false;
            yield break;
        }

        var transport = nm.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null)
        {
            SetStatus("UnityTransport is missing on the NetworkManager.");
            isBusy = false;
            yield break;
        }

        transport.SetConnectionData(targetIP, port);
        transport.MaxConnectAttempts = 5; // fail in ~5s instead of the default ~60s

        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        if (!nm.StartClient())
        {
            UnsubscribeClientCallbacks();
            SetStatus("Failed to start client.");
            isBusy = false;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // Check if the person who just connected is US (the local client)
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            SetStatus("Connected! Loading lobby...");

            // 1. Force the join panel to turn off
            if (joinPanel != null) joinPanel.SetActive(false);

            // 2. Turn off the entire Main Menu Canvas to be absolutely safe
            gameObject.SetActive(false);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        string reason = nm != null ? nm.DisconnectReason : string.Empty;

        UnsubscribeClientCallbacks();
        SetStatus(string.IsNullOrEmpty(reason)
            ? $"Could not connect to host (port {port})."
            : $"Disconnected: {reason}");
        isBusy = false;
    }

    private void UnsubscribeClientCallbacks()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // ---------------- Helpers ----------------

    private static IEnumerator ShutdownIfRunning(NetworkManager nm)
    {
        if (nm == null) yield break;

        if (nm.IsListening || nm.ShutdownInProgress)
        {
            nm.Shutdown();
            // Use the cached reference: Singleton can become null during shutdown.
            yield return new WaitUntil(() => nm == null || (!nm.IsListening && !nm.ShutdownInProgress));
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        if (!string.IsNullOrEmpty(message)) Debug.Log($"[MainMenu] {message}");
    }
}