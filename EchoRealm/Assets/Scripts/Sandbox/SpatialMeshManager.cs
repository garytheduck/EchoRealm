using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace EchoRealm.Sandbox
{
    /// <summary>Forces every AR-generated spatial mesh chunk onto the SpatialMesh layer with a
    /// MeshCollider + friction material, so the ball (and only the ball) bounces off the real room.
    /// Belt-and-suspenders even if the ARMeshManager.meshPrefab is misconfigured. Also toggles a
    /// debug wireframe.</summary>
    public class SpatialMeshManager : MonoBehaviour
    {
        [SerializeField] private ARMeshManager meshManager;
        [SerializeField] private PhysicMaterial meshMaterial;   // low friction; bounce comes from the ball
        [SerializeField] private bool showWireframe = false;
        [SerializeField] private Material wireframeMaterial;

        private int _spatialLayer;

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

        private void OnMeshesChanged(ARMeshesChangedEventArgs args)
        {
            Configure(args.added);
            Configure(args.updated);
        }

        private void Configure(List<MeshFilter> filters)
        {
            if (filters == null) return;
            foreach (var mf in filters)
            {
                if (mf == null) continue;
                var go = mf.gameObject;
                if (_spatialLayer >= 0) go.layer = _spatialLayer;

                var mc = go.GetComponent<MeshCollider>();
                if (mc == null) mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                if (meshMaterial != null) mc.material = meshMaterial;

                var r = go.GetComponent<MeshRenderer>();
                if (r != null)
                {
                    r.enabled = showWireframe;
                    if (showWireframe && wireframeMaterial != null) r.sharedMaterial = wireframeMaterial;
                }
            }
        }
    }
}
