using System.Collections.Generic;
using UnityEngine;
using EchoRealm.AI;
using EchoRealm.Characters;
using EchoRealm.Networking;

namespace EchoRealm.Film
{
    /// <summary>Master-side observer that records everything that happens into a TimelineLog.
    /// Pure observer: subscribes to existing/added events and appends — never calls back into
    /// the film. Remove this component and the film is unchanged. Attach to a persistent object
    /// (e.g. GameManager) in MainScene.</summary>
    public class TimelineRecorder : MonoBehaviour
    {
        public static TimelineRecorder Instance { get; private set; }

        public TimelineLog Log { get; } = new TimelineLog();
        public SceneTimeline Timeline => Log.Timeline;

        private readonly List<AiMemoryState> _aiSnapshots = new List<AiMemoryState>();

        private float _startTime = -1f;
        private bool _subscribedInstances;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            OracleController.OnSpoke += HandleSpoke;
            ActManager.OnActStarted += HandleActStarted;
            FilmSync.OnObjectOpApplied += HandleObjectOp;
        }

        private void OnDisable()
        {
            OracleController.OnSpoke -= HandleSpoke;
            ActManager.OnActStarted -= HandleActStarted;
            FilmSync.OnObjectOpApplied -= HandleObjectOp;
            UnsubscribeInstances();
        }

        private void Start() => SubscribeInstances();

        private void SubscribeInstances()
        {
            if (_subscribedInstances) return;
            if (CommandExecutor.Instance != null)
                CommandExecutor.Instance.OnCommandExecuted += HandleWorldCommand;
            if (VoiceCommandProcessor.Instance != null)
            {
                VoiceCommandProcessor.Instance.OnAIResponseReceived += HandleAIResponse;
                VoiceCommandProcessor.Instance.OnSpeechRecognized += HandleSpeech;
            }
            _subscribedInstances = true;
        }

        private void UnsubscribeInstances()
        {
            if (!_subscribedInstances) return;
            if (CommandExecutor.Instance != null)
                CommandExecutor.Instance.OnCommandExecuted -= HandleWorldCommand;
            if (VoiceCommandProcessor.Instance != null)
            {
                VoiceCommandProcessor.Instance.OnAIResponseReceived -= HandleAIResponse;
                VoiceCommandProcessor.Instance.OnSpeechRecognized -= HandleSpeech;
            }
            _subscribedInstances = false;
        }

        // Seconds since the first recorded event (lazy clock start).
        private float Now()
        {
            if (_startTime < 0f) _startTime = Time.time;
            return Time.time - _startTime;
        }

        private void HandleWorldCommand(string command) { Log.AddWorldCommand(command, Now()); SnapshotAiMemory(); }

        private void HandleObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
        { Log.AddObjectOp(id, opType, factor, delta, degrees, Now()); SnapshotAiMemory(); }

        private void HandleActStarted(int act, string variant)
        {
            Log.AddActTransition(act, variant, Now());
            Log.Timeline.meta.finalAct = act;
            SnapshotAiMemory();
        }

        private void HandleSpoke(string text, string mood) { Log.AddUtterance("Oracle", text, Now()); SnapshotAiMemory(); }

        private void HandleSpeech(string text) { Log.AddUtterance("User", text, Now()); SnapshotAiMemory(); }

        private void HandleAIResponse(AICommandResponse r)
        {
            if (r == null) return;
            string cmds = (r.commands != null) ? string.Join(",", r.commands) : "";
            Log.AddUtterance("AI", $"decided [{cmds}] mood={r.mood}", Now());
            SnapshotAiMemory();
        }

        // Capture the AI's cumulative memory at the current time (called after each recorded event).
        private void SnapshotAiMemory()
        {
            var ac = EchoRealm.AI.ActionCollector.Instance;
            var m = ac != null ? ac.CaptureMemory(Now()) : new AiMemoryState { t = Now() };
            NarrativeManager.Instance?.CaptureInto(m);
            _aiSnapshots.Add(m);
        }

        /// <summary>Rewind: restore the AI's memory to the latest snapshot at or before t (empty
        /// baseline if none), and drop snapshots after t. Called by FilmSync.DoRewind.</summary>
        public void RestoreAiMemoryAt(float t)
        {
            AiMemoryState chosen = null;
            for (int k = 0; k < _aiSnapshots.Count; k++)
            {
                if (_aiSnapshots[k].t <= t) chosen = _aiSnapshots[k];
                else break;
            }
            var state = chosen ?? new AiMemoryState { t = 0f }; // before first command → empty
            EchoRealm.AI.ActionCollector.Instance?.RestoreMemory(state);
            NarrativeManager.Instance?.RestoreFrom(state);

            for (int k = _aiSnapshots.Count - 1; k >= 0; k--)
                if (_aiSnapshots[k].t > t) _aiSnapshots.RemoveAt(k);
        }

        /// <summary>Set the session id used in the saved filename (call once when the film starts).</summary>
        public void SetSessionId(string id) => Log.Timeline.meta.sessionId = id;

        /// <summary>Rewind support: drop events after the cutoff.</summary>
        public void TruncateAfter(float cutoff) => Log.TruncateAfter(cutoff);

        /// <summary>Current recording time, for the rewind controller.</summary>
        public float CurrentTime => Now();

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
