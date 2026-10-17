using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum PlayerRole
{
    None = 0,
    Knight = 1,
    Arbalist = 2,
    Rogue = 3
}

public class LobbyManager : NetworkBehaviour
{
    public static LobbyManager Instance { get; private set; }

    /// <summary>
    /// Filled on the server right before the gameplay scene loads. Static, so it survives the
    /// scene change (this object and its NetworkVariables are destroyed with the lobby scene).
    /// Your gameplay spawner should read from here (server side) to know who plays what.
    /// </summary>
    public static readonly Dictionary<ulong, PlayerRole> RoleAssignments = new Dictionary<ulong, PlayerRole>();

    public static PlayerRole GetAssignedRole(ulong clientId) =>
        RoleAssignments.TryGetValue(clientId, out var role) ? role : PlayerRole.None;

    [Header("UI & Display Elements")]
    [SerializeField] private TMP_Text hostIpText;
    [SerializeField] private TMP_Text playerListText;
    [Tooltip("Optional: shows how many roles are locked, e.g. 'Roles locked: 2/3'.")]
    [SerializeField] private TMP_Text statusText;
    [Tooltip("Optional: shows the local player's chosen role, e.g. 'Your role: Arbalist'.")]
    [SerializeField] private TMP_Text myRoleText;

    [Header("Role Buttons (optional)")]
    [Tooltip("If assigned, a button is greyed out while another player holds that role.")]
    [SerializeField] private Button knightButton;
    [SerializeField] private Button arbalistButton;
    [SerializeField] private Button rogueButton;

    [Header("Scene Settings")]
    [SerializeField] private string gameplaySceneName = "GameScene";

    private const int Unclaimed = -1;

    // Role tracking (-1 = Unclaimed, otherwise the owning ClientId)
    private readonly NetworkVariable<int> knightPlayerId = new NetworkVariable<int>(Unclaimed);
    private readonly NetworkVariable<int> arbalistPlayerId = new NetworkVariable<int>(Unclaimed);
    private readonly NetworkVariable<int> roguePlayerId = new NetworkVariable<int>(Unclaimed);

    // Server-maintained list of connected players. Clients cannot read
    // NetworkManager.ConnectedClientsList, so we replicate it ourselves.
    private readonly NetworkList<ulong> connectedPlayers = new NetworkList<ulong>();

    private bool gameStarting;
    private int lastUiSignature = int.MinValue;

    // Safety net: if a network event is ever missed, the UI still catches up within a frame.
    private void Update()
    {
        if (!IsSpawned) return;

        int signature = 17;
        signature = signature * 31 + knightPlayerId.Value;
        signature = signature * 31 + arbalistPlayerId.Value;
        signature = signature * 31 + roguePlayerId.Value;
        for (int i = 0; i < connectedPlayers.Count; i++)
            signature = signature * 31 + (int)connectedPlayers[i];

        if (signature != lastUiSignature)
        {
            lastUiSignature = signature;
            UpdatePlayerListUI();
        }
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        knightPlayerId.OnValueChanged += OnRoleChanged;
        arbalistPlayerId.OnValueChanged += OnRoleChanged;
        roguePlayerId.OnValueChanged += OnRoleChanged;
        connectedPlayers.OnListChanged += OnPlayerListChanged;

        if (IsServer)
        {
            gameStarting = false;
            RoleAssignments.Clear();

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            // Host (and anyone already connected) is not announced via the callback.
            foreach (ulong id in NetworkManager.ConnectedClientsIds)
                AddPlayer(id);
        }

        if (hostIpText != null)
            hostIpText.text = IsServer ? $"Host IP: {GetLocalIPAddress()}" : "Connected to Lobby";

        UpdatePlayerListUI();
    }

    public override void OnNetworkDespawn()
    {
        knightPlayerId.OnValueChanged -= OnRoleChanged;
        arbalistPlayerId.OnValueChanged -= OnRoleChanged;
        roguePlayerId.OnValueChanged -= OnRoleChanged;
        connectedPlayers.OnListChanged -= OnPlayerListChanged;

        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (Instance == this) Instance = null;

        base.OnNetworkDespawn();
    }

