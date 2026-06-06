using System.Collections.Generic;
using Fusion;
using UnityEngine;
using EchoRealm.Networking; // FusionNetworkManager

namespace EchoRealm.Sandbox
{
    /// <summary>Spawns/clears balls on the LOCAL device. In Shared Mode any client may spawn and
    /// owns what it spawns. Caps live count (evicts oldest). Decoupled from the film entirely.</summary>
    public class BallSpawner : MonoBehaviour
    {
        [Header("Prefab (NetworkObject + NetworkedBall)")]
        [SerializeField] private NetworkObject ballPrefab;

        [Header("Tuning")]
        [SerializeField] private int maxBalls = 5;
        [SerializeField] private float spawnForward = 0.5f; // metres in front of the head
        [SerializeField] private float spawnDown = 0.2f;    // metres below eye line (~chest)

        private readonly List<NetworkObject> _balls = new List<NetworkObject>();

        private NetworkRunner Runner =>
            FusionNetworkManager.Instance != null ? FusionNetworkManager.Instance.Runner : null;

        public void SpawnBall()
        {
            var runner = Runner;
            if (runner == null || !runner.IsRunning) { Debug.LogWarning("[BallSandbox] Runner not running — cannot spawn."); return; }
            if (ballPrefab == null) { Debug.LogError("[BallSandbox] ballPrefab not assigned."); return; }

            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[BallSandbox] No Camera.main — cannot place ball."); return; }

            Vector3 pos = SandboxMath.SpawnPositionInFront(
                cam.transform.position, cam.transform.rotation, spawnForward, spawnDown);

            _balls.RemoveAll(b => b == null);
            if (_balls.Count >= maxBalls)
            {
                var oldest = _balls[0];
                _balls.RemoveAt(0);
                if (oldest != null) runner.Despawn(oldest);
            }

            var ball = runner.Spawn(ballPrefab, pos, Quaternion.identity, runner.LocalPlayer);
            if (ball != null) { _balls.Add(ball); Debug.Log($"[BallSandbox] Spawned ball at {pos}. Live={_balls.Count}"); }
        }

        public void ClearBalls()
        {
            var runner = Runner;
            foreach (var b in _balls)
                if (b != null && runner != null) runner.Despawn(b); // only succeeds for balls we own
            _balls.Clear();
            Debug.Log("[BallSandbox] Cleared local balls.");
        }
    }
}
