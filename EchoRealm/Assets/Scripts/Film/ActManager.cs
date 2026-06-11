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

        [Header("Act 4 — Diverging Endings (final Oracle line per AI-chosen outcome)")]
        [Tooltip("'triumphant' Passage ending — the traveler goes home through the portal.")]
        [TextArea(2, 3)]
        [SerializeField] private string passageBlessing =
            "Go now, traveler. The grove will remember you both.";
        [Tooltip("'bittersweet' Reckoning ending — the world burns, the portal collapses, the traveler stays.")]
        [TextArea(2, 3)]
        [SerializeField] private string reckoningBlessing =
            "The door has closed, traveler. Until the grove heals, you remain among us.";
        [Tooltip("'mysterious' Sealed Door ending — the portal never wakes; the traveler stays as guardian.")]
        [TextArea(2, 3)]
        [SerializeField] private string sealedBlessing =
            "Some doors open only when the grove is ready. Stay, watcher — your answer is already growing.";

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>Current act number (1-4), 0 if not started.</summary>
        public int CurrentAct { get; private set; }

        /// <summary>The AI-chosen variant for the current act. Null if default/AI unavailable.</summary>
        public AI.AINarrativeDecision CurrentDecision { get; private set; }

        /// <summary>Variant key of the act this device last applied (via StartAct or ApplyActState).
        /// FilmSync.DoRewind reads it to keep the networked ChosenVariant in step with the rewound
        /// act, so peers reconstruct the correct obstacle/ending after a rewind.</summary>
        public string CurrentVariant { get; private set; } = "default";

        /// <summary>Fired when an act completes.</summary>
        public event Action<int> OnActCompleted;

        /// <summary>Fired when an act starts (act number, variant). Observational — used by
        /// TimelineRecorder. No effect if unsubscribed.</summary>
        public static event System.Action<int, string> OnActStarted;

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
            StopAllCoroutines();          // stop any prior act coroutine (e.g. a client's lingering Act 3 wait)
            DeactivateTrialObstacles();   // clear Act-3 trial visuals on every transition (incl. on clients)

            CurrentAct    = actNumber;
            CurrentDecision = decision;
            CurrentVariant  = decision?.chosen_variant ?? "default";
            OnActStarted?.Invoke(actNumber, decision?.chosen_variant ?? "default");

            // The portal is Act-4 state — clear it whenever an earlier act (re)starts, so a rewind
            // out of Act 4 never leaves a stale portal (or a vanished one) in the scene.
            if (portalEffect != null && actNumber != 4) portalEffect.SetActive(false);

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

        /// <summary>Replay/rewind helper: set an act's VISUAL state only — no coroutines, no
        /// Oracle narration, no waits. Mirrors the obstacle/portal setup that the act coroutines
        /// perform, so a reconstructed scene matches without re-performing the act.</summary>
        public void ApplyActState(int actNumber, string variant)
        {
            StopAllCoroutines();
            DeactivateTrialObstacles();
            CurrentAct = actNumber;
            CurrentVariant = string.IsNullOrEmpty(variant) ? "default" : variant;

            // Portal is Act-4 state: off for every non-Act-4 reconstruction (fixes the latent leak
            // where rewinding out of Act 4 left the portal visible).
            if (portalEffect != null && actNumber != 4) portalEffect.SetActive(false);

            if (actNumber == 3)
            {
                var obstacle = PickObstacleForVariant(variant ?? "default");
                if (obstacle != null) obstacle.SetActive(true);
            }
            else if (actNumber == 4)
            {
                // End-state per ending: the Passage leaves the portal open; the Reckoning
                // (collapsed) and the Sealed Door (faded) end with it gone. World effects
                // (fire/night/fog/close_path) are recorded WorldCommand events and replay separately.
                if (portalEffect != null) portalEffect.SetActive(!IsPortalGoneEnding(variant));
            }
        }

        /// <summary>Endings whose final state has NO portal (it collapsed or stayed sealed).</summary>
        private static bool IsPortalGoneEnding(string variant)
            => variant == "bittersweet" || variant == "mysterious";

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

        private void DeactivateTrialObstacles()
        {
            if (challengeObstacle != null)         challengeObstacle.SetActive(false);
            if (obstacleChaoticVariant != null)    obstacleChaoticVariant.SetActive(false);
            if (obstacleMysteriousVariant != null) obstacleMysteriousVariant.SetActive(false);
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
        // Act 4 — The Origin Echo (AI-variant aware, DIVERGING OUTCOMES)
        // ------------------------------------------------------------------
        //
        // The variant the AI chose at the Act 3→4 transition decides what actually HAPPENS,
        // not just what the Oracle says:
        //   triumphant  → The Passage:     the grove rewards them; the traveler goes home.
        //   bittersweet → The Reckoning:   the grove retaliates — fire, night, the path slams
        //                                  shut, the portal collapses; the traveler CANNOT leave.
        //   mysterious  → The Sealed Door: mist falls, the portal never wakes; the traveler
        //                                  stays as the grove's guardian.
        //   default / unknown → identical to the original fixed ending (AI unavailable).
        //
        // World changes go through CommandExecutor.ExecuteCommand so they behave exactly like
        // player-spoken commands: identical on every device (each device runs this coroutine
        // from the same RPC_StartAct) and recorded in the timeline for replay/rewind.

        // Session summary snapshotted at Act-4 start, BEFORE the ending stages its own scripted
        // world commands — so the final monologue reflects what the PLAYERS did, not the finale.
        private string _act4SessionSummary;

        private IEnumerator RunAct4(AI.AINarrativeDecision decision)
        {
            _act4SessionSummary = NarrativeManager.Instance?.BuildSessionSummary();

            switch (decision?.chosen_variant)
            {
                case "bittersweet": return RunEndingReckoning(decision);
                case "mysterious":  return RunEndingSealed(decision);
                default:            return RunEndingPassage(decision); // triumphant / default
            }
        }

        /// <summary>'triumphant' (and the AI-unavailable default): the homecoming. With the
        /// triumphant variant the grove also celebrates — daylight, butterflies, fireflies.</summary>
        private IEnumerator RunEndingPassage(AI.AINarrativeDecision decision)
        {
            string mood       = decision?.mood            ?? "mysterious";
            string oracleLine = decision?.oracle_narration ?? "";
            bool celebrate    = decision?.chosen_variant == "triumphant";

            if (portalEffect != null)
                portalEffect.SetActive(true);

            yield return new WaitForSeconds(2f);

            var oracle = OracleController.Instance;
            if (oracle != null && !string.IsNullOrEmpty(oracleLine))
            {
                oracle.SetMood(mood);
                oracle.Speak(oracleLine);
                yield return new WaitForSeconds(4f);
            }

            // The grove rewards a nurturing session (recorded world commands, like spoken ones).
            if (celebrate)
            {
                ExecCmd("day");
                ExecCmd("spawn_butterflies");
                ExecCmd("spawn_fireflies");
                yield return new WaitForSeconds(2f);
            }

            yield return SpeakFinalMonologue(mood);

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
                oracle.Speak(passageBlessing);
                yield return new WaitForSeconds(3f);
            }

            Log("Act 4 complete — ending: Passage (traveler went home). Film ended.");
            OnActCompleted?.Invoke(4);
        }

        /// <summary>'bittersweet': the grove retaliates against a chaotic session — the world
        /// catches fire, the path slams shut and the portal collapses. The traveler cannot leave.</summary>
        private IEnumerator RunEndingReckoning(AI.AINarrativeDecision decision)
        {
            string mood       = decision?.mood            ?? "scared";
            string oracleLine = decision?.oracle_narration ?? "";

            if (portalEffect != null)
                portalEffect.SetActive(true);

            yield return new WaitForSeconds(2f);

            var oracle = OracleController.Instance;
            if (oracle != null && !string.IsNullOrEmpty(oracleLine))
            {
                oracle.SetMood(mood);
                oracle.Speak(oracleLine);
                yield return new WaitForSeconds(4f);
            }

            // The grove answers the chaos it was shown (all recorded world commands).
            ExecCmd("lightning");
            yield return new WaitForSeconds(1f);
            ExecCmd("night");
            ExecCmd("fire");
            yield return new WaitForSeconds(1.5f);
            ExecCmd("earthquake");
            ExecCmd("close_path");
            yield return new WaitForSeconds(2.5f);

            if (oracle != null)
            {
                oracle.SetMood("scared");
                oracle.Speak("The grove remembers every storm you raised. Now it answers.");
                yield return new WaitForSeconds(4f);
            }

            var astronaut = AstronautController.Instance;
            if (astronaut != null)
            {
                astronaut.ReactToEvent("fire");
                yield return new WaitForSeconds(2f);
            }

            // The portal gutters and collapses — the way home is gone.
            yield return FlickerPortal(times: 3, interval: 0.35f, endActive: false);
            yield return new WaitForSeconds(2f);

            yield return SpeakFinalMonologue(mood);

            if (astronaut != null)
            {
                astronaut.PlayAnimation("Wave"); // a farewell to the home he cannot reach
                yield return new WaitForSeconds(2f);
            }

            if (oracle != null)
            {
                oracle.Speak(reckoningBlessing);
                yield return new WaitForSeconds(3f);
            }

            Log("Act 4 complete — ending: Reckoning (portal collapsed; traveler remains). Film ended.");
            OnActCompleted?.Invoke(4);
        }

        /// <summary>'mysterious': the grove withholds judgment from a watchful session — mist
        /// falls, the portal never wakes, and the traveler stays as the grove's guardian.</summary>
        private IEnumerator RunEndingSealed(AI.AINarrativeDecision decision)
        {
            string mood       = decision?.mood            ?? "mysterious";
            string oracleLine = decision?.oracle_narration ?? "";

            if (portalEffect != null)
                portalEffect.SetActive(true);

            yield return new WaitForSeconds(2f);

            var oracle = OracleController.Instance;
            if (oracle != null && !string.IsNullOrEmpty(oracleLine))
            {
                oracle.SetMood(mood);
                oracle.Speak(oracleLine);
                yield return new WaitForSeconds(4f);
            }

            // Twilight settles over the grove (recorded world commands).
            ExecCmd("night");
            ExecCmd("fog");
            ExecCmd("spawn_fireflies");
            yield return new WaitForSeconds(3f);

            if (oracle != null)
            {
                oracle.SetMood("mysterious");
                oracle.Speak("You watched more than you shaped. The door, too, only watches.");
                yield return new WaitForSeconds(4f);
            }

            var astronaut = AstronautController.Instance;
            if (astronaut != null)
            {
                astronaut.LookAround();
                yield return new WaitForSeconds(2f);
            }

            // The dormant portal dims and fades — sealed until the grove decides otherwise.
            yield return FlickerPortal(times: 2, interval: 0.8f, endActive: false);
            yield return new WaitForSeconds(2f);

            yield return SpeakFinalMonologue(mood);

            if (astronaut != null)
            {
                astronaut.PlayAnimation("Wave");
                yield return new WaitForSeconds(2f);
            }

            if (oracle != null)
            {
                oracle.Speak(sealedBlessing);
                yield return new WaitForSeconds(3f);
            }

            Log("Act 4 complete — ending: Sealed Door (portal dormant; traveler stays as guardian). Film ended.");
            OnActCompleted?.Invoke(4);
        }

        // ------------------------------------------------------------------
        // Act 4 — shared ending beats
        // ------------------------------------------------------------------

        /// <summary>Shared beat: the AI-generated final monologue, dramatic word-by-word reveal.</summary>
        private IEnumerator SpeakFinalMonologue(string mood)
        {
            var oracle = OracleController.Instance;
            var narrative = NarrativeManager.Instance;
            if (oracle == null || narrative == null) yield break;

            oracle.SetMood(mood);

            // Start async monologue generation and wait for it in coroutine
            var monologueTask = narrative.GenerateFinalMonologue(_act4SessionSummary);
            yield return new WaitUntil(() => monologueTask.IsCompleted);
            string monologue = monologueTask.Result;

            oracle.SpeakDramatic(monologue, wordsPerSecond: 2f);

            // Wait for monologue to finish (estimate from word count)
            float monologueDuration = monologue.Split(' ').Length / 2f + 3f;
            yield return new WaitForSeconds(monologueDuration);
        }

        /// <summary>Blink the portal, ending in the given state (collapse/fade beat).</summary>
        private IEnumerator FlickerPortal(int times, float interval, bool endActive)
        {
            if (portalEffect == null) yield break;
            for (int i = 0; i < times; i++)
            {
                portalEffect.SetActive(false);
                yield return new WaitForSeconds(interval);
                portalEffect.SetActive(true);
                yield return new WaitForSeconds(interval);
            }
            portalEffect.SetActive(endActive);
        }

        /// <summary>Run a world command through CommandExecutor so it is identical on every device
        /// AND recorded in the timeline like a player-spoken command. No-op if missing.</summary>
        private void ExecCmd(string command) => CommandExecutor.Instance?.ExecuteCommand(command);

        private void Log(string message)
        {
            if (logEvents)
                Debug.Log($"[ActManager] {message}");
        }
    }
}
