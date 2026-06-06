using UnityEngine;
using TMPro;
using MixedReality.Toolkit;

namespace EchoRealm.Film
{
    /// <summary>Decides at startup whether to run the live film or load a saved scene. When
    /// "View Saved Scene" is chosen, ReplayMode is set true BEFORE the normal boot proceeds, so
    /// the bootstrapper skips QR/Fusion/voice/film entirely and hands off to ReplaySessionController.
    ///
    /// UI correction: panel is placed once and faces the user with (camera - panel) so text is
    /// readable. It is NOT repositioned every frame — interactive choosers are fixed in world space.</summary>
    public class ReplayModeGate : MonoBehaviour
    {
        public static bool ReplayMode { get; private set; }

        [SerializeField] private ReplaySessionController replayController;
        private GameObject _go;

        /// <summary>Called by the bootstrapper at the very start. Returns true if it showed the
        /// chooser (boot should pause); false to proceed with the live film immediately.</summary>
        public bool ShowChooser()
        {
            if (replayController == null) replayController = FindObjectOfType<ReplaySessionController>(true);
            Build();
            return true;
        }

        private void ChooseLive()
        {
            ReplayMode = false;
            Hide();
            var boot = FindObjectOfType<EchoRealmBootstrapper>();
            if (boot != null) boot.BeginLiveBoot();
        }

        private void ChooseReplay()
        {
            ReplayMode = true;
            Hide();
            if (replayController != null) replayController.Begin();
            else Debug.LogError("[ReplayModeGate] No ReplaySessionController assigned.");
        }

        private void Hide() { if (_go != null) _go.SetActive(false); }

        private void Build()
        {
            _go = new GameObject("StartChooser(Runtime)");
            var cam = Camera.main;
            if (cam != null)
            {
                _go.transform.position = cam.transform.position + cam.transform.forward * 0.7f;
                // UI correction: face toward camera = (camera - panel), not (panel - camera).
                _go.transform.rotation = Quaternion.LookRotation(
                    cam.transform.position - _go.transform.position, Vector3.up);
            }
            Button("Start Live Film",   new Vector3(-0.14f, 0, 0), ChooseLive,   new Color(0.10f, 0.35f, 0.45f));
            Button("View Saved Scene",  new Vector3( 0.14f, 0, 0), ChooseReplay, new Color(0.32f, 0.20f, 0.45f));
        }

        private void Button(string label, Vector3 localPos, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var root = new GameObject($"Btn_{label}");
            root.transform.SetParent(_go.transform, false);
            root.transform.localPosition = localPos;

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.26f, 0.10f, 0.02f);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(0.26f, 0.10f, 0.012f);
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
            tmp.rectTransform.sizeDelta = new Vector2(0.24f, 0.09f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.01f;
            tmp.fontSizeMax = 0.045f;

            root.AddComponent<StatefulInteractable>().OnClicked.AddListener(onClick);
        }
    }
}
