using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace EchoRealm.Sandbox
{
    /// <summary>Configures AR-generated spatial-mesh chunks so the ball bounces off the real room (layer
    /// "B"): each chunk goes on the SpatialMesh layer with a MeshCollider + low-friction material.
    ///
    /// HARDENED: <c>meshesChanged</c> only ENQUEUES; the actual collider work is THROTTLED to a few
    /// chunks per frame in <see cref="Update"/>. Assigning <c>MeshCollider.sharedMesh</c> forces a
    /// synchronous PhysX bake, so baking a whole room in one event stalls the main thread (the leading
    /// perf-crash suspect). Every step is wrapped in try/catch so a single bad chunk can never crash
    /// the app — and the <see cref="SceneUnderstandingWatchdog"/> can disable this whole path, leaving
    /// the deterministic <see cref="SandboxFloor"/> to keep the ball bouncing. Remove this and the film
    /// is unchanged.</summary>
    public class SpatialMeshManager : MonoBehaviour
    {
        [SerializeField] private ARMeshManager meshManager;
        [SerializeField] private PhysicMaterial meshMaterial;   // low friction; bounce comes from the ball
        [SerializeField] private bool showWireframe = false;
        [SerializeField] private Material wireframeMaterial;

        [Header("Throttle (prevents the per-frame collider-bake flood)")]
        [Tooltip("Max mesh chunks baked into colliders per frame.")]
        [SerializeField] private int maxChunksPerFrame = 2;
        [Tooltip("Minimum seconds between batches (0 = every frame).")]
        [SerializeField] private float minSecondsBetweenBatches = 0f;

        private int _spatialLayer;
        private readonly Queue<MeshFilter> _pending = new Queue<MeshFilter>();
        private readonly HashSet<int> _queued = new HashSet<int>();
        private float _lastBatchTime = -999f;

        private void Awake()
        {
            _spatialLayer = LayerMask.NameToLayer("SpatialMesh");
            if (_spatialLayer < 0) Debug.LogError("[BallSandbox] 'SpatialMesh' layer missing — run EchoRealm > Ball Sandbox > Setup Scene.");
            if (meshManager == null) meshManager = GetComponentInChildren<ARMeshManager>();
            if (meshManager != null) meshManager.meshesChanged += OnMeshesChanged;
        }

        private void OnDestroy()
        {
            if (meshManager != null) meshManager.meshesChanged -= OnMeshesChanged;
        }

        // Main thread, but cheap: ONLY enqueue (deduped). Never bake here — a room's worth of chunks in
        // one event would stall the frame. Wrapped so a malformed event can never propagate.
        private void OnMeshesChanged(ARMeshesChangedEventArgs args)
        {
            try
            {
                Enqueue(args.added);
                Enqueue(args.updated);
            }
            catch (System.Exception e) { Debug.LogWarning($"[BallSandbox] meshesChanged enqueue skipped: {e.Message}"); }
        }

        private void Enqueue(List<MeshFilter> filters)
        {
            if (filters == null) return;
            foreach (var mf in filters)
            {
                if (mf == null) continue;
                if (_queued.Add(mf.GetInstanceID())) _pending.Enqueue(mf);
            }
        }

        private void Update()
        {
            if (_pending.Count == 0) return;
            if (minSecondsBetweenBatches > 0f && Time.unscaledTime - _lastBatchTime < minSecondsBetweenBatches) return;
            _lastBatchTime = Time.unscaledTime;

            int n = 0;
            while (_pending.Count > 0 && n < maxChunksPerFrame)
            {
                var mf = _pending.Dequeue();
                if (mf != null) _queued.Remove(mf.GetInstanceID());
                ConfigureOne(mf);
                n++;
            }
        }

        // Per-chunk collider config, hardened: skip null/destroyed/degenerate meshes; never throw.
        private void ConfigureOne(MeshFilter mf)
        {
            try
            {
                if (mf == null) return;
                var mesh = mf.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) return; // degenerate — cooking it can crash PhysX

                var go = mf.gameObject;
                if (_spatialLayer >= 0) go.layer = _spatialLayer;

                var mc = go.GetComponent<MeshCollider>();
                if (mc == null) mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;                       // synchronous bake — throttled to N/frame above
                if (meshMaterial != null) mc.material = meshMaterial;

                var r = go.GetComponent<MeshRenderer>();
                if (r != null)
                {
                    r.enabled = showWireframe;
                    if (showWireframe && wireframeMaterial != null) r.sharedMaterial = wireframeMaterial;
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[BallSandbox] chunk skipped: {e.Message}"); }
        }
    }
}
