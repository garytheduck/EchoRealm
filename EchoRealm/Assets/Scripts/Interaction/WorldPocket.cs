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

        // World pose saved at pocket time, restored on unpocket (returns it where/what it was).
        private Vector3 _savedScale = Vector3.one;
        private Vector3 _savedPos;
        private Quaternion _savedRot = Quaternion.identity;
        private bool _hasSaved;

        // True on the device that TRIGGERED this pocket — it gets the floating Resume button.
        private bool _iInitiated;

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

            // Ensure a Resume button exists (on this persistent object, NOT under SceneRoot).
            if (ResumeButton.Instance == null) gameObject.AddComponent<ResumeButton>();
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

        // Public entry points (gesture/voice). They REQUEST a networked pocket so every headset
        // pauses together; the local effect runs in ApplyPocket/ApplyUnpocket — called directly
        // when solo, or by FilmSync's RPC on every device when networked.
        public void Pocket()
        {
            if (IsPocketed) return;
            _iInitiated = true; // this device put the world away → it gets the floating Resume button
            var sync = Networking.FilmSync.Instance;
            if (sync != null) sync.RequestPocket(true);
            else ApplyPocket();
        }

        public void Unpocket()
        {
            if (!IsPocketed) return;
            var sync = Networking.FilmSync.Instance;
            if (sync != null) sync.RequestPocket(false);
            else ApplyUnpocket();
        }

        /// <summary>Local effect (runs on EVERY headset): save pose, hide the world, pause time.</summary>
        public void ApplyPocket()
        {
            if (IsPocketed || sceneRoot == null) return;
            _savedScale = sceneRoot.localScale;
            _savedPos   = sceneRoot.position;
            _savedRot   = sceneRoot.rotation;
            _hasSaved   = true;

            IsPocketed = true;
            sceneRoot.gameObject.SetActive(false); // hides all world visuals/characters/effects (they stop updating)
            // NOTE: deliberately NOT using Time.timeScale = 0 — that also freezes Photon Fusion,
            // which would block the networked "unpocket" from ever arriving. The film is paused via
            // hiding SceneRoot + FilmDirector's pocket gate instead, so networking stays alive.

            // Show the floating Resume button ONLY on the device that pocketed it.
            if (_iInitiated) ResumeButton.Instance?.Show();

            if (logEvents) Debug.Log("[WorldPocket] Pocketed — world hidden & film paused for all. Say 'resume' or tap Resume.");
        }

        /// <summary>Local effect (runs on EVERY headset): restore the world where it was, resume time.</summary>
        public void ApplyUnpocket()
        {
            if (!IsPocketed || sceneRoot == null) return;
            sceneRoot.gameObject.SetActive(true);
            if (_hasSaved)
            {
                sceneRoot.localScale = _savedScale;
                sceneRoot.position   = _savedPos;
                sceneRoot.rotation   = _savedRot;
            }
            IsPocketed = false;
            _iInitiated = false;
            ResumeButton.Instance?.Hide();
            if (logEvents) Debug.Log("[WorldPocket] Unpocketed — world restored & film resumed.");
        }
    }
}
