using UnityEngine;
using TMPro;
using MixedReality.Toolkit;

namespace EchoRealm.Film
{
    /// <summary>Shown once when the scene ends: "Save this scene?" with Save / Discard.
    /// Save writes the recorded timeline + SessionLogger text via SceneArchive. Master-only
    /// (the master owns the authoritative timeline). Attach to GameManager; call Show().</summary>
    public class SceneSavePrompt : MonoBehaviour
    {
        public static SceneSavePrompt Instance { get; private set; }
        private GameObject _go;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>Show the prompt (only meaningful on the device that recorded — the master).</summary>
        public void Show()
        {
            if (TimelineRecorder.Instance == null)
            {
                Debug.Log("[SceneSavePrompt] No recorder on this device — nothing to save.");
                return;
            }
            if (_go == null) Build();
            _go.SetActive(true);
            Place();
        }

        private void Save()
        {
            var rec = TimelineRecorder.Instance;
            string log = SessionLogger.Instance != null ? SessionLogger.Instance.ExportLog() : "";
            if (SessionLogger.Instance != null) rec.SetSessionId(SessionLogger.Instance.SessionId);
            string path = SceneArchive.Save(rec.Timeline, log);
            Debug.Log($"[SceneSavePrompt] Scene saved → {path}");
            Hide();
        }

        private void Hide() { if (_go != null) _go.SetActive(false); }

        private void Place()
        {
            var cam = Camera.main; if (cam == null) return;
            _go.transform.position = cam.transform.position + cam.transform.forward * 0.7f;
            // Face the user (+Z toward the camera) so the prompt reads correctly. Placed once, then fixed.
            _go.transform.rotation = Quaternion.LookRotation(cam.transform.position - _go.transform.position, Vector3.up);
        }

        private void Build()
        {
            _go = new GameObject("SceneSavePrompt(Runtime)");
            MakeLabel("Save this scene?", new Vector3(0, 0.10f, 0));
            MakeButton("Save", new Vector3(-0.13f, 0, 0), Save, new Color(0.10f, 0.45f, 0.20f));
            MakeButton("Discard", new Vector3(0.13f, 0, 0), Hide, new Color(0.45f, 0.12f, 0.12f));
        }

        private void MakeLabel(string text, Vector3 localPos)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(_go.transform, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.4f, 0.08f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.05f;
        }

        private void MakeButton(string label, Vector3 localPos, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var root = new GameObject($"Btn_{label}");
            root.transform.SetParent(_go.transform, false);
            root.transform.localPosition = localPos;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.22f, 0.10f, 0.02f);
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(0.22f, 0.10f, 0.012f);
            var bgCol = bg.GetComponent<Collider>(); if (bgCol != null) Destroy(bgCol);
            bg.GetComponent<MeshRenderer>().material.color = color;
            var textGo = new GameObject("Label");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0, 0, 0.012f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = label; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.2f, 0.09f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.05f;
            var si = root.AddComponent<StatefulInteractable>();
            si.OnClicked.AddListener(onClick);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
