using System;
using System.Threading.Tasks;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System.Collections.Generic;

namespace EchoRealm.Networking
{
    /// <summary>
    /// Manages Photon Fusion 2 networking in Shared Mode.
    /// Handles session creation/joining and tracks connection state.
    /// Attach to a persistent GameObject in the scene (e.g., "NetworkManager").
    /// </summary>
    public class FusionNetworkManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Session Settings")]
        [SerializeField] private string sessionName = "EchoRealm";
        [SerializeField] private int maxPlayers = 4;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>The active Fusion NetworkRunner instance.</summary>
        public NetworkRunner Runner { get; private set; }

        /// <summary>True if this client is the master (first to join / State Authority host).</summary>
        public bool IsMaster => Runner != null && Runner.IsSharedModeMasterClient;

        /// <summary>True if the runner is active and connected to a session.</summary>
        public bool IsConnected => Runner != null && Runner.IsRunning;

        /// <summary>Fired when this client successfully joins a session.</summary>
        public event Action OnSessionJoined;

        /// <summary>Fired when another player joins the session.</summary>
        public event Action<PlayerRef> OnPlayerJoinedSession;

        /// <summary>Fired when another player leaves the session.</summary>
        public event Action<PlayerRef> OnPlayerLeftSession;

        /// <summary>Fired on disconnect or shutdown.</summary>
        public event Action OnDisconnected;

        // Singleton access (optional convenience — only one should exist).
        public static FusionNetworkManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Start or join the shared Fusion session.
        /// Call this after the QR anchor has been established.
        /// </summary>
        public async Task StartSession()
        {
            if (Runner != null)
            {
                Log("Session already running — ignoring StartSession call.");
                return;
            }

            Runner = gameObject.AddComponent<NetworkRunner>();
            Runner.ProvideInput = false; // Shared Mode does not use input authority

            var startArgs = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                PlayerCount = maxPlayers,
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            };

            Log($"Starting Fusion session '{sessionName}' (Shared Mode, max {maxPlayers} players)...");

            var result = await Runner.StartGame(startArgs);

            if (result.Ok)
            {
                Log($"Session started! Master: {IsMaster}");
                OnSessionJoined?.Invoke();
            }
            else
            {
                Log($"Failed to start session: {result.ShutdownReason}", isError: true);
            }
        }

        /// <summary>
        /// Gracefully leave the current session.
        /// </summary>
        public async Task LeaveSession()
        {
            if (Runner == null) return;

            Log("Leaving session...");
            await Runner.Shutdown();
            Runner = null;
        }

        // ------------------------------------------------------------------
        // INetworkRunnerCallbacks
        // ------------------------------------------------------------------

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Log($"Player joined: {player}");
            OnPlayerJoinedSession?.Invoke(player);
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Log($"Player left: {player}");
            OnPlayerLeftSession?.Invoke(player);
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Log($"Session shutdown: {shutdownReason}");
            Runner = null;
            OnDisconnected?.Invoke();
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Log($"Disconnected from server: {reason}", isError: true);
            OnDisconnected?.Invoke();
        }

        // --- Required but unused callbacks (Shared Mode) ---
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            Log($"Connect failed: {reason}", isError: true);
        }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void Log(string message, bool isError = false)
        {
            if (!logEvents) return;
            if (isError)
                Debug.LogError($"[FusionNetwork] {message}");
            else
                Debug.Log($"[FusionNetwork] {message}");
        }
    }
}
