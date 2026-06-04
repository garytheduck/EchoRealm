using System.Collections;
using UnityEngine;

namespace EchoRealm.Characters
{
    /// <summary>
    /// Controls the Oracle: a mysterious sphere of light and particles that narrates
    /// the EchoRealm experience. The Oracle has no physical model — it's built from
    /// a sphere mesh + emissive material + particle system.
    ///
    /// Visual behavior:
    /// - Pulses gently when idle
    /// - Pulses faster/brighter when speaking
    /// - Changes color based on mood (blue=calm, gold=excited, red=warning, purple=mysterious)
    ///
    /// Setup:
    /// - Create a Sphere → scale to ~0.15
    /// - Add emissive material (emission color driven by this script)
    /// - Add Particle System as child (soft glowing particles orbiting the sphere)
    /// - Add TextMeshPro as child (dialogue text, billboard)
    /// - Attach this script
    /// </summary>
    public class OracleController : MonoBehaviour
    {
        [Header("Visual References")]
        [SerializeField] private Renderer sphereRenderer;
        [SerializeField] private ParticleSystem orbitalParticles;
        [SerializeField] private Light oracleLight;

        [Header("Humanoid Oracle (optional — for a character model like the priest)")]
        [Tooltip("If set, the Oracle is a humanoid (priest). Talk/Idle triggers drive it while speaking.")]
        [SerializeField] private Animator bodyAnimator;
        [SerializeField] private string talkTrigger = "Talk";
        [SerializeField] private string idleTrigger = "Idle";

        [Header("Dialogue")]
        [SerializeField] private TMPro.TextMeshPro dialogueText;
        [SerializeField] private float dialogueDisplayTime = 6f;
        [SerializeField] private bool billboardDialogue = true;

        [Tooltip("Optional — speaks lines aloud via Windows TTS on device (auto-found if on the same object).")]
        [SerializeField] private OracleVoice voice;

        [Header("Pulse Settings")]
        [SerializeField] private float idlePulseSpeed = 1f;
        [SerializeField] private float speakingPulseSpeed = 3f;
        [SerializeField] private float minEmission = 0.5f;
        [SerializeField] private float maxEmission = 2f;
        [SerializeField] private float speakingMaxEmission = 4f;

        [Header("Mood Colors")]
        [SerializeField] private Color calmColor = new Color(0.3f, 0.5f, 1f);       // Blue
        [SerializeField] private Color excitedColor = new Color(1f, 0.8f, 0.2f);     // Gold
        [SerializeField] private Color mysteriousColor = new Color(0.6f, 0.2f, 1f);  // Purple
        [SerializeField] private Color warningColor = new Color(1f, 0.3f, 0.1f);     // Red-orange

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>True if the Oracle is currently displaying dialogue.</summary>
        public bool IsSpeaking { get; private set; }

        /// <summary>Current Oracle mood.</summary>
        public string CurrentMood { get; private set; } = "mysterious";

        public static OracleController Instance { get; private set; }

        private Material sphereMaterial;
        private Color targetColor;
        private float dialogueTimer;
        private float currentPulseSpeed;

        private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            // Get material instance (so we don't modify the shared material)
            if (sphereRenderer != null)
            {
                sphereMaterial = sphereRenderer.material;
            }

            if (voice == null) voice = GetComponent<OracleVoice>();

            targetColor = mysteriousColor;
            currentPulseSpeed = idlePulseSpeed;
            HideDialogue();
        }

