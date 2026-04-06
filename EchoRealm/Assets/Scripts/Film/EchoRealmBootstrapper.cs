using UnityEngine;
using EchoRealm.Networking;
using EchoRealm.AI;

namespace EchoRealm.Film
{
    /// <summary>
    /// Main bootstrapper script that coordinates the startup sequence:
    /// 1. Wait for QR anchor to be established
    /// 2. Start Photon Fusion 2 session
    /// 3. Initialize AI systems (Ollama + Voice)
    /// 4. Start the film
    ///
    /// Attach to a persistent GameObject in MainScene (e.g., "GameManager").
    /// </summary>
    public class EchoRealmBootstrapper : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private QRAnchorManager qrAnchorManager;
        [SerializeField] private FusionNetworkManager fusionNetworkManager;
        [SerializeField] private OllamaClient ollamaClient;
        [SerializeField] private VoiceCommandProcessor voiceProcessor;
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private CommandExecutor commandExecutor;

        [Header("UI References")]
        [Tooltip("Text mesh to show status messages during setup (optional).")]
        [SerializeField] private TMPro.TextMeshPro statusText;

        [Header("Settings")]
        [Tooltip("Automatically start the film after all systems are ready.")]
        [SerializeField] private bool autoStartFilm = true;

        private enum BootState
        {
            WaitingForQRAnchor,
            ConnectingToPhoton,
            CheckingOllama,
            Ready,
            FilmStarted,
            Error
        }

        private BootState currentState = BootState.WaitingForQRAnchor;

        private void Start()
        {
            SetStatus("Scanning for QR code...\nPoint your HoloLens at the QR anchor.");

            // Auto-find components if not assigned
            if (qrAnchorManager == null) qrAnchorManager = FindObjectOfType<QRAnchorManager>();
            if (fusionNetworkManager == null) fusionNetworkManager = FindObjectOfType<FusionNetworkManager>();
            if (ollamaClient == null) ollamaClient = FindObjectOfType<OllamaClient>();
            if (voiceProcessor == null) voiceProcessor = FindObjectOfType<VoiceCommandProcessor>();
            if (narrativeManager == null) narrativeManager = FindObjectOfType<NarrativeManager>();
            if (commandExecutor == null) commandExecutor = FindObjectOfType<CommandExecutor>();

            // Subscribe to QR anchor event
            if (qrAnchorManager != null)
            {
                qrAnchorManager.OnAnchorEstablished += OnQRAnchorEstablished;
            }
            else
            {
                Debug.LogWarning("[Boot] QRAnchorManager not found. Proceeding without QR anchor.");
                OnQRAnchorEstablished();
            }
        }

        private async void OnQRAnchorEstablished()
        {
            Debug.Log("[Boot] QR Anchor established. Starting Photon session...");
            currentState = BootState.ConnectingToPhoton;
            SetStatus("QR anchor set!\nConnecting to Photon...");

            // Step 2: Start Photon Fusion 2 session
            if (fusionNetworkManager != null)
            {
                fusionNetworkManager.OnSessionJoined += OnPhotonSessionJoined;
                await fusionNetworkManager.StartSession();
            }
            else
            {
                Debug.LogWarning("[Boot] FusionNetworkManager not found. Proceeding without networking.");
                OnPhotonSessionJoined();
            }
        }

        private async void OnPhotonSessionJoined()
        {
            string masterStatus = fusionNetworkManager != null && fusionNetworkManager.IsMaster
                ? " (You are MASTER)"
                : " (Connected as CLIENT)";

            Debug.Log($"[Boot] Photon session joined.{masterStatus}");
            currentState = BootState.CheckingOllama;
            SetStatus($"Photon connected!{masterStatus}\nChecking Ollama AI server...");

            // Step 3: Check Ollama connection
            bool ollamaOk = false;
            if (ollamaClient != null)
            {
                ollamaOk = await ollamaClient.CheckServerConnection();
            }

            if (ollamaOk)
            {
                Debug.Log("[Boot] Ollama server is reachable. AI features enabled.");
                SetStatus("All systems ready!\nAI features: ENABLED");
            }
            else
            {
                Debug.LogWarning("[Boot] Ollama not reachable. AI features will be disabled.");
                SetStatus("All systems ready!\nAI features: DISABLED (Ollama not found)");
            }

            // Step 4: Ready
            currentState = BootState.Ready;

            if (autoStartFilm)
            {
                // Small delay so user can read the status
                await System.Threading.Tasks.Task.Delay(2000);
                StartFilm();
            }
        }

        /// <summary>
        /// Start the EchoRealm film experience.
        /// </summary>
        public void StartFilm()
        {
            if (currentState == BootState.FilmStarted) return;

            currentState = BootState.FilmStarted;
            Debug.Log("[Boot] Starting EchoRealm film!");
            SetStatus(""); // Clear status text

            // Start voice listening
            if (voiceProcessor != null)
                voiceProcessor.StartListening();

            // Narrative starts at Act 1
            if (narrativeManager != null)
                Debug.Log($"[Boot] Narrative manager ready. Act: {narrativeManager.CurrentAct}");

            // TODO: Trigger Act 1 intro sequence (Oracle appears, intro dialogue)
            // This will be implemented in FilmDirector.cs
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;

            if (!string.IsNullOrEmpty(message))
                Debug.Log($"[Boot] Status: {message.Replace("\n", " | ")}");
        }

        private void OnDestroy()
        {
            if (qrAnchorManager != null)
                qrAnchorManager.OnAnchorEstablished -= OnQRAnchorEstablished;
            if (fusionNetworkManager != null)
                fusionNetworkManager.OnSessionJoined -= OnPhotonSessionJoined;
        }
    }
}
