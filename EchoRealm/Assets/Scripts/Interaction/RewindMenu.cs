using UnityEngine;
using TMPro;
using MixedReality.Toolkit;

namespace EchoRealm.Interaction
{
    /// <summary>A small head-following panel with "Rewind 20s" / "Rewind 1m" buttons that call
    /// FilmSync.RequestRewind (networked → both headsets jump together). Runtime-built like
    /// ResumeButton. Attach to a persistent object (GameManager) in MainScene.</summary>
    public class RewindMenu : MonoBehaviour
    {
        [SerializeField] private float distance = 0.6f;
        [SerializeField] private float verticalOffset = -0.18f;
        [Range(0.02f, 1f)][SerializeField] private float followLerp = 0.12f;

        private GameObject _go;

        private void Start() => Build();

        private void LateUpdate()
        {
            if (_go == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 target = cam.transform.position + cam.transform.forward * distance
                             + cam.transform.up * verticalOffset;
            _go.transform.position = Vector3.Lerp(_go.transform.position, target, followLerp);
            _go.transform.rotation = Quaternion.LookRotation(_go.transform.position - cam.transform.position, cam.transform.up);
        }

        private void Build()
        {
            _go = new GameObject("RewindMenu(Runtime)");
            MakeButton("⟲ 20s", new Vector3(-0.13f, 0, 0), () => Rewind(20f));
            MakeButton("⟲ 1m", new Vector3(0.13f, 0, 0), () => Rewind(60f));
        }

        private void Rewind(float seconds)
        {
            var sync = EchoRealm.Networking.FilmSync.Instance;
            if (sync != null) sync.RequestRewind(seconds);
            else Debug.LogWarning("[RewindMenu] No FilmSync — rewind needs a session.");
        }

        private void MakeButton(string label, Vector3 localPos, UnityEngine.Events.UnityAction onClick)
        {
            var root = new GameObject($"Btn_{label}");
            root.transform.SetParent(_go.transform, false);
            root.transform.localPosition = localPos;

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.22f, 0.10f, 0.02f);

            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(0.22f, 0.10f, 0.012f);
            var bgCol = bg.GetComponent<Collider>();
            if (bgCol != null) Destroy(bgCol);
            var mat = bg.GetComponent<MeshRenderer>().material;
            mat.color = new Color(0.30f, 0.10f, 0.45f, 1f);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0, 0, 0.012f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.2f, 0.09f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.05f;

            var si = root.AddComponent<StatefulInteractable>();
            si.OnClicked.AddListener(onClick);
        }
    }
}
