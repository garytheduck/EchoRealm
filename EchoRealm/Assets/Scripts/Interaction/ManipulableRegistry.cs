using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit.SpatialManipulation;

namespace EchoRealm.Interaction
{
    /// <summary>
    /// Discovers the scene's manipulable props and gives each a stable id shared across devices.
    /// A prop counts as manipulable if it already has an ObjectManipulator (the grabbable props you
    /// set up) under SceneRoot, and is NOT in the protected set (Oracle, Astronaut, HeartStone,
    /// portal, trials). Id = hierarchy path under SceneRoot — deterministic and identical on every
    /// device since they load the same scene. Resolves a gazed GameObject to its ManipulableObject.
    ///
    /// Attach to a persistent object (GameManager) or SceneRoot; assign SceneRoot + Protected Objects.
    /// </summary>
    public class ManipulableRegistry : MonoBehaviour
    {
        public static ManipulableRegistry Instance { get; private set; }

        [SerializeField] private Transform sceneRoot;

        [Tooltip("Subtrees that must NOT be voice-manipulable: Oracle, Astronaut, HeartStone, portal, trials.")]
        [SerializeField] private Transform[] protectedObjects;

        [SerializeField] private bool logEvents = true;

        private readonly Dictionary<string, ManipulableObject> _byId = new Dictionary<string, ManipulableObject>();

        private void Awake() { Instance = this; }

        private void Start()
        {
            if (sceneRoot == null && EchoRealm.Networking.QRAnchorManager.Instance != null)
                sceneRoot = EchoRealm.Networking.QRAnchorManager.Instance.SceneRoot;
            if (sceneRoot == null)
            {
                Debug.LogError("[Manip] No SceneRoot assigned — cannot register manipulable objects.");
                return;
            }

            foreach (var om in sceneRoot.GetComponentsInChildren<ObjectManipulator>(true))
            {
                Transform t = om.transform;
                if (t == sceneRoot) continue;     // that's the whole-world grab, not a prop
                if (IsProtected(t)) continue;

                var mo = t.GetComponent<ManipulableObject>();
                if (mo == null) mo = t.gameObject.AddComponent<ManipulableObject>();
                mo.Id = PathUnder(sceneRoot, t);
                mo.Kind = InferKind(t.name);
                _byId[mo.Id] = mo;
            }

            if (logEvents) Debug.Log($"[Manip] Registered {_byId.Count} manipulable props under '{sceneRoot.name}'.");
        }

        /// <summary>Find a registered object by its stable id.</summary>
        public ManipulableObject FindById(string id)
            => (id != null && _byId.TryGetValue(id, out var mo)) ? mo : null;

        /// <summary>All registered manipulable props (read-only). Used by replay to reset
        /// every prop to its baseline. Additive — existing lookups are unchanged.</summary>
        public System.Collections.Generic.IEnumerable<ManipulableObject> All => _byId.Values;

        /// <summary>Walk up from a gazed GameObject to its ManipulableObject (null if none).</summary>
        public ManipulableObject Resolve(GameObject gazed)
        {
            for (Transform t = gazed != null ? gazed.transform : null; t != null; t = t.parent)
            {
                var mo = t.GetComponent<ManipulableObject>();
                if (mo != null) return mo;
            }
            return null;
        }

        private bool IsProtected(Transform t)
        {
            if (protectedObjects == null) return false;
            foreach (var p in protectedObjects)
                if (p != null && (t == p || t.IsChildOf(p))) return true;
            return false;
        }

        private static string PathUnder(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (Transform c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string InferKind(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("cloud")) return "cloud";
            if (n.Contains("shrub") || n.Contains("bush")) return "bush";
            if (n.Contains("rock")) return "rock";
            if (n.Contains("tree") || n.Contains("pine")) return "tree";
            if (n.Contains("flower")) return "flower";
            if (n.Contains("mushroom")) return "mushroom";
            return "object";
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
