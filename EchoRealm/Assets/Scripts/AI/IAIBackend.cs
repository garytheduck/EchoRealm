using System.Threading.Tasks;

namespace EchoRealm.AI
{
    /// <summary>
    /// Common interface for all AI backends used in EchoRealm.
    /// Implemented by OllamaBackend (local LLM) and ClaudeBackend (Anthropic API).
    /// AIManager uses this interface to route requests to the correct backend
    /// and to fall back automatically when the primary backend is unavailable.
    /// </summary>
    public interface IAIBackend
    {
        /// <summary>Display name of this backend (e.g. "Ollama", "Claude").</summary>
        string BackendName { get; }

        /// <summary>True if the last connectivity check succeeded.</summary>
        bool IsReachable { get; }

        /// <summary>True if a request is currently in progress.</summary>
        bool IsBusy { get; }

        /// <summary>
        /// Ping the backend to verify it is reachable.
        /// Updates <see cref="IsReachable"/> and returns the same value.
        /// </summary>
        Task<bool> CheckConnectionAsync();

        /// <summary>
        /// Send a free-form prompt and return the raw text response.
        /// Used for Oracle monologues and hint generation.
        /// Returns null on failure.
        /// </summary>
        Task<string> SendPromptAsync(string prompt);

        /// <summary>
        /// Send a structured command request (speech → AI → commands).
        /// Returns a parsed <see cref="AICommandResponse"/> or null on failure.
        /// </summary>
        Task<AICommandResponse> SendCommandRequestAsync(
            string userSpeech,
            string sceneState,
            string[] availableCommands);

        /// <summary>
        /// Parse a spoken request into a single object manipulation (scale/move/rotate/reset)
        /// for the object the user is looking at. Returns null on failure or if unsupported.
        /// </summary>
        Task<AIObjectOp> SendObjectOpAsync(string phrase, string objectContext);
    }
}
