using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace EchoRealm.AI
{
    /// <summary>
    /// HTTP client for communicating with the Ollama REST API (localhost:11434).
    /// Sends prompts to Llama 3.1 8B and returns structured JSON responses.
    ///
    /// Usage:
    ///   var client = OllamaClient.Instance;
    ///   var response = await client.SendPromptAsync("Your prompt here");
    /// </summary>
    public class OllamaClient : MonoBehaviour
    {
        [Header("Ollama Server")]
        [Tooltip("Base URL of the Ollama server. Default: http://localhost:11434")]
        [SerializeField] private string serverUrl = "http://127.0.0.1:11500";

        [Tooltip("Model to use for generation.")]
        [SerializeField] private string modelName = "llama3.2:3b";

        [Header("Request Settings")]
        [Tooltip("Request timeout in seconds.")]
        [SerializeField] private float timeoutSeconds = 30f;

        [Tooltip("Temperature for generation (0 = deterministic, 1 = creative).")]
        [SerializeField, Range(0f, 1f)] private float temperature = 0.7f;

        [Header("Debug")]
        [SerializeField] private bool logRequests = true;
        [SerializeField] private bool logResponses = true;

        /// <summary>True if a request is currently in progress.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>True if the last connectivity check to Ollama succeeded.</summary>
        public bool IsServerReachable { get; private set; }

        public static OllamaClient Instance { get; private set; }

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
            await CheckServerConnection();
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Send a prompt to Ollama and get a raw text response.
        /// </summary>
        public async Task<string> SendPromptAsync(string prompt)
        {
            if (IsBusy)
            {
                Debug.LogWarning("[Ollama] Request already in progress. Ignoring.");
                return null;
            }

            IsBusy = true;

            try
            {
                var requestBody = new OllamaRequest
                {
                    model = modelName,
                    prompt = prompt,
                    stream = false,
                    format = "json",
                    options = new OllamaOptions { temperature = temperature }
                };

                string json = JsonUtility.ToJson(requestBody);

                if (logRequests)
                    Debug.Log($"[Ollama] Request → {modelName}: {TruncateForLog(prompt, 200)}");

                string responseJson = await PostAsync($"{serverUrl}/api/generate", json);

                if (string.IsNullOrEmpty(responseJson))
                    return null;

                // Parse Ollama's response envelope to extract the generated text
                var responseEnvelope = JsonUtility.FromJson<OllamaResponse>(responseJson);
                string generatedText = responseEnvelope.response;

                if (logResponses)
                    Debug.Log($"[Ollama] Response ← {TruncateForLog(generatedText, 300)}");

                return generatedText;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Ollama] Error: {ex.Message}");
                return null;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Send a structured prompt for EchoRealm AI commands.
        /// Returns parsed AICommandResponse or null on failure.
        /// </summary>
        public async Task<AICommandResponse> SendCommandRequestAsync(string userSpeech, string sceneState, string[] availableCommands)
        {
            string commandList = string.Join(", ", availableCommands);

            string prompt =
                "You are the AI director of EchoRealm, a mixed reality film on HoloLens 2. " +
                $"Available commands: [{commandList}]. " +
                $"Scene: {sceneState}. " +
                $"User said: \"{userSpeech}\". " +
                "Respond with JSON containing ALL of these fields: " +
                "commands (array of command strings from the available list), " +
                "consequence (string: what happens next in the story), " +
                "dobby_dialogue (string: what Dobby the character says in response), " +
                "mood (string: one of joyful/scared/curious/mysterious/calm/excited/sad). " +
                "ALL fields must have string values, not null.";

            string responseText = await SendPromptAsync(prompt);
            if (string.IsNullOrEmpty(responseText)) return null;

            try
            {
                var parsed = JsonUtility.FromJson<AICommandResponse>(responseText);
                return parsed;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Ollama] Failed to parse AI response as AICommandResponse: {ex.Message}\nRaw: {responseText}");
                return null;
            }
        }

        /// <summary>
        /// Check if the Ollama server is reachable.
        /// </summary>
        public async Task<bool> CheckServerConnection()
        {
            try
            {
                string response = await GetAsync($"{serverUrl}/");
                IsServerReachable = !string.IsNullOrEmpty(response) && response.Contains("Ollama is running");

                if (IsServerReachable)
                    Debug.Log("[Ollama] Server is reachable and running.");
                else
                    Debug.LogWarning("[Ollama] Server responded but may not be Ollama.");

                return IsServerReachable;
            }
            catch
            {
                IsServerReachable = false;
                Debug.LogWarning($"[Ollama] Server NOT reachable at {serverUrl}. Make sure Ollama is running.");
                return false;
            }
        }

        // ------------------------------------------------------------------
        // HTTP Helpers (using UnityWebRequest for UWP compatibility)
        // ------------------------------------------------------------------

        private async Task<string> PostAsync(string url, string jsonBody)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)timeoutSeconds;

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                    await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[Ollama] HTTP POST failed: {request.error} (URL: {url})");
                    return null;
                }

                return request.downloadHandler.text;
            }
        }

        private async Task<string> GetAsync(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 5;

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                    await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                    return null;

                return request.downloadHandler.text;
            }
        }

        private string TruncateForLog(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        // ------------------------------------------------------------------
        // JSON Data Classes
        // ------------------------------------------------------------------

        [Serializable]
        private class OllamaRequest
        {
            public string model;
            public string prompt;
            public bool stream;
            public string format;
            public OllamaOptions options;
        }

        [Serializable]
        private class OllamaOptions
        {
            public float temperature;
        }

        [Serializable]
        private class OllamaResponse
        {
            public string model;
            public string response;
            public bool done;
        }
    }

    /// <summary>
    /// Structured response from Ollama for EchoRealm AI commands.
    /// Parsed from JSON returned by the LLM.
    /// </summary>
    [Serializable]
    public class AICommandResponse
    {
        public string[] commands;
        public string consequence;
        public string dobby_dialogue;
        public string mood;
    }
}
