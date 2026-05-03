using System;
using UnityEngine;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// Bridges MRTK3 gesture events to EchoRealm systems.
    /// Listens for ObjectManipulator events on the SceneRoot and individual objects,
    /// and reports interactions to CooperationDetector and NarrativeManager.
    ///
    /// MRTK3 handles the actual hand tracking and manipulation —
    /// this script only forwards events to EchoRealm's game logic.
    ///
    /// Setup: Attach to SceneRoot. Add ObjectManipulator component to SceneRoot
    /// (enables pinch-drag, scale, rotate on the entire scene).
    /// </summary>
    public class GestureManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The SceneRoot transform (can be manipulated by users).")]
        [SerializeField] private Transform sceneRoot;

        [Header("Scale Limits")]
        [Tooltip("Minimum scale for SceneRoot (miniature view).")]
        [SerializeField] private float minScale = 0.1f;
        [Tooltip("Maximum scale for SceneRoot (room-scale view).")]
        [SerializeField] private float maxScale = 3f;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>True if any user is currently manipulating an object.</summary>
        public bool IsManipulating { get; private set; }

        /// <summary>Fired when a manipulation (grab) starts on any tracked object.</summary>
        public event Action<GameObject> OnManipulationStarted;

        /// <summary>Fired when a manipulation ends.</summary>
        public event Action<GameObject> OnManipulationEnded;

        public static GestureManager Instance { get; private set; }

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
            if (sceneRoot == null)
                sceneRoot = transform;
        }

        private void LateUpdate()
        {
            // Clamp SceneRoot scale to prevent it from becoming too small or too large
            if (sceneRoot != null)
            {
                Vector3 scale = sceneRoot.localScale;
                float uniformScale = Mathf.Clamp(scale.x, minScale, maxScale);
                sceneRoot.localScale = Vector3.one * uniformScale;
            }
        }

        // ------------------------------------------------------------------
        // Public API (called by MRTK3 ObjectManipulator events via UnityEvents)
        // ------------------------------------------------------------------

        /// <summary>
        /// Call from ObjectManipulator's OnManipulationStarted UnityEvent.
        /// Wire this in Inspector: ObjectManipulator → ManipulationStarted → GestureManager.OnObjectGrabbed
        /// </summary>
        public void OnObjectGrabbed()
        {
            IsManipulating = true;
            Log($"Manipulation STARTED on: {gameObject.name}");
            OnManipulationStarted?.Invoke(gameObject);

            // Report to cooperation detector
            ReportGesture(gameObject, InteractionType.Grab);

            // Feed into behavior profile for AI narrative decisions
            AI.ActionCollector.Instance?.RecordGesture(InteractionType.Grab, gameObject.name);
        }

        /// <summary>
        /// Call from ObjectManipulator's OnManipulationEnded UnityEvent.
        /// </summary>
        public void OnObjectReleased()
        {
            IsManipulating = false;
            Log($"Manipulation ENDED on: {gameObject.name}");
            OnManipulationEnded?.Invoke(gameObject);
        }

        /// <summary>
        /// Call from any interactable's OnClicked/OnSelected UnityEvent (air tap).
        /// </summary>
        public void OnObjectTapped()
        {
            Log($"Air Tap on: {gameObject.name}");
            ReportGesture(gameObject, InteractionType.AirTap);

            // Feed into behavior profile for AI narrative decisions
            AI.ActionCollector.Instance?.RecordGesture(InteractionType.AirTap, gameObject.name);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void ReportGesture(GameObject target, InteractionType type)
        {
            var cooperation = CooperationDetector.Instance;
            if (cooperation == null) return;

            // Determine player index from network (0 = local master, 1 = other)
            // For now, default to player 0 (local)
            int playerIndex = 0;
            var networkManager = Networking.FusionNetworkManager.Instance;
            if (networkManager != null && !networkManager.IsMaster)
                playerIndex = 1;

            cooperation.ReportInteraction(playerIndex, target, type);
        }

        private void Log(string message)
        {
            if (logEvents)
                Debug.Log($"[Gesture] {message}");
        }
    }
}
