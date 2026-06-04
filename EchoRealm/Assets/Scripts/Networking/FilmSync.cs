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

        // Minimal late-join snapshot. Live transitions go via RPC_StartAct.
        [Networked] public int CurrentAct { get; set; }
        [Networked] public NetworkString<_16> ChosenVariant { get; set; }
        [Networked] public bool IsPocketed { get; set; }

        // Shared world transform (master-driven). Synced RELATIVE to the QR anchor pose so it stays
        // co-located on every device. SceneScale is the absolute uniform scale; 0 = not published yet.
        [Networked] public Vector3 SceneRelPos { get; set; }
        [Networked] public Quaternion SceneRelRot { get; set; }
        [Networked] public float SceneScale { get; set; }

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
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // Shared world transform — master publishes, clients follow (co-located via the QR anchor)
        // ------------------------------------------------------------------

        // Master: publish the SceneRoot's QR-relative pose + uniform scale when it changes.
        public override void FixedUpdateNetwork()
        {
            if (!syncSceneTransform || !HasStateAuthority) return;
            if (IsPocketed) return;            // the pocket owns the transform while hidden
            if (!TryGetSceneRefs()) return;
            PublishSceneTransform(force: false);
        }

        // Clients: apply the master's QR-relative pose + scale to their own co-located SceneRoot.
        public override void Render()
        {
            if (!syncSceneTransform || HasStateAuthority) return; // master is the source of truth
            if (IsPocketed) return;
            if (SceneScale <= 0f) return;       // master hasn't published yet
            if (!TryGetSceneRefs()) return;

            _sceneRoot.position   = _anchor.AnchorPosition + _anchor.AnchorRotation * SceneRelPos;
            _sceneRoot.rotation   = _anchor.AnchorRotation * SceneRelRot;
            _sceneRoot.localScale = new Vector3(SceneScale, SceneScale, SceneScale);
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

        /// <summary>Master only: tell every device to execute these commands.</summary>
        public void BroadcastCommands(string[] commands)
        {
            if (!HasStateAuthority || commands == null || commands.Length == 0) return;
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
    }
}
