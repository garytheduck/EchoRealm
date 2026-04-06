using UnityEngine;

namespace EchoRealm.Characters
{
    /// <summary>
    /// Controls the Astronaut character: animations and movement.
    /// The Astronaut is the co-protagonist — follows the story, reacts to events.
    /// Less expressive than Dobby, more stoic.
    ///
    /// Setup:
    /// - Attach to Astronaut's root GameObject (with Animator component)
    /// - Animator must have triggers: Jump, Wave, LookAround, Walk, EnterPortal
    /// </summary>
    public class AstronautController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Portal Sequence")]
        [Tooltip("The portal GameObject the astronaut walks toward in Act 4.")]
        [SerializeField] private Transform portalTarget;

        [Tooltip("Speed at which astronaut walks toward portal.")]
        [SerializeField] private float walkSpeed = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>True if the astronaut is currently walking toward the portal.</summary>
        public bool IsEnteringPortal { get; private set; }

        public static AstronautController Instance { get; private set; }

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
            if (animator == null)
                animator = GetComponent<Animator>();
        }

        private void Update()
        {
            if (IsEnteringPortal && portalTarget != null)
            {
                // Walk toward portal
                Vector3 direction = (portalTarget.position - transform.position).normalized;
                transform.position += direction * walkSpeed * Time.deltaTime;

                // Face the portal
                direction.y = 0;
                if (direction.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation, Quaternion.LookRotation(direction), 5f * Time.deltaTime);

                // Check if reached portal
                float distance = Vector3.Distance(transform.position, portalTarget.position);
                if (distance < 0.3f)
                {
                    IsEnteringPortal = false;
                    PlayAnimation("EnterPortal");
                    Log("Astronaut reached the portal.");
                }
            }
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Play a specific animation trigger.</summary>
        public void PlayAnimation(string triggerName)
        {
            if (animator != null)
                animator.SetTrigger(triggerName);
            Log($"Animation: {triggerName}");
        }

        /// <summary>
        /// Start the Act 4 portal sequence: astronaut walks toward the portal
        /// and plays the enter animation when reaching it.
        /// </summary>
        public void StartPortalSequence()
        {
            if (portalTarget == null)
            {
                Log("Portal target not assigned! Cannot start portal sequence.", isWarning: true);
                return;
            }

            IsEnteringPortal = true;
            PlayAnimation("Walk");
            Log("Starting portal sequence — walking toward portal.");
        }

        /// <summary>Make the astronaut look around (idle curiosity animation).</summary>
        public void LookAround()
        {
            PlayAnimation("LookAround");
        }

        /// <summary>React to an event (e.g., earthquake — stumble animation).</summary>
        public void ReactToEvent(string eventType)
        {
            switch (eventType)
            {
                case "earthquake":
                    PlayAnimation("Jump"); // Startled jump
                    break;
                case "fire":
                    PlayAnimation("LookAround"); // Looks around alarmed
                    break;
                default:
                    PlayAnimation("Wave"); // Default reaction
                    break;
            }
        }

        private void Log(string message, bool isWarning = false)
        {
            if (!logEvents) return;
            if (isWarning)
                Debug.LogWarning($"[Astronaut] {message}");
            else
                Debug.Log($"[Astronaut] {message}");
        }
    }
}
