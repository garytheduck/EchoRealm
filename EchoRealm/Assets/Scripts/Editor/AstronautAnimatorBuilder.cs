using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Linq;

namespace EchoRealm.EditorTools
{
    /// <summary>
    /// Builds an Animator Controller for the Generic Stylized Astronaut, mapping the
    /// trigger names AstronautController uses onto the FBX's bundled clips.
    /// Menu: EchoRealm ▸ Build Astronaut Animator
    /// </summary>
    public static class AstronautAnimatorBuilder
    {
        private const string FbxPath = "Assets/Stylized_Astronaut/Character/Astronaut.fbx";
        private const string OutPath = "Assets/EchoRealm/Animators/AstronautAnimator.controller";

        // trigger name -> clip name inside the FBX
        private static readonly (string trigger, string clip)[] Map =
        {
            ("Walk",        "Walk"),
            ("Jump",        "Jump_start"),
            ("LookAround",  "Suprise"),   // FBX spelling is "Suprise"
            ("Wave",        "Suprise"),   // no wave clip; reuse Surprise
            ("EnterPortal", "Float"),
        };
        private const string IdleClip = "Idle";

        [MenuItem("EchoRealm/Build Astronaut Animator")]
        public static void Build()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
                .OfType<AnimationClip>()
                .GroupBy(c => c.name).Select(g => g.First())
                .ToDictionary(c => c.name, c => c);

            if (clips.Count == 0)
            {
                Debug.LogError($"[AnimatorBuilder] No clips found at {FbxPath}. Check the path/import.");
                return;
            }

            System.IO.Directory.CreateDirectory("Assets/EchoRealm/Animators");
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(OutPath);
            var sm = ctrl.layers[0].stateMachine;

            // Idle = default state
            AnimatorState idle = sm.AddState("Idle");
            if (clips.TryGetValue(IdleClip, out var idleClip)) idle.motion = idleClip;
            sm.defaultState = idle;

            foreach (var (trigger, clipName) in Map)
            {
                ctrl.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
                if (!clips.TryGetValue(clipName, out var clip))
                {
                    Debug.LogWarning($"[AnimatorBuilder] Clip '{clipName}' not found for trigger '{trigger}'.");
                    continue;
                }
                var state = sm.AddState(trigger);
                state.motion = clip;

                var toState = sm.AddAnyStateTransition(state);
                toState.AddCondition(AnimatorConditionMode.If, 0, trigger);
                toState.duration = 0.15f;
                toState.canTransitionToSelf = false;

                var back = state.AddTransition(idle);
                back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.2f;
            }

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimatorBuilder] Built {OutPath} with {Map.Length} triggers + Idle.");
            Selection.activeObject = ctrl;
        }
    }
}
