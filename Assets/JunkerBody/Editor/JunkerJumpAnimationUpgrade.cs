using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace JunkerBody.Editor
{
    [InitializeOnLoad]
    static class JunkerJumpAnimationUpgrade
    {
        const string ControllerPath="Assets/JunkerBody/JunkerLocomotion.controller";
        const string ClipPath="Assets/MeleeCombat/ImportedAnimations/ProMeleeAxePack/unarmed jump running.fbx";
        static JunkerJumpAnimationUpgrade()=>EditorApplication.delayCall+=Upgrade;
        static void Upgrade()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);if(!controller)return;
            if(!controller.parameters.Any(p=>p.name=="Jumping"))controller.AddParameter("Jumping",AnimatorControllerParameterType.Bool);
            var machine=controller.layers[0].stateMachine;var jump=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Jump");
            if(!jump)jump=machine.AddState("Jump",new Vector3(420,100,0));
            var clip=AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview__"));if(clip)jump.motion=clip;
            if(!machine.anyStateTransitions.Any(t=>t.destinationState==jump))
            {var enter=machine.AddAnyStateTransition(jump);enter.hasExitTime=false;enter.duration=.08f;enter.AddCondition(AnimatorConditionMode.If,0,"Jumping");}
            if(!jump.transitions.Any(t=>t.destinationState==machine.defaultState))
            {var exit=jump.AddTransition(machine.defaultState);exit.hasExitTime=true;exit.exitTime=.8f;exit.duration=.12f;exit.AddCondition(AnimatorConditionMode.IfNot,0,"Jumping");}
            EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
        }
    }
}
