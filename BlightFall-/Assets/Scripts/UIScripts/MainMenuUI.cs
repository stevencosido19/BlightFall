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
    [SerializeField] private float connectTimeoutSeconds = 4f;
    [SerializeField] private int maxPacketQueueSize = 1024;
    [SerializeField] private int maxSendQueueSize = 524288;

    private bool isBusy;
    private Coroutine connectTimeoutRoutine;

    private void Start()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (joinPanel != null) joinPanel.SetActive(false);
        SetStatus(string.Empty);
    }

    private void OnDestroy()
    {
        StopTimeoutRoutine();
        UnsubscribeClientCallbacks();
    }

   

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

        var transport = nm.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport != null)
        {
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            transport.MaxPacketQueueSize = maxPacketQueueSize;
            transport.MaxSendQueueSize = maxSendQueueSize;
        }

        if (!nm.StartHost())
        {
            SetStatus("Failed to start host. Is the port already in use?");
            isBusy = false;
            yield break;
        }

        nm.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        isBusy = false;
    }

 

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
        
        transport.MaxConnectAttempts = 10;
        transport.MaxPacketQueueSize = maxPacketQueueSize;
        transport.MaxSendQueueSize = maxSendQueueSize;

        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        if (!nm.StartClient())
        {
            UnsubscribeClientCallbacks();
            SetStatus("Failed to start client.");
            isBusy = false;
            yield break;
        }

        StopTimeoutRoutine();
        connectTimeoutRoutine = StartCoroutine(ConnectTimeoutRoutine(targetIP));
    }

    private IEnumerator ConnectTimeoutRoutine(string targetIP)
    {
        yield return new WaitForSecondsRealtime(connectTimeoutSeconds);

       
        if (!isBusy) yield break;

        UnsubscribeClientCallbacks();

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsListening || nm.IsClient))
        {
            nm.Shutdown();
        }

        SetStatus($"No host found at {targetIP}. Check the IP and try again.");
        isBusy = false;
        connectTimeoutRoutine = null;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            StopTimeoutRoutine();
            SetStatus("Connected! Loading lobby...");

            if (joinPanel != null) joinPanel.SetActive(false);
            gameObject.SetActive(false);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        StopTimeoutRoutine();

        NetworkManager nm = NetworkManager.Singleton;
        string reason = nm != null ? nm.DisconnectReason : string.Empty;

        UnsubscribeClientCallbacks();

        if (string.IsNullOrEmpty(reason) || reason.Contains("MaxConnectionAttempts"))
        {
            SetStatus("Could not connect to host. Check the IP and try again.");
        }
        else
        {
            SetStatus($"Disconnected: {reason}");
        }

        isBusy = false;
    }

    private void StopTimeoutRoutine()
    {
        if (connectTimeoutRoutine != null)
        {
            StopCoroutine(connectTimeoutRoutine);
            connectTimeoutRoutine = null;
        }
    }

    private void UnsubscribeClientCallbacks()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    

    private static IEnumerator ShutdownIfRunning(NetworkManager nm)
    {
        if (nm == null) yield break;

        if (nm.IsListening || nm.ShutdownInProgress)
        {
            nm.Shutdown();
            yield return new WaitUntil(() => nm == null || (!nm.IsListening && !nm.ShutdownInProgress));
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        if (!string.IsNullOrEmpty(message)) Debug.Log($"[MainMenu] {message}");
    }
}