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

        /// <summary>True while walking toward a WalkTo target (ambient story moves).</summary>
        public bool IsWalking => _walkTarget.HasValue;

        public static AstronautController Instance { get; private set; }

        // Ambient walk target (world space). Null = not walking. The portal sequence has priority.
        private Vector3? _walkTarget;
        private float _walkStopDist = 0.25f;

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
            else if (_walkTarget.HasValue)
            {
                // Ambient walk toward an arbitrary point (same gait as the portal walk).
                Vector3 target = _walkTarget.Value;
                Vector3 direction = (target - transform.position).normalized;
                transform.position += direction * walkSpeed * Time.deltaTime;

                direction.y = 0;
                if (direction.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation, Quaternion.LookRotation(direction), 5f * Time.deltaTime);

                if (Vector3.Distance(transform.position, target) < _walkStopDist)
                {
                    _walkTarget = null;
                    PlayAnimation("LookAround");   // arrive → break out of the Walk state
                    Log("Astronaut arrived at walk target.");
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
        public void StartPortalSequence(Transform fallbackTarget = null)
        {
            // Use the wired portalTarget; if none, fall back to whatever the caller passes (e.g. the
            // portal-effect transform) so the default-ending homecoming walk always plays.
            if (portalTarget == null) portalTarget = fallbackTarget;
            if (portalTarget == null)
            {
                Log("Portal target not assigned and no fallback — cannot start portal sequence.", isWarning: true);
                return;
            }

            _walkTarget = null;            // the portal sequence owns movement from here on
            IsEnteringPortal = true;
            PlayAnimation("Walk");
            Log($"Starting portal sequence — walking toward {portalTarget.name}.");
        }

        /// <summary>Ambient story move: walk to an arbitrary world point with the Walk animation;
        /// plays LookAround on arrival. Ignored while the Act-4 portal sequence is running.</summary>
        public void WalkTo(Vector3 worldPos, float stopDistance = 0.25f)
        {
            if (IsEnteringPortal) return;
            _walkTarget = worldPos;
            _walkStopDist = Mathf.Max(0.05f, stopDistance);
            PlayAnimation("Walk");
            Log($"Walking to {worldPos}.");
        }

        /// <summary>Stop ALL astronaut locomotion in place (ambient walk AND the Act-4 portal walk),
        /// used when an act ends or a rewind reconstructs the scene — otherwise he keeps marching
        /// toward a stale target (e.g. a deactivated portal after a rewind out of Act 4).</summary>
        public void StopWalking()
        {
            _walkTarget = null;
            IsEnteringPortal = false;
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
