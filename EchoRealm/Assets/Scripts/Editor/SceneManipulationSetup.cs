using System;
using UnityEditor;
using UnityEngine;

namespace EchoRealm.EditorTools
{
    /// <summary>
    /// Adds MRTK3 hand-manipulation so the two users can grab/move/rotate/scale props
    /// individually, and grab/move/scale the whole SceneRoot as one world.
    ///
    /// The MRTK ObjectManipulator is added via reflection so this Editor script always
    /// compiles, even if Assembly-CSharp(-Editor) doesn't directly reference the MRTK
    /// SpatialManipulation assembly. Colliders are plain UnityEngine.
    ///
    /// Menu:
    ///   EchoRealm ▸ Make Selected Objects Grabbable  — select prop roots first (NOT characters/story objects)
    ///   EchoRealm ▸ Setup Whole-Scene Manipulation    — adds grab/scale to SceneRoot
    /// </summary>
    public static class SceneManipulationSetup
    {
        private const string ObjectManipulatorType = "MixedReality.Toolkit.SpatialManipulation.ObjectManipulator";

        private static Type Resolve(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        [MenuItem("EchoRealm/Make Selected Objects Grabbable")]
        public static void MakeSelectedGrabbable()
        {
            var om = Resolve(ObjectManipulatorType);
            if (om == null)
            {
                Debug.LogError("[Manip] ObjectManipulator type not found — is the MRTK3 SpatialManipulation package present?");
                return;
            }
            var selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
            {
                Debug.LogWarning("[Manip] Select the prop GameObjects first (trees/rocks/etc.) — NOT the characters, Heart Stone, or effect controllers.");
                return;
            }

            int added = 0;
            foreach (var go in selection)
            {
                EnsureBoxCollider(go);
                if (go.GetComponent(om) == null)
                {
                    Undo.AddComponent(go, om);
                    added++;
                }
                EditorUtility.SetDirty(go);
            }
            Debug.Log($"[Manip] Made {added} object(s) grabbable (ObjectManipulator + collider). Grab them with hands to move/rotate/scale.");
        }

        [MenuItem("EchoRealm/Setup Whole-Scene Manipulation")]
        public static void SetupWholeScene()
        {
            var om = Resolve(ObjectManipulatorType);
            if (om == null)
            {
                Debug.LogError("[Manip] ObjectManipulator type not found — is the MRTK3 SpatialManipulation package present?");
                return;
            }
            var root = GameObject.Find("SceneRoot");
            if (root == null)
            {
                Debug.LogError("[Manip] No GameObject named 'SceneRoot' in the scene.");
                return;
            }

            // A central 'world-grab' box: grabbing open ground here moves & scales the whole
            // SceneRoot. Resize this BoxCollider in the Inspector to cover the zone you want.
            if (root.GetComponent<BoxCollider>() == null)
            {
                var bc = Undo.AddComponent<BoxCollider>(root);
                bc.center = new Vector3(0f, 0.5f, 0f);
                bc.size = new Vector3(2f, 1f, 2f);
            }
            if (root.GetComponent(om) == null)
                Undo.AddComponent(root, om);

            EditorUtility.SetDirty(root);
            Debug.Log("[Manip] SceneRoot is now grab/move/scale-able as a whole. Resize its BoxCollider to taste; GestureManager clamps the scale to 0.1–3x.");
        }

        /// <summary>Adds a BoxCollider fitted to the object's renderers, only if it has no collider.</summary>
        private static void EnsureBoxCollider(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>() != null) return;

            var renderers = go.GetComponentsInChildren<Renderer>();
            var bc = Undo.AddComponent<BoxCollider>(go);
            if (renderers.Length == 0) return; // leaves default 1x1x1

            Bounds world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);

            // World bounds → local (ignores rotation; close enough for a grab collider)
            Transform t = go.transform;
            Vector3 ls = t.lossyScale;
            bc.center = t.InverseTransformPoint(world.center);
            bc.size = new Vector3(
                Mathf.Abs(ls.x) < 1e-4f ? world.size.x : world.size.x / Mathf.Abs(ls.x),
                Mathf.Abs(ls.y) < 1e-4f ? world.size.y : world.size.y / Mathf.Abs(ls.y),
                Mathf.Abs(ls.z) < 1e-4f ? world.size.z : world.size.z / Mathf.Abs(ls.z));
        }
    }
}
