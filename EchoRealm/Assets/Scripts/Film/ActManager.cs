using System;
using System.Collections;
using UnityEngine;
using EchoRealm.AI;
using EchoRealm.Characters;

namespace EchoRealm.Film
{
    /// <summary>
    /// Manages the logic for each act of the EchoRealm film.
    /// Called by FilmDirector to start/end individual acts.
    ///
    /// Act 1: Awakening (30-45s) — intro, Oracle appears, explains EchoRealm
    /// Act 2: The World Responds (2-3min) — voice commands transform the world
    /// Act 3: Cooperative Challenge (1-2min) — obstacle requiring teamwork
    /// Act 4: The Origin Echo (30-45s) — final monologue, portal, farewell
    /// </summary>
    public class ActManager : MonoBehaviour
    {
        [Header("Act 1 — Awakening")]
        [SerializeField] private string[] oracleIntroLines = new string[]
        {
            "Welcome, travelers, to EchoRealm.",
            "This grove lives, and it listens. Your voice shapes what grows here.",
            "A wanderer has fallen among us, far from home.",
            "Find the Origin Echo — the grove's heart — and you may send him back.",
            "Speak... and the world will answer."
        };
        [SerializeField] private float introLinePause = 3f;

        [Header("Act 3 — Cooperative Challenge")]
        [Tooltip("Default obstacle (used for 'cooperative' variant or when AI is unavailable).")]
        [SerializeField] private GameObject challengeObstacle;
        [Tooltip("Obstacle for the 'chaotic' variant (voice-storm challenge).")]
        [SerializeField] private GameObject obstacleChaoticVariant;
        [Tooltip("Obstacle for the 'mysterious' variant (hidden glyphs/symbols challenge).")]
        [SerializeField] private GameObject obstacleMysteriousVariant;
        [Tooltip("Default cooperation events needed to solve the challenge.")]
        [SerializeField] private int cooperationGoal = 3;

        [Header("Act 4 — Origin Echo")]
        [SerializeField] private GameObject portalEffect;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>Current act number (1-4), 0 if not started.</summary>
        public int CurrentAct { get; private set; }

        /// <summary>The AI-chosen variant for the current act. Null if default/AI unavailable.</summary>
        public AI.AINarrativeDecision CurrentDecision { get; private set; }

        /// <summary>Fired when an act completes.</summary>
        public event Action<int> OnActCompleted;

        public static ActManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // ------------------------------------------------------------------
        // Public API — called by FilmDirector
        // ------------------------------------------------------------------

        /// <summary>
        /// Start an act. <paramref name="decision"/> carries the AI-chosen variant and
        /// Oracle narration line. Pass null to use act defaults (AI unavailable / Act 1-2).
        /// </summary>
        public void StartAct(int actNumber, AI.AINarrativeDecision decision = null)
        {
            CurrentAct    = actNumber;
            CurrentDecision = decision;

            string variant = decision?.chosen_variant ?? "default";
            Log($"=== ACT {actNumber} STARTED (variant: {variant}) ===");
            SessionLogger.Instance?.LogEvent(EventType.ActTransition,
                $"Act {actNumber} started | variant={variant}");

            switch (actNumber)
            {
                case 1: StartCoroutine(RunAct1()); break;
                case 2: StartCoroutine(RunAct2()); break;
                case 3: StartCoroutine(RunAct3(decision)); break;
                case 4: StartCoroutine(RunAct4(decision)); break;
            }
        }

        // ------------------------------------------------------------------
        // Act 1 — Awakening
        // ------------------------------------------------------------------

        private IEnumerator RunAct1()
        {
            // Brief pause for users to orient
            yield return new WaitForSeconds(2f);

            // Oracle appears
            var oracle = OracleController.Instance;
            if (oracle != null)
            {
                oracle.Appear();
                oracle.SetMood("mysterious");

                yield return new WaitForSeconds(1f);

                // Oracle delivers intro lines
                foreach (string line in oracleIntroLines)
                {
                    oracle.Speak(line);
                    yield return new WaitForSeconds(introLinePause);
                }

                yield return new WaitForSeconds(1f);
            }

            // The lost traveler stirs and looks around, startled.
            var astronaut = AstronautController.Instance;
            if (astronaut != null)
            {
                astronaut.PlayAnimation("LookAround");
                yield return new WaitForSeconds(3f);
            }

            Log("Act 1 complete.");
            OnActCompleted?.Invoke(1);
        }

        // ------------------------------------------------------------------
        // Act 2 — The World Responds
        // ------------------------------------------------------------------

