using System;
using System.Threading.Tasks;
using UnityEngine;

namespace EchoRealm.AI
{
    /// <summary>
    /// Central AI orchestrator for EchoRealm.
    /// Routes requests to the active backend (Ollama or Claude) and automatically
    /// falls back to the secondary backend when the primary is unavailable.
    ///
    /// All other scripts should talk to AIManager — never directly to OllamaClient
    /// or ClaudeBackend. This keeps the rest of the codebase backend-agnostic.
    ///
    /// Recommended dissertation setup:
    ///   Mode = HybridClaudePrimary  →  Claude handles live demos (fast, reliable),
    ///                                   Ollama kicks in when offline (academic merit).
    /// </summary>
    public class AIManager : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Inspector
        // ------------------------------------------------------------------

        [Header("Backend Selection")]
        [Tooltip("Which backend(s) to use.\n" +
                 "OllamaOnly      — local LLM, no internet needed\n" +
                 "ClaudeOnly      — Anthropic API, fastest responses\n" +
                 "HybridClaudePrimary — Claude first, Ollama as fallback\n" +
                 "HybridOllamaPrimary — Ollama first, Claude as fallback")]
        [SerializeField] private AIBackendMode mode = AIBackendMode.HybridClaudePrimary;

        [Header("References")]
        [SerializeField] private OllamaClient ollamaClient;
        [SerializeField] private ClaudeBackend claudeBackend;

        [Header("Debug")]
        [SerializeField] private bool logFallback = true;

        // ------------------------------------------------------------------
        // Public state
        // ------------------------------------------------------------------

        /// <summary>Name of the backend that handled the most recent request.</summary>
        public string ActiveBackendName { get; private set; } = "None";

        /// <summary>True if either backend is currently processing a request.</summary>
        public bool IsBusy =>
            (ollamaClient != null && ollamaClient.IsBusy) ||
            (claudeBackend != null && claudeBackend.IsBusy);

        /// <summary>True if at least one backend is reachable.</summary>
        public bool IsReachable =>
            (ollamaClient != null && ollamaClient.IsServerReachable) ||
            (claudeBackend != null && claudeBackend.IsReachable);

        /// <summary>True if Ollama specifically is reachable.</summary>
        public bool IsOllamaReachable => ollamaClient != null && ollamaClient.IsServerReachable;

        /// <summary>True if Claude specifically is reachable.</summary>
        public bool IsClaudeReachable => claudeBackend != null && claudeBackend.IsReachable;

