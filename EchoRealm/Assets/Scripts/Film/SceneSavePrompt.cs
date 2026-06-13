using UnityEngine;
using TMPro;
using MixedReality.Toolkit;

namespace EchoRealm.Film
{
    /// <summary>Shown once when the scene ends: "Save this scene?" with Save / Discard. Save writes
    /// the recorded timeline + SessionLogger text via SceneArchive, then offers to view it. Master-only
    /// (the master owns the authoritative timeline). Attach to GameManager; call Show().
    ///
    /// The film does NOT restart after Save/Discard — the scene stays on its ending. To watch a saved
    /// run, tap "View saved scene", which hands off to ReplaySessionController (offline, read-only).</summary>
    public class SceneSavePrompt : MonoBehaviour
    {
        public static SceneSavePrompt Instance { get; private set; }
        private GameObject _go;
        private bool _saving;   // debounce: gaze-dwell can raise OnClicked several times per press

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
            _saving = false;
            BuildAsk();
            Place();
        }

        // ---- Step 1: ask ----
        private void BuildAsk()
        {
            NewPanel();
            MakeLabel("Save this scene?", new Vector3(0, 0.10f, 0));
            MakeButton("Save", new Vector3(-0.13f, 0, 0), Save, new Color(0.10f, 0.45f, 0.20f));
            MakeButton("Discard", new Vector3(0.13f, 0, 0), Hide, new Color(0.45f, 0.12f, 0.12f));
        }

        private void Save()
        {
            if (_saving) return;   // a single gaze-dwell can fire OnClicked repeatedly — save once
            _saving = true;
            var rec = TimelineRecorder.Instance;
            string log = SessionLogger.Instance != null ? SessionLogger.Instance.ExportLog() : "";
            if (SessionLogger.Instance != null) rec.SetSessionId(SessionLogger.Instance.SessionId);
            string path = SceneArchive.Save(rec.Timeline, log);
            Debug.Log($"[SceneSavePrompt] Scene saved → {path}");
            BuildSaved();   // step 2: confirm + offer to view it
            Place();
        }

        // ---- Step 2: saved → view or done ----
        private void BuildSaved()
        {
            NewPanel();
            MakeLabel("Scene saved!", new Vector3(0, 0.10f, 0));
            MakeButton("View saved scene", new Vector3(-0.13f, 0, 0), OpenViewer, new Color(0.32f, 0.20f, 0.45f));
            MakeButton("Done", new Vector3(0.13f, 0, 0), Hide, new Color(0.20f, 0.30f, 0.38f));
        }

        // Hand off to the offline, read-only saved-scene viewer (disables manipulation/recorder/rewind,
        // shows the save picker). Safe here because the film has already ended.
        private void OpenViewer()
        {
            Hide();
            var replay = FindObjectOfType<ReplaySessionController>(true);
            if (replay != null) replay.Begin();
            else Debug.LogWarning("[SceneSavePrompt] No ReplaySessionController in scene — cannot open the viewer.");
        }

        private void Hide() { if (_go != null) _go.SetActive(false); }

        // Recreate a fresh panel placed in front of the user.
        private void NewPanel()
        {
            if (_go != null) Destroy(_go);
            _go = new GameObject("SceneSavePrompt(Runtime)");
            _go.SetActive(true);
        }

        private void Place()
        {
            var cam = Camera.main; if (cam == null || _go == null) return;
            _go.transform.position = cam.transform.position + cam.transform.forward * 0.7f;
            // Face the user (+Z toward the camera) so the prompt reads correctly. Placed once, then fixed.
            _go.transform.rotation = Quaternion.LookRotation(cam.transform.position - _go.transform.position, Vector3.up);
        }

        private void MakeLabel(string text, Vector3 localPos)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(_go.transform, false);
            go.transform.localPosition = localPos;
            // Un-mirror: the panel faces +Z toward the head, but TMP reads from its -Z side.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(0.5f, 0.08f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.02f; tmp.fontSizeMax = 0.07f;
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
            // Un-mirror (same as the rewind/resume buttons): flip 180° so the readable face points at the user.
            textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = label; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            // Bigger text on the SAME button size: wider rect (may overhang slightly) + higher auto-size cap.
            tmp.rectTransform.sizeDelta = new Vector2(0.26f, 0.10f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.03f; tmp.fontSizeMax = 0.085f;

            var si = root.AddComponent<StatefulInteractable>();
            si.OnClicked.AddListener(onClick);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
