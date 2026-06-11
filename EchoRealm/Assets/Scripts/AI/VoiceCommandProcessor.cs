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

        /// <summary>
        /// Start listening for voice input.
        /// </summary>
        public void StartListening()
        {
            if (IsListening) return;

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
            if (!IsListening) return;

#if WINDOWS_UWP
            StopContinuousRecognition();
#endif
            IsListening = false;
            Log("Voice recognition STOPPED.");
        }

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
            try
            {
                speechRecognizer = new SpeechRecognizer();

                // Use free-form dictation (understands any phrase)
                var dictationConstraint = new SpeechRecognitionTopicConstraint(
                    SpeechRecognitionScenario.Dictation, "EchoRealmDictation");
                speechRecognizer.Constraints.Add(dictationConstraint);

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
                    Debug.LogError($"[Voice] Failed to compile speech constraints: {compileResult.Status}");
                    return;
                }

                // Wire up continuous recognition events
                speechRecognizer.ContinuousRecognitionSession.ResultGenerated += OnSpeechResult;
                speechRecognizer.ContinuousRecognitionSession.Completed += OnSpeechSessionCompleted;
                speechRecognizer.HypothesisGenerated += OnSpeechHypothesis;

                Log("Speech recognizer initialized. Ready to listen.");

                if (continuousListening)
                    StartListening();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Voice] Speech recognition init failed: {ex.Message}");
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
                UnityEngine.WSA.Application.InvokeOnAppThread(() => OnSpeechUnclear?.Invoke(args.Result.Text ?? "", 0f), false);
                return;
            }

            float confidence = (float)args.Result.RawConfidence;
            string text = args.Result.Text;

            if (confidence < confidenceThreshold)
            {
                Log($"Speech below threshold: '{text}' (confidence: {confidence:F2})");
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
            if (continuousListening)
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
            LastRecognizedText = text;
            OnSpeechRecognized?.Invoke(text);

            // Isolated modules get first refusal on the raw utterance. If one consumes it, it dies
            // here — never reaching the AI, FilmSync, or ActionCollector. Keeps the ball sandbox
            // invisible to the narrative/variant decision.
            if (SpeechInterceptor != null && SpeechInterceptor(text)) return;

            // Meta-commands handled locally (pocket / unpocket the whole scene) — they bypass the
            // AI/network command pipeline. The speech recognizer often splits "unpocket" into
            // "un pocket", so match several forms; "restore"/"resume" also work as clear unpocket words.
            string meta = text.ToLowerInvariant();

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
            if (!filmPlaying &&
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
