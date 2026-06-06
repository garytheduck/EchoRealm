using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Fusion;
using EchoRealm.Sandbox;

namespace EchoRealm.SandboxEditor
{
    /// <summary>One-click setup for the ball sandbox: layers, collision matrix, physic materials,
    /// the ball + mesh-chunk prefabs, and the scene objects. Idempotent — safe to re-run.</summary>
    public static class BallSandboxSetup
    {
        private const string MatDir = "Assets/Sandbox";
        private const string BallPrefabPath = "Assets/Sandbox/Ball.prefab";
        private const string ChunkPrefabPath = "Assets/Sandbox/SpatialMeshChunk.prefab";

        [MenuItem("EchoRealm/Ball Sandbox/Setup Scene")]
        public static void Setup()
        {
            EnsureLayer("SpatialMesh");
            EnsureLayer("BallSandbox");
            int ball = LayerMask.NameToLayer("BallSandbox");
            int mesh = LayerMask.NameToLayer("SpatialMesh");

            // Collision matrix: BallSandbox collides ONLY with SpatialMesh.
            for (int i = 0; i < 32; i++)
                Physics.IgnoreLayerCollision(ball, i, true);       // ignore everything…
            Physics.IgnoreLayerCollision(ball, mesh, false);       // …except the spatial mesh.

            EnsureDir(MatDir);
            var bouncy = EnsurePhysicMaterial("BallBouncy", 0.7f, 0.2f, PhysicMaterialCombine.Maximum, PhysicMaterialCombine.Minimum);
            var lowFric = EnsurePhysicMaterial("MeshLowFriction", 0f, 0.2f, PhysicMaterialCombine.Average, PhysicMaterialCombine.Minimum);

            var chunkPrefab = EnsureChunkPrefab(mesh, lowFric);
            var ballPrefab = EnsureBallPrefab(ball, bouncy);

            EnsureSceneObjects(ballPrefab, chunkPrefab, lowFric);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[BallSandbox] Setup complete. Now do the two MANUAL steps in the plan (ObjectManipulator events + OpenXR mesh feature).");
        }

        // ---- layers ----
        private static void EnsureLayer(string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return; // already present
            for (int i = 8; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue)) { el.stringValue = name; tagManager.ApplyModifiedProperties(); return; }
            }
            Debug.LogError($"[BallSandbox] No free user layer slot for '{name}'.");
        }

        // ---- physic materials ----
        private static PhysicMaterial EnsurePhysicMaterial(string name, float bounce, float dynFric,
            PhysicMaterialCombine bounceCombine, PhysicMaterialCombine frictionCombine)
        {
            string path = $"{MatDir}/{name}.physicMaterial";
            var m = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path);
            if (m == null) { m = new PhysicMaterial(name); AssetDatabase.CreateAsset(m, path); }
            m.bounciness = bounce;
            m.dynamicFriction = dynFric;
            m.staticFriction = dynFric;
            m.bounceCombine = bounceCombine;
            m.frictionCombine = frictionCombine;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---- mesh chunk prefab (ARMeshManager.meshPrefab) ----
        private static GameObject EnsureChunkPrefab(int meshLayer, PhysicMaterial mat)
        {
            var go = new GameObject("SpatialMeshChunk", typeof(MeshFilter), typeof(MeshCollider));
            go.layer = meshLayer;
            go.GetComponent<MeshCollider>().material = mat;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, ChunkPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---- ball prefab ----
        private static GameObject EnsureBallPrefab(int ballLayer, PhysicMaterial mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); // sphere mesh + SphereCollider
            go.name = "Ball";
            go.layer = ballLayer;
            go.transform.localScale = Vector3.one * 0.12f;
            go.GetComponent<SphereCollider>().material = mat;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkedBall>();
            // NOTE: MRTK ObjectManipulator is added + its events wired in the MANUAL step below
            // (its API/event names vary by MRTK3 version; wiring 2 events by hand is the test-cube convention).

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, BallPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---- scene objects ----
        private static void EnsureSceneObjects(GameObject ballPrefab, GameObject chunkPrefab, PhysicMaterial meshMat)
        {
            var root = GameObject.Find("BallSandbox") ?? new GameObject("BallSandbox");

            var spawner = root.GetComponent<BallSpawner>() ?? root.AddComponent<BallSpawner>();
            SetPrivate(spawner, "ballPrefab", ballPrefab.GetComponent<NetworkObject>());

            var hook = root.GetComponent<BallVoiceHook>() ?? root.AddComponent<BallVoiceHook>();
            SetPrivate(hook, "spawner", spawner);

            // Spatial mesh holder. ARMeshManager is added in the MANUAL step (must sit under the XR Origin).
            var meshGo = GameObject.Find("SpatialMesh") ?? new GameObject("SpatialMesh");
            var smm = meshGo.GetComponent<SpatialMeshManager>() ?? meshGo.AddComponent<SpatialMeshManager>();
            SetPrivate(smm, "meshMaterial", meshMat);
        }

        // ---- helpers ----
        private static void EnsureDir(string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Sandbox");
        }

        private static void SetPrivate(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop != null) { prop.objectReferenceValue = value; so.ApplyModifiedProperties(); }
            else Debug.LogWarning($"[BallSandbox] Field '{field}' not found on {target.GetType().Name}.");
        }
    }
}
