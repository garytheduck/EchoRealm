using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoRealm.AI
{
    /// <summary>
    /// Executes AI commands by activating pre-built Unity effects, animations,
    /// and character behaviors. Maps string command names from Ollama responses
    /// to concrete Unity actions.
    ///
    /// Each command corresponds to a pre-built effect (particle system, animation,
    /// shader change, etc.) that is ready in the scene as a disabled GameObject or component.
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

        [Header("Effect References (assign in Inspector)")]
        [Tooltip("Particle system for rain effect.")]
        [SerializeField] private ParticleSystem rainEffect;
        [Tooltip("Particle system for fire effect.")]
        [SerializeField] private ParticleSystem fireEffect;
        [Tooltip("Particle system for fog/mist.")]
        [SerializeField] private ParticleSystem fogEffect;
        [Tooltip("Particle system for wind/leaves.")]
        [SerializeField] private ParticleSystem windEffect;
        [Tooltip("Particle system for butterflies.")]
        [SerializeField] private ParticleSystem butterfliesEffect;
        [Tooltip("Particle system for fireflies.")]
        [SerializeField] private ParticleSystem firefliesEffect;
        [Tooltip("Particle system for dust (earthquake).")]
        [SerializeField] private ParticleSystem dustEffect;

        [Header("Environment References")]
        [SerializeField] private GameObject forestGroup;
        [SerializeField] private GameObject flowersGroup;
        [SerializeField] private GameObject pathBlockade;
        [SerializeField] private Light mainLight;
        [SerializeField] private Material daySkybox;
        [SerializeField] private Material nightSkybox;

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
                // --- Weather ---
                case "rain":
                    SetParticle(rainEffect, true);
                    isRaining = true;
                    break;
                case "stop_rain":
                    SetParticle(rainEffect, false);
                    isRaining = false;
                    break;

                case "night":
                    SetTimeOfDay(night: true);
                    break;
                case "day":
                    SetTimeOfDay(night: false);
                    break;

                case "fire":
                    SetParticle(fireEffect, true);
                    hasFire = true;
                    break;
                case "stop_fire":
                    SetParticle(fireEffect, false);
                    hasFire = false;
                    break;

                case "wind":
                    SetParticle(windEffect, true);
                    break;
                case "stop_wind":
                    SetParticle(windEffect, false);
                    break;

                case "fog":
                    SetParticle(fogEffect, true);
                    hasFog = true;
                    break;
                case "stop_fog":
                    SetParticle(fogEffect, false);
                    hasFog = false;
                    break;

                case "earthquake":
                    StartCoroutine(EarthquakeSequence());
                    break;

                case "lightning":
                    StartCoroutine(LightningFlash());
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
                    SetParticle(butterfliesEffect, true);
                    break;
                case "stop_butterflies":
                    SetParticle(butterfliesEffect, false);
                    break;

                case "spawn_fireflies":
                    SetParticle(firefliesEffect, true);
                    break;
                case "stop_fireflies":
                    SetParticle(firefliesEffect, false);
                    break;

                case "shrink_scene":
                    // Handled externally by SceneRoot scale animation
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

        // ------------------------------------------------------------------
        // Effect Helpers
        // ------------------------------------------------------------------

        private void SetParticle(ParticleSystem ps, bool active)
        {
            if (ps == null)
            {
                Log("ParticleSystem reference not assigned.", isWarning: true);
                return;
            }

            if (active)
            {
                ps.gameObject.SetActive(true);
                ps.Play();
            }
            else
            {
                ps.Stop();
                ps.gameObject.SetActive(false);
            }
        }

        private void SetGameObject(GameObject go, bool active)
        {
            if (go == null)
            {
                Log("GameObject reference not assigned.", isWarning: true);
                return;
            }
            go.SetActive(active);
        }

        private void SetTimeOfDay(bool night)
        {
            isNight = night;

            if (mainLight != null)
            {
                mainLight.intensity = night ? 0.1f : 1.0f;
                mainLight.color = night ? new Color(0.3f, 0.3f, 0.5f) : Color.white;
            }

            if (night && nightSkybox != null)
                RenderSettings.skybox = nightSkybox;
            else if (!night && daySkybox != null)
                RenderSettings.skybox = daySkybox;

            Log($"Time of day: {(night ? "NIGHT" : "DAY")}");
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

        private System.Collections.IEnumerator EarthquakeSequence()
        {
            SetParticle(dustEffect, true);

            // Simple camera shake for 2 seconds
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 originalPos = cam.transform.localPosition;
                float elapsed = 0f;
                float duration = 2f;
                float magnitude = 0.05f;

                while (elapsed < duration)
                {
                    float x = UnityEngine.Random.Range(-1f, 1f) * magnitude;
                    float y = UnityEngine.Random.Range(-1f, 1f) * magnitude;
                    cam.transform.localPosition = originalPos + new Vector3(x, y, 0);
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                cam.transform.localPosition = originalPos;
            }

            yield return new WaitForSeconds(1f);
            SetParticle(dustEffect, false);
        }

        private System.Collections.IEnumerator LightningFlash()
        {
            if (mainLight == null) yield break;

            float originalIntensity = mainLight.intensity;
            Color originalColor = mainLight.color;

            // Flash sequence
            mainLight.color = Color.white;
            mainLight.intensity = 3f;
            yield return new WaitForSeconds(0.1f);
            mainLight.intensity = 0.5f;
            yield return new WaitForSeconds(0.05f);
            mainLight.intensity = 2.5f;
            yield return new WaitForSeconds(0.15f);

            // Return to original
            mainLight.intensity = originalIntensity;
            mainLight.color = originalColor;
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