        private IEnumerator RunAct2()
        {
            // Oracle encourages users to speak
            var oracle = OracleController.Instance;
            if (oracle != null)
            {
                oracle.SetMood("excited");
                oracle.Speak("Speak what you wish to see. EchoRealm listens.");
                yield return new WaitForSeconds(4f);
            }

            // Voice commands are now active (VoiceCommandProcessor is listening)
            // This act runs until FilmDirector decides to advance (time-based or command-count)
            Log("Act 2 active — voice commands enabled. Waiting for user interaction...");

            // Act 2 doesn't auto-complete — FilmDirector controls the timing
            // It listens for a minimum number of commands or a time limit
        }

        /// <summary>Called by FilmDirector when Act 2 criteria are met.</summary>
        public void CompleteAct2()
        {
            Log("Act 2 complete.");
            OnActCompleted?.Invoke(2);
        }

        // ------------------------------------------------------------------
        // Act 3 — Cooperative Challenge (AI-variant aware)
        // ------------------------------------------------------------------

        private IEnumerator RunAct3(AI.AINarrativeDecision decision)
        {
            string variant     = decision?.chosen_variant ?? "verdant";
            string mood        = decision?.mood           ?? "warning";
            string oracleLine  = decision?.oracle_narration ??
                                 "The grove's heart is hidden. Reach it — together.";

            // Oracle delivers the AI-chosen transition narration
            var oracle = OracleController.Instance;
            if (oracle != null)
            {
                oracle.SetMood(mood);
                oracle.Speak(oracleLine);
                yield return new WaitForSeconds(4f);
            }

            // Activate the obstacle matching the chosen variant
            GameObject activeObstacle = PickObstacleForVariant(variant);
            if (activeObstacle != null)
                activeObstacle.SetActive(true);

            Log($"Act 3 active — variant '{variant}'. Waiting for cooperation events...");

            // Monitor cooperation events (same win condition for all variants for now)
            var cooperation = Interaction.CooperationDetector.Instance;
            if (cooperation != null)
            {
                while (cooperation.CooperationCount < cooperationGoal)
                    yield return new WaitForSeconds(1f);
            }
            else
            {
                yield return new WaitForSeconds(60f);
            }

            // Challenge solved
            if (oracle != null)
            {
                oracle.SetMood("excited");
                oracle.Speak("Together, you have found the way. The path is open!");
            }

            if (activeObstacle != null)
                activeObstacle.SetActive(false);

            yield return new WaitForSeconds(3f);

            Log("Act 3 complete.");
            OnActCompleted?.Invoke(3);
        }

        /// <summary>Returns the correct obstacle prefab for the given variant key.</summary>
        private GameObject PickObstacleForVariant(string variant)
        {
            switch (variant)
            {
                case "scorched" when obstacleChaoticVariant    != null: return obstacleChaoticVariant;
                case "twilight"  when obstacleMysteriousVariant != null: return obstacleMysteriousVariant;
                default: return challengeObstacle; // verdant / default
            }
        }

        // ------------------------------------------------------------------
        // Act 4 — The Origin Echo (AI-variant aware)
        // ------------------------------------------------------------------

        private IEnumerator RunAct4(AI.AINarrativeDecision decision)
        {
            string mood       = decision?.mood            ?? "mysterious";
            string oracleLine = decision?.oracle_narration ?? "";

            // Show portal
            if (portalEffect != null)
                portalEffect.SetActive(true);

            yield return new WaitForSeconds(2f);

            // Oracle delivers the AI-chosen transition line first, then the full monologue
            var oracle = OracleController.Instance;
            var narrative = NarrativeManager.Instance;

            if (oracle != null && !string.IsNullOrEmpty(oracleLine))
            {
                oracle.SetMood(mood);
                oracle.Speak(oracleLine);
                yield return new WaitForSeconds(4f);
            }

            if (oracle != null && narrative != null)
            {
                oracle.SetMood(mood);

                // Start async monologue generation and wait for it in coroutine
                var monologueTask = narrative.GenerateFinalMonologue();
                yield return new WaitUntil(() => monologueTask.IsCompleted);
                string monologue = monologueTask.Result;

                oracle.SpeakDramatic(monologue, wordsPerSecond: 2f);

                // Wait for monologue to finish (estimate from word count)
                float monologueDuration = monologue.Split(' ').Length / 2f + 3f;
                yield return new WaitForSeconds(monologueDuration);
            }

            yield return new WaitForSeconds(2f);

            // Astronaut walks toward portal
            var astronaut = AstronautController.Instance;
            if (astronaut != null)
            {
                astronaut.StartPortalSequence();
                yield return new WaitForSeconds(5f);
            }

            // The Oracle gives a final blessing as the traveler departs.
            if (oracle != null)
            {
                oracle.Speak("Go now, traveler. The grove will remember you both.");
                yield return new WaitForSeconds(3f);
            }

            Log("Act 4 complete. Film ended.");
            OnActCompleted?.Invoke(4);
        }

        private void Log(string message)
        {
            if (logEvents)
                Debug.Log($"[ActManager] {message}");
        }
    }
}
