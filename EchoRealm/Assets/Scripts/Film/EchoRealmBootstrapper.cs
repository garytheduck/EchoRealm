using UnityEngine;
using EchoRealm.Networking;
using EchoRealm.AI;
using System.Threading.Tasks;

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
        [SerializeField] private AIManager aiManager;
        [SerializeField] private VoiceCommandProcessor voiceProcessor;
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private CommandExecutor commandExecutor;

        [Header("UI References")]
        [Tooltip("Text mesh to show status messages during setup (optional).")]
        [SerializeField] private TMPro.TextMeshPro statusText;

        [Header("Settings")]
        [Tooltip("Automatically start the film after all systems are ready (only used when Require Start Command is OFF).")]
        [SerializeField] private bool autoStartFilm = true;

        [Tooltip("If true (default), the grove + characters appear but the acts DON'T begin until a viewer says " +
                 "\"START\". The master then starts Act 1 on every headset together. Uncheck to fall back to auto-start.")]
        [SerializeField] private bool requireStartCommand = true;

        [Tooltip("If QR code isn't detected after this many seconds, skip QR anchoring and continue with default origin.")]
        [SerializeField] private float qrTimeoutSeconds = 8f;

        [Tooltip("If true, the app will NOT proceed until a QR code is scanned (required for multi-device co-location). " +
                 "If false, it skips QR after the timeout and runs un-anchored (solo/dev testing).")]
        [SerializeField] private bool requireQR = false;

        private bool qrAnchorHandled = false;

        [Header("Replay Gate (optional)")]
        [Tooltip("Assign a ReplayModeGate in the scene to show a Live/Replay chooser at startup. " +
                 "If left empty the live boot proceeds immediately as before.")]
        [SerializeField] private ReplayModeGate replayGate;

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
            if (replayGate == null) replayGate = FindObjectOfType<ReplayModeGate>(true);
            if (replayGate != null) { replayGate.ShowChooser(); return; } // user picks Live vs Saved
            BeginLiveBoot(); // no gate present → behave exactly as before
        }

        /// <summary>Original live-film boot sequence. Public so ReplayModeGate can invoke it when
        /// the user selects "Start Live Film". When no ReplayModeGate is in the scene, Start()
        /// calls this directly — zero change to existing boot behaviour.</summary>
        public void BeginLiveBoot()
        {
            SetStatus("Scanning for QR code...\nPoint your HoloLens at the QR anchor.");

            // Auto-find components if not assigned
            if (qrAnchorManager == null) qrAnchorManager = FindObjectOfType<QRAnchorManager>();
            if (fusionNetworkManager == null) fusionNetworkManager = FindObjectOfType<FusionNetworkManager>();
            if (aiManager == null) aiManager = FindObjectOfType<AIManager>();
            if (voiceProcessor == null) voiceProcessor = FindObjectOfType<VoiceCommandProcessor>();
            if (narrativeManager == null) narrativeManager = FindObjectOfType<NarrativeManager>();
            if (commandExecutor == null) commandExecutor = FindObjectOfType<CommandExecutor>();

            // Subscribe to QR anchor event
            if (qrAnchorManager != null)
            {
                qrAnchorManager.OnAnchorEstablished += OnQRAnchorEstablished;
                // Start timeout coroutine to skip QR if it's not scanned in time
                StartCoroutine(QRTimeoutFallback());
            }
            else
            {
                Debug.LogWarning("[Boot] QRAnchorManager not found. Proceeding without QR anchor.");
                OnQRAnchorEstablished();
            }
        }

        private System.Collections.IEnumerator QRTimeoutFallback()
        {
            yield return new WaitForSeconds(qrTimeoutSeconds);
            if (qrAnchorHandled) yield break;

            if (requireQR)
            {
                // Multi-device co-located session: do NOT proceed un-anchored.
                Debug.LogWarning($"[Boot] QR not detected after {qrTimeoutSeconds}s. requireQR=true → still waiting for the shared QR anchor.");
                SetStatus("Still scanning for QR code...\nCo-location needs the shared QR anchor.\nLook at the EchoRealm-Anchor code.");
                yield break; // a real QR detection will fire OnQRAnchorEstablished and continue boot
            }

            Debug.LogWarning($"[Boot] QR code not detected after {qrTimeoutSeconds}s. requireQR=false → continuing un-anchored (NOT co-located).");
            SetStatus("No QR code detected.\nContinuing without spatial anchor...");
            OnQRAnchorEstablished();
        }

        private async void OnQRAnchorEstablished()
        {
            // Guard against double-invocation (event + timeout)
            if (qrAnchorHandled) return;
            qrAnchorHandled = true;

            // If this was a real QR scan (not the timeout fallback), confirm on the
            // boot label and auto-hide it after 2 seconds.
            if (qrAnchorManager != null && qrAnchorManager.IsAnchored)
                BootStatusLabel.Instance?.FlashThenDismiss("QR code detected!\nSpatial anchor set.", 2f);

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

            // Step 3: Check AI backends
            if (aiManager != null)
                await aiManager.CheckAllConnectionsAsync();

            bool aiOk = aiManager != null && aiManager.IsReachable;
            string aiStatus = aiManager != null
                ? $"Ollama:{(aiManager.IsOllamaReachable ? "✓" : "✗")}  Claude:{(aiManager.IsClaudeReachable ? "✓" : "✗")}"
                : "no AIManager";

            if (aiOk)
            {
                Debug.Log($"[Boot] AI ready — {aiStatus}");
                SetStatus($"All systems ready!\nAI: ENABLED ({aiStatus})");
            }
            else
            {
                Debug.LogWarning($"[Boot] No AI backend reachable — {aiStatus}. AI features disabled.");
                SetStatus("All systems ready!\nAI: DISABLED (no backend reachable)");
            }

            // Step 4: Ready
            currentState = BootState.Ready;

            if (requireStartCommand)
            {
                // START-gated: show the grove + characters and listen, but don't begin the acts
                // until a viewer says "START" (networked — the master then starts Act 1 for everyone).
                ArmStartCommand();
            }
            else if (autoStartFilm)
            {
                // Small delay so user can read the status
                await System.Threading.Tasks.Task.Delay(2000);
                StartFilm();
            }
        }

        /// <summary>
        /// Ready, but waiting for the spoken "START" command. The grove and characters are already
        /// visible; start the mic so we can hear "START", but DON'T begin the acts. When any headset
        /// says "START", FilmSync starts Act 1 on every device together.
        /// </summary>
        private void ArmStartCommand()
        {
            Debug.Log("[Boot] Ready — waiting for the spoken 'START' command before the film begins.");
            if (voiceProcessor != null)
                voiceProcessor.StartListening();
            SetStatus("The grove awaits.\nSay \"START\" to begin.");
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

            // Start the film via FilmDirector
            var filmDirector = FindObjectOfType<FilmDirector>();
            if (filmDirector != null)
            {
                filmDirector.StartFilm();
            }
            else
            {
                Debug.LogWarning("[Boot] FilmDirector not found. Film will not auto-start.");
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;

            // Also drive the runtime billboarded boot label, if present.
            BootStatusLabel.Instance?.Show(message);

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
