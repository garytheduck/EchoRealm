using Fusion;
using UnityEngine;

namespace EchoRealm.Testing
{
    /// <summary>
    /// A simple networked cube for validating Photon Fusion 2 Shared Mode sync.
    /// Any player can grab/move/rotate this cube; all clients see the same state.
    ///
    /// Requirements on the prefab GameObject:
    ///  - NetworkObject (Fusion)
    ///  - NetworkTransform (Fusion) — syncs position/rotation
    ///  - MeshRenderer + MeshFilter (visual cube)
    ///  - BoxCollider
    ///  - MRTK ObjectManipulator (for hand grab)
    ///  - This script (NetworkedTestCube)
    /// </summary>
    public class NetworkedTestCube : NetworkBehaviour
    {
        [Header("Visual")]
        [SerializeField] private Renderer cubeRenderer;
        [SerializeField] private Color masterColor = new Color(1f, 0.3f, 0.3f); // red
        [SerializeField] private Color clientColor = new Color(0.3f, 0.6f, 1f); // blue

        [Header("Debug")]
        [SerializeField] private bool logEvents = true;

        // Networked property — last player who touched the cube.
        [Networked] public PlayerRef LastInteractor { get; set; }

        public override void Spawned()
        {
            Log($"Cube spawned. HasStateAuthority={HasStateAuthority}, InputAuthority={Object.InputAuthority}");

            // Tint differently on master vs. client so you can visually tell
            // which machine is the "authoritative" spawner at a glance.
            if (cubeRenderer != null)
            {
                bool isMaster = Runner != null && Runner.IsSharedModeMasterClient;
                cubeRenderer.material.color = isMaster ? masterColor : clientColor;
            }
        }

        /// <summary>
        /// Called by MRTK ObjectManipulator.OnManipulationStarted (wire in Inspector).
        /// Takes state authority so this client owns the movement while grabbing.
        /// </summary>
        public void OnGrabStart()
        {
            if (Object == null || !Object.IsValid) return;

            if (!HasStateAuthority)
            {
                Object.RequestStateAuthority();
                Log("Requested state authority (grab start).");
            }

            LastInteractor = Runner.LocalPlayer;
        }

        /// <summary>
        /// Called by MRTK ObjectManipulator.OnManipulationEnded.
        /// </summary>
        public void OnGrabEnd()
        {
            Log($"Grab ended. Final position: {transform.position}");
        }

        /// <summary>
        /// Test helper: randomize position within a small box around current anchor.
        /// Only the state authority should call this (others will be ignored by Fusion).
        /// </summary>
        public void RandomizePosition()
        {
            if (!HasStateAuthority)
            {
                Object.RequestStateAuthority();
            }

            Vector3 offset = new Vector3(
                Random.Range(-0.3f, 0.3f),
                Random.Range(0f, 0.3f),
                Random.Range(-0.3f, 0.3f)
            );
            transform.position += offset;
            Log($"Randomized position to {transform.position}");
        }

        private void Log(string msg)
        {
            if (logEvents) Debug.Log($"[TestCube] {msg}");
        }
    }
}
