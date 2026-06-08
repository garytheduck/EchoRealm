using UnityEngine;
using TMPro;
using MixedReality.Toolkit;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// A floating "Resume" button shown — on the device that POCKETED the world — while pocketed.
    /// Tapping it (air-tap / poke) calls WorldPocket.Unpocket(), which is networked, so it resumes
    /// every headset. This is the voice-independent way back (handy since the recognizer is finicky
    /// and a client's mic may be off).
    ///
    /// Auto-created by WorldPocket if absent. Lives on a PERSISTENT object (GameManager) — never under
    /// SceneRoot, which is hidden while pocketed. The button itself is a runtime-built panel, unless
    /// you assign your own MRTK button prefab.
    /// </summary>
    public class ResumeButton : MonoBehaviour
    {
        [Header("Optional")]
        [Tooltip("Assign your own MRTK button prefab for nicer visuals. If empty, a simple panel is built at runtime.")]
        [SerializeField] private GameObject buttonPrefab;

        [Header("Placement (floats in front of the head)")]
        [Tooltip("Distance in meters in front of the camera.")]
        [SerializeField] private float distance = 0.55f;
        [Tooltip("Vertical offset (negative = a bit below eye-line, easier to tap).")]
        [SerializeField] private float verticalOffset = -0.06f;
        [Tooltip("0 = locked in place, 1 = instantly follows the head. ~0.12 is a comfortable tag-along.")]
        [Range(0.02f, 1f)] [SerializeField] private float followLerp = 0.12f;
        [SerializeField] private string label = "▶  RESUME";

        public static ResumeButton Instance { get; private set; }

        private GameObject _go;
        private Material _bgMat;
        private Color _bgEmission;
        private bool _visible;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>Show the button and place it in front of the user.</summary>
        public void Show()
        {
            if (_go == null) Build();
            if (_go == null) return;
            _go.SetActive(true);
            _visible = true;
            PlaceInFront(snap: true);
        }

        /// <summary>Hide the button (world resumed).</summary>
        public void Hide()
        {
            _visible = false;
            if (_go != null) _go.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!_visible || _go == null) return;
            // Fixed in world space: placed once in Show(), it does NOT follow the head (a moving target
            // is hard to tap). Only the emission pulse animates.
            if (_bgMat != null)
            {
                float k = 1f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
                _bgMat.SetColor("_EmissionColor", _bgEmission * k);
            }
        }

        private void PlaceInFront(bool snap)
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 target = cam.transform.position
                             + cam.transform.forward * distance
                             + cam.transform.up * verticalOffset;

            var t = _go.transform;
            t.position = snap ? target : Vector3.Lerp(t.position, target, followLerp);
            // Face the camera so the label is readable (+Z toward the head).
            t.rotation = Quaternion.LookRotation(cam.transform.position - t.position, cam.transform.up);
        }

        // ------------------------------------------------------------------

        private void Build()
        {
            _go = buttonPrefab != null ? Instantiate(buttonPrefab) : BuildRuntimePanel();
            WireClick(_go);
            _go.SetActive(false);
        }

        private GameObject BuildRuntimePanel()
        {
            // Unscaled root holds the interactable + collider (so the child text isn't distorted).
            var root = new GameObject("ResumeButton(Runtime)");

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.22f, 0.10f, 0.02f);

            // Background: a thin cube (visible from any angle, unlike a single-sided quad).
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "BG";
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(0.22f, 0.10f, 0.012f);
            var bgCol = bg.GetComponent<Collider>();
            if (bgCol != null) Destroy(bgCol); // use the root's collider only

            _bgMat = bg.GetComponent<MeshRenderer>().material; // instanced Standard material
            Color teal = new Color(0.05f, 0.42f, 0.5f, 1f);
            _bgMat.color = teal;
            _bgMat.EnableKeyword("_EMISSION");
            _bgEmission = teal * 1.7f;
            _bgMat.SetColor("_EmissionColor", _bgEmission);

            // Label (unscaled child → crisp text).
            var textGo = new GameObject("Label");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0.012f); // just toward the camera
            // Flip 180° so the readable face points at the user (the panel faces +Z toward the head, but
            // TextMeshPro reads from its -Z side — without this the label shows mirrored).
            textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.2f, 0.09f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.02f;
            tmp.fontSizeMax = 0.06f;

            return root;
        }

        private void WireClick(GameObject go)
        {
            var si = go.GetComponentInChildren<StatefulInteractable>();
            if (si == null) si = go.AddComponent<StatefulInteractable>();
            si.OnClicked.AddListener(OnResumePressed);
        }

        private void OnResumePressed()
        {
            Debug.Log("[ResumeButton] Pressed — resuming the world.");
            WorldPocket.Instance?.Unpocket();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
