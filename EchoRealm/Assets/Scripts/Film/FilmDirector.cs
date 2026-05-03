using UnityEngine;
using EchoRealm.AI;
using System.Threading.Tasks;

namespace EchoRealm.Film
{
    /// <summary>
    /// Top-level orchestrator for the EchoRealm film experience.
    /// Controls act flow, timing, and transitions.
    ///
    /// Flow: Act 1 → Act 2 → Act 3 → Act 4 → End
    ///
    /// Act 2 auto-advances based on either:
    /// - Minimum voice commands given (default 5), OR
    /// - Maximum time elapsed (default 180s)
    ///
    /// Called by EchoRealmBootstrapper.StartFilm() to begin.
    /// </summary>
    public class FilmDirector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ActManager actManager;

        [Header("Act 2 Timing")]
        [Tooltip("Minimum voice commands before Act 2 can end.")]
        [SerializeField] private int minVoiceCommands = 5;
        [Tooltip("Maximum time (seconds) for Act 2 before auto-advancing.")]
        [SerializeField] private float act2MaxDuration = 180f;

        [Header("End Screen")]
        [Tooltip("GameObject to activate when the film ends (fade-out panel, credits, etc.).")]
        [SerializeField] private GameObject endScreen;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>True if the film is currently playing.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>True if the film has ended.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>Current act number (1-4).</summary>
        public int CurrentAct => actManager != null ? actManager.CurrentAct : 0;

        public static FilmDirector Instance { get; private set; }

        private float act2StartTime;
        private bool act2Active;

        // The AI-chosen variants for acts 3 and 4, decided at the previous transition.
        // Null until the decision is made; ActManager uses these when starting each act.
        private AINarrativeDecision _act3Decision;
        private AINarrativeDecision _act4Decision;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (actManager == null)
                actManager = FindObjectOfType<ActManager>();

            if (actManager != null)
                actManager.OnActCompleted += OnActCompleted;
        }

        private void Update()
        {
            // Monitor Act 2 completion criteria
            if (act2Active)
            {
                bool timeUp = Time.time - act2StartTime >= act2MaxDuration;
                bool enoughCommands = false;

                var narrative = NarrativeManager.Instance;
                if (narrative != null)
                    enoughCommands = narrative.VoiceCommandLog.Count >= minVoiceCommands;

                if (timeUp || enoughCommands)
                {
                    act2Active = false;
                    string reason = timeUp ? "time limit reached" : $"voice commands reached ({minVoiceCommands})";
                    Log($"Act 2 advancing: {reason}");
                    actManager.CompleteAct2();
                }
            }
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Start the film from Act 1.
        /// Called by EchoRealmBootstrapper after all systems are ready.
        /// </summary>
        public void StartFilm()
        {
            if (IsPlaying) return;

            IsPlaying = true;
            IsFinished = false;
            Log("Film STARTED.");

            SessionLogger.Instance?.LogEvent(EventType.System, "Film started");

            actManager.StartAct(1);
        }

        /// <summary>
        /// Skip to a specific act (for testing purposes).
        /// </summary>
        public void SkipToAct(int actNumber)
        {
            if (actNumber < 1 || actNumber > 4) return;
            Log($"Skipping to Act {actNumber}");
            act2Active = false;
            actManager.StartAct(actNumber);
        }

        /// <summary>
        /// End the film immediately (emergency stop).
        /// </summary>
        public void EndFilm()
        {
            IsPlaying = false;
            IsFinished = true;
            act2Active = false;

            // Stop voice recognition
            var voice = VoiceCommandProcessor.Instance;
            if (voice != null)
                voice.StopListening();

            Log("Film ENDED.");
            SessionLogger.Instance?.LogEvent(EventType.System, "Film ended");

            // Show end screen
            if (endScreen != null)
                endScreen.SetActive(true);

            // Log session summary
            var logger = SessionLogger.Instance;
            if (logger != null)
            {
                string log = logger.ExportLog();
                Debug.Log(log);
            }
        }

        // ------------------------------------------------------------------
        // Act Transitions
        // ------------------------------------------------------------------

        private async void OnActCompleted(int completedAct)
        {
            Log($"Act {completedAct} completed.");

            // Update NarrativeManager
            NarrativeManager.Instance?.AdvanceAct();

            // Reset rolling action window so AI gets act-scoped context
            ActionCollector.Instance?.ResetForNewAct();

            switch (completedAct)
            {
                case 1:
                    act2StartTime = Time.time;
                    act2Active    = true;
                    actManager.StartAct(2, null);
                    break;

                case 2:
                    // Ask AI which Act 3 to play based on player behavior so far.
                    // Only the MASTER HoloLens makes this decision; the chosen variant key
                    // must be broadcast to all peers so every device runs the same act.
                    // TODO: add FusionNetworkManager.BroadcastActVariant(decision) here.
                    _act3Decision = await RequestActDecision(fromAct: 2, toAct: 3);
                    actManager.StartAct(3, _act3Decision);
                    break;

                case 3:
                    // Ask AI which Act 4 ending to play based on how Act 3 went.
                    _act4Decision = await RequestActDecision(fromAct: 3, toAct: 4);
                    actManager.StartAct(4, _act4Decision);
                    break;

                case 4:
                    EndFilm();
                    break;
            }
        }

        /// <summary>
        /// Wrapper that calls NarrativeDecisionEngine and logs the result.
        /// Returns null if the engine is not present (acts fall back to defaults internally).
        /// </summary>
        private async Task<AINarrativeDecision> RequestActDecision(int fromAct, int toAct)
        {
            var engine = NarrativeDecisionEngine.Instance;
            if (engine == null)
            {
                Log($"NarrativeDecisionEngine not found. Act {toAct} will use defaults.", isWarning: true);
                return null;
            }

            Log($"Requesting AI decision for Act {fromAct}→{toAct}...");
            var decision = await engine.RequestDecisionAsync(fromAct, toAct);

            Log($"Act {toAct} variant chosen: '{decision.chosen_variant}' | mood: '{decision.mood}'");
            SessionLogger.Instance?.LogEvent(EventType.System,
                $"Act{toAct} variant='{decision.chosen_variant}' reason='{decision.narrative_reason}'");

            return decision;
        }

        private void Log(string message, bool isWarning = false)
        {
            if (!logEvents) return;
            if (isWarning) Debug.LogWarning($"[FilmDirector] {message}");
            else           Debug.Log($"[FilmDirector] {message}");
        }

        private void OnDestroy()
        {
            if (actManager != null)
                actManager.OnActCompleted -= OnActCompleted;
        }
    }
}
