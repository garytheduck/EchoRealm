using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EchoRealm.Networking;

namespace EchoRealm.Testing
{
    /// <summary>
    /// Minimal world-space UI panel for the sync test.
    /// Shows Photon connection status + player count and exposes
    /// "Spawn Cube" / "Randomize" / "Despawn" buttons.
    ///
    /// Setup (Canvas in World Space):
    ///  - Canvas (World Space, Render Mode = World Space, size ~ 0.3x0.2 m)
    ///  - Add TextMeshProUGUI for status line
    ///  - Add 3 UGUI Buttons; wire their OnClick to this script's methods
    ///  - Attach this script to the Canvas root
    /// </summary>
    public class SyncTestUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TestCubeSpawner spawner;

        [Header("Optional buttons (for interactivity toggle)")]
        [SerializeField] private Button spawnButton;
        [SerializeField] private Button randomizeButton;
        [SerializeField] private Button despawnButton;

        private FusionNetworkManager network;
        private float refreshInterval = 0.5f;
        private float lastRefresh;

        private void Start()
        {
            network = FusionNetworkManager.Instance ?? FindObjectOfType<FusionNetworkManager>();
            if (spawner == null) spawner = FindObjectOfType<TestCubeSpawner>();

            // Wire buttons if assigned.
            if (spawnButton != null) spawnButton.onClick.AddListener(OnSpawnPressed);
            if (randomizeButton != null) randomizeButton.onClick.AddListener(OnRandomizePressed);
            if (despawnButton != null) despawnButton.onClick.AddListener(OnDespawnPressed);
        }

        private void Update()
        {
            if (Time.time - lastRefresh < refreshInterval) return;
            lastRefresh = Time.time;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (statusText == null) return;

            if (network == null || network.Runner == null)
            {
                statusText.text = "Photon: <color=#ff6666>disconnected</color>";
                SetButtonsInteractable(false);
                return;
            }

            var runner = network.Runner;
            int players = 0;
            foreach (var p in runner.ActivePlayers) players++;

            string masterTag = network.IsMaster ? " <color=#ffd966>[MASTER]</color>" : "";
            string state = runner.IsRunning ? "<color=#66ff66>connected</color>" : "starting...";

            statusText.text =
                $"Photon: {state}{masterTag}\n" +
                $"Session: {runner.SessionInfo?.Name ?? "-"}\n" +
                $"Players: {players}";

            SetButtonsInteractable(runner.IsRunning);
        }

        private void SetButtonsInteractable(bool interactable)
        {
            // Only the master can spawn/despawn in this simple test.
            bool masterOnly = interactable && network != null && network.IsMaster;
            if (spawnButton != null) spawnButton.interactable = masterOnly;
            if (randomizeButton != null) randomizeButton.interactable = masterOnly;
            if (despawnButton != null) despawnButton.interactable = masterOnly;
        }

        // --- Button handlers (also wireable directly from Inspector) ---

        public void OnSpawnPressed()
        {
            if (spawner != null) spawner.SpawnCube();
        }

        public void OnRandomizePressed()
        {
            if (spawner != null) spawner.RandomizeCubePosition();
        }

        public void OnDespawnPressed()
        {
            if (spawner != null) spawner.DespawnCube();
        }
    }
}
