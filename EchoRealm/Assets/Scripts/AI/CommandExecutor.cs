using System;
using System.Collections.Generic;
using UnityEngine;
using EchoRealm.Effects;

namespace EchoRealm.AI
{
    /// <summary>
    /// Executes AI commands by activating pre-built Unity effects, animations,
    /// and character behaviors. Maps string command names from Ollama responses
    /// to concrete Unity actions.
    ///
    /// Uses WeatherController, EnvironmentController, and CameraEffects
    /// for particle effects (auto-created if not manually assigned).
    /// </summary>
    public class CommandExecutor : MonoBehaviour
    {
        [Header("Scene State")]
        [SerializeField] private bool isNight = false;
        [SerializeField] private bool isRaining = false;
        [SerializeField] private bool hasFog = false;
        [SerializeField] private bool hasForest = false;
        [SerializeField] private bool hasFire = false;
        [SerializeField] private bool pathOpen = true;

        [Header("Effect Controllers (auto-found if null)")]
        [SerializeField] private WeatherController weatherController;
        [SerializeField] private EnvironmentController environmentController;
        [SerializeField] private CameraEffects cameraEffects;
        [SerializeField] private SkyboxController skyboxController;

        [Header("Environment References")]
        [SerializeField] private GameObject forestGroup;
        [SerializeField] private GameObject flowersGroup;
        [SerializeField] private GameObject pathBlockade;

        [Header("Character References")]
        [SerializeField] private Animator dobbyAnimator;
        [SerializeField] private Animator astronautAnimator;
        // Oracle is handled separately by OracleController

        [Header("Debug")]
        [SerializeField] private bool logCommands = true;

        /// <summary>Fired after each command is executed with the command name.</summary>
        public event Action<string> OnCommandExecuted;

        // Track all available commands
        private static readonly string[] allCommands = new string[]
        {
            "rain", "stop_rain",
            "night", "day",
            "fire", "stop_fire",
            "wind", "stop_wind",
            "earthquake",
            "fog", "stop_fog",
            "open_path", "close_path",
            "spawn_butterflies", "stop_butterflies",
            "spawn_fireflies", "stop_fireflies",
            "lightning",
            "grow_tree", "grow_flowers",
            "shrink_scene", "glow_objects",
            "dobby_dance", "dobby_wave", "dobby_scared", "dobby_celebrate",
            "astronaut_jump", "astronaut_wave", "astronaut_look_around"
        };

        public static CommandExecutor Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Auto-find controllers if not assigned
            if (weatherController == null) weatherController = FindObjectOfType<WeatherController>();
            if (environmentController == null) environmentController = FindObjectOfType<EnvironmentController>();
            if (cameraEffects == null) cameraEffects = FindObjectOfType<CameraEffects>();
            if (skyboxController == null) skyboxController = FindObjectOfType<SkyboxController>();
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Returns the list of all command names the AI can use.</summary>
        public string[] GetAvailableCommands() => allCommands;

