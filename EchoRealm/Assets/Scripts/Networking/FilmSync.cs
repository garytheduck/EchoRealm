using Fusion;
using UnityEngine;
using EchoRealm.AI;
using EchoRealm.Film;

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
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
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
    }
}
