using Fusion;
using UnityEngine;
using EchoRealm.AI;
using EchoRealm.Film;
using EchoRealm.Interaction;

namespace EchoRealm.Networking
{
    /// <summary>
    /// Master-authoritative spine for the shared film. The master owns this NetworkObject
    /// (spawned by FusionNetworkManager). It holds the current act/variant as networked
    /// state (a late-join snapshot) and relays everything else via RPCs:
    ///   • clients send recognized speech to the master (RPC_SubmitSpeech),
    ///   • the master interprets it with the AI and broadcasts world commands (RPC_ApplyCommands),
    ///   • the master broadcasts act transitions (RPC_StartAct).
    /// Clients run no AI/act-flow logic of their own — they replay the master's decisions
    /// on their own co-located content (aligned via the QR SceneRoot).
    /// </summary>
    public class FilmSync : NetworkBehaviour
    {
        /// <summary>Singleton — set on every device in Spawned().</summary>
        public static FilmSync Instance { get; private set; }

        /// <summary>Fired on every device after an object op is applied (id, opType, factor,
        /// delta, degrees). Observational — used by TimelineRecorder on the master. No effect
        /// if unsubscribed.</summary>
        public static event System.Action<string, int, float, Vector3, float> OnObjectOpApplied;

        // Minimal late-join snapshot. Live transitions go via RPC_StartAct.
        [Networked] public int CurrentAct { get; set; }
        [Networked] public NetworkString<_16> ChosenVariant { get; set; }
        [Networked] public bool IsPocketed { get; set; }

        // Shared world transform (master-driven). Synced RELATIVE to the QR anchor pose so it stays
        // co-located on every device. SceneScale is the absolute uniform scale; 0 = not published yet.
        [Networked] public Vector3 SceneRelPos { get; set; }
        [Networked] public Quaternion SceneRelRot { get; set; }
        [Networked] public float SceneScale { get; set; }

        // Cumulative world-state snapshot (rain, grown trees, day/night, ...) for late joiners to replay.
        [Networked] public NetworkString<_512> WorldStateCsv { get; set; }

        [Header("Shared world transform")]
        [Tooltip("If true, the master's world scale/move is synced to every headset (co-located via the QR). " +
                 "Uncheck on the FilmSync prefab to make scaling/moving the world purely local again.")]
        [SerializeField] private bool syncSceneTransform = true;

        // Cached refs + last-published values (master) to avoid spamming networked writes.
        private Transform _sceneRoot;
        private QRAnchorManager _anchor;
        private Vector3 _lastPubPos;
        private Quaternion _lastPubRot = Quaternion.identity;
        private float _lastPubScale = -1f;

        // Master-side log of every world command issued, replayed to late joiners via WorldStateCsv.
        private readonly System.Collections.Generic.List<string> _worldCommandLog = new System.Collections.Generic.List<string>();
        private const int MaxWorldStateChars = 480; // headroom under NetworkString<_512>

        // Per-object manipulation state (master-authoritative): the current absolute local transform
        // of every prop touched by a "Claude, …" op, replayed to late joiners (idempotent).
        private struct ObjState { public Vector3 scale, pos; public Quaternion rot; }
        private readonly System.Collections.Generic.Dictionary<string, ObjState> _objStates =
            new System.Collections.Generic.Dictionary<string, ObjState>();

        // True only during a rewind apply, so live writes/AI don't fight the reconstruction.
        public bool IsRewinding { get; private set; }