    // ---------------- Server-side connection tracking ----------------

    private void OnClientConnected(ulong clientId) => AddPlayer(clientId);

    private void OnClientDisconnected(ulong clientId)
    {
        // Free whatever role the leaving player held so someone else can take it.
        ReleaseRoles(clientId);

        for (int i = connectedPlayers.Count - 1; i >= 0; i--)
        {
            if (connectedPlayers[i] == clientId) connectedPlayers.RemoveAt(i);
        }
    }

    private void AddPlayer(ulong clientId)
    {
        if (!connectedPlayers.Contains(clientId))
            connectedPlayers.Add(clientId);
    }

    private void ReleaseRoles(ulong clientId)
    {
        int id = (int)clientId;
        if (knightPlayerId.Value == id) knightPlayerId.Value = Unclaimed;
        if (arbalistPlayerId.Value == id) arbalistPlayerId.Value = Unclaimed;
        if (roguePlayerId.Value == id) roguePlayerId.Value = Unclaimed;
    }

    // ---------------- UI ----------------

    private void OnRoleChanged(int previous, int current) => UpdatePlayerListUI();

    private void OnPlayerListChanged(NetworkListEvent<ulong> changeEvent) => UpdatePlayerListUI();

    private void UpdatePlayerListUI()
    {
        if (playerListText != null)
        {
            ulong localId = NetworkManager != null ? NetworkManager.LocalClientId : ulong.MaxValue;

            var sb = new StringBuilder("<b>Lobby Players:</b>\n");
            foreach (ulong clientId in connectedPlayers)
            {
                sb.Append("- Player ").Append(clientId);
                if (clientId == localId) sb.Append(" (You)");
                sb.Append(": ").Append(GetRoleLabel(clientId)).Append('\n');
            }

            playerListText.text = sb.ToString();
        }

        if (statusText != null)
        {
            int locked = 0;
            if (knightPlayerId.Value != Unclaimed) locked++;
            if (arbalistPlayerId.Value != Unclaimed) locked++;
            if (roguePlayerId.Value != Unclaimed) locked++;

            statusText.text = locked >= 3
                ? "All roles locked - starting game..."
                : $"Roles locked: {locked}/3";
        }

        UpdateMyRoleUI();
    }

    private PlayerRole GetLocalRole()
    {
        if (NetworkManager == null) return PlayerRole.None;

        int id = (int)NetworkManager.LocalClientId;
        if (knightPlayerId.Value == id) return PlayerRole.Knight;
        if (arbalistPlayerId.Value == id) return PlayerRole.Arbalist;
        if (roguePlayerId.Value == id) return PlayerRole.Rogue;
        return PlayerRole.None;
    }

    private void UpdateMyRoleUI()
    {
        PlayerRole mine = GetLocalRole();

        if (myRoleText != null)
        {
            myRoleText.text = mine == PlayerRole.None
                ? "Your role: <i>not chosen yet</i>"
                : $"Your role: <b>{mine}</b>";
        }

        int localId = NetworkManager != null ? (int)NetworkManager.LocalClientId : Unclaimed;
        SetRoleButtonState(knightButton, knightPlayerId.Value, localId);
        SetRoleButtonState(arbalistButton, arbalistPlayerId.Value, localId);
        SetRoleButtonState(rogueButton, roguePlayerId.Value, localId);
    }

    // Clickable if the role is free or already mine; greyed out if someone else holds it.
    private static void SetRoleButtonState(Button button, int ownerId, int localId)
    {
        if (button == null) return;
        button.interactable = ownerId == Unclaimed || ownerId == localId;
    }

    private string GetRoleLabel(ulong clientId)
    {
        int id = (int)clientId;
        if (knightPlayerId.Value == id) return "Knight";
        if (arbalistPlayerId.Value == id) return "Arbalist";
        if (roguePlayerId.Value == id) return "Rogue";
        return "Selecting...";
    }