        private void Update()
        {
            UpdatePulse();
            UpdateDialogue();

            // Billboard dialogue
            if (billboardDialogue && dialogueText != null && Camera.main != null)
            {
                dialogueText.transform.rotation = Quaternion.LookRotation(
                    dialogueText.transform.position - Camera.main.transform.position);
            }
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Show Oracle dialogue (floating text + pulse effect).</summary>
        public void Speak(string text)
        {
            ShowDialogue(text);
            if (voice != null) voice.Speak(text);
            currentPulseSpeed = speakingPulseSpeed;
            Log($"Oracle speaks: \"{text}\"");
        }

        /// <summary>
        /// Show Oracle dialogue and play the full speaking sequence.
        /// Text appears word-by-word for dramatic effect.
        /// </summary>
        public void SpeakDramatic(string text, float wordsPerSecond = 3f)
        {
            if (voice != null) voice.Speak(text);
            StartCoroutine(DramaticSpeechCoroutine(text, wordsPerSecond));
        }

        /// <summary>Set Oracle mood (changes color and particle behavior).</summary>
        public void SetMood(string mood)
        {
            CurrentMood = mood;

            switch (mood)
            {
                case "calm":
                case "joyful":
                    targetColor = calmColor;
                    break;
                case "excited":
                    targetColor = excitedColor;
                    break;
                case "mysterious":
                case "curious":
                    targetColor = mysteriousColor;
                    break;
                case "warning":
                case "scared":
                    targetColor = warningColor;
                    break;
                default:
                    targetColor = mysteriousColor;
                    break;
            }

            // Adjust particle speed based on mood
            if (orbitalParticles != null)
            {
                var main = orbitalParticles.main;
                main.simulationSpeed = mood == "excited" ? 2f : mood == "calm" ? 0.5f : 1f;
            }

            Log($"Mood: {mood} → color: {targetColor}");
        }

        /// <summary>Make the Oracle appear (fade in).</summary>
        public void Appear()
        {
            gameObject.SetActive(true);
            if (orbitalParticles != null)
                orbitalParticles.Play();
            if (oracleLight != null)
                oracleLight.enabled = true;
            Log("Oracle appeared.");
        }

        /// <summary>Make the Oracle disappear (fade out).</summary>
        public void Disappear()
        {
            HideDialogue();
            if (orbitalParticles != null)
                orbitalParticles.Stop();
            if (oracleLight != null)
                oracleLight.enabled = false;
            // Could add fade-out coroutine here
            gameObject.SetActive(false);
            Log("Oracle disappeared.");
        }

        // ------------------------------------------------------------------
        // Visual Updates
        // ------------------------------------------------------------------

        private void UpdatePulse()
        {
            // Sinusoidal pulse (drives both the optional sphere emission and the aura light)
            float pulse = Mathf.Lerp(minEmission,
                IsSpeaking ? speakingMaxEmission : maxEmission,
                (Mathf.Sin(Time.time * currentPulseSpeed) + 1f) * 0.5f);

            // Optional glowing sphere (only when a sphereRenderer is assigned)
            if (sphereMaterial != null)
            {
                Color currentEmission = sphereMaterial.GetColor(EmissionColorID);
                Color newEmission = Color.Lerp(currentEmission, targetColor * pulse, Time.deltaTime * 3f);
                sphereMaterial.SetColor(EmissionColorID, newEmission);
            }

            // Aura light: mood color + pulse — works for a humanoid Oracle (priest) with no sphere
            if (oracleLight != null)
            {
                oracleLight.color = targetColor;
                oracleLight.intensity = pulse * 0.5f;
            }
        }

        private void UpdateDialogue()
        {
            if (!IsSpeaking) return;

            dialogueTimer -= Time.deltaTime;
            if (dialogueTimer <= 0)
            {
                HideDialogue();
                currentPulseSpeed = idlePulseSpeed;
            }
        }

        private void ShowDialogue(string text)
        {
            if (dialogueText == null) return;
            dialogueText.text = text;
            dialogueText.gameObject.SetActive(true);
            dialogueTimer = dialogueDisplayTime;
            IsSpeaking = true;
            if (bodyAnimator != null) bodyAnimator.SetTrigger(talkTrigger);
        }

        private void HideDialogue()
        {
            if (dialogueText != null)
                dialogueText.gameObject.SetActive(false);
            IsSpeaking = false;
            if (bodyAnimator != null) bodyAnimator.SetTrigger(idleTrigger);
        }

        private IEnumerator DramaticSpeechCoroutine(string fullText, float wordsPerSecond)
        {
            string[] words = fullText.Split(' ');
            string displayed = "";

            currentPulseSpeed = speakingPulseSpeed;

            if (dialogueText != null)
                dialogueText.gameObject.SetActive(true);

            IsSpeaking = true;
            if (bodyAnimator != null) bodyAnimator.SetTrigger(talkTrigger);

            foreach (string word in words)
            {
                displayed += (displayed.Length > 0 ? " " : "") + word;
                if (dialogueText != null)
                    dialogueText.text = displayed;

                yield return new WaitForSeconds(1f / wordsPerSecond);
            }

            // Keep final text visible for a bit
            dialogueTimer = dialogueDisplayTime;

            // Then UpdateDialogue() will hide it after timer expires
        }

        private void OnDestroy()
        {
            // Clean up material instance
            if (sphereMaterial != null)
                Destroy(sphereMaterial);
        }

        private void Log(string message)
        {
            if (logEvents)
                Debug.Log($"[Oracle] {message}");
        }
    }
}