        public override void Spawned()
        {
            Instance = this;
            Debug.Log($"[FilmSync] Spawned. HasStateAuthority={HasStateAuthority} (master={Runner.IsSharedModeMasterClient}).");

            // Late-join catch-up: if the film already advanced before we joined, jump to it.
            if (CurrentAct > 0)
            {
                var d = new AINarrativeDecision
                {
                    chosen_variant = ChosenVariant.ToString(),
                    mood = "mysterious",
                    oracle_narration = ""
                };
                Debug.Log($"[FilmSync] Late join — catching up to Act {CurrentAct} (variant '{d.chosen_variant}').");
                ActManager.Instance?.StartAct(CurrentAct, d);
            }

            // Late join while the world is pocketed: come up paused too.
            if (IsPocketed) WorldPocket.Instance?.ApplyPocket();

            // Master seeds the shared world transform from the scene's current pose/scale so clients
            // have a valid value to apply (a [Networked] Quaternion/scale defaults to zero otherwise).
            if (HasStateAuthority && syncSceneTransform && TryGetSceneRefs())
                PublishSceneTransform(force: true);

            // Late join: replay the world changes the master already made (rain, grown trees, ...).
            // Spawned runs once on THIS joining device; peers already here don't re-run it → no double-apply.
            if (!HasStateAuthority)
            {
                ApplyWorldStateSnapshot();
                RPC_RequestObjectStates(); // ask the master to re-send every manipulated object's transform
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // Shared world transform — master publishes, clients follow (co-located via the QR anchor)
        // ------------------------------------------------------------------

        // Master only: when the MASTER is the one grabbing the world, write the networked truth from
        // its own SceneRoot. A client's grab arrives via RPC_PushSceneTransform instead.
        public override void FixedUpdateNetwork()
        {
            if (!syncSceneTransform || !HasStateAuthority) return;
            if (IsPocketed) return;            // the pocket owns the transform while hidden
            if (IsRewinding) return;
            if (!TryGetSceneRefs()) return;
            if (IsLocallyManipulating())
                PublishSceneTransform(force: false);
        }

        // Every device: whoever is grabbing the world drives it (and a client streams it to the
        // master); everyone else follows the networked value so all headsets see the same size/place.
        public override void Render()
        {
            if (!syncSceneTransform) return;
            if (IsPocketed) return;
            if (!TryGetSceneRefs()) return;

            if (IsLocallyManipulating())
            {
                // I'm the source. The master already wrote it in FixedUpdateNetwork; a client streams
                // its transform up to the master (throttled). Either way, don't follow the network here.
                if (!HasStateAuthority) MaybePushToMaster();
                return;
            }

            if (SceneScale <= 0f) return;       // nothing published yet
            _sceneRoot.position   = _anchor.AnchorPosition + _anchor.AnchorRotation * SceneRelPos;
            _sceneRoot.rotation   = _anchor.AnchorRotation * SceneRelRot;
            _sceneRoot.localScale = new Vector3(SceneScale, SceneScale, SceneScale);
        }

        private static bool IsLocallyManipulating()
            => Interaction.SceneManipulationReporter.Instance != null
               && Interaction.SceneManipulationReporter.Instance.IsManipulating;

        // Client: stream the grabbed world's QR-relative transform to the master at ~20 Hz.
        private float _pushTimer;
        private void MaybePushToMaster()
        {
            _pushTimer += Time.unscaledDeltaTime;
            if (_pushTimer < 0.05f) return;
            _pushTimer = 0f;

            Quaternion invAnchor = Quaternion.Inverse(_anchor.AnchorRotation);
            Vector3 relPos = invAnchor * (_sceneRoot.position - _anchor.AnchorPosition);
            Quaternion relRot = invAnchor * _sceneRoot.rotation;
            float scale = _sceneRoot.localScale.x;
            if (scale <= 0f) scale = 0.0001f;
            RPC_PushSceneTransform(relPos, relRot, scale);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_PushSceneTransform(Vector3 relPos, Quaternion relRot, float scale)
        {
            // A client is grabbing the world; the master adopts its transform as the shared truth,
            // which then replicates to every device (including the master's own Render → it follows).
            SceneRelPos = relPos;
            SceneRelRot = relRot;
            SceneScale  = scale;
        }

        private bool TryGetSceneRefs()
        {
            if (_anchor == null) _anchor = QRAnchorManager.Instance;
            if (_anchor == null) return false;
            if (_sceneRoot == null) _sceneRoot = _anchor.SceneRoot;
            return _sceneRoot != null;
        }

        // Express the SceneRoot's world pose relative to the QR anchor (frame-independent) and write
        // it to networked state. Relative-to-anchor is what keeps it co-located: each device composes
        // the same relative offset with ITS OWN physical QR pose.
        private void PublishSceneTransform(bool force)
        {
            Quaternion invAnchor = Quaternion.Inverse(_anchor.AnchorRotation);
            Vector3 relPos = invAnchor * (_sceneRoot.position - _anchor.AnchorPosition);
            Quaternion relRot = invAnchor * _sceneRoot.rotation;
            float scale = _sceneRoot.localScale.x;
            if (scale <= 0f) scale = 0.0001f;   // keep the "published" sentinel positive

            bool changed = force
                || (relPos - _lastPubPos).sqrMagnitude > 1e-8f
                || Quaternion.Angle(relRot, _lastPubRot) > 0.05f
                || Mathf.Abs(scale - _lastPubScale) > 1e-5f;
            if (!changed) return;

            SceneRelPos = relPos;
            SceneRelRot = relRot;
            SceneScale  = scale;
            _lastPubPos = relPos;
            _lastPubRot = relRot;
            _lastPubScale = scale;
        }

        // ------------------------------------------------------------------
        // Act flow — called by the master's FilmDirector
        // ------------------------------------------------------------------

        /// <summary>Master only: record act+variant as networked state and broadcast the transition.</summary>
        public void DriveAct(int act, AINarrativeDecision decision)
        {
            if (!HasStateAuthority) return;

            string variant = decision?.chosen_variant ?? "default";
            CurrentAct = act;
            ChosenVariant = variant;
            RPC_StartAct(act, variant, decision?.mood ?? "mysterious", decision?.oracle_narration ?? "");
        }

        // RpcTargets.All includes the master, so ActManager.StartAct runs once on every device.
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_StartAct(int act, string variant, string mood, string narration)
        {
            var d = new AINarrativeDecision { chosen_variant = variant, mood = mood, oracle_narration = narration };
            Debug.Log($"[FilmSync] RPC_StartAct → Act {act} (variant '{variant}').");
            ActManager.Instance?.StartAct(act, d);
        }

        // ------------------------------------------------------------------
        // Film start — "START" voice command, networked so both headsets begin together
        // ------------------------------------------------------------------

        /// <summary>Any device requests the film to begin; only the master actually starts it,
        /// which broadcasts Act 1 to every headset via RPC_StartAct.</summary>
        public void RequestStartFilm()
        {
            if (HasStateAuthority) FilmDirector.Instance?.StartFilm();
            else RPC_RequestStartFilm();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestStartFilm()
        {
            FilmDirector.Instance?.StartFilm(); // runs on the master → DriveAct(1) → RPC_StartAct to all
        }

        // ------------------------------------------------------------------
        // Voice → shared world
        // ------------------------------------------------------------------

        /// <summary>Called on any device. Master interprets locally; clients forward to the master.</summary>
        public void SubmitSpeech(string text)
        {
            if (HasStateAuthority) ProcessSpeechAsAuthority(text);
            else RPC_SubmitSpeech(text);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_SubmitSpeech(string text)
        {
            // Log the client's phrase into the master's timeline so the saved transcript is complete —
            // the client's own OnSpeechRecognized only logged into the client's (unsaved) timeline.
            EchoRealm.Film.TimelineRecorder.Instance?.RecordRemoteUtterance("User (client)", text);
            ProcessSpeechAsAuthority(text); // runs on the master
        }

        /// <summary>Master only: pool the behavior, interpret with the AI, broadcast the commands.</summary>
        private async void ProcessSpeechAsAuthority(string text)
        {
            ActionCollector.Instance?.RecordVoiceCommand(text); // combined profile lives on the master

            var ai = AIManager.Instance;
            if (ai == null || !ai.IsReachable)
            {
                Debug.LogWarning("[FilmSync] No AI backend reachable on the master; ignoring speech.");
                return;
            }

            var exec = CommandExecutor.Instance;
            string scene = exec != null ? exec.GetSceneStateDescription() : "unknown";
            string[] available = exec != null ? exec.GetAvailableCommands() : new string[0];

            var response = await ai.SendCommandRequestAsync(text, scene, available);
            if (response?.commands != null && response.commands.Length > 0)
            {
                foreach (var cmd in response.commands)
                    ActionCollector.Instance?.RecordWorldChange(cmd);
                BroadcastCommands(response.commands);
                VoiceCommandProcessor.Instance?.RaiseAIResponse(response); // master UI/NarrativeManager react
            }
            else
            {
                Debug.LogWarning("[FilmSync] AI returned no commands for the submitted speech.");
            }
        }

        /// <summary>Master only: tell every device to execute these commands, and fold them into the
        /// cumulative world-state snapshot so a late joiner can replay them on connect.</summary>
        public void BroadcastCommands(string[] commands)
        {
            if (!HasStateAuthority || commands == null || commands.Length == 0) return;
            RecordAndPublishWorldState(commands);
            RPC_ApplyCommands(string.Join(",", commands));
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ApplyCommands(string commandsCsv)
        {
            var exec = CommandExecutor.Instance;
            if (exec == null || string.IsNullOrEmpty(commandsCsv)) return;
            foreach (var raw in commandsCsv.Split(','))
            {
                string cmd = raw.Trim();
                if (cmd.Length > 0) exec.ExecuteCommand(cmd);
            }
        }

        // Master: append commands to the log and republish the capped snapshot to networked state.
        private void RecordAndPublishWorldState(string[] commands)
        {
            foreach (var c in commands)
            {
                var t = c?.Trim();
                if (!string.IsNullOrEmpty(t)) _worldCommandLog.Add(t);
            }

            // Keep the most RECENT commands that fit the networked string (drop oldest if over cap).
            int start = 0;
            string csv = string.Join(",", _worldCommandLog);
            while (csv.Length > MaxWorldStateChars && start < _worldCommandLog.Count - 1)
            {
                start++;
                csv = string.Join(",", _worldCommandLog.GetRange(start, _worldCommandLog.Count - start));
            }
            if (start > 0)
                Debug.LogWarning($"[FilmSync] World-state snapshot truncated to the last " +
                                 $"{_worldCommandLog.Count - start} of {_worldCommandLog.Count} commands (NetworkString cap).");

            WorldStateCsv = csv;
        }

        // Late joiner only: replay the master's cumulative world state onto our freshly co-located scene.
        private void ApplyWorldStateSnapshot()
        {
            string csv = WorldStateCsv.ToString();
            if (string.IsNullOrEmpty(csv)) return;
            var exec = CommandExecutor.Instance;
            if (exec == null) return;

            Debug.Log($"[FilmSync] Late join — replaying world state: {csv}");
            foreach (var raw in csv.Split(','))
            {
                string cmd = raw.Trim();
                if (cmd.Length > 0) exec.ExecuteCommand(cmd);
            }
        }

        // ------------------------------------------------------------------
        // Object manipulation — "Claude, make this bigger" (networked + co-located)
        // ------------------------------------------------------------------

        /// <summary>Any device submits a resolved object op; the master applies it on EVERY device
        /// (RpcTargets.All includes the master) and records the resulting absolute transform.</summary>
        public void SubmitObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
        {
            if (HasStateAuthority) RPC_ApplyObjectOp(id, opType, factor, delta, degrees);
            else RPC_SubmitObjectOp(id, opType, factor, delta, degrees);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_SubmitObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
            => RPC_ApplyObjectOp(id, opType, factor, delta, degrees); // master fans it out to all

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ApplyObjectOp(string id, int opType, float factor, Vector3 delta, float degrees)
        {
            var mo = ManipulableRegistry.Instance?.FindById(id);
            if (mo == null) return;
            switch ((ObjOpType)opType)
            {
                case ObjOpType.Scale:  mo.ApplyScale(factor); break;
                case ObjOpType.Move:   mo.ApplyMove(delta);   break;
                case ObjOpType.Rotate: mo.ApplyYaw(degrees);  break;
                case ObjOpType.Reset:  mo.ResetTransform();   break;
            }
            if (HasStateAuthority) // master remembers the result so late joiners can catch up
            {
                mo.GetLocal(out var s, out var p, out var r);
                _objStates[id] = new ObjState { scale = s, pos = p, rot = r };
            }
            OnObjectOpApplied?.Invoke(id, opType, factor, delta, degrees);
        }

        // Late join: a joining client asks; the master re-sends every manipulated object's absolute
        // transform. Absolute sync is idempotent, so re-applying on peers that already match is a no-op.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestObjectStates()
        {
            foreach (var kv in _objStates)
                RPC_SetObjectState(kv.Key, kv.Value.scale, kv.Value.pos, kv.Value.rot);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_SetObjectState(string id, Vector3 scale, Vector3 pos, Quaternion rot)
        {
            var mo = ManipulableRegistry.Instance?.FindById(id);
            if (mo != null) mo.SetLocal(scale, pos, rot);
        }

        // ------------------------------------------------------------------
        // Interaction → master (cooperation detection across both headsets)
        // ------------------------------------------------------------------

        /// <summary>Any device reports a player interaction; the master's detector evaluates cooperation.</summary>
        public void SubmitInteraction(int playerIndex, string objectId, InteractionType type)
        {
            if (HasStateAuthority) CooperationDetector.Instance?.ReportInteraction(playerIndex, objectId, type);
            else RPC_SubmitInteraction(playerIndex, objectId, (int)type);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_SubmitInteraction(int playerIndex, string objectId, int type)
        {
            CooperationDetector.Instance?.ReportInteraction(playerIndex, objectId, (InteractionType)type);
        }

        // ------------------------------------------------------------------
        // Pocket the world — shared pause across every headset (anyone can trigger)
        // ------------------------------------------------------------------

        /// <summary>Any device requests pocket(true)/unpocket(false); the master broadcasts it to all.</summary>
        public void RequestPocket(bool pocketed)
        {
            if (HasStateAuthority) { IsPocketed = pocketed; RPC_SetPocket(pocketed); }
            else RPC_RequestPocket(pocketed);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestPocket(bool pocketed)
        {
            IsPocketed = pocketed;     // record on the master (for late-joiners)
            RPC_SetPocket(pocketed);   // fan out to everyone
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_SetPocket(bool pocketed)
        {
            if (pocketed) WorldPocket.Instance?.ApplyPocket();
            else          WorldPocket.Instance?.ApplyUnpocket();
        }

        // ------------------------------------------------------------------
        // Rewind — jump the shared world back to its state N seconds ago
        // ------------------------------------------------------------------

        /// <summary>Any device asks to rewind; only the master computes + broadcasts it.</summary>
        public void RequestRewind(float seconds)
        {
            if (HasStateAuthority) DoRewind(seconds);
            else RPC_RequestRewind(seconds);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestRewind(float seconds) => DoRewind(seconds);

        // Master only: truncate the timeline to T, replay locally, broadcast absolute state.
        private void DoRewind(float seconds)
        {
            if (!HasStateAuthority) return;
            var recorder = EchoRealm.Film.TimelineRecorder.Instance;
            if (recorder == null) return;

            float t = Mathf.Max(0f, recorder.CurrentTime - seconds);
            IsRewinding = true;

            recorder.TruncateAfter(t);
            recorder.RestoreAiMemoryAt(t);   // roll the AI's behavioral memory back to T (spec R9)
            var target = new EchoRealm.Film.UnityReplayTarget();
            EchoRealm.Film.TimelineReplayer.ApplyStateAt(recorder.Timeline, t, seeking: true, target);

            // Rebuild authoritative per-object state from the reconstructed scene, then push to peers.
            _objStates.Clear();
            var reg = ManipulableRegistry.Instance;
            if (reg != null)
            {
                foreach (var mo in reg.All)
                {
                    if (mo == null) continue;
                    mo.GetLocal(out var s, out var p, out var r);
                    _objStates[mo.Id] = new ObjState { scale = s, pos = p, rot = r };
                }
            }

            // Recompute the world-state CSV from the surviving WorldCommand events.
            _worldCommandLog.Clear();
            foreach (var e in recorder.Timeline.events)
                if (e.kind == EchoRealm.Film.EventKind.WorldCommand && !e.transient)
                    _worldCommandLog.Add(e.id);
            WorldStateCsv = TrimWorldCsv();

            CurrentAct = ActManager.Instance != null ? ActManager.Instance.CurrentAct : CurrentAct;
            // Keep the networked variant in step with the rewound act (the local replay pass above
            // set it via ApplyActState). Without this, peers would reconstruct the rewound act with
            // the LAST DriveAct's variant — e.g. an Act-4 ending key while rewinding into Act 3 —
            // and stage the wrong obstacle/ending.
            if (ActManager.Instance != null) ChosenVariant = ActManager.Instance.CurrentVariant ?? "default";
            EchoRealm.Film.FilmDirector.Instance?.RewindToAct(CurrentAct); // re-arm the act flow to T (spec R9)

            RPC_RewindApply(t, CurrentAct, ChosenVariant.ToString(), WorldStateCsv.ToString());
            foreach (var kv in _objStates)
                RPC_SetObjectState(kv.Key, kv.Value.scale, kv.Value.pos, kv.Value.rot);

            IsRewinding = false;
            Debug.Log($"[FilmSync] Rewound to t={t:F1}s ({seconds:F0}s back).");
        }

        // Every device: reset to baseline, replay world + act state to T. Objects arrive via RPC_SetObjectState.
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_RewindApply(float t, int act, string variant, string worldCsv)
        {
            var reg = ManipulableRegistry.Instance;
            if (reg != null)
                foreach (var mo in reg.All)
                    if (mo != null) mo.ResetTransform();

            CommandExecutor.Instance?.ResetWorldToDefaults();
            ActManager.Instance?.ApplyActState(act, variant);

            var exec = CommandExecutor.Instance;
            if (exec != null && !string.IsNullOrEmpty(worldCsv))
                foreach (var raw in worldCsv.Split(','))
                {
                    string cmd = raw.Trim();
                    if (cmd.Length > 0) exec.ExecuteCommand(cmd);
                }
        }

        // Cap the world CSV to the networked-string budget (most-recent wins) — same rule as RecordAndPublishWorldState.
        private NetworkString<_512> TrimWorldCsv()
        {
            int start = 0;
            string csv = string.Join(",", _worldCommandLog);
            while (csv.Length > MaxWorldStateChars && start < _worldCommandLog.Count - 1)
            {
                start++;
                csv = string.Join(",", _worldCommandLog.GetRange(start, _worldCommandLog.Count - start));
            }
            return csv;
        }
    }
}
