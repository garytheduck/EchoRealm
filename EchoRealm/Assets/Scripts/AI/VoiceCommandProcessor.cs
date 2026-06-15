// CS0414: fields used only in UWP builds appear "unused" in Editor
// CS1998: async method without await (Start has await only in UWP)
#pragma warning disable CS0414, CS1998

using System;
using UnityEngine;

#if WINDOWS_UWP
using Windows.Media.SpeechRecognition;
using System.Threading.Tasks;
#endif

namespace EchoRealm.AI
{
    /// <summary>
    /// Captures voice input from HoloLens 2 microphone using Windows Speech Recognition,
    /// converts it to text, and forwards it to OllamaClient for AI interpretation.
    ///
    /// On HoloLens (UWP): Uses Windows.Media.SpeechRecognition (free, built-in, no internet needed).
    /// In Unity Editor: Uses a debug text field for testing.
    /// </summary>
    public class VoiceCommandProcessor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AIManager aiManager;
        [SerializeField] private CommandExecutor commandExecutor;

        [Header("Voice Settings")]
        [Tooltip("Minimum confidence threshold for speech recognition (0-1).")]
        [SerializeField, Range(0f, 1f)] private float confidenceThreshold = 0.5f;

        [Tooltip("If true, speech recognition restarts automatically after each utterance.")]
        [SerializeField] private bool continuousListening = true;

        [Header("Relevance Gate (ignore side conversations)")]
        [Tooltip("Forward an utterance to the AI only when it contains at least one command-vocabulary " +
                 "word. Stops side conversations (e.g. discussing in Romanian next to the headset, " +
                 "transcribed as garbled English) from becoming world commands and polluting the " +
                 "combined behavior profile. NOTE: gated utterances may still appear in the saved " +
                 "transcript ('heard' fires before the gate) — for fully private discussion say " +
                 "'stop listening', which keeps everything out.")]
        [SerializeField] private bool relevanceGate = true;
        [Tooltip("Vocabulary the gate looks for (lowercase substrings). Extend freely in the Inspector.")]
        [SerializeField] private string[] commandVocabulary = new[]
        {
            // weather / sky
            "rain", "storm", "wind", "fog", "mist", "cloud", "sun", "day", "night", "dark", "sky",
            "light", "bright", "morning", "evening", "sunset", "sunrise", "dawn", "dusk", "stop",
            // fire / disasters
            "fire", "burn", "flame", "smoke", "earthquake", "quake", "shake", "lightning", "thunder",
            // flora / fauna / world
            "tree", "forest", "flower", "plant", "grow", "bloom", "butterfl", "firefl", "glow", "path",
            "world", "scene", "grove",
            // characters
            "astronaut", "oracle", "dobby", "traveler", "jump", "wave", "dance", "celebrate", "scare",
            "look", "cheer",
            // object manipulation (when the wake word was dropped and nothing is gazed)
            "bigger", "larger", "smaller", "tinier", "shrink", "rotate", "spin", "scale", "reset",
            "big", "small", "move", "back", "open", "close", "clear", "block", "way", "star", "moon",
        };

        // Soft mute ("stop listening"): utterances are discarded until "start listening". A soft flag,
        // not StopListening() — OnSpeechSessionCompleted auto-restarts the recognizer, which would
        // silently undo a hard stop.
        private bool _muted;

        // Watchdog: the recognizer's auto-restart is driven ONLY by the OS "session completed" callback
        // (OnSpeechSessionCompleted). On HoloLens that callback sometimes never fires across a long
        // silence (e.g. the ~30 s Act-3 cooperation wait), or the re-arm StartAsync throws — and then the
        // mic is dead for the rest of the film (IsListening stuck true → StartListening is a no-op, no
        // session, no Completed, no "didn't catch that" feedback). This 1 Hz poll re-arms whenever we
        // INTEND to listen but no session is live. Additive / observer-only.
        private float _rearmCheckTimer;
        private bool _watchdogStopped;   // true after an explicit StopListening; cleared by StartListening
        private bool _recognizerReady;   // true once the recognizer is built + constraints compiled (init retries until then)

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>True if voice recognition is active and listening.</summary>
        public bool IsListening { get; private set; }

