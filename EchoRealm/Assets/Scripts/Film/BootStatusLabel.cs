using System.Collections;
using TMPro;
using UnityEngine;

namespace EchoRealm.Film
{
    /// <summary>
    /// A small, always-facing-you world-space status label shown during startup
    /// (e.g. "Scanning for QR code..."). Created entirely at runtime — just add this
    /// component to a GameObject (e.g. GameManager); no manual text placement needed.
    ///
    /// EchoRealmBootstrapper routes its boot messages here. On a successful QR scan,
    /// it calls FlashThenDismiss() to show a confirmation and hide the label after a delay.
    /// </summary>
    public class BootStatusLabel : MonoBehaviour
    {
        [Header("Placement (relative to your head)")]
        [Tooltip("Distance in front of the camera, in meters.")]
        [SerializeField] private float distance = 2f;
        [Tooltip("Vertical offset from straight-ahead, in meters (negative = a bit below center).")]
        [SerializeField] private float verticalOffset = -0.25f;

        [Header("Appearance (tweak if too big/small)")]
        [Tooltip("Uniform world scale of the label object.")]
        [SerializeField] private float worldScale = 0.005f;
        [Tooltip("TextMeshPro font size (combined with World Scale).")]
        [SerializeField] private float fontSize = 36f;
        [SerializeField] private Color textColor = Color.white;

        public static BootStatusLabel Instance { get; private set; }

        private TextMeshPro _tmp;
        private Transform _cam;
        private bool _dismissed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            CreateLabel();
        }

        private void CreateLabel()
        {
            var go = new GameObject("BootStatusLabel_Text");
            _tmp = go.AddComponent<TextMeshPro>();

            if (TMP_Settings.defaultFontAsset != null)
                _tmp.font = TMP_Settings.defaultFontAsset;

            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.fontSize = fontSize;
            _tmp.enableAutoSizing = false;
            _tmp.color = textColor;
            _tmp.text = string.Empty;
            _tmp.enableWordWrapping = true;

            var rt = _tmp.rectTransform;
            rt.sizeDelta = new Vector2(400f, 150f); // generous text area (local units)
            go.transform.localScale = Vector3.one * worldScale;
        }

        private void Start()
        {
            CacheCamera();
        }

        private void CacheCamera()
        {
            if (Camera.main != null) _cam = Camera.main.transform;
            else { var c = FindObjectOfType<Camera>(); if (c != null) _cam = c.transform; }
        }

        private void LateUpdate()
        {
            if (_tmp == null) return;
            if (_cam == null) { CacheCamera(); if (_cam == null) return; }

            // Float in front of the user and face them (forward points away from camera = readable).
            Vector3 pos = _cam.position + _cam.forward * distance + _cam.up * verticalOffset;
            _tmp.transform.position = pos;
            _tmp.transform.rotation = Quaternion.LookRotation(_tmp.transform.position - _cam.position);
        }

        /// <summary>Show a status message (ignored once the label has been dismissed).</summary>
        public void Show(string message)
        {
            if (_dismissed || _tmp == null) return;
            _tmp.gameObject.SetActive(true);
            _tmp.text = message;
        }

        /// <summary>Hide the label immediately.</summary>
        public void Hide()
        {
            if (_tmp != null) _tmp.gameObject.SetActive(false);
        }

        /// <summary>
        /// Show a final confirmation message, then hide the label permanently after `seconds`.
        /// Subsequent Show() calls are ignored, so later boot messages won't bring it back.
        /// </summary>
        public void FlashThenDismiss(string message, float seconds)
        {
            if (_tmp == null) return;
            _tmp.gameObject.SetActive(true);
            _tmp.text = message;
            _dismissed = true;
            StartCoroutine(HideAfter(seconds));
        }

        private IEnumerator HideAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Hide();
        }

        private void OnDestroy()
        {
            if (_tmp != null) Destroy(_tmp.gameObject);
        }
    }
}