    // ---------------- Role selection buttons ----------------

    public void SelectKnight() => SendRoleSelection(PlayerRole.Knight);
    public void SelectArbalist() => SendRoleSelection(PlayerRole.Arbalist);
    public void SelectRogue() => SendRoleSelection(PlayerRole.Rogue);

    private void SendRoleSelection(PlayerRole role)
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("[Lobby] Role button clicked but LobbyManager is not spawned. " +
                             "Enter the lobby by hosting/joining from the main menu, and make sure " +
                             "this GameObject has a NetworkObject component.");
            return;
        }

        Debug.Log($"[Lobby] Requesting role {role}");
        SelectRoleServerRpc((int)role);
    }

    // The sender id comes from the transport, not from a parameter, so a client can't pick roles for someone else.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SelectRoleServerRpc(int roleIndex, RpcParams rpcParams = default)
    {
        if (gameStarting) return;
        if (roleIndex < 1 || roleIndex > 3) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        NetworkVariable<int> target = GetRoleVariable((PlayerRole)roleIndex);
        if (target == null) return;

        Debug.Log($"[Lobby] Server: player {senderId} asked for {(PlayerRole)roleIndex}");

        // Already mine, or taken by someone else: leave everything as it is,
        // so a failed pick never wipes the player's current role.
        if (target.Value != Unclaimed)
        {
            Debug.Log($"[Lobby] Server: {(PlayerRole)roleIndex} already held by player {target.Value}; request ignored.");
            return;
        }

        ReleaseRoles(senderId);
        target.Value = (int)senderId;
        Debug.Log($"[Lobby] Server: player {senderId} is now {(PlayerRole)roleIndex}");

        TryStartGame();
    }

    private NetworkVariable<int> GetRoleVariable(PlayerRole role)
    {
        switch (role)
        {
            case PlayerRole.Knight: return knightPlayerId;
            case PlayerRole.Arbalist: return arbalistPlayerId;
            case PlayerRole.Rogue: return roguePlayerId;
            default: return null;
        }
    }

    private void TryStartGame()
    {
        if (!IsServer || gameStarting) return;

        if (knightPlayerId.Value == Unclaimed ||
            arbalistPlayerId.Value == Unclaimed ||
            roguePlayerId.Value == Unclaimed)
            return;

        // The three roles must belong to three different players who are still connected.
        ulong knight = (ulong)knightPlayerId.Value;
        ulong arbalist = (ulong)arbalistPlayerId.Value;
        ulong rogue = (ulong)roguePlayerId.Value;

        if (knight == arbalist || knight == rogue || arbalist == rogue) return;
        if (!connectedPlayers.Contains(knight) ||
            !connectedPlayers.Contains(arbalist) ||
            !connectedPlayers.Contains(rogue))
            return;

        gameStarting = true;

        // Persist the choices before this scene (and its NetworkVariables) goes away.
        RoleAssignments.Clear();
        RoleAssignments[(ulong)knightPlayerId.Value] = PlayerRole.Knight;
        RoleAssignments[(ulong)arbalistPlayerId.Value] = PlayerRole.Arbalist;
        RoleAssignments[(ulong)roguePlayerId.Value] = PlayerRole.Rogue;

        Debug.Log("All roles selected! Starting game...");
        var status = NetworkManager.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"Could not load '{gameplaySceneName}' ({status}). Is it in Build Settings?");
            gameStarting = false;
        }
    }

    // ---------------- Helpers ----------------

    private static string GetLocalIPAddress()
    {
        // Ask the OS which interface it would use to reach the internet (no packets are sent).
        // This avoids picking a virtual adapter (VPN, WSL, VirtualBox) like "first IPv4 found" often does.
        try
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint)
                    return endPoint.Address.ToString();
            }
        }
        catch { /* no route: fall through */ }

        try
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip.ToString();
            }
        }
        catch { }

        return "127.0.0.1";
    }
}