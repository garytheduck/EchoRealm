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

        [Header("Shared Film")]
        [Tooltip("Prefab with NetworkObject + FilmSync. The master spawns it once per session.")]
        [SerializeField] private NetworkObject filmSyncPrefab;

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

            Log("Creating NetworkRunner component...");
            Runner = gameObject.AddComponent<NetworkRunner>();
            Runner.ProvideInput = false; // Shared Mode does not use input authority

            var startArgs = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                PlayerCount = maxPlayers,
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            };

            // Surface the Photon config so we can confirm AppId/region are loaded.
            LogPhotonConfig();

            Log($"Calling StartGame → GameMode.Shared, session='{sessionName}', maxPlayers={maxPlayers}. " +
                "If this is the last Fusion log you see, StartGame is HANGING (check internet / firewall / AppId).");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            StartGameResult result;
            try
            {
                result = await Runner.StartGame(startArgs);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Log($"StartGame THREW after {stopwatch.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}", isError: true);
                return;
            }
            stopwatch.Stop();

            if (result.Ok)
            {
                int playerCount = Runner.SessionInfo != null ? Runner.SessionInfo.PlayerCount : -1;
                Log($"✓ SESSION STARTED in {stopwatch.ElapsedMilliseconds}ms | " +
                    $"LocalPlayer={Runner.LocalPlayer} | IsMaster={IsMaster} | " +
                    $"PlayersInSession={playerCount} | Region={(Runner.SessionInfo != null ? Runner.SessionInfo.Region : "?")}");

                // The master spawns the shared-film state object; clients receive it by replication.
                if (IsMaster && filmSyncPrefab != null)
                {
                    Runner.Spawn(filmSyncPrefab);
                    Log("Spawned FilmSync (master is the film authority).");
                }
                else if (filmSyncPrefab == null)
                {
                    Log("filmSyncPrefab not assigned — film will run un-networked (per-device).", isError: true);
                }

                OnSessionJoined?.Invoke();
            }
            else
            {
                Log($"✗ START GAME FAILED after {stopwatch.ElapsedMilliseconds}ms | " +
                    $"ShutdownReason={result.ShutdownReason} | ErrorMessage={result.ErrorMessage}", isError: true);
            }
        }

        /// <summary>Logs the loaded Photon AppSettings so we can confirm AppId/region at runtime.</summary>
        private void LogPhotonConfig()
        {
            try
            {
                var settings = Fusion.Photon.Realtime.PhotonAppSettings.Global?.AppSettings;
                if (settings == null)
                {
                    Log("Photon AppSettings is NULL — PhotonAppSettings.asset not configured!", isError: true);
                    return;
                }

                string appId = settings.AppIdFusion;
                string maskedId = string.IsNullOrEmpty(appId)
                    ? "<EMPTY — Fusion will fail to connect!>"
                    : (appId.Length > 8 ? appId.Substring(0, 8) + "…" : appId);

                Log($"Photon config → AppIdFusion={maskedId} | FixedRegion='{settings.FixedRegion}' | " +
                    $"UseNameServer={settings.UseNameServer}");
            }
            catch (Exception ex)
            {
                Log($"Could not read Photon AppSettings: {ex.Message}", isError: true);
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
            int count = runner.SessionInfo != null ? runner.SessionInfo.PlayerCount : -1;
            bool isLocal = player == runner.LocalPlayer;
            Log($"Player JOINED: {player}{(isLocal ? " (THIS DEVICE)" : " (remote peer)")} | Total players now: {count}");
            OnPlayerJoinedSession?.Invoke(player);
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            int count = runner.SessionInfo != null ? runner.SessionInfo.PlayerCount : -1;
            Log($"Player LEFT: {player} | Total players now: {count}");
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

        public void OnConnectedToServer(NetworkRunner runner)
        {
            Log($"Connected to Photon relay server. LocalPlayer={runner.LocalPlayer}");
        }

        // --- Required but unused callbacks (Shared Mode) ---
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            Log($"CONNECT FAILED to {remoteAddress}: {reason}", isError: true);
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
