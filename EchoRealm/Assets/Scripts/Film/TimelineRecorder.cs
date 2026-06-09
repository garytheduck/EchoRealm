using System.Collections.Generic;
using UnityEngine;
using EchoRealm.AI;
using EchoRealm.Characters;
using EchoRealm.Interaction;
using EchoRealm.Networking;
using MixedReality.Toolkit.SpatialManipulation;

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

        private void Start()
        {
            SubscribeInstances();
            StartCoroutine(HookManipulablesWhenReady());
        }

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

        // While a rewind is reconstructing the scene, CommandExecutor re-fires OnCommandExecuted for the
        // reset + replay commands. Those are NOT new player actions — ignore ALL capture during a rewind,
        // otherwise the rewind pollutes its own timeline and corrupts subsequent rewinds.
        private static bool RewindInProgress =>
            EchoRealm.Networking.FilmSync.Instance != null && EchoRealm.Networking.FilmSync.Instance.IsRewinding;

        private void HandleWorldCommand(string command)
        {
            if (RewindInProgress) return;
            Log.AddWorldCommand(command, Now());
            SnapshotAiMemory();
        }

        private void HandleObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
        {
            if (RewindInProgress) return;
            Log.AddObjectOp(id, opType, factor, delta, degrees, Now());
            SnapshotAiMemory();
        }

        private void HandleActStarted(int act, string variant)
        {
            if (RewindInProgress) return;
            Log.AddActTransition(act, variant, Now());
            Log.Timeline.meta.finalAct = act;
            SnapshotAiMemory();
        }

        private void HandleSpoke(string text, string mood)
        {
            if (RewindInProgress) return;
            Log.AddUtterance("Oracle", text, Now());
            SnapshotAiMemory();
        }

        private void HandleSpeech(string text)
        {
            if (RewindInProgress) return;
            Log.AddUtterance("User", text, Now());
            SnapshotAiMemory();
        }

        private void HandleAIResponse(AICommandResponse r)
        {
            if (RewindInProgress || r == null) return;
            string cmds = (r.commands != null) ? string.Join(",", r.commands) : "";
            Log.AddUtterance("AI", $"decided [{cmds}] mood={r.mood}", Now());
            SnapshotAiMemory();
        }

        private System.Collections.IEnumerator HookManipulablesWhenReady()
        {
            // ManipulableRegistry registers props in its own Start(); wait until it's populated.
            float timeout = 5f;
            while (timeout > 0f && (ManipulableRegistry.Instance == null))
            { timeout -= Time.deltaTime; yield return null; }
            yield return null; // let registration finish
            var reg = ManipulableRegistry.Instance;
            if (reg == null) yield break;
            foreach (var mo in reg.All)
            {
                if (mo == null) continue;
                var om = mo.GetComponent<ObjectManipulator>();
                if (om == null) continue;
                var captured = mo;
                om.lastSelectExited.AddListener(_ => OnPropManipulated(captured));
            }
        }

        // A hand-grab of a prop ended — record its resulting absolute local transform.
        private void OnPropManipulated(ManipulableObject mo)
        {
            if (RewindInProgress || mo == null) return;
            mo.GetLocal(out var s, out var p, out var r);
            Log.AddObjectState(mo.Id, s, p, r, Now());
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

        /// <summary>Record an utterance that originated on ANOTHER device (e.g. the client's speech, which
        /// the master interprets but whose raw OnSpeechRecognized only fired on the client). Lets the
        /// master's saved transcript include both players' phrases — not just the client's resulting
        /// commands. Respects the rewind guard, like the local handlers.</summary>
        public void RecordRemoteUtterance(string speaker, string text)
        {
            if (RewindInProgress || string.IsNullOrEmpty(text)) return;
            Log.AddUtterance(speaker, text, Now());
            SnapshotAiMemory();
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
