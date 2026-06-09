using UnityEngine;
using EchoRealm.Networking;

namespace EchoRealm.Sandbox
{
    /// <summary>
    /// The deterministic bounce surface for the ball sandbox (layer "A"): a world-horizontal floor
    /// plus an optional containment box (walls + ceiling), anchored to the QR pose. Uses ONLY
    /// primitive BoxColliders and managed Unity calls — it never touches AR Foundation / OpenXR /
    /// Scene Understanding, so it can NEVER trigger the native scene-understanding crash. This is the
    /// always-on baseline: even with the room mesh (B) off or disabled, the ball always bounces.
    ///
    /// Attach to the BallSandbox root. Colliders go on the "SpatialMesh" layer (which already collides
    /// only with the ball's "BallSandbox" layer) with the low-friction material. Fully isolated:
    /// remove it and EchoRealm is unchanged.
    /// </summary>
    public class SandboxFloor : MonoBehaviour
    {
        [Header("Anchor offset")]
        [Tooltip("Metres BELOW the QR anchor where the floor sits. The QR is often on a table/wall, not " +
                 "the floor — tune so the ball lands on the real floor. Default assumes a table-mounted QR ~1 m up.")]
        [SerializeField] private float floorDropMeters = 1.0f;

        [Header("Floor")]
        [Tooltip("X by Z extent of the floor (metres).")]
        [SerializeField] private Vector2 floorSize = new Vector2(6f, 6f);
        [SerializeField] private float floorThickness = 0.1f;

        [Header("Containment box (keeps a thrown ball in play)")]
        [SerializeField] private bool buildWalls = true;
        [SerializeField] private bool buildCeiling = true;
        [SerializeField] private float wallHeight = 2.0f;
        [SerializeField] private float wallThickness = 0.1f;

        [Header("Material / debug")]
        [Tooltip("Low-friction physic material (bounce comes from the ball). Wired by Setup.")]
        [SerializeField] private PhysicMaterial floorMaterial;
        [Tooltip("Show thin renderers on the floor/walls so you can see them in the Editor.")]
        [SerializeField] private bool showDebugSurfaces = false;

        private int _spatialLayer;
        private GameObject _floorRoot;
        private bool _subscribed;

        private void Awake()
        {
            _spatialLayer = LayerMask.NameToLayer("SpatialMesh");
            if (_spatialLayer < 0)
                Debug.LogError("[BallSandbox] 'SpatialMesh' layer missing — run EchoRealm > Ball Sandbox > Setup Scene.");
        }

        // Hook from both OnEnable (runtime re-enable: anchor already up) and Start (active-at-load:
        // QRAnchorManager.Awake has definitely run by Start, so Instance is available).
        private void OnEnable() => TryHook();
        private void Start() => TryHook();

        private void TryHook()
        {
            if (_floorRoot != null) return;             // already built
            var qr = QRAnchorManager.Instance;
            if (qr == null) return;                     // not ready yet — Start (or a later enable) retries
            if (qr.IsAnchored) { BuildFloor(); return; }
            if (!_subscribed) { qr.OnAnchorEstablished += BuildFloor; _subscribed = true; }
        }

        private void OnDisable()
        {
            var qr = QRAnchorManager.Instance;
            if (_subscribed && qr != null) qr.OnAnchorEstablished -= BuildFloor;
            _subscribed = false;
            if (_floorRoot != null) Destroy(_floorRoot);
            _floorRoot = null;
        }

        /// <summary>Build (or rebuild) the floor + containment box at the current QR anchor pose. Idempotent.</summary>
        public void BuildFloor()
        {
            var qr = QRAnchorManager.Instance;
            if (qr == null) return;
            if (_floorRoot != null) { Destroy(_floorRoot); _floorRoot = null; }

            // World-horizontal: drop straight down under gravity from the anchor, so the floor is flat
            // regardless of how the QR card is tilted on a wall/table.
            Vector3 floorTop = SandboxFloorMath.FloorTopCenter(qr.AnchorPosition, floorDropMeters);

            _floorRoot = new GameObject("SandboxFloor(Runtime)");
            _floorRoot.transform.SetPositionAndRotation(floorTop, Quaternion.identity);

            // Floor: a thin box whose TOP face sits at floorTop.y.
            AddBox("Floor", new Vector3(0f, -floorThickness * 0.5f, 0f),
                   new Vector3(floorSize.x, floorThickness, floorSize.y));

            if (buildWalls)
            {
                float hx = floorSize.x * 0.5f, hz = floorSize.y * 0.5f, hy = wallHeight * 0.5f;
                AddBox("Wall+X", new Vector3(hx, hy, 0f), new Vector3(wallThickness, wallHeight, floorSize.y));
                AddBox("Wall-X", new Vector3(-hx, hy, 0f), new Vector3(wallThickness, wallHeight, floorSize.y));
                AddBox("Wall+Z", new Vector3(0f, hy, hz), new Vector3(floorSize.x, wallHeight, wallThickness));
                AddBox("Wall-Z", new Vector3(0f, hy, -hz), new Vector3(floorSize.x, wallHeight, wallThickness));
            }
            if (buildCeiling)
                AddBox("Ceiling", new Vector3(0f, wallHeight + floorThickness * 0.5f, 0f),
                       new Vector3(floorSize.x, floorThickness, floorSize.y));
        }

        // One containment surface: a BoxCollider on the SpatialMesh layer with the low-friction material.
        private void AddBox(string label, Vector3 localCenter, Vector3 size)
        {
            var go = new GameObject(label);
            go.transform.SetParent(_floorRoot.transform, false);
            go.transform.localPosition = localCenter;
            if (_spatialLayer >= 0) go.layer = _spatialLayer;

            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            if (floorMaterial != null) col.material = floorMaterial;

            if (showDebugSurfaces)
            {
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var visCol = vis.GetComponent<Collider>();
                if (visCol != null) Destroy(visCol);
                vis.transform.SetParent(go.transform, false);
                vis.transform.localScale = size;
                if (_spatialLayer >= 0) vis.layer = _spatialLayer;
            }
        }
    }

    /// <summary>Pure floor-placement math (unit-tested by the editor self-check).</summary>
    public static class SandboxFloorMath
    {
        /// <summary>World position of the floor's TOP surface: straight down from the anchor under
        /// gravity (world -Y), centred under the anchor — so the floor is flat regardless of QR tilt.</summary>
        public static Vector3 FloorTopCenter(Vector3 anchorPos, float dropMeters)
            => new Vector3(anchorPos.x, anchorPos.y - dropMeters, anchorPos.z);
    }
}