        /// <summary>The last recognized speech text.</summary>
        public string LastRecognizedText { get; private set; }

        /// <summary>Fired when speech is recognized, before AI processing.</summary>
        public event Action<string> OnSpeechRecognized;

        /// <summary>Fired when speech was REJECTED / below the confidence threshold (the text may be a
        /// low-confidence guess, or empty). Lets the "what I heard" label show "didn't catch that".</summary>
        // Raised only in the WINDOWS_UWP recognizer path below; the Editor compile strips that path,
        // so CS0067 ("event never used") would fire here despite external subscribers — suppress it.
#pragma warning disable 67
        public event Action<string, float> OnSpeechUnclear;
#pragma warning restore 67

        /// <summary>Fired when AI finishes processing and returns commands.</summary>
        public event Action<AICommandResponse> OnAIResponseReceived;

        /// <summary>Fired when the soft-mute state toggles (true = muted/ignoring, false = listening).
        /// Lets the "what I heard" label flash a "voice muted"/"voice listening" confirmation.</summary>
        public event Action<bool> OnMuteChanged;

        /// <summary>Optional first-chance interceptor for raw recognized speech. If it returns true,
        /// the utterance is fully consumed and is NOT forwarded to the AI / narrative / ActionCollector
        /// pipeline. Lets isolated modules (e.g. the ball sandbox) handle their own voice phrases
        /// without coupling this class to them. Null by default. Set by EchoRealm.Sandbox.BallVoiceHook.</summary>
        public static System.Func<string, bool> SpeechInterceptor;