        /// <summary>
        /// Returns a human-readable description of the current scene state.
        /// Sent to Ollama so the AI knows what's already active.
        /// </summary>
        public string GetSceneStateDescription()
        {
            var parts = new List<string>();

            parts.Add(isNight ? "night" : "day");
            if (isRaining) parts.Add("raining");
            if (hasFog) parts.Add("foggy");
            if (hasForest) parts.Add("forest visible");
            if (hasFire) parts.Add("fire burning");
            if (!pathOpen) parts.Add("path blocked");

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Execute all commands from an AI response.
        /// </summary>
        public void ExecuteCommands(AICommandResponse response)
        {
            if (response.commands == null || response.commands.Length == 0)
            {
                Log("No commands to execute.");
                return;
            }

            foreach (string cmd in response.commands)
            {
                ExecuteCommand(cmd.Trim().ToLowerInvariant());
            }
        }

        /// <summary>
        /// Execute a single command by name.
        /// </summary>
        public void ExecuteCommand(string command)
        {
            Log($"Executing: {command}");

            switch (command)
            {
                // --- Weather (via WeatherController) ---
                case "rain":
                    if (weatherController != null) weatherController.SetEffect(weatherController.Rain, true);
                    isRaining = true;
                    break;
                case "stop_rain":
                    if (weatherController != null) weatherController.SetEffect(weatherController.Rain, false);
                    isRaining = false;
                    break;

                case "night":
                    if (skyboxController != null) skyboxController.TransitionToNight();
                    isNight = true;
                    break;
                case "day":
                    if (skyboxController != null) skyboxController.TransitionToDay();
                    isNight = false;
                    break;

                case "fire":
                    if (environmentController != null) environmentController.SetFire(true);
                    hasFire = true;
                    break;
                case "stop_fire":
                    if (environmentController != null) environmentController.SetFire(false);
                    hasFire = false;
                    break;

                case "wind":
                    if (weatherController != null) weatherController.SetEffect(weatherController.Wind, true);
                    break;
                case "stop_wind":
                    if (weatherController != null) weatherController.SetEffect(weatherController.Wind, false);
                    break;

                case "fog":
                    if (weatherController != null) weatherController.SetEffect(weatherController.Fog, true);
                    hasFog = true;
                    break;
                case "stop_fog":
                    if (weatherController != null) weatherController.SetEffect(weatherController.Fog, false);
                    hasFog = false;
                    break;

                case "earthquake":
                    if (cameraEffects != null) cameraEffects.Earthquake();
                    if (weatherController != null) weatherController.SetEffect(weatherController.Dust, true);
                    break;

                case "lightning":
                    if (cameraEffects != null) cameraEffects.LightningFlash();
                    break;

                // --- Environment ---
                case "open_path":
                    SetGameObject(pathBlockade, false);
                    pathOpen = true;
                    break;
                case "close_path":
                    SetGameObject(pathBlockade, true);
                    pathOpen = false;
                    break;

                case "grow_tree":
                case "grow_flowers":
                    SetGameObject(command == "grow_tree" ? forestGroup : flowersGroup, true);
                    if (command == "grow_tree") hasForest = true;
                    break;

                case "spawn_butterflies":
                    if (environmentController != null) environmentController.SetEffect(environmentController.Butterflies, true);
                    break;
                case "stop_butterflies":
                    if (environmentController != null) environmentController.SetEffect(environmentController.Butterflies, false);
                    break;

                case "spawn_fireflies":
                    if (environmentController != null) environmentController.SetEffect(environmentController.Fireflies, true);
                    break;
                case "stop_fireflies":
                    if (environmentController != null) environmentController.SetEffect(environmentController.Fireflies, false);
                    break;

                case "shrink_scene":
                    Log("shrink_scene — trigger scale animation on SceneRoot.");
                    break;

                case "glow_objects":
                    Log("glow_objects — trigger emission increase on all scene materials.");
                    break;

                // --- Character Animations ---
                case "dobby_dance":
                    TriggerAnimation(dobbyAnimator, "Dance");
                    break;
                case "dobby_wave":
                    TriggerAnimation(dobbyAnimator, "Wave");
                    break;
                case "dobby_scared":
                    TriggerAnimation(dobbyAnimator, "Scared");
                    break;
                case "dobby_celebrate":
                    TriggerAnimation(dobbyAnimator, "Celebrate");
                    break;

                case "astronaut_jump":
                    TriggerAnimation(astronautAnimator, "Jump");
                    break;
                case "astronaut_wave":
                    TriggerAnimation(astronautAnimator, "Wave");
                    break;
                case "astronaut_look_around":
                    TriggerAnimation(astronautAnimator, "LookAround");
                    break;

                default:
                    Debug.LogWarning($"[CommandExecutor] Unknown command: '{command}'");
                    break;
            }

            OnCommandExecuted?.Invoke(command);
        }

        /// <summary>Replay/rewind helper: turn every persistent effect OFF and reset state flags
        /// to their authored defaults. Reuses ExecuteCommand so behavior matches exactly.
        /// Additive — never called by the live film.</summary>
        public void ResetWorldToDefaults()
        {
            ExecuteCommand("stop_rain");
            ExecuteCommand("day");
            ExecuteCommand("stop_fire");
            ExecuteCommand("stop_wind");
            ExecuteCommand("stop_fog");
            ExecuteCommand("stop_butterflies");
            ExecuteCommand("stop_fireflies");
            ExecuteCommand("open_path");
            SetGameObject(forestGroup, false);
            SetGameObject(flowersGroup, false);
            hasForest = false;
        }

        // ------------------------------------------------------------------
        // Effect Helpers
        // ------------------------------------------------------------------

        private void SetGameObject(GameObject go, bool active)
        {
            if (go == null)
            {
                Log("GameObject reference not assigned.", isWarning: true);
                return;
            }
            go.SetActive(active);
        }

        private void TriggerAnimation(Animator animator, string triggerName)
        {
            if (animator == null)
            {
                Log($"Animator not assigned for trigger '{triggerName}'.", isWarning: true);
                return;
            }
            animator.SetTrigger(triggerName);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void Log(string message, bool isWarning = false)
        {
            if (!logCommands) return;
            if (isWarning)
                Debug.LogWarning($"[CommandExecutor] {message}");
            else
                Debug.Log($"[CommandExecutor] {message}");
        }
    }
}
