using Fusion;
using UnityEngine;
using EchoRealm.Networking;

namespace EchoRealm.Testing
{
    /// <summary>
    /// Spawns a NetworkedTestCube above the QR anchor so all clients see it in the
    /// same physical location. Only the Shared Mode master spawns to avoid duplicates.
    ///
    /// Setup:
    ///  - Attach to any GameObject in MainScene (e.g., "TestCubeSpawner").
    ///  - Assign cubePrefab (must have NetworkObject + NetworkedTestCube).
    ///  - Assign anchorTransform (usually SceneRoot or the QR anchor object).
    ///  - Call SpawnCube() from a button, or enable spawnOnSessionJoin.
    /// </summary>
    public class TestCubeSpawner : MonoBehaviour
    {
        [Header("Prefab (must have NetworkObject)")]
        [SerializeField] private NetworkObject cubePrefab;

        [Header("Spawn Location")]
        [Tooltip("Transform that represents the QR anchor / SceneRoot origin.")]
        [SerializeField] private Transform anchorTransform;

        [Tooltip("Offset from the anchor in anchor-local space (meters).")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.3f, 0.5f);

        [Header("Behavior")]
        [SerializeField] private bool spawnOnSessionJoin = false;

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        private FusionNetworkManager network;
        private NetworkObject spawnedCube;

        private void Start()
        {
            network = FusionNetworkManager.Instance ?? FindObjectOfType<FusionNetworkManager>();
            if (network == null)
            {
                Debug.LogError("[TestCubeSpawner] No FusionNetworkManager found in scene.");
                return;
            }

            if (spawnOnSessionJoin)
            {
                network.OnSessionJoined += HandleSessionJoined;
            }
        }

        private void OnDestroy()
        {
            if (network != null)
            {
                network.OnSessionJoined -= HandleSessionJoined;
            }
        }

        private void HandleSessionJoined()
        {
            // Small delay to make sure runner is fully ready.
            Invoke(nameof(SpawnCube), 0.5f);
        }

        /// <summary>
        /// Public entry point. Wire this to a button OnClick in the Inspector.
        /// </summary>
        public void SpawnCube()
        {
            if (network == null || network.Runner == null || !network.Runner.IsRunning)
            {
                Log("Cannot spawn — Fusion runner not running.", isError: true);
                return;
            }

            if (!network.IsMaster)
            {
                Log("Not the master client — skipping spawn (master will spawn for everyone).");
                return;
            }

            if (cubePrefab == null)
            {
                Log("cubePrefab not assigned.", isError: true);
                return;
            }

            if (spawnedCube != null)
            {
                Log("Cube already spawned — despawning old one first.");
                network.Runner.Despawn(spawnedCube);
                spawnedCube = null;
            }

            Vector3 worldPos = anchorTransform != null
                ? anchorTransform.TransformPoint(spawnOffset)
                : spawnOffset;

            Quaternion rot = anchorTransform != null
                ? anchorTransform.rotation
                : Quaternion.identity;

            spawnedCube = network.Runner.Spawn(cubePrefab, worldPos, rot, network.Runner.LocalPlayer);
            Log($"Spawned cube at {worldPos} (anchor: {(anchorTransform != null ? anchorTransform.name : "world origin")})");
        }

        /// <summary>
        /// Despawn the cube (master only).
        /// </summary>
        public void DespawnCube()
        {
            if (spawnedCube != null && network != null && network.IsMaster)
            {
                network.Runner.Despawn(spawnedCube);
                spawnedCube = null;
                Log("Cube despawned.");
            }
        }

        /// <summary>
        /// Tell the currently-spawned cube to jump to a random nearby position.
        /// Useful for visually verifying sync.
        /// </summary>
        public void RandomizeCubePosition()
        {
            if (spawnedCube == null)
            {
                Log("No cube spawned to randomize.", isError: true);
                return;
            }

            var testCube = spawnedCube.GetComponent<NetworkedTestCube>();
            if (testCube != null) testCube.RandomizePosition();
        }

        private void Log(string msg, bool isError = false)
        {
            if (!logEvents) return;
            if (isError) Debug.LogError($"[TestCubeSpawner] {msg}");
            else Debug.Log($"[TestCubeSpawner] {msg}");
        }
    }
}