        public static VoiceCommandProcessor Instance { get; private set; }

#if WINDOWS_UWP
        private SpeechRecognizer speechRecognizer;
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private async void Start()
        {
            if (aiManager == null)
                aiManager = FindObjectOfType<AIManager>();
            if (commandExecutor == null)
                commandExecutor = FindObjectOfType<CommandExecutor>();

#if WINDOWS_UWP
            await InitializeSpeechRecognition();
#else
            Log("Running in Editor — use ProcessDebugInput() to simulate voice commands.");
#endif
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Programmatically mute/unmute the command pipeline (same soft-mute as the
        /// "stop listening" voice toggle — survives the recognizer's auto-restart because it gates
        /// inside ProcessSpeechText). Used by the offline saved-scene viewer so a spoken command
        /// can't mutate the reconstructed, read-only scene.</summary>
        public void SetMuted(bool muted)
        {
            if (_muted == muted) return;
            _muted = muted;
            Log(muted ? "Voice MUTED (programmatic)." : "Voice UNMUTED (programmatic).");
            OnMuteChanged?.Invoke(muted);
        }

        /// <summary>
        /// Start listening for voice input.
        /// </summary>
        public void StartListening()
        {
            if (IsListening) return;

            _watchdogStopped = false;   // a (re)start means we INTEND to keep listening
#if WINDOWS_UWP
            StartContinuousRecognition();
#else
            Log("StartListening called in Editor — no-op. Use ProcessDebugInput().");
#endif
            IsListening = true;
            Log("Voice recognition STARTED.");
        }

        /// <summary>
        /// Stop listening for voice input.
        /// </summary>
        public void StopListening()
        {
            // Set the stop intent BEFORE the guard: between sessions IsListening is transiently false
            // (OnSpeechSessionCompleted clears it, then posts StartListening a frame later). If the
            // finale stop landed in that window the early return would skip the flag and the watchdog
            // would re-arm the mic. Setting it first makes an explicit stop always stick.
            _watchdogStopped = true;   // explicit stop (finale / saved-scene viewer) — watchdog must NOT re-arm
            if (!IsListening) return;

#if WINDOWS_UWP
            StopContinuousRecognition();
#endif
            IsListening = false;
            Log("Voice recognition STOPPED.");
        }

#if WINDOWS_UWP
        // Self-healing re-arm. The OS-driven restart chain (OnSpeechSessionCompleted) dies permanently
        // the first time HoloLens fails to raise Completed across a long silence, or a re-arm StartAsync
        // throws (single-active-session). Without an independent poll nothing notices. This re-arms a
        // dead session at ~1 Hz; it stays out of the way after an explicit StopListening and is a no-op
        // whenever a session is genuinely live. Muted ≠ stopped: muted keeps listening (and discarding),
        // so the watchdog still keeps the mic alive while muted, exactly as intended.
        private void Update()
        {
            if (!_recognizerReady || speechRecognizer == null || !continuousListening || _watchdogStopped) return;
            _rearmCheckTimer += Time.unscaledDeltaTime;   // unscaled → immune to any time-scale change
            if (_rearmCheckTimer < 1.0f) return;
            _rearmCheckTimer = 0f;
            if (!IsListening) StartListening();           // no-op when a session is actually running
        }
#endif

        /// <summary>
        /// Process a text command directly (for Editor testing or text input fallback).
        /// </summary>
        public async void ProcessDebugInput(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Log($"Processing debug input: '{text}'");
            await ProcessSpeechText(text);
        }

        // ------------------------------------------------------------------
        // Speech-to-Text (UWP / HoloLens)
        // ------------------------------------------------------------------

#if WINDOWS_UWP
        private async Task InitializeSpeechRecognition()
        {
            // The microphone consent broker + audio stack aren't always ready the instant the app
            // launches. On a FRESH install the consent dialog adds a delay, so init happens to succeed;
            // on a plain RELAUNCH there's no dialog, init races the audio stack, the first
            // CompileConstraintsAsync fails — and the old code gave up for the whole session. That is the
            // "mic works right after a deploy but not after closing & reopening" symptom. Retry a few
            // times with a delay so a cold relaunch recovers on its own.
            const int maxAttempts = 6;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (await TryInitRecognizer(attempt)) return;
                if (attempt < maxAttempts) await Task.Delay(1500);
            }
            Debug.LogError("[Voice] Speech recognizer could not initialize after retries — microphone unavailable this " +
                           "session. If this persists, check Settings ▸ Privacy ▸ Microphone (and 'Online speech " +
                           "recognition') on the HoloLens.");
        }

        // One initialization attempt. Returns true once the recognizer is built, constraints compiled,
        // events wired and listening started. On any failure it disposes the half-built recognizer so the
        // next attempt starts clean, and returns false (the caller waits, then retries).
        private async Task<bool> TryInitRecognizer(int attempt)
        {
            try
            {
                if (speechRecognizer != null)
                {
                    try { speechRecognizer.Dispose(); } catch { }
                    speechRecognizer = null;
                }
                _recognizerReady = false;

                // Force ENGLISH recognition explicitly. The parameterless constructor uses the device
                // language, and nearby conversation in another language (e.g. Romanian) gets transcribed
                // as garbled English "commands". Falls back to the device default if en-US is missing.
                try { speechRecognizer = new SpeechRecognizer(new Windows.Globalization.Language("en-US")); }
                catch (Exception lex)
                {
                    Debug.LogWarning($"[Voice] en-US speech pack unavailable ({lex.Message}) — using device default.");
                    speechRecognizer = new SpeechRecognizer();
                }

                // Use free-form dictation (understands any phrase)
                speechRecognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                    SpeechRecognitionScenario.Dictation, "EchoRealmDictation"));

                // Don't let the session die during quiet stretches (e.g. the Act 1 intro).
                try
                {
                    speechRecognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(20);
                    speechRecognizer.Timeouts.BabbleTimeout = TimeSpan.FromSeconds(0);
                    speechRecognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(2);
                }
                catch (Exception tex) { Debug.LogWarning($"[Voice] Could not set speech timeouts: {tex.Message}"); }

                var compileResult = await speechRecognizer.CompileConstraintsAsync();
                if (compileResult.Status != SpeechRecognitionResultStatus.Success)
                {
                    // Common on a cold relaunch before the mic device is ready (e.g. MicrophoneUnavailable).
                    Debug.LogWarning($"[Voice] Constraint compile attempt {attempt} failed: {compileResult.Status} — retrying shortly.");
                    return false;
                }

                // Wire up continuous recognition events
                speechRecognizer.ContinuousRecognitionSession.ResultGenerated += OnSpeechResult;
                speechRecognizer.ContinuousRecognitionSession.Completed += OnSpeechSessionCompleted;
                speechRecognizer.HypothesisGenerated += OnSpeechHypothesis;

                _recognizerReady = true;
                Log($"Speech recognizer initialized (attempt {attempt}). Ready to listen.");

                if (continuousListening)
                    StartListening();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Voice] Speech init attempt {attempt} threw: {ex.Message} — retrying shortly.");
                try { if (speechRecognizer != null) { speechRecognizer.Dispose(); speechRecognizer = null; } } catch { }
                _recognizerReady = false;
                return false;
            }
        }

        private async void StartContinuousRecognition()
        {
            try
            {
                await speechRecognizer.ContinuousRecognitionSession.StartAsync();
            }
            catch (Exception ex)
            {
                // StartAsync threw — most often the previous session hasn't fully reached Completed yet
                // (UWP allows only ONE active session). Reset the flag so the watchdog (or the next
                // Completed) can retry, instead of leaving IsListening==true forever — which would make
                // every future StartListening() a silent no-op and kill the mic for the rest of the film.
                IsListening = false;
                Debug.LogError($"[Voice] Failed to start continuous recognition: {ex.Message}");
            }
        }

        private async void StopContinuousRecognition()
        {
            try
            {
                await speechRecognizer.ContinuousRecognitionSession.StopAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Voice] Failed to stop recognition: {ex.Message}");
            }
        }

        private void OnSpeechResult(SpeechContinuousRecognitionSession sender,
                                     SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            if (args.Result.Confidence == SpeechRecognitionConfidence.Rejected)
            {
                Log("Speech rejected (too low confidence).");
                // While muted, don't spam "didn't catch that" over side conversations.
                if (!_muted)
                    UnityEngine.WSA.Application.InvokeOnAppThread(() => OnSpeechUnclear?.Invoke(args.Result.Text ?? "", 0f), false);
                return;
            }

            float confidence = (float)args.Result.RawConfidence;
            string text = args.Result.Text;

            if (confidence < confidenceThreshold)
            {
                Log($"Speech below threshold: '{text}' (confidence: {confidence:F2})");
                if (!_muted)
                    UnityEngine.WSA.Application.InvokeOnAppThread(() => OnSpeechUnclear?.Invoke(text, confidence), false);
                return;
            }

            Log($"Speech recognized: '{text}' (confidence: {confidence:F2})");

            // Must dispatch to Unity main thread
            UnityEngine.WSA.Application.InvokeOnAppThread(async () =>
            {
                await ProcessSpeechText(text);
            }, false);
        }

        private void OnSpeechSessionCompleted(SpeechContinuousRecognitionSession sender,
                                               SpeechContinuousRecognitionCompletedEventArgs args)
        {
            Log($"Speech session completed: {args.Status}");
            IsListening = false;

            // Restart for ANY completion (including TimeoutExceeded during quiet stretches) — otherwise
            // the recognizer dies after the first silence timeout and never hears commands again.
            // But NOT after an explicit StopListening (finale / saved-scene viewer): _watchdogStopped
            // guards it, so the Completed raised by our OWN StopAsync can't resurrect the mic.
            if (continuousListening && !_watchdogStopped)
            {
                UnityEngine.WSA.Application.InvokeOnAppThread(() => StartListening(), false);
            }
        }

        private void OnSpeechHypothesis(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
        {
            string h = args.Hypothesis != null ? args.Hypothesis.Text : null;
            if (string.IsNullOrEmpty(h)) return;
            UnityEngine.WSA.Application.InvokeOnAppThread(() =>
            {
                // Muted users are DISCUSSING — don't let their partial words resume a pocketed film.
                if (_muted) return;
                // A partial that looks like the START of a mute phrase ("stop…", "pause…") must not
                // resume a pocketed film either — wait for the final result, which may be the mute.
                string hl = h.ToLowerInvariant().TrimStart();
                if (hl.StartsWith("stop") || hl.StartsWith("pause")) return;
                Log($"Speech hypothesis (hearing you): '{h}'");
                // While pocketed, resume on the FIRST partial word — don't wait for a final result.
                var pocket = EchoRealm.Interaction.WorldPocket.Instance;
                if (pocket != null && pocket.IsPocketed)
                {
                    Log($"Pocketed — resuming on hypothesis '{h}'.");
                    pocket.Unpocket();
                }
            }, false);
        }
#endif

        // ------------------------------------------------------------------
        // AI Processing Pipeline
        // ------------------------------------------------------------------

        /// <summary>
        /// Core pipeline: speech text → AI backend → execute commands.
        /// </summary>
        private async System.Threading.Tasks.Task ProcessSpeechText(string text)
        {
            string meta = text.ToLowerInvariant();

            // ---- Soft mute ----
            // While muted, EVERY utterance is discarded except the unmute phrase — lets the users
            // discuss freely (any language) without the AI reacting. Mute survives the recognizer's
            // auto-restart because it gates here, not in Start/StopListening.
            if (_muted)
            {
                if (meta.Contains("start listening") || meta.Contains("listen again") ||
                    meta.Contains("resume listening") || meta.Contains("wake up"))
                {
                    _muted = false;
                    Log("Voice UNMUTED — commands are interpreted again.");
                    OnMuteChanged?.Invoke(false);
                }
                // Allow PAUSING the film while discussing — "pocket" works muted and STAYS muted,
                // so mute+pocket (the discuss-privately combo) is reachable by voice in any order.
                else if (meta.Contains("pocket") && !meta.Contains("unpocket") && !meta.Contains("un pocket"))
                {
                    Log("Meta-command POCKET while muted — pausing the film (still muted).");
                    EchoRealm.Interaction.WorldPocket.Instance?.Pocket();
                }
                return;
            }
            if (meta.Contains("stop listening") || meta.Contains("pause listening") ||
                meta.Contains("stop the listening"))
            {
                _muted = true;
                Log("Voice MUTED — say 'start listening' to resume command interpretation.");
                OnMuteChanged?.Invoke(true);
                return;
            }
            // Unmute words said while NOT muted must be consumed here too — "start listening"
            // contains "start", which would otherwise begin the film on the pre-film START branch.
            if (meta.Contains("start listening") || meta.Contains("resume listening") ||
                meta.Contains("listen again"))
            {
                Log("Already listening.");
                return;
            }

            // Fire the "heard" event only for speech that survived the mute — TimelineRecorder (the
            // saved transcript) and NarrativeManager (the final-monologue context) subscribe to it,
            // and muted discussion must not leak into either.
            LastRecognizedText = text;
            OnSpeechRecognized?.Invoke(text);

            // Isolated modules get first refusal on the raw utterance. If one consumes it, it dies
            // here — never reaching the AI, FilmSync, or ActionCollector. Keeps the ball sandbox
            // invisible to the narrative/variant decision.
            if (SpeechInterceptor != null && SpeechInterceptor(text)) return;

            // Meta-commands handled locally (pocket / unpocket the whole scene) — they bypass the
            // AI/network command pipeline. The speech recognizer often splits "unpocket" into
            // "un pocket", so match several forms; "restore"/"resume" also work as clear unpocket words.

            // While the world is pocketed, the ONLY sensible intent is to bring it back. The recognizer
            // is finicky (a clean "resume" often never finalizes), so be maximally forgiving: ANY
            // recognized speech resumes. Unpocket is networked, so this resumes every headset.
            var pocket = EchoRealm.Interaction.WorldPocket.Instance;
            if (pocket != null && pocket.IsPocketed)
            {
                Log($"World is pocketed — resuming on speech (heard: '{text}').");
                pocket.Unpocket();
                return;
            }

            // "START" — only meaningful BEFORE the film begins. Once the film is playing we let it
            // through as a normal command (so "start a fire" still works). When heard pre-film it
            // begins Act 1 on EVERY headset at once (networked via FilmSync → the master).
            var film = EchoRealm.Film.FilmDirector.Instance;
            bool filmPlaying = film != null && film.IsPlaying;
            bool filmFinished = film != null && film.IsFinished;
            // START only BEFORE the film begins — and NOT after it has ended. Without the
            // !filmFinished guard, a stray "start a fire" said after the finale (when IsPlaying is
            // false again) re-triggers START and restarts the whole film. After the ending the scene
            // stays put; re-view a saved run via the Save prompt's "View saved scene" instead.
            if (!filmPlaying && !filmFinished &&
                (meta.Contains("start") || meta.Contains("begin") ||
                 meta.Contains("lets go") || meta.Contains("let's go") || meta.Contains("let us go")))
            {
                Log($"Meta-command START (heard: '{text}') — beginning the film for all headsets.");
                var startSync = EchoRealm.Networking.FilmSync.Instance;
                if (startSync != null) startSync.RequestStartFilm();
                else film?.StartFilm();
                return;
            }

            bool wantsUnpocket = meta.Contains("unpocket") || meta.Contains("un pocket")
                                 || meta.Contains("unproket") || meta.Contains("unprocket") || meta.Contains("unfrocket")
                                 || meta.Contains("restor")    // matches "restore" AND "restoration"
                                 || meta.Contains("resume")
                                 || meta.Contains("bring back") || meta.Contains("come back");
            if (wantsUnpocket)
            {
                Log($"Meta-command UNPOCKET (heard: '{text}')");
                EchoRealm.Interaction.WorldPocket.Instance?.Unpocket();
                return;
            }
            if (meta.Contains("pocket"))
            {
                Log($"Meta-command POCKET (heard: '{text}')");
                EchoRealm.Interaction.WorldPocket.Instance?.Pocket();
                return;
            }

            // "Claude, ..." → manipulate the object I'm looking at (gaze-targeted). Matched only at the
            // START of the phrase (accept the common "cloud" mishearing there) so "cloud" still works
            // later in the sentence as an object word.
            string lead = meta.TrimStart();
            bool addressed = lead.StartsWith("claude") || lead.StartsWith("claud")
                          || lead.StartsWith("cloud")  || lead.StartsWith("clyde") || lead.StartsWith("klaus");

            // ---- Palm hold (optional PalmHold component; null-safe, additive) ----
            // "bring the world to my palm" → shrink the scene onto the open palm and follow it;
            // "make it big again" / "put the world back" → restore the pre-hold pose. Networked via
            // the same scene-transform streaming used for hand grabs. Skipped for "Claude, ..."
            // phrases so addressed OBJECT commands keep their existing routing.
            var palmHold = EchoRealm.Interaction.PalmHold.Instance;
            if (palmHold != null && !addressed)
            {
                // Destination phrasings only — bare "palm"/"the palm" would hijack "grow the palm trees".
                bool wantsPalm = meta.Contains("my palm") || meta.Contains("to the palm") ||
                                 meta.Contains("in the palm") || meta.Contains("into the palm") ||
                                 meta.Contains("on the palm") ||
                                 (meta.Contains("my hand")
                                  && (meta.Contains("scene") || meta.Contains("world") || meta.Contains("come") || meta.Contains("bring")));
                if (meta.Contains("palm tree")) wantsPalm = false;
                bool wantsRelease = palmHold.IsHolding &&
                                    (meta.Contains("big again") || meta.Contains("scene back") ||
                                     meta.Contains("world back") || meta.Contains("put it back") ||
                                     meta.Contains("release"));
                if (wantsRelease) { palmHold.Release(); return; }
                if (wantsPalm) { palmHold.TryHold(); return; }
            }

            // The HoloLens recognizer often drops/garbles the leading wake word ("club make this smaller"
            // → final transcript "make this smaller"), so the wake-word check alone misses real object
            // commands. ALSO treat a phrase as an object command when you're gazing at a manipulable prop
            // AND use a demonstrative ("this"/"that") together with a size/rotate/move verb. Requiring all
            // three keeps ordinary scene speech ("make it rain") from being misrouted to the object path.
            bool hasDemonstrative = meta.Contains("this") || meta.Contains("that");
            bool hasManipVerb = meta.Contains("bigger") || meta.Contains("larger") || meta.Contains("grow")
                             || meta.Contains("smaller") || meta.Contains("tinier") || meta.Contains("shrink")
                             || meta.Contains("rotate") || meta.Contains("spin") || meta.Contains("move")
                             || meta.Contains("scale") || meta.Contains("reset");
            bool looksLikeObjectCommand = !addressed && hasDemonstrative && hasManipVerb && GazingAtManipulable();

            if (addressed || looksLikeObjectCommand)
            {
                await HandleObjectCommand(text);
                return;
            }

            // ---- Relevance gate ----
            // Whatever reaches this point would be sent to the AI as a world command. Side
            // conversations transcribed as garbled English contain none of the command vocabulary —
            // drop them silently instead of bothering the AI / profile / transcript with them.
            if (relevanceGate && !ContainsCommandVocabulary(meta))
            {
                Log($"Ignored (no command vocabulary): '{text}'");
                return;
            }

            // Networked path: hand the speech to the master via FilmSync. The master
            // interprets it (AI), pools it into the combined behavior profile, and
            // broadcasts the resulting world commands to every headset.
            var sync = EchoRealm.Networking.FilmSync.Instance;
            if (sync != null)
            {
                Log($"Routing speech to FilmSync (master interprets): '{text}'");
                sync.SubmitSpeech(text);
                return;
            }

            // Fallback (no networking yet / editor before spawn): interpret + execute locally.
            ActionCollector.Instance?.RecordVoiceCommand(text);

            if (aiManager == null || !aiManager.IsReachable)
            {
                Debug.LogWarning("[Voice] No AI backend available. Cannot process voice command.");
                return;
            }

            if (aiManager.IsBusy)
            {
                Log("AI backend is busy processing another request. Skipping.");
                return;
            }

            string sceneState = commandExecutor != null ? commandExecutor.GetSceneStateDescription() : "unknown";
            string[] availableCommands = commandExecutor != null ? commandExecutor.GetAvailableCommands() : new string[0];

            Log($"Sending to AI ({aiManager.ActiveBackendName}): speech='{text}', scene='{sceneState}'");
            var response = await aiManager.SendCommandRequestAsync(text, sceneState, availableCommands);

            if (response != null)
            {
                Log($"AI response: commands=[{string.Join(", ", response.commands ?? new string[0])}], mood={response.mood}");
                OnAIResponseReceived?.Invoke(response);
                if (commandExecutor != null && response.commands != null)
                    commandExecutor.ExecuteCommands(response);
            }
            else
            {
                Log("AI returned null response.", isWarning: true);
            }
        }

        // True if the (lowercased) utterance contains at least one command-vocabulary word — the
        // relevance gate's test. Empty/null vocabulary disables the gate (everything passes).
        private bool ContainsCommandVocabulary(string meta)
        {
            if (commandVocabulary == null || commandVocabulary.Length == 0) return true;
            foreach (var w in commandVocabulary)
                if (!string.IsNullOrEmpty(w) && meta.Contains(w)) return true;
            return false;
        }

        // True if eye-gaze currently lands on a registered manipulable prop. Lets object commands work
        // even when the recognizer drops the "Claude" wake word (see the speech routing above).
        private bool GazingAtManipulable()
        {
            var reg = EchoRealm.Interaction.ManipulableRegistry.Instance;
            var eyes = EchoRealm.Interaction.EyeTrackingManager.Instance;
            return reg != null && eyes != null && reg.Resolve(eyes.CurrentTarget) != null;
        }

        /// <summary>
        /// "Claude, …" object manipulation. Runs on the SPEAKING device: resolves the gazed-at prop,
        /// asks Claude for the op, converts the egocentric direction to a frame-independent op, and
        /// submits it through FilmSync so it applies on every headset.
        /// </summary>
        private async System.Threading.Tasks.Task HandleObjectCommand(string text)
        {
            var reg = EchoRealm.Interaction.ManipulableRegistry.Instance;
            var eyes = EchoRealm.Interaction.EyeTrackingManager.Instance;
            var mo = (reg != null && eyes != null) ? reg.Resolve(eyes.CurrentTarget) : null;
            if (mo == null)
            {
                Log("Claude (object): you're not looking at a manipulable object — ignored.");
                return;
            }

            if (aiManager == null || !aiManager.IsReachable)
            {
                Log("Claude (object): AI backend unavailable.", isWarning: true);
                return;
            }

            var op = await aiManager.SendObjectOpAsync(text, mo.Context());
            if (op == null || string.IsNullOrEmpty(op.action))
            {
                Log("Claude (object): no operation parsed.", isWarning: true);
                return;
            }

            var cam = Camera.main != null ? Camera.main.transform : null;

            int opType;
            float factor = 1f, degrees = 0f;
            Vector3 delta = Vector3.zero;
            switch (op.action)
            {
                case "scale":
                    opType = (int)EchoRealm.Interaction.ObjOpType.Scale;
                    factor = EchoRealm.Interaction.ObjectOpMath.ScaleFactor(op.direction, op.magnitude, op.amount);
                    break;
                case "move":
                    opType = (int)EchoRealm.Interaction.ObjOpType.Move;
                    delta = EchoRealm.Interaction.ObjectOpMath.MoveDelta(cam, mo.transform, op.direction, op.magnitude, op.amount);
                    break;
                case "rotate":
                    opType = (int)EchoRealm.Interaction.ObjOpType.Rotate;
                    degrees = EchoRealm.Interaction.ObjectOpMath.YawDegrees(op.direction, op.magnitude, op.amount);
                    break;
                case "reset":
                    opType = (int)EchoRealm.Interaction.ObjOpType.Reset;
                    break;
                default:
                    Log($"Claude (object): unknown action '{op.action}'.", isWarning: true);
                    return;
            }

            Log($"Claude (object): {op.action}/{op.direction}/{op.magnitude} x{op.amount:F1} on '{mo.Id}'.");
            var sync = EchoRealm.Networking.FilmSync.Instance;
            if (sync != null) sync.SubmitObjectOp(mo.Id, opType, factor, delta, degrees);
        }

        /// <summary>
        /// Raise the AI-response event. Called by FilmSync on the master after it
        /// interprets speech authoritatively, so the master's NarrativeManager/UI still react.
        /// </summary>
        public void RaiseAIResponse(AICommandResponse response)
        {
            OnAIResponseReceived?.Invoke(response);
        }

        private void OnDestroy()
        {
#if WINDOWS_UWP
            if (speechRecognizer != null)
            {
                speechRecognizer.ContinuousRecognitionSession.ResultGenerated -= OnSpeechResult;
                speechRecognizer.ContinuousRecognitionSession.Completed -= OnSpeechSessionCompleted;
                speechRecognizer.HypothesisGenerated -= OnSpeechHypothesis;
                speechRecognizer.Dispose();
                speechRecognizer = null;
            }
#endif
        }

        private void Log(string message, bool isWarning = false)
        {
            if (!logEvents) return;
            if (isWarning)
                Debug.LogWarning($"[Voice] {message}");
            else
                Debug.Log($"[Voice] {message}");
        }
    }
}
