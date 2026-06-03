using UnityEngine;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// "Pocket the world." When the user shrinks SceneRoot to ~5% and pulls it close and low
    /// (toward the bottom of the HoloLens view), the world is hidden and the film PAUSES
    /// (Time.timeScale = 0). Saying "unpocket scene" (intercepted by VoiceCommandProcessor,
    /// which still fires while paused) pops the world back out at 5% in front of the user and resumes.
    ///
    /// Attach to a PERSISTENT object (e.g. GameManager) — NOT under SceneRoot, since SceneRoot is
    /// disabled while pocketed. Assign sceneRoot (or it auto-finds a GameObject named "SceneRoot").
    /// </summary>
    public class WorldPocket : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform sceneRoot;

        [Header("Pocket trigger (gesture)")]
        [Tooltip("World counts as 'shrunk' at or below this uniform scale (0.06 ≈ 6%).")]
        [SerializeField] private float pocketScale = 0.06f;
        [Tooltip("Max distance (m) from the camera to count as 'pulled in'.")]
        [SerializeField] private float pocketDistance = 0.45f;
        [Tooltip("Viewport Y at/below which the world is 'at the bottom of the screen' (0 = bottom, 1 = top).")]
        [SerializeField] private float bottomViewport = 0.35f;

        [Header("Pop-out (unpocket)")]
        [Tooltip("Scale the world returns at when unpocketed.")]
        [SerializeField] private float popOutScale = 0.05f;
        [Tooltip("Distance (m) in front of the camera the world reappears.")]
        [SerializeField] private float popOutDistance = 0.6f;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>True while the world is pocketed and the film is paused.</summary>
        public bool IsPocketed { get; private set; }

        public static WorldPocket Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            if (sceneRoot == null)
            {
                var go = GameObject.Find("SceneRoot");
                if (go != null) sceneRoot = go.transform;
            }
        }

        private void Update()
        {
            if (IsPocketed || sceneRoot == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            if (sceneRoot.localScale.x > pocketScale) return; // not shrunk enough

            float dist = Vector3.Distance(sceneRoot.position, cam.transform.position);
            Vector3 vp = cam.WorldToViewportPoint(sceneRoot.position);
            bool pulledIn = dist <= pocketDistance;
            bool atBottom = vp.z > 0f && vp.y <= bottomViewport;

            if (pulledIn && atBottom) Pocket();
        }

        /// <summary>Hide the world and pause the film. Resume with Unpocket().</summary>
        public void Pocket()
        {
            if (IsPocketed || sceneRoot == null) return;
            IsPocketed = true;
            sceneRoot.gameObject.SetActive(false);
            Time.timeScale = 0f;
            if (logEvents) Debug.Log("[WorldPocket] Pocketed — scene PAUSED. Say 'unpocket scene' to resume.");
        }

        /// <summary>Pop the world back out at pop-out scale in front of the user and resume the film.</summary>
        public void Unpocket()
        {
            if (!IsPocketed || sceneRoot == null) return;
            Time.timeScale = 1f;
            sceneRoot.gameObject.SetActive(true);

            var cam = Camera.main;
            if (cam != null)
            {
                sceneRoot.localScale = Vector3.one * popOutScale;
                sceneRoot.position = cam.transform.position + cam.transform.forward * popOutDistance;
            }
            IsPocketed = false;
            if (logEvents) Debug.Log("[WorldPocket] Unpocketed — scene RESUMED at pop-out scale.");
        }
    }
}
