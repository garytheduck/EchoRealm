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
        [Tooltip("Minimum voice commands (pooled across BOTH headsets) before Act 2 can end. Higher = the " +
                 "AI gathers more input before choosing the branch. NOTE: the value saved on the FilmDirector " +
                 "in the scene wins over this default — change it in the Inspector too.")]
        [SerializeField] private int minVoiceCommands = 10;
        [Tooltip("Max seconds for Act 2 before auto-advancing even if minVoiceCommands wasn't reached. " +
                 "Raise this so there's actually time to collect that many commands.")]
        [SerializeField] private float act2MaxDuration = 300f;

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

        /// <summary>True if this device drives the film (the Photon master, or solo/editor with no Fusion master).</summary>
        private bool IsMaster =>
            EchoRealm.Networking.FusionNetworkManager.Instance == null ||
            EchoRealm.Networking.FusionNetworkManager.Instance.IsMaster;

        /// <summary>Start an act through FilmSync (networked) when available, else locally (solo/editor).</summary>
        private void GoToAct(int act, AINarrativeDecision decision)
        {
            if (EchoRealm.Networking.FilmSync.Instance != null)
                EchoRealm.Networking.FilmSync.Instance.DriveAct(act, decision);
            else if (actManager != null)
                actManager.StartAct(act, decision);
        }

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

            // Disruption-aware pacing (additive): scores audience interventions and lets heavy
            // disruption trigger the AI decision early; without it the film paces as before.
            if (DisruptionMeter.Instance == null)
                gameObject.AddComponent<DisruptionMeter>();
        }

        private void Update()
        {
            if (!IsMaster) return; // only the master advances the film

            // Pause the film's progression while the world is pocketed (networking stays live).
            if (EchoRealm.Interaction.WorldPocket.Instance != null &&
                EchoRealm.Interaction.WorldPocket.Instance.IsPocketed) return;

            // Monitor Act 2 completion criteria using the COMBINED behavior profile
            // (the master pools every headset's voice commands into ActionCollector).
            if (act2Active)
            {
                bool timeUp = Time.time - act2StartTime >= act2MaxDuration;
                int voiceCount = AI.ActionCollector.Instance != null
                    ? AI.ActionCollector.Instance.Profile.VoiceCommandCount
                    : 0;
                bool enoughCommands = voiceCount >= minVoiceCommands;

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
            if (!IsMaster)
            {
                Log("Not master — the master drives the film via FilmSync. Skipping local StartFilm.");
                return;
            }

            IsPlaying = true;
            IsFinished = false;
            Log("Film STARTED.");

            // Clear any interactions made BEFORE the film began (boot/QR experimentation): otherwise
            // pre-film commands inflate the behavior profile and can instantly satisfy the Act-2 voice
            // quota (skipping the ambient beats) or the disruption score. The film starts from zero.
            AI.ActionCollector.Instance?.Profile.Reset();
            AI.ActionCollector.Instance?.ResetForNewAct();

            SessionLogger.Instance?.LogEvent(EventType.System, "Film started");

            GoToAct(1, null);
        }

        /// <summary>Disruption hook: complete Act 2 NOW (heavy audience disruption), so the AI
        /// decides the branch immediately instead of waiting for the command quota / timer.
        /// Clears act2Active BEFORE completing — calling ActManager.CompleteAct2 directly would
        /// let Update fire it a second time (double AI decision + double act transition).</summary>
        public void ForceCompleteAct2(string reason)
        {
            if (!IsMaster || !act2Active || actManager == null) return;
            act2Active = false;
            Log($"Act 2 advancing EARLY: {reason}");
            actManager.CompleteAct2();
        }

        /// <summary>
        /// Skip to a specific act (for testing purposes).
        /// </summary>
        public void SkipToAct(int actNumber)
        {
            if (actNumber < 1 || actNumber > 4) return;
            Log($"Skipping to Act {actNumber}");
            act2Active = false;
            GoToAct(actNumber, null);
        }

        /// <summary>Rewind helper: re-arm the act-flow state machine to the act active at T, so the
        /// film resumes correctly (and re-makes later AI decisions). Decisions for acts not yet
        /// reached are cleared. Additive — never called by the live flow.</summary>
        public void RewindToAct(int act)
        {
            IsPlaying = true; IsFinished = false;
            if (act < 4) _act4Decision = null;
            if (act < 3) _act3Decision = null;
            act2Active = (act == 2);
            if (act == 2) act2StartTime = Time.time;
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

            // Offer to save the recorded scene (master holds the authoritative timeline).
            if (IsMaster) SceneSavePrompt.Instance?.Show();
        }

        // ------------------------------------------------------------------
        // Act Transitions
        // ------------------------------------------------------------------

        private async void OnActCompleted(int completedAct)
        {
            if (!IsMaster) return; // clients replay acts but never drive transitions

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
                    GoToAct(2, null);
                    break;

                case 2:
                    // Ask AI which Act 3 to play based on the COMBINED behavior so far.
                    // GoToAct → FilmSync.DriveAct broadcasts the chosen variant to every peer.
                    _act3Decision = await RequestActDecision(fromAct: 2, toAct: 3);
                    GoToAct(3, _act3Decision);
                    break;

                case 3:
                    // Default-ending floor: an audience that mostly WATCHED gets the guaranteed
                    // outcome — the traveler goes home — without consulting the AI at all.
                    var meter = DisruptionMeter.Instance;
                    if (meter != null && meter.TotalPoints < meter.DefaultEndingFloor)
                    {
                        Log($"Low disruption ({meter.TotalPoints:F1} pts < floor {meter.DefaultEndingFloor:F0}) — " +
                            "default Passage ending, AI skipped.");
                        _act4Decision = null;
                        GoToAct(4, null);
                        break;
                    }

                    // Ask AI which Act 4 ending to play based on how Act 3 went.
                    _act4Decision = await RequestActDecision(fromAct: 3, toAct: 4);
                    GoToAct(4, _act4Decision);
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