        public static AIManager Instance { get; private set; }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Auto-find if not assigned in Inspector
            if (ollamaClient == null) ollamaClient = FindObjectOfType<OllamaClient>();
            if (claudeBackend == null) claudeBackend = FindObjectOfType<ClaudeBackend>();
        }

        private async void Start()
        {
            await CheckAllConnectionsAsync();
        }

        // ------------------------------------------------------------------
        // Public API — used by VoiceCommandProcessor and NarrativeManager
        // ------------------------------------------------------------------

        /// <summary>
        /// Check connectivity for all configured backends and log results.
        /// Called at startup by the bootstrapper.
        /// </summary>
        public async Task CheckAllConnectionsAsync()
        {
            bool ollamaOk = false;
            bool claudeOk = false;

            if (UsesOllama() && ollamaClient != null)
                ollamaOk = await ollamaClient.CheckServerConnection();

            if (UsesClaude() && claudeBackend != null)
                claudeOk = await claudeBackend.CheckConnectionAsync();

            Debug.Log($"[AIManager] Status — Ollama: {(ollamaOk ? "✓" : "✗")}  Claude: {(claudeOk ? "✓" : "✗")}  Mode: {mode}");
        }

        /// <summary>
        /// Send a structured command request and return the parsed AI response.
        /// Automatically routes to primary backend and falls back on failure.
        /// </summary>
        public async Task<AICommandResponse> SendCommandRequestAsync(
            string userSpeech,
            string sceneState,
            string[] availableCommands)
        {
            var (primary, fallback) = GetBackendPair();

            // Try primary
            if (primary != null && CanUse(primary))
            {
                var result = await primary.SendCommandRequestAsync(userSpeech, sceneState, availableCommands);
                if (result != null)
                {
                    ActiveBackendName = primary.BackendName;
                    return result;
                }
                LogFallback(primary.BackendName, "SendCommandRequestAsync returned null");
            }

            // Try fallback
            if (fallback != null && CanUse(fallback))
            {
                var result = await fallback.SendCommandRequestAsync(userSpeech, sceneState, availableCommands);
                if (result != null)
                {
                    ActiveBackendName = fallback.BackendName;
                    return result;
                }
            }

            Debug.LogWarning("[AIManager] All backends failed for SendCommandRequestAsync.");
            return null;
        }

        /// <summary>
        /// Send a free-form prompt (for Oracle monologues, hints, etc.).
        /// Automatically routes to primary backend and falls back on failure.
        /// </summary>
        public async Task<string> SendPromptAsync(string prompt)
        {
            var (primary, fallback) = GetBackendPair();

            if (primary != null && CanUse(primary))
            {
                string result = await primary.SendPromptAsync(prompt);
                if (!string.IsNullOrEmpty(result))
                {
                    ActiveBackendName = primary.BackendName;
                    return result;
                }
                LogFallback(primary.BackendName, "SendPromptAsync returned empty");
            }

            if (fallback != null && CanUse(fallback))
            {
                string result = await fallback.SendPromptAsync(prompt);
                if (!string.IsNullOrEmpty(result))
                {
                    ActiveBackendName = fallback.BackendName;
                    return result;
                }
            }

            Debug.LogWarning("[AIManager] All backends failed for SendPromptAsync.");
            return null;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns (primaryBackend, fallbackBackend) according to current mode.
        /// Either entry may be null if that backend isn't configured.
        /// </summary>
        private (IAIBackend primary, IAIBackend fallback) GetBackendPair()
        {
            return mode switch
            {
                AIBackendMode.OllamaOnly => (WrapOllama(), null),
                AIBackendMode.ClaudeOnly => (claudeBackend, null),
                AIBackendMode.HybridClaudePrimary => (claudeBackend, WrapOllama()),
                AIBackendMode.HybridOllamaPrimary => (WrapOllama(), claudeBackend),
                _ => (WrapOllama(), null)
            };
        }

        /// <summary>
        /// Returns true if the backend is not busy and was reachable at last check.
        /// We still attempt the request even if !IsReachable so a transient failure
        /// doesn't permanently disable a backend.
        /// </summary>
        private static bool CanUse(IAIBackend backend)
        {
            // Don't skip a backend just because it's mid-request — Claude handles
            // concurrent calls. We only fall back to the secondary on an actual
            // failure (null/empty result), not on transient busyness.
            return backend != null;
        }

        private bool UsesOllama() =>
            mode == AIBackendMode.OllamaOnly ||
            mode == AIBackendMode.HybridClaudePrimary ||
            mode == AIBackendMode.HybridOllamaPrimary;

        private bool UsesClaude() =>
            mode == AIBackendMode.ClaudeOnly ||
            mode == AIBackendMode.HybridClaudePrimary ||
            mode == AIBackendMode.HybridOllamaPrimary;

        /// <summary>Wraps OllamaClient in the IAIBackend interface.</summary>
        private IAIBackend WrapOllama() => ollamaClient != null ? new OllamaBackendAdapter(ollamaClient) : null;

        private void LogFallback(string backendName, string reason)
        {
            if (logFallback)
                Debug.LogWarning($"[AIManager] {backendName} failed ({reason}). Trying fallback...");
        }
    }

    // ------------------------------------------------------------------
    // Backend mode enum
    // ------------------------------------------------------------------

    public enum AIBackendMode
    {
        /// <summary>Use only local Ollama. Works offline, no API costs.</summary>
        OllamaOnly,

        /// <summary>Use only Claude API. Fastest, best quality, requires internet + key.</summary>
        ClaudeOnly,

        /// <summary>Claude first, Ollama as fallback. Best for dissertation demos.</summary>
        HybridClaudePrimary,

        /// <summary>Ollama first, Claude as fallback. Better when offline is preferred.</summary>
        HybridOllamaPrimary
    }

    // ------------------------------------------------------------------
    // Adapter: wraps OllamaClient (MonoBehaviour) into IAIBackend
    // ------------------------------------------------------------------

    /// <summary>
    /// Thin adapter so OllamaClient (which predates IAIBackend) can be used
    /// polymorphically without modifying its source.
    /// </summary>
    internal class OllamaBackendAdapter : IAIBackend
    {
        private readonly OllamaClient _client;

        public OllamaBackendAdapter(OllamaClient client) => _client = client;

        public string BackendName => "Ollama";
        public bool IsReachable => _client.IsServerReachable;
        public bool IsBusy => _client.IsBusy;

        public Task<bool> CheckConnectionAsync() => _client.CheckServerConnection();
        public Task<string> SendPromptAsync(string prompt) => _client.SendPromptAsync(prompt);

        public Task<AICommandResponse> SendCommandRequestAsync(
            string userSpeech, string sceneState, string[] availableCommands)
            => _client.SendCommandRequestAsync(userSpeech, sceneState, availableCommands);
    }
}
