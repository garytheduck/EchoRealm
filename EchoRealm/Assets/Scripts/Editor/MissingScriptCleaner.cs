using UnityEditor;
using UnityEngine;

namespace EchoRealm.EditorTools
{
    /// <summary>
    /// Removes all "missing script" components from the selected GameObject(s) AND their children.
    /// Use in Prefab Mode (select the root) to clean leftover demo-script references that block saving.
    /// Menu: EchoRealm ▸ Remove Missing Scripts (selection + children)
    /// </summary>
    public static class MissingScriptCleaner
    {
        [MenuItem("EchoRealm/Remove Missing Scripts (selection + children)")]
        public static void Clean()
        {
            if (Selection.gameObjects.Length == 0)
            {
                Debug.LogWarning("[MissingScriptCleaner] Select a GameObject (or a prefab root in Prefab Mode) first.");
                return;
            }

            int total = 0;
            foreach (var root in Selection.gameObjects)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (removed > 0)
                    {
                        total += removed;
                        Debug.Log($"[MissingScriptCleaner] Removed {removed} missing script(s) from '{t.name}'.");
                        EditorUtility.SetDirty(t.gameObject);
                    }
                }
            }
            Debug.Log($"[MissingScriptCleaner] Done — removed {total} missing script(s) total. Now save (Ctrl+S).");
        }
    }
}
