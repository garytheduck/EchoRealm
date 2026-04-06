using UnityEngine;
using EchoRealm.AI;

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

        private void OnActCompleted(int completedAct)
        {
            Log($"Act {completedAct} completed.");

            // Update NarrativeManager
            var narrative = NarrativeManager.Instance;
            if (narrative != null)
                narrative.AdvanceAct();

            switch (completedAct)
            {
                case 1:
                    // Start Act 2
                    act2StartTime = Time.time;
                    act2Active = true;
                    actManager.StartAct(2);
                    break;

                case 2:
                    // Start Act 3
                    actManager.StartAct(3);
                    break;

                case 3:
                    // Start Act 4
                    actManager.StartAct(4);
                    break;

                case 4:
                    // Film complete
                    EndFilm();
                    break;
            }
        }

        private void OnDestroy()
        {
            if (actManager != null)
                actManager.OnActCompleted -= OnActCompleted;
        }

        private void Log(string message)
        {
            if (logEvents)
                Debug.Log($"[FilmDirector] {message}");
        }
    }
}
