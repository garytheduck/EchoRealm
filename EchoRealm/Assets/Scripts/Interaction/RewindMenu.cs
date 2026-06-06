using UnityEngine;
using TMPro;
using MixedReality.Toolkit;

namespace EchoRealm.Interaction
{
    /// <summary>A small panel with "Rewind 20s" / "Rewind 1m" buttons that call
    /// FilmSync.RequestRewind (networked → both headsets jump together). Placed ONCE in front of the
    /// user at startup and then left FIXED in world space — it does NOT follow the head. Runtime-built
    /// like ResumeButton. Attach to a persistent object (GameManager) in MainScene.</summary>
    public class RewindMenu : MonoBehaviour
    {
        [Tooltip("Distance in front of the user where the panel is placed once, at startup.")]
        [SerializeField] private float distance = 0.6f;
        [Tooltip("Vertical offset from eye-line at placement (negative = a bit lower, easier to reach).")]
        [SerializeField] private float verticalOffset = -0.2f;

        private GameObject _go;

        private void Start() => Build();

        private void Build()
        {
            _go = new GameObject("RewindMenu(Runtime)");
            PlaceOnce();   // fixed in world space — no per-frame head-follow
            MakeButton("<< 20s", new Vector3(-0.13f, 0, 0), () => Rewind(20f));
            MakeButton("<< 1m", new Vector3(0.13f, 0, 0), () => Rewind(60f));
        }

        // Position the panel once, in front of the user, facing them so the labels read correctly
        // (+Z toward the camera, matching ResumeButton). Then it stays put in world space.
        private void PlaceOnce()
        {
            var cam = Camera.main;
            if (cam == null) return;
            _go.transform.position = cam.transform.position
                                     + cam.transform.forward * distance
                                     + cam.transform.up * verticalOffset;
            _go.transform.rotation = Quaternion.LookRotation(cam.transform.position - _go.transform.position, Vector3.up);
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
            var baseColor = mat.color;
            si.OnClicked.AddListener(() => { onClick(); StartCoroutine(FlashPress(mat, baseColor)); });
        }

        // Brief bright flash on click so a press feels registered, then ease back to the base colour.
        private System.Collections.IEnumerator FlashPress(Material mat, Color baseColor)
        {
            if (mat == null) yield break;
            var flash = new Color(0.85f, 0.65f, 1f, 1f);
            const float dur = 0.3f;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                if (mat == null) yield break;
                mat.color = Color.Lerp(flash, baseColor, t / dur);
                yield return null;
            }
            if (mat != null) mat.color = baseColor;
        }
    }
}
