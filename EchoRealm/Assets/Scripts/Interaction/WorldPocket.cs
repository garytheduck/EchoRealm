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

        // Re-arm guard: the auto-pocket gesture only fires when the scene FRESHLY enters the pocket
        // zone. Unpocket restores the small/close/low pose (the very pose that triggered the pocket),
        // so without this the gesture re-fires immediately and the world flickers away after each
        // Resume. Set false on pocket; set true again only once the scene leaves the zone.
        private bool _armed = true;

        [Header("Resume method")]
        [Tooltip("OFF (default): resume the pocketed world with a 'push from the chest forward' hand " +
                 "gesture (PocketResumeGesture). ON: also show the tappable floating Resume button. " +
                 "Voice resume (any speech while pocketed) always works either way.")]
        [SerializeField] private bool useResumeButton = false;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        /// <summary>True while the world is pocketed and the film is paused.</summary>
        public bool IsPocketed { get; private set; }

        /// <summary>Other systems (e.g. PalmHold) set this while they INTENTIONALLY keep the scene
        /// small/close/low — exactly the auto-pocket gesture's zone — so the gesture doesn't fire.
        /// Voice "pocket" still works. Default false: behavior identical to before.</summary>
        public static bool SuppressAutoPocket;

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

            // Ensure a Resume button exists (on this persistent object, NOT under SceneRoot). It is
            // only SHOWN when useResumeButton is on; otherwise the push-forward gesture is the way back.
            if (useResumeButton && ResumeButton.Instance == null) gameObject.AddComponent<ResumeButton>();

            // Push-from-the-chest-forward gesture to resume (replaces the button by default).
            if (PocketResumeGesture.Instance == null) gameObject.AddComponent<PocketResumeGesture>();

            // Ensure the palm-hold driver exists too (same persistent-object convention; opt-in
            // voice feature — without a working hand pose it simply never activates).
            if (PalmHold.Instance == null) gameObject.AddComponent<PalmHold>();
        }

        private void Update()
        {
            if (IsPocketed || sceneRoot == null) return;
            if (SuppressAutoPocket) return;   // a programmatic driver (PalmHold) owns the small/close pose
            var cam = Camera.main;
            if (cam == null) return;

            bool shrunk = sceneRoot.localScale.x <= pocketScale;
            float dist = Vector3.Distance(sceneRoot.position, cam.transform.position);
            Vector3 vp = cam.WorldToViewportPoint(sceneRoot.position);
            bool pulledIn = dist <= pocketDistance;
            bool atBottom = vp.z > 0f && vp.y <= bottomViewport;
            bool inPocketZone = shrunk && pulledIn && atBottom;

            // Re-arm only after the scene LEAVES the zone, so unpocket (which restores the small/close/low
            // pose) doesn't instantly re-pocket — that caused the world to flicker away after each Resume.
            if (!inPocketZone) { _armed = true; return; }

            // The gesture is by definition a LOCAL hand action: only fire while THIS device is driving
            // the scene (grab). A pose streamed from another headset (their grab, their palm-hold)
            // entering our pocket zone must not pocket the shared world for everyone.
            bool locallyDriven = SceneManipulationReporter.Instance != null
                                 && SceneManipulationReporter.Instance.IsManipulating;
            if (_armed && locallyDriven) Pocket();
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

            // If the scene is being held in a palm on THIS device, return it to its pre-hold pose
            // FIRST — otherwise we'd save (and later restore) the transient tiny palm pose, stranding
            // the shared world at 4.5% scale in mid-air after an unpocket.
            if (PalmHold.Instance != null && PalmHold.Instance.IsHolding) PalmHold.Instance.Release();

            _savedScale = sceneRoot.localScale;
            _savedPos   = sceneRoot.position;
            _savedRot   = sceneRoot.rotation;
            _hasSaved   = true;

            IsPocketed = true;
            _armed = false; // don't let the restored (small/close/low) pose re-trigger the pocket on Resume
            sceneRoot.gameObject.SetActive(false); // hides all world visuals/characters/effects (they stop updating)
            // NOTE: deliberately NOT using Time.timeScale = 0 — that also freezes Photon Fusion,
            // which would block the networked "unpocket" from ever arriving. The film is paused via
            // hiding SceneRoot + FilmDirector's pocket gate instead, so networking stays alive.

            // Show the floating Resume button ONLY on the device that pocketed it, and ONLY if the
            // button mode is on; by default the push-forward gesture (or voice) brings the world back.
            if (_iInitiated && useResumeButton) ResumeButton.Instance?.Show();

            if (logEvents) Debug.Log("[WorldPocket] Pocketed — world hidden & film paused for all. " +
                                     "Push your palm forward from the chest to resume (or say 'resume').");
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
