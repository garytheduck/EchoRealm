using UnityEngine;
using TMPro;
using MixedReality.Toolkit;
using EchoRealm.Film;
using System.IO;

namespace EchoRealm.Interaction
{
    /// <summary>The offline playback UI: a save-picker, then a transport bar (Prev/Next,
    /// Rewind 20s/1m), an AI transcript panel, and a persistent read-only banner.
    ///
    /// UI corrections applied:
    ///   1. All panels face the user using (camera − panel) so text is readable (not backwards).
    ///   2. Only the read-only banner HUD follows the head each frame; the picker and transport
    ///      are placed once (snap) and stay fixed in world space.</summary>
    public class ReplayUI : MonoBehaviour
    {
        [SerializeField] private ReplaySessionController controller;

        private GameObject _picker, _transport, _banner;
        private TextMeshPro _transcript, _status;

        private void Awake()
        {
            if (controller == null) controller = FindObjectOfType<ReplaySessionController>(true);
        }

        // ---- Save picker ----
        public void ShowPicker()
        {
            _picker = new GameObject("ReplayPicker(Runtime)");
            // Place once, face the user — NOT repositioned each frame.
            PlaceSnap(_picker.transform, 0.8f);

            var files = controller.ListSaves();
            MakeLabel(_picker.transform, "Saved scenes", new Vector3(0, 0.22f, 0), 0.05f);
            if (files.Count == 0)
                MakeLabel(_picker.transform, "(none found)", Vector3.zero, 0.04f);

            float y = 0.10f;
            foreach (var path in files)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string captured = path; // closure capture
                MakeButton(_picker.transform, name, new Vector3(0, y, 0), () => Pick(captured),
                           new Color(0.18f, 0.22f, 0.35f), width: 0.5f);
                y -= 0.12f;
            }
        }

        private void Pick(string path)
        {
            if (_picker != null) Destroy(_picker);
            controller.OnChanged += Refresh;
            controller.LoadFile(path);
            BuildTransport();
            BuildBanner();
            Refresh();
        }

        // ---- Transport ----
        private void BuildTransport()
        {
            _transport = new GameObject("ReplayTransport(Runtime)");

            MakeButton(_transport.transform, "◄ Prev",  new Vector3(-0.30f, 0, 0), () => controller.StepBack(),           Teal());
            MakeButton(_transport.transform, "Next ►", new Vector3(-0.10f, 0, 0), () => controller.StepForward(),         Teal());
            MakeButton(_transport.transform, "⟲ 20s",  new Vector3( 0.10f, 0, 0), () => controller.RewindSeconds(20f),   Purple());
            MakeButton(_transport.transform, "⟲ 1m",   new Vector3( 0.30f, 0, 0), () => controller.RewindSeconds(60f),   Purple());

            var s = new GameObject("Status");
            s.transform.SetParent(_transport.transform, false);
            s.transform.localPosition = new Vector3(0, 0.10f, 0);
            _status = s.AddComponent<TextMeshPro>();
            _status.alignment = TextAlignmentOptions.Center;
            _status.color = Color.white;
            _status.rectTransform.sizeDelta = new Vector2(0.7f, 0.06f);
            _status.enableAutoSizing = true;
            _status.fontSizeMin = 0.01f;
            _status.fontSizeMax = 0.04f;

            var tr = new GameObject("Transcript");
            tr.transform.SetParent(_transport.transform, false);
            tr.transform.localPosition = new Vector3(0, 0.28f, 0);
            _transcript = tr.AddComponent<TextMeshPro>();
            _transcript.alignment = TextAlignmentOptions.Top;
            _transcript.color = new Color(0.85f, 0.85f, 1f);
            _transcript.rectTransform.sizeDelta = new Vector2(0.8f, 0.30f);
            _transcript.fontSize = 0.028f;

            // Place transport once in world space — fixed, not head-following.
            PlaceSnap(_transport.transform, 0.75f);
        }

        private void BuildBanner()
        {
            _banner = new GameObject("ReadOnlyBanner(Runtime)");
            var tmp = _banner.AddComponent<TextMeshPro>();
            tmp.text = "VIEWING SAVED SCENE — read only";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1f, 0.85f, 0.3f);
            tmp.rectTransform.sizeDelta = new Vector2(0.8f, 0.06f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.01f;
            tmp.fontSizeMax = 0.045f;
            // Banner is positioned on first LateUpdate below.
        }

        private void Refresh()
        {
            if (_status != null)
                _status.text = $"Step {controller.Index} / {controller.StepCount}";
            if (_transcript != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var u in controller.UtterancesUpToNow())
                    sb.AppendLine($"<b>{u.id}:</b> {u.text}");
                _transcript.text = sb.ToString();
            }
        }

        private void LateUpdate()
        {
            // Only the non-interactive banner HUD tracks the head each frame.
            // Transport and picker are fixed in world space (placed once).
            if (_banner != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    _banner.transform.position =
                        cam.transform.position
                        + cam.transform.forward * 0.7f
                        + cam.transform.up * 0.22f;
                    // UI correction: (camera − panel) so text faces the user.
                    _banner.transform.rotation = Quaternion.LookRotation(
                        cam.transform.position - _banner.transform.position, Vector3.up);
                }
            }
        }

        // ---- helpers ----
        private static Color Teal()   => new Color(0.05f, 0.42f, 0.5f);
        private static Color Purple() => new Color(0.32f, 0.18f, 0.45f);

        /// <summary>Place a panel once, directly in front of the user, facing toward the camera
        /// (camera − panel direction). Called once at creation — no per-frame update.</summary>
        private static void PlaceSnap(Transform t, float dist)
        {
            var cam = Camera.main;
            if (cam == null) return;
            t.position = cam.transform.position
                       + cam.transform.forward * dist
                       + cam.transform.up * -0.12f;
            // UI correction: face toward camera = (camera - panel), not (panel - camera).
            t.rotation = Quaternion.LookRotation(
                cam.transform.position - t.position, Vector3.up);
        }

        private static void MakeLabel(Transform parent, string text, Vector3 localPos, float maxFont)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.5f, 0.06f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.01f;
            tmp.fontSizeMax = maxFont;
        }

        private static void MakeButton(Transform parent, string label, Vector3 localPos,
                                       UnityEngine.Events.UnityAction onClick, Color color, float width = 0.18f)
        {
            var root = new GameObject($"Btn_{label}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(width, 0.09f, 0.02f);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(width, 0.09f, 0.012f);
            var bgc = bg.GetComponent<Collider>();
            if (bgc != null) Destroy(bgc);
            bg.GetComponent<MeshRenderer>().material.color = color;

            var t = new GameObject("Label");
            t.transform.SetParent(root.transform, false);
            t.transform.localPosition = new Vector3(0, 0, 0.012f);
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(width * 0.9f, 0.08f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.01f;
            tmp.fontSizeMax = 0.035f;

            root.AddComponent<StatefulInteractable>().OnClicked.AddListener(onClick);
        }
    }
}
