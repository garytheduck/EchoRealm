using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace EchoRealm.AI
{
    /// <summary>
    /// AI backend that uses the Anthropic Claude Messages API.
    /// Implements <see cref="IAIBackend"/> so AIManager can swap it in/out
    /// alongside OllamaBackend transparently.
    ///
    /// Setup:
    ///   1. Get an API key from https://console.anthropic.com/
    ///   2. Attach this component to the GameManager GameObject
    ///   3. Paste the key into the "Api Key" field in the Inspector
    ///      (or set ANTHROPIC_API_KEY environment variable for Editor builds)
    ///
    /// Recommended model: claude-haiku-4-5  (~$0.001 per command, ~1 s response)
    /// </summary>
    public class ClaudeBackend : MonoBehaviour, IAIBackend
    {
        [Header("Anthropic API")]
        [Tooltip("Your Anthropic API key. Get one at https://console.anthropic.com/")]
        [SerializeField] private string apiKey = "";

        [Tooltip("Model to use. claude-haiku-4-5 is fastest and cheapest; claude-sonnet-4-5 is smarter.")]
        [SerializeField] private string modelName = "claude-haiku-4-5";

        [Header("Request Settings")]
        [Tooltip("Maximum tokens in the response (256 is plenty for JSON commands).")]
        [SerializeField] private int maxTokens = 512;

        [Tooltip("Request timeout in seconds.")]
        [SerializeField] private float timeoutSeconds = 15f;

        [Tooltip("Temperature for generation (0 = deterministic, 1 = creative).")]
        [SerializeField, Range(0f, 1f)] private float temperature = 0.3f;

        [Header("Debug")]
        [SerializeField] private bool logRequests = true;
        [SerializeField] private bool logResponses = true;

        // ------------------------------------------------------------------
        // IAIBackend implementation
        // ------------------------------------------------------------------

        /// <inheritdoc/>
        public string BackendName => "Claude";

        /// <inheritdoc/>
        public bool IsReachable { get; private set; }

        /// <inheritdoc/>
        public bool IsBusy => _inFlight > 0;

        // Number of requests currently in flight. Claude handles concurrent calls,
        // so we count rather than single-flight-gate (lets a voice command and an
        // act-transition decision overlap instead of one knocking the other to fallback).
        private int _inFlight;

        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";

        private void Start()
        {
            // Prefer a gitignored Resources file so the secret never lives in the committed scene.
            // Create Assets/Resources/anthropic_api_key.txt (gitignored) and paste your key there.
            if (string.IsNullOrEmpty(apiKey))
            {
                var keyAsset = Resources.Load<TextAsset>("anthropic_api_key");
                if (keyAsset != null && !string.IsNullOrWhiteSpace(keyAsset.text))
                {
                    apiKey = keyAsset.text.Trim();
                    Debug.Log("[Claude] API key loaded from Resources/anthropic_api_key.txt");
                }
            }

            if (string.IsNullOrEmpty(apiKey))
                Debug.LogWarning("[Claude] API key not set (Inspector field empty and no Resources/anthropic_api_key.txt). Claude backend will be unavailable.");
        }

        /// <inheritdoc/>
        public async Task<bool> CheckConnectionAsync()
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                IsReachable = false;
                Debug.LogWarning("[Claude] Cannot check connection — API key is empty.");
                return false;
            }

            // Send a minimal ping request to verify the key is valid
            string pingPrompt = "Reply with the single word: OK";
            string result = await SendRawAsync(pingPrompt);
            IsReachable = !string.IsNullOrEmpty(result);

            if (IsReachable)
                Debug.Log("[Claude] API is reachable and key is valid.");
            else
                Debug.LogWarning("[Claude] API check failed. Verify your API key and internet connection.");

            return IsReachable;
        }

        /// <inheritdoc/>
        public async Task<string> SendPromptAsync(string prompt)
        {
            if (!IsKeyConfigured()) return null;

            _inFlight++; // allow concurrent requests; each is an independent HTTP call
            try
            {
                if (logRequests)
                    Debug.Log($"[Claude] Prompt → {TruncateForLog(prompt, 200)}");

                string response = await SendRawAsync(prompt);

                if (logResponses)
                    Debug.Log($"[Claude] Response ← {TruncateForLog(response, 300)}");

                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Claude] SendPromptAsync error: {ex.Message}");
                return null;
            }
            finally
            {
                _inFlight--;
            }
        }

        /// <inheritdoc/>
        public async Task<AICommandResponse> SendCommandRequestAsync(
            string userSpeech,
            string sceneState,
            string[] availableCommands)
        {
            if (!IsKeyConfigured()) return null;

            string commandList = string.Join(", ", availableCommands);

            // Claude understands strict JSON instructions better than Llama,
            // so we can be more precise here.
            string prompt =
                "You are the AI director of EchoRealm, a mixed reality film on HoloLens 2. " +
                $"Available commands: [{commandList}]. " +
                $"Current scene state: {sceneState}. " +
                $"A viewer said: \"{userSpeech}\". " +
                "Translate the viewer's words into world commands, using ONLY commands from the available list. Rules: " +
                "(1) A single sentence may contain SEVERAL requests — include EVERY matching command in the array " +
                "(e.g. \"make it rain and add a few more trees\" -> [\"rain\",\"grow_tree\"]). " +
                "(2) To remove / stop / undo / clear something, pick the matching opposite command " +
                "(e.g. \"make the rain go away\" -> [\"stop_rain\"]; \"put out the fire\" -> [\"stop_fire\"]; " +
                "\"turn it back to day\" -> [\"day\"]; \"clear the fog\" -> [\"stop_fog\"]; \"open the path\" -> [\"open_path\"]). " +
                "(3) Don't repeat something already active in the scene state, and if nothing in the list matches, return an empty array. " +
                "Respond ONLY with a valid JSON object — no markdown, no explanation. The JSON must contain exactly these fields: " +
                "\"commands\" (array of strings chosen from the available list), " +
                "\"consequence\" (string: what happens next in the story), " +
                "\"dobby_dialogue\" (string: a short line of in-world narration), " +
                "\"mood\" (string: exactly one of joyful/scared/curious/mysterious/calm/excited/sad).";

            string responseText = await SendPromptAsync(prompt);
            if (string.IsNullOrEmpty(responseText)) return null;

            // Strip markdown code fences if Claude added them
            responseText = StripCodeFences(responseText);

            try
            {
                var parsed = JsonUtility.FromJson<AICommandResponse>(responseText);
                return parsed;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Claude] Failed to parse response as AICommandResponse: {ex.Message}\nRaw: {responseText}");
                return null;
            }
        }

        // ------------------------------------------------------------------
        // HTTP
        // ------------------------------------------------------------------

        /// <summary>
        /// Send a single user message to the Claude Messages API and return the text reply.
        /// </summary>
        private async Task<string> SendRawAsync(string userContent)
        {
            var requestBody = new ClaudeRequest
            {
                model = modelName,
                max_tokens = maxTokens,
                temperature = temperature,
                messages = new[] { new ClaudeMessage { role = "user", content = userContent } }
            };

            string json = JsonUtility.ToJson(requestBody);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(ApiUrl, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-api-key", apiKey);
                request.SetRequestHeader("anthropic-version", AnthropicVersion);
                request.timeout = (int)timeoutSeconds;

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                    await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[Claude] HTTP error: {request.error} | Status: {request.responseCode} | Body: {request.downloadHandler.text}");
                    IsReachable = false;
                    return null;
                }

                IsReachable = true;

                // Parse the response envelope to extract the text content
                var envelope = JsonUtility.FromJson<ClaudeResponse>(request.downloadHandler.text);
                if (envelope?.content != null && envelope.content.Length > 0)
                    return envelope.content[0].text;

                Debug.LogWarning("[Claude] Response envelope had no content.");
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private bool IsKeyConfigured()
        {
            if (!string.IsNullOrEmpty(apiKey)) return true;
            Debug.LogWarning("[Claude] API key is not configured. Skipping request.");
            return false;
        }

        /// <summary>Remove ```json ... ``` fences that Claude sometimes adds.</summary>
        private static string StripCodeFences(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Trim();
            if (text.StartsWith("```"))
            {
                int firstNewline = text.IndexOf('\n');
                if (firstNewline >= 0) text = text.Substring(firstNewline + 1);
                if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3);
                text = text.Trim();
            }
            return text;
        }

        private static string TruncateForLog(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        // ------------------------------------------------------------------
        // JSON data classes (Claude Messages API format)
        // ------------------------------------------------------------------

        [Serializable]
        private class ClaudeRequest
        {
            public string model;
            public int max_tokens;
            public float temperature;
            public ClaudeMessage[] messages;
        }

        [Serializable]
        private class ClaudeMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class ClaudeResponse
        {
            public string id;
            public string type;
            public string role;
            public ClaudeContent[] content;
            public string model;
            public string stop_reason;
        }

        [Serializable]
        private class ClaudeContent
        {
            public string type;
            public string text;
        }
    }
}
