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
            "Welcome to EchoRealm.",
            "This world listens. Your voice shapes reality.",
            "Find the Origin Echo to return home.",
            "Speak... and the world will answer."
        };
        [SerializeField] private float introLinePause = 3f;

        [Header("Act 3 — Cooperative Challenge")]
        [Tooltip("The obstacle GameObject that appears in Act 3.")]
        [SerializeField] private GameObject challengeObstacle;
        [Tooltip("Cooperation events needed to solve the challenge.")]
        [SerializeField] private int cooperationGoal = 3;

        [Header("Act 4 — Origin Echo")]
        [SerializeField] private GameObject portalEffect;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>Current act number (1-4), 0 if not started.</summary>
        public int CurrentAct { get; private set; }

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

        public void StartAct(int actNumber)
        {
            CurrentAct = actNumber;
            Log($"=== ACT {actNumber} STARTED ===");
            SessionLogger.Instance?.LogEvent(EventType.ActTransition, $"Act {actNumber} started");

            switch (actNumber)
            {
                case 1: StartCoroutine(RunAct1()); break;
                case 2: StartCoroutine(RunAct2()); break;
                case 3: StartCoroutine(RunAct3()); break;
                case 4: StartCoroutine(RunAct4()); break;
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

            // Dobby reacts
            var dobby = DobbyController.Instance;
            if (dobby != null)
            {
                dobby.ShowDialogue("Where... where are we?");
                dobby.PlayAnimation("LookAround");
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
        // Act 3 — Cooperative Challenge
        // ------------------------------------------------------------------

        private IEnumerator RunAct3()
        {
            // Oracle introduces the challenge
            var oracle = OracleController.Instance;
            if (oracle != null)
            {
                oracle.SetMood("warning");
                oracle.Speak("An obstacle blocks your path. You must work together to overcome it.");
                yield return new WaitForSeconds(4f);
            }

            // Show the obstacle
            if (challengeObstacle != null)
                challengeObstacle.SetActive(true);

            Log("Act 3 active — cooperative challenge. Waiting for cooperation events...");

            // Monitor cooperation events
            var cooperation = Interaction.CooperationDetector.Instance;
            if (cooperation != null)
            {
                while (cooperation.CooperationCount < cooperationGoal)
                {
                    yield return new WaitForSeconds(1f);
                }
            }
            else
            {
                // No cooperation detector — wait a fixed time
                yield return new WaitForSeconds(60f);
            }

            // Challenge solved!
            if (oracle != null)
            {
                oracle.SetMood("excited");
                oracle.Speak("Together, you have found the way. The path is open!");
            }

            if (challengeObstacle != null)
                challengeObstacle.SetActive(false);

            yield return new WaitForSeconds(3f);

            Log("Act 3 complete.");
            OnActCompleted?.Invoke(3);
        }

        // ------------------------------------------------------------------
        // Act 4 — The Origin Echo
        // ------------------------------------------------------------------

        private IEnumerator RunAct4()
        {
            // Show portal
            if (portalEffect != null)
                portalEffect.SetActive(true);

            yield return new WaitForSeconds(2f);

            // Oracle delivers final monologue (generated by AI)
            var oracle = OracleController.Instance;
            var narrative = NarrativeManager.Instance;

            if (oracle != null && narrative != null)
            {
                oracle.SetMood("mysterious");

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

            // Dobby waves goodbye
            var dobby = DobbyController.Instance;
            if (dobby != null)
            {
                dobby.PlayAnimation("Wave");
                dobby.ShowDialogue("Goodbye, friends... until we meet again.");
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
