using System.Collections;
using TMPro;
using UnityEngine;

namespace EchoRealm.AI
{
    /// <summary>
    /// A "what I heard" label. Appears low in front of you — placed ONCE when you speak, then left
    /// FIXED in world space (it does NOT follow your head) — and briefly shows what the speech
    /// recognizer understood: green <c>heard: "phrase"</c> on success, amber <c>didn't catch that</c>
    /// when speech was rejected / below the confidence threshold. Closes the feedback loop so the user
    /// knows whether to repeat (the HoloLens recognizer is finicky).
    ///
    /// Built entirely at runtime; just add this component to a persistent object (e.g. GameManager).
    /// Pure observer of <see cref="VoiceCommandProcessor"/> — remove it and nothing else changes.
    /// </summary>
    public class VoiceFeedbackLabel : MonoBehaviour
    {
        [Header("Placement (placed once in front of you, then FIXED — does not follow the head)")]
        [Tooltip("Distance in front of the camera at the moment the label appears, in metres.")]
        [SerializeField] private float distance = 1.4f;
        [Tooltip("Vertical offset (negative = lower, subtitle position).")]
        [SerializeField] private float verticalOffset = -0.45f;
        [SerializeField] private float worldScale = 0.005f;
        [SerializeField] private float fontSize = 36f;

        [Header("Timing")]
        [Tooltip("Seconds the message stays fully visible before it fades out.")]
        [SerializeField] private float holdSeconds = 2.5f;
        [SerializeField] private float fadeSeconds = 0.5f;

        [Header("Colours")]
        [SerializeField] private Color heardColor = new Color(0.70f, 1f, 0.70f, 1f);   // soft green
        [SerializeField] private Color unclearColor = new Color(1f, 0.85f, 0.40f, 1f);  // amber

        private TextMeshPro _tmp;
        private Transform _cam;
        private Coroutine _hide;
        private bool _subscribed;

        private void Awake() => CreateLabel();

        // Subscribe from both OnEnable (runtime add) and Start (VoiceCommandProcessor.Awake has run by Start).
        private void OnEnable() => TrySubscribe();
        private void Start() => TrySubscribe();

        private void TrySubscribe()
        {
            if (_subscribed) return;
            var vcp = VoiceCommandProcessor.Instance;
            if (vcp == null) return;                       // not ready yet — Start (or a later enable) retries
            vcp.OnSpeechRecognized += ShowHeard;
            vcp.OnSpeechUnclear += ShowUnclear;
            _subscribed = true;
        }

        private void OnDisable()
        {
            var vcp = VoiceCommandProcessor.Instance;
            if (_subscribed && vcp != null)
            {
                vcp.OnSpeechRecognized -= ShowHeard;
                vcp.OnSpeechUnclear -= ShowUnclear;
            }
            _subscribed = false;
        }

        private void OnDestroy() { if (_tmp != null) Destroy(_tmp.gameObject); }

        private void CreateLabel()
        {
            var go = new GameObject("VoiceFeedbackLabel_Text");
            _tmp = go.AddComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset != null) _tmp.font = TMP_Settings.defaultFontAsset;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.enableAutoSizing = false;
            _tmp.fontSize = fontSize;
            _tmp.enableWordWrapping = true;
            _tmp.text = string.Empty;
            _tmp.rectTransform.sizeDelta = new Vector2(440f, 140f);
            go.transform.localScale = Vector3.one * worldScale;
            go.SetActive(false);
        }

        private void ShowHeard(string text) => Show($"heard: \"{text}\"", heardColor);

        private void ShowUnclear(string text, float confidence)
        {
            string msg = string.IsNullOrWhiteSpace(text)
                ? "didn't catch that"
                : $"didn't catch that\n(\"{text}\"?)";
            Show(msg, unclearColor);
        }

        private void Show(string message, Color color)
        {
            if (_tmp == null) return;
            if (_hide != null) StopCoroutine(_hide);
            color.a = 1f;
            _tmp.color = color;
            _tmp.text = message;
            _tmp.gameObject.SetActive(true);
            PlaceNow();
            _hide = StartCoroutine(HoldThenFade(color));
        }

        private IEnumerator HoldThenFade(Color color)
        {
            yield return new WaitForSecondsRealtime(holdSeconds);
            for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                if (_tmp == null) yield break;
                color.a = Mathf.Lerp(1f, 0f, t / fadeSeconds);
                _tmp.color = color;
                yield return null;
            }
            if (_tmp != null) _tmp.gameObject.SetActive(false);
            _hide = null;
        }

        // Place the label ONCE (when a message appears), then leave it FIXED in world space — it does
        // NOT follow the head. You glance at it right after speaking; it stays put while it fades.
        private void PlaceNow()
        {
            if (_cam == null) { CacheCamera(); if (_cam == null) return; }
            _tmp.transform.position = _cam.position + _cam.forward * distance + _cam.up * verticalOffset;
            // Face the camera so the label reads correctly (matches BootStatusLabel's convention).
            _tmp.transform.rotation = Quaternion.LookRotation(_tmp.transform.position - _cam.position);
        }

        private void CacheCamera()
        {
            if (Camera.main != null) _cam = Camera.main.transform;
            else { var c = FindObjectOfType<Camera>(); if (c != null) _cam = c.transform; }
        }
    }
}
