#if UNITY_EDITOR
using JunkerBody;
using JunkerEquipment;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    [InitializeOnLoad]
    public static class PlayerMartialSetup
    {
        const string Player = "Assets/MyAssets/prefabs/Player/VR player - JunkerWeapon.prefab";
        const string Controller = "Assets/JunkerBody/JunkerLocomotion.controller";
        const string Mask = "Assets/MeleeCombat/MartialArts/PlayerKickLegs.mask";
        const string Clips = "Assets/EEJANAI_Team/FreeFighterAnimations/Animations/";

        static PlayerMartialSetup()
        {
            EditorApplication.delayCall += ConfigureIfReady;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += ConfigureIfReady;
            };
        }

        static void ConfigureIfReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Configure();
        }

        [MenuItem("Kenshi VR/Configure Player Martial Arts")]
        public static void Configure()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            var front = AssetDatabase.LoadAssetAtPath<AnimationClip>(Clips + "low kick.anim");
            var roundhouse = AssetDatabase.LoadAssetAtPath<AnimationClip>(Clips + "cressent kick.anim");
            if (!controller || !front || !roundhouse) return;
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Mask);
            if (!mask)
            {
                mask = new AvatarMask();
                mask.name = "PlayerKickLegs";
                for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                    mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, false);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, true);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, true);
                AssetDatabase.CreateAsset(mask, Mask);
            }
            int index = -1;
            for (int i = 0; i < controller.layers.Length; i++)
                if (controller.layers[i].name == "Martial Kicks") { index = i; break; }
            if (index < 0)
            {
                controller.AddLayer("Martial Kicks");
                index = controller.layers.Length - 1;
                var machine = controller.layers[index].stateMachine;
                var idle = machine.AddState("KickIdle");
                machine.defaultState = idle;
                machine.AddState("FrontKick").motion = front;
                machine.AddState("RoundhouseKick").motion = roundhouse;
            }
            var layers = controller.layers;
            layers[index].avatarMask = mask;
            layers[index].defaultWeight = 0f;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Player);
            if (!source) return;
            var root = PrefabUtility.LoadPrefabContents(Player);
            try
            {
                bool changed = false;
                var body = root.GetComponentInChildren<VRBodyController>(true);
                foreach (var glove in root.GetComponentsInChildren<JunkerGloveHand>(true))
                {
                    var hand = glove.GetComponent<PlayerMartialHand>();
                    if (!hand) { hand = glove.gameObject.AddComponent<PlayerMartialHand>(); changed = true; }
                    hand.body = body;
                    hand.glove = glove;
                    hand.left = glove.left;
                    if (body)
                    {
                        var controllerTransform = glove.left ? body.leftController : body.rightController;
                        foreach (var interactor in body.xrOrigin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>(true))
                            if (controllerTransform && (interactor.transform == controllerTransform ||
                                interactor.transform.IsChildOf(controllerTransform)))
                            { hand.interactor = interactor; break; }
                    }
                }
                if (body)
                {
                    if (!body.GetComponent<PlayerMartialHandsLate>())
                    { body.gameObject.AddComponent<PlayerMartialHandsLate>(); changed = true; }
                    var kick = body.GetComponent<PlayerMartialKick>();
                    if (!kick) { kick = body.gameObject.AddComponent<PlayerMartialKick>(); changed = true; }
                    kick.body = body;
                    kick.frontClip = front;
                    kick.roundhouseClip = roundhouse;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, Player);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
