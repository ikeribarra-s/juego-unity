using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the session: builds the NetworkManager at runtime (UnityTransport, settings from <see cref="NetConfig"/>), starts / stops
/// Host, Client and Solo, hands out player spawns, enforces the player cap. One persistent instance (DontDestroyOnLoad); a copy in a
/// later scene destroys itself. Solo play is a host with no clients, so there is one code path.
///
/// Where it lives: the Lobby scene (menu) and the game scene. Playing the game scene directly in the editor auto-starts a solo host
/// (<see cref="_autoSolo"/>), so every existing workflow (probes through the editor, quick tests) keeps working. Built test clients
/// are driven by command-line: <c>-client 127.0.0.1</c>, <c>-host</c>, <c>-solo</c>, <c>-port 7777</c>.
/// </summary>
[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
[AddComponentMenu("Fluffterror/Network/Network Bootstrap")]
public class NetworkBootstrap : MonoBehaviour
{
    public static NetworkBootstrap Instance { get; private set; }

    [SerializeField] private NetConfig _config;
    [Tooltip("When the game scene is played directly (no session yet), start a solo host so the scene works exactly like before.")]
    [SerializeField] private bool _autoSolo = true;
    [Tooltip("Read -host / -solo / -client <ip> / -port <n> from the command line (test builds).")]
    [SerializeField] private bool _readCommandLine = true;

    public NetConfig Config => _config;
    public string Status { get; private set; } = "";
    public event Action<string> StatusChanged;

    private NetworkManager _nm;
    private UnityTransport _utp;
    private PlayerFactory _factory;
    private bool _managerHooked;
    private bool _gameReady;
    private readonly HashSet<ulong> _spawned = new();

    // ── lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (_config == null) _config = NetConfig.Load();
        if (_config == null || _config.PlayerPrefab == null)
        {
            Debug.LogError("[NetworkBootstrap] No NetConfig (Resources/NetConfig) or it has no PlayerPrefab. Run Fluffterror > Network > Build Network Setup.", this);
            enabled = false;
            return;
        }
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;   // a session must keep ticking when the window loses focus (otherwise peers time out)
    }

    private void Start()
    {
        if (Instance != this || Net.IsOnline) return;

        if (_readCommandLine)
        {
            string[] args = Environment.GetCommandLineArgs();
            ushort port = _config.Port;
            string clientIp = null; bool host = false, solo = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-host": host = true; break;
                    case "-solo": solo = true; break;
                    case "-client": if (i + 1 < args.Length) clientIp = args[++i]; break;
                    case "-port": if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out var p)) { port = p; i++; } break;
                }
            }
            if (clientIp != null) { StartClientSession(clientIp, port); return; }
            if (host || solo) { StartHostSession(loadGameScene: SceneManager.GetActiveScene().name != _config.GameScene, port); return; }
        }

        if (_autoSolo && SceneManager.GetActiveScene().name == _config.GameScene)
            StartHostSession(loadGameScene: false, _config.Port);
    }

    // ── public API ──────────────────────────────────────────────────────────

    /// <summary>Start hosting (solo = nobody joins). <paramref name="loadGameScene"/> loads the game scene for everyone; false when it is already the active scene.</summary>
    public bool StartHostSession(bool loadGameScene, ushort port = 0)
    {
        if (!EnsureManager()) return false;
        if (Net.IsOnline) { SetStatus("A session is already running."); return false; }
        _utp.SetConnectionData("127.0.0.1", port != 0 ? port : _config.Port, "0.0.0.0");
        _spawned.Clear(); _gameReady = false;
        if (!_nm.StartHost()) { SetStatus("Could not start the host (is the port in use?)."); return false; }
        HookSession();
        SetStatus($"Hosting on port {_utp.ConnectionData.Port}");

        if (loadGameScene)
        {
            _nm.SceneManager.LoadScene(_config.GameScene, LoadSceneMode.Single);   // players spawn on LoadEventCompleted
        }
        else
        {
            _gameReady = SceneManager.GetActiveScene().name == _config.GameScene;
            if (_gameReady) SpawnPlayer(_nm.LocalClientId);
        }
        return true;
    }

    public bool StartClientSession(string ip, ushort port = 0)
    {
        if (!EnsureManager()) return false;
        if (Net.IsOnline) { SetStatus("A session is already running."); return false; }
        _utp.SetConnectionData(string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim(), port != 0 ? port : _config.Port);
        if (!_nm.StartClient()) { SetStatus("Could not start the client."); return false; }
        HookSession();
        SetStatus($"Connecting to {_utp.ConnectionData.Address}:{_utp.ConnectionData.Port} ...");
        return true;
    }

    /// <summary>Leave the session (host: ends it for everybody) and go back to the lobby scene.</summary>
    public void StopSession(bool loadLobby = true)
    {
        if (_nm != null && _nm.IsListening) _nm.Shutdown();
        _spawned.Clear(); _gameReady = false;
        SetStatus("Disconnected.");
        if (loadLobby && !string.IsNullOrEmpty(_config.LobbyScene) && SceneManager.GetActiveScene().name != _config.LobbyScene
            && Application.CanStreamedLevelBeLoaded(_config.LobbyScene))
            SceneManager.LoadScene(_config.LobbyScene);
    }

    // ── NetworkManager ──────────────────────────────────────────────────────

    private bool EnsureManager()
    {
        if (_nm != null) return true;
        if (NetworkManager.Singleton != null) { _nm = NetworkManager.Singleton; _utp = _nm.GetComponent<UnityTransport>(); }
        else
        {
            var go = new GameObject("NetworkManager");
            go.SetActive(false);                       // configure before NetworkManager.Awake initialises
            _utp = go.AddComponent<UnityTransport>();
            _nm = go.AddComponent<NetworkManager>();
            _nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _utp,
                TickRate = _config.TickRate,
                ConnectionApproval = true,              // player cap; players are spawned manually, not by NGO
                EnableSceneManagement = true,
                PlayerPrefab = null,
            };
            go.SetActive(true);
        }
        if (_utp == null) { Debug.LogError("[NetworkBootstrap] The NetworkManager has no UnityTransport.", this); return false; }

        _factory = new PlayerFactory(_config);
        _nm.AddNetworkPrefab(_config.PlayerPrefab.gameObject);
        _nm.PrefabHandler.AddHandler(_config.PlayerPrefab, _factory);
        _nm.ConnectionApprovalCallback = Approve;
        return true;
    }

    private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = false;           // we spawn through PlayerFactory once the game scene is loaded
        response.Approved = _nm.ConnectedClientsIds.Count < _config.MaxPlayers;
        if (!response.Approved) response.Reason = "The session is full.";
    }

    private void HookSession()
    {
        if (!_managerHooked)
        {
            _nm.OnClientConnectedCallback += OnClientConnected;
            _nm.OnClientDisconnectCallback += OnClientDisconnected;
            _managerHooked = true;
        }
        if (_nm.SceneManager != null)
        {
            _nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            _nm.SceneManager.OnSceneEvent += OnSceneEvent;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (clientId == _nm.LocalClientId && !_nm.IsServer) SetStatus("Connected.");
        else if (_nm.IsServer) SetStatus($"Player {clientId} joined ({_nm.ConnectedClientsIds.Count}/{_config.MaxPlayers}).");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        _spawned.Remove(clientId);
        if (_nm.IsServer) { SetStatus($"Player {clientId} left."); return; }
        // client side: lost / refused connection
        string reason = _nm.DisconnectReason;
        SetStatus(string.IsNullOrEmpty(reason) ? "Disconnected from the host." : $"Disconnected: {reason}");
        if (_nm.IsListening) _nm.Shutdown();
        if (!string.IsNullOrEmpty(_config.LobbyScene) && SceneManager.GetActiveScene().name != _config.LobbyScene
            && Application.CanStreamedLevelBeLoaded(_config.LobbyScene))
            SceneManager.LoadScene(_config.LobbyScene);
    }

    // ── spawning (server) ───────────────────────────────────────────────────

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!_nm.IsServer || sceneName != _config.GameScene) return;
        _gameReady = true;
        SpawnPlayer(_nm.LocalClientId);
        foreach (ulong id in clientsCompleted) SpawnPlayer(id);
    }

    // A client that joins a run in progress finishes its scene synchronisation -> spawn it.
    private void OnSceneEvent(SceneEvent e)
    {
        if (e.SceneEventType == SceneEventType.SynchronizeComplete && _nm.IsServer && _gameReady) SpawnPlayer(e.ClientId);
    }

    private void SpawnPlayer(ulong clientId)
    {
        if (!_nm.IsServer || !_gameReady || _spawned.Contains(clientId)) return;
        if (!_nm.ConnectedClients.ContainsKey(clientId)) return;
        _spawned.Add(clientId);
        PlayerSpawnPoint.GetPose(_spawned.Count - 1, out Vector3 pos, out Quaternion rot);
        GameObject go = _factory.Create(clientId, pos, rot);
        go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, destroyWithScene: true);
    }

    // ── misc ────────────────────────────────────────────────────────────────

    private void SetStatus(string s)
    {
        Status = s;
        Debug.Log($"[Net] {s}", this);
        StatusChanged?.Invoke(s);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
