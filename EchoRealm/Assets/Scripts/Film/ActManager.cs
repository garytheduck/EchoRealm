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

        [Tooltip("Appended AFTER the intro lines: states the traveler's problem and invites the " +
                 "audience to intervene at any moment. (New field — the scene predates it, so these " +
                 "code defaults apply without any scene edit.)")]
        [TextArea(2, 3)]
        [SerializeField] private string[] introProblemLines = new[]
        {
            "This traveler fell through a broken door between worlds. The way home sleeps inside the Heart Stone.",
            "Watch his journey — or shape it. Speak at any moment, and the grove will listen. Your voices can help him… or test him."
        };

        [Header("Act 2 — Ambient Story (the film lives on its own)")]
        [Tooltip("Scripted Oracle/Astronaut story beats while waiting for (optional) audience commands.")]
        [SerializeField] private bool ambientStory = true;
        [Tooltip("Seconds after Act 2 starts before the first story beat.")]
        [SerializeField] private float ambientStartDelay = 6f;
        [Tooltip("First echo waypoint — scene-local metres relative to the Heart Stone.")]
        [SerializeField] private Vector3 echoOneOffset = new Vector3(1.2f, 0f, 0.6f);
        [Tooltip("Second echo waypoint — scene-local metres relative to the Heart Stone.")]
        [SerializeField] private Vector3 echoTwoOffset = new Vector3(-1.1f, 0f, -0.5f);
        [Tooltip("Max horizontal distance (metres, authored scale) any ambient waypoint may sit from " +
                 "the Heart Stone. Keeps both characters wandering INSIDE the grove instead of drifting " +
                 "off to one side. Code default — no scene edit needed.")]
        [SerializeField] private float ambientWanderRadius = 0.9f;

        [Header("Act 3 — Ambient fallback")]
        [Tooltip("If nobody cooperates for this long (seconds), the grove resolves the trial by " +
                 "itself — the default film always reaches its ending without the audience.")]
        [SerializeField] private float act3AmbientTimeout = 30f;

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
            StopAmbientMotion();          // characters stop moving toward stale story targets

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
            StopAmbientMotion();          // rewind/replay reconstruction: no stale walks/glides
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

        /// <summary>Stop ambient story motion (astronaut walk / Oracle glide) — their coroutines or
        /// targets live on the character controllers, so ActManager's StopAllCoroutines alone
        /// wouldn't halt them on act changes and rewinds.</summary>
        private static void StopAmbientMotion()
        {
            AstronautController.Instance?.StopWalking();
            OracleController.Instance?.StopGlide();
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
                    yield return SpeakAndWait(oracle, line);
                }

                // The traveler's problem + the open invitation to intervene (audience onboarding).
                foreach (string line in introProblemLines)
                {
                    yield return SpeakAndWait(oracle, line);
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

            // …then gathers himself and approaches his guide.
            if (astronaut != null && oracle != null)
            {
                Vector3 toward = oracle.transform.position
                               + (astronaut.transform.position - oracle.transform.position).normalized * 0.7f;
                astronaut.WalkTo(AtHeight(toward, astronaut.transform.position.y), 0.3f);
                yield return new WaitForSeconds(4f);
                astronaut.PlayAnimation("Wave");
                yield return new WaitForSeconds(2f);
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
                yield return SpeakAndWait(oracle, "Speak what you wish to see. EchoRealm listens.");
            }

            // Voice commands are now active (VoiceCommandProcessor is listening)
            // This act runs until FilmDirector decides to advance (time-based or command-count)
            Log("Act 2 active — voice commands enabled. Waiting for user interaction...");

            // The film lives on its own while it waits: scripted Oracle/Astronaut story beats.
            // FilmDirector still owns the act's completion; StartAct/ApplyActState's
            // StopAllCoroutines kills the beats on act change and on rewind.
            if (ambientStory) yield return RunAct2AmbientStory();
        }

        // ------------------------------------------------------------------
        // Act 2 — ambient story beats ("The Echoes of the Grove")
        // ------------------------------------------------------------------
        //
        // The Oracle guides the traveler through the grove waking "echoes", with a key beat where
        // the Heart rejects him ALONE — planting the cooperation idea even for an audience that
        // only watches. Deterministic (no randomness): every device runs the same beats from the
        // same RPC_StartAct, so all headsets stay in sync. Waits pause while the world is pocketed.
        // Scripted world effects go through ExecCmd → recorded in the timeline like any command,
        // but NOT through FilmSync's user-command path, so DisruptionMeter never scores them.

        private IEnumerator RunAct2AmbientStory()
        {
            var oracle = OracleController.Instance;
            var astronaut = AstronautController.Instance;
            var heart = HeartAnchor();
            if (oracle == null || heart == null) yield break;

            float ay = astronaut != null ? astronaut.transform.position.y : heart.position.y;

            yield return WaitFilm(ambientStartDelay);

            // B1 — toward the first echo (the standing stones). They move first, then he speaks.
            Vector3 p1 = heart.position + StoryDirection(echoOneOffset);
            oracle.SetMood("mysterious");
            oracle.GlideTo(p1, 2.2f);
            if (astronaut != null) astronaut.WalkTo(AtHeight(p1, ay), 0.4f);
            yield return SpeakAndWait(oracle, "Come, traveler. The first echo sleeps by the standing stones.");
            yield return WaitFilm(2.5f);

            // B2 — the first echo wakes (fireflies); the traveler circles it, curious.
            ExecCmd("spawn_fireflies");
            if (astronaut != null) astronaut.PlayAnimation("LookAround");
            yield return SpeakAndWait(oracle, "One echo wakes. Two more remain.");
            if (astronaut != null) astronaut.WalkTo(AtHeight(heart.position + StoryDirection(new Vector3(0.4f, 0f, 1.0f)), ay), 0.4f);
            yield return WaitFilm(4f);

            // B3 — across the clearing, the second echo (butterflies). Both cross the grove.
            Vector3 p2 = heart.position + StoryDirection(echoTwoOffset);
            ExecCmd("spawn_butterflies");
            oracle.SetMood("joyful");
            oracle.GlideTo(p2, 2.8f);
            if (astronaut != null) astronaut.WalkTo(AtHeight(p2, ay), 0.4f);
            yield return SpeakAndWait(oracle, "There — the second dances on painted wings. Follow it.");
            yield return WaitFilm(3f);

            // …a little more wandering so the grove feels alive.
            oracle.GlideTo(heart.position + StoryDirection(new Vector3(-0.7f, 0f, 1.1f)), 2.8f);
            if (astronaut != null) astronaut.WalkTo(AtHeight(heart.position + StoryDirection(new Vector3(-0.9f, 0f, 0.8f)), ay), 0.4f);
            yield return WaitFilm(5f);

            // B4 — the KEY beat: the Heart rejects one alone (plants the cooperation idea).
            oracle.GlideTo(heart.position + StoryDirection(new Vector3(0f, 0f, 0.5f)), 2.2f);
            if (astronaut != null) astronaut.WalkTo(AtHeight(heart.position, ay), 0.55f);
            yield return WaitFilm(3.5f);
            ExecCmd("lightning");
            if (astronaut != null) astronaut.PlayAnimation("Jump");
            oracle.SetMood("warning");
            yield return SpeakAndWait(oracle, "Patience, traveler! The heart will not open for one alone. It waits for two — or for the grove's own mercy.");
            yield return WaitFilm(2.5f);

            // B5 — regroup beside the heart; invite the audience in.
            oracle.SetMood("mysterious");
            oracle.GlideTo(heart.position + StoryDirection(new Vector3(0.7f, 0f, -0.4f)), 2.5f);
            if (astronaut != null) astronaut.WalkTo(AtHeight(heart.position + StoryDirection(new Vector3(0.5f, 0f, -0.3f)), ay), 0.45f);
            yield return SpeakAndWait(oracle, "The third echo waits in your voices. Speak, and shape his road home.");
            yield return WaitFilm(4f);
            if (astronaut != null) astronaut.PlayAnimation("LookAround");
            yield return WaitFilm(4f);

            // Default film: with no early advance from the audience, end Act 2 HERE (master only) so
            // the story doesn't idle until the long max-duration timer. Clients no-op (not master).
            FilmDirector.Instance?.ForceCompleteAct2("ambient story finished");
        }

        // Wait that pauses while the world is pocketed (the film is paused).
        private IEnumerator WaitFilm(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                var pocket = Interaction.WorldPocket.Instance;
                if (pocket == null || !pocket.IsPocketed) t += Time.deltaTime;
                yield return null;
            }
        }

        // Speak a line and wait until it has (roughly) finished, so the NEXT line never cuts it off.
        // Duration is estimated from the word count (TTS rate ≈ 2.4 words/s), clamped, plus a short
        // tail. Pause-aware (freezes while pocketed), like WaitFilm.
        private IEnumerator SpeakAndWait(OracleController oracle, string line, float tailGap = 0.6f)
        {
            if (oracle == null || string.IsNullOrEmpty(line)) yield break;
            oracle.Speak(line);
            int words = line.Split(' ').Length;
            float dur = Mathf.Clamp(words * 0.42f + 1.2f, 2.8f, 13f) + tailGap;
            yield return WaitFilm(dur);
        }

        // The Heart Stone — the story's anchor point (falls back to the Oracle's spot).
        private Transform _heart;
        private Transform HeartAnchor()
        {
            if (_heart == null)
            {
                var go = GameObject.Find("HeartStone");
                if (go != null) _heart = go.transform;
                else if (OracleController.Instance != null) _heart = OracleController.Instance.transform;
            }
            return _heart;
        }

        // Scene-local offset (metres at authored scale) → world offset, honoring the scene's
        // current rotation and scale so waypoints stay inside the grove wherever it is placed.
        // The horizontal offset is clamped to ambientWanderRadius FIRST, so neither character can
        // wander outside the grove (the targets are all heart.position + StoryDirection(offset)).
        private Vector3 StoryDirection(Vector3 sceneLocalOffset)
        {
            // Contain wandering: cap the horizontal distance from the Heart Stone, keep the direction.
            Vector3 flat = new Vector3(sceneLocalOffset.x, 0f, sceneLocalOffset.z);
            if (flat.magnitude > ambientWanderRadius)
                flat = flat.normalized * ambientWanderRadius;
            sceneLocalOffset = new Vector3(flat.x, sceneLocalOffset.y, flat.z);

            var anchor = Networking.QRAnchorManager.Instance;
            var sr = anchor != null ? anchor.SceneRoot : null;
            if (sr == null) return sceneLocalOffset;
            return sr.rotation * (sceneLocalOffset * Mathf.Max(sr.localScale.x, 0.01f));
        }

        private static Vector3 AtHeight(Vector3 p, float y) { p.y = y; return p; }

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

            // Oracle delivers the AI-chosen transition narration, then an EXPLICIT instruction so the
            // audience knows the concrete cooperative action required to finish the trial.
            var oracle = OracleController.Instance;
            if (oracle != null)
            {
                oracle.SetMood(mood);
                yield return SpeakAndWait(oracle, oracleLine);
                oracle.SetMood("warning");
                yield return SpeakAndWait(oracle,
                    "Now — both of you, together — place your hands upon the Heart Stone. Only your joined touch will open the way.");
            }

            // Activate the obstacle matching the chosen variant
            GameObject activeObstacle = PickObstacleForVariant(variant);
            if (activeObstacle != null)
                activeObstacle.SetActive(true);

            Log($"Act 3 active — variant '{variant}'. Waiting for cooperation events...");

            // Monitor cooperation events (same win condition for all variants for now). NEW: with an
            // ambient timeout — a film without audience cooperation must still reach its ending.
            var cooperation = Interaction.CooperationDetector.Instance;
            bool autoResolved = false;
            if (cooperation != null)
            {
                float waited = 0f;
                while (cooperation.CooperationCount < cooperationGoal)
                {
                    if (waited >= act3AmbientTimeout) { autoResolved = true; break; }
                    var pocket = Interaction.WorldPocket.Instance;
                    if (pocket == null || !pocket.IsPocketed) waited += 1f;   // paused while pocketed
                    yield return new WaitForSeconds(1f);
                }
            }
            else
            {
                yield return new WaitForSeconds(60f);
            }

            if (autoResolved)
            {
                // Nobody cooperated — the grove answers for them: the Oracle circles the heart and
                // opens it itself, and the traveler rejoices. (Default film, guaranteed to finish.)
                if (oracle != null)
                {
                    oracle.SetMood("mysterious");
                    oracle.Speak("Very well. The grove itself shall answer for you.");
                }
                var heart = HeartAnchor();
                if (oracle != null && heart != null)
                {
                    oracle.GlideTo(heart.position + StoryDirection(new Vector3(0.8f, 0f, 0f)), 2f);
                    yield return WaitFilm(2f);
                    oracle.GlideTo(heart.position + StoryDirection(new Vector3(-0.8f, 0f, 0f)), 2.5f);
                    yield return WaitFilm(2.5f);
                }
                AstronautController.Instance?.PlayAnimation("Jump");
                yield return WaitFilm(2f);
            }

            // Challenge solved
            if (oracle != null)
            {
                oracle.SetMood("excited");
                yield return SpeakAndWait(oracle, autoResolved
                    ? "The heart yields. Go gently, traveler."
                    : "Your hands moved as one — the heart opens! The way home is clear.");
            }

            if (activeObstacle != null)
                activeObstacle.SetActive(false);

            yield return WaitFilm(1.5f);

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

            yield return WaitFilm(1.5f);

            var oracle = OracleController.Instance;
            if (oracle != null && !string.IsNullOrEmpty(oracleLine))
            {
                oracle.SetMood(mood);
                yield return SpeakAndWait(oracle, oracleLine);
            }

            // The grove rewards a nurturing session (recorded world commands, like spoken ones).
            if (celebrate)
            {
                ExecCmd("day");
                ExecCmd("spawn_butterflies");
                ExecCmd("spawn_fireflies");
                yield return WaitFilm(2f);
            }

            yield return SpeakFinalMonologue(mood);

            // Explicit outcome: the traveler crosses ON HIS OWN now (no further cooperation needed).
            if (oracle != null)
            {
                oracle.SetMood("joyful");
                yield return SpeakAndWait(oracle, "The way stands open. Traveler — cross now, and go home.");
            }

            // Astronaut walks toward the portal (robust: fall back to the portal effect's position if
            // no explicit portalTarget is wired, so the homecoming walk always plays).
            var astronaut = AstronautController.Instance;
            if (astronaut != null)
            {
                astronaut.StartPortalSequence(portalEffect != null ? portalEffect.transform : null);
                yield return WaitFilm(6f);
            }

            // The Oracle gives a final blessing as the traveler departs.
            if (oracle != null)
                yield return SpeakAndWait(oracle, passageBlessing);

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
