using System;
using System.Linq;
using JunkerEquipment;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Zandea.FriendlyNPC.Editor
{
    // Copies only merchant interaction pieces from the reviewed V2 player.
    public static class PromoteMerchantPlayer
    {
        const string Source = "Assets/CharacterCreator/Player/VR player - Man Creator.prefab";
        const string V1 = "Assets/CharacterCreator/Player/Versions/VR player - Man Creator V1.prefab";
        const string V2 = "Assets/CharacterCreator/Player/Versions/VR player - Man Creator V2.prefab";

        [MenuItem("Zandea/Friendly NPC/Merge merchant player setup into Man Creator")]
        public static void Merge()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before editing the player prefab.");
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(V1))
                throw new InvalidOperationException("The preserved V1 player prefab is missing.");
            var source = PrefabUtility.LoadPrefabContents(Source);
            var review = PrefabUtility.LoadPrefabContents(V2);
            try
            {
                var sourceBackpack = source.GetComponentInChildren<BackpackInventory>(true);
                var reviewUtility = review.GetComponentInChildren<BackpackUtilityAttachments>(true);
                if (!sourceBackpack || !reviewUtility || !reviewUtility.personalPursePrefab)
                    throw new InvalidOperationException("The backpack or reviewed external purse setup is missing.");
                var utility = sourceBackpack.GetComponent<BackpackUtilityAttachments>();
                if (!utility) utility = sourceBackpack.gameObject.AddComponent<BackpackUtilityAttachments>();
                EditorUtility.CopySerialized(reviewUtility, utility);
                // The anchors are created on Awake by the existing attachment component.
                utility.coinPurseAnchor = null;
                utility.mapAnchor = null;

                var reviewedChoices = review.GetComponentInChildren<PalmDialogueChoices>(true);
                if (!reviewedChoices || !reviewedChoices.panel ||
                    reviewedChoices.optionLabels == null || reviewedChoices.optionLabels.Length != 4)
                    throw new InvalidOperationException("The reviewed notebook is incomplete.");
                var choices = source.GetComponentInChildren<PalmDialogueChoices>(true);
                if (!choices)
                {
                    var notebook = UnityEngine.Object.Instantiate(reviewedChoices.gameObject, source.transform);
                    notebook.name = reviewedChoices.gameObject.name;
                    notebook.transform.localPosition = reviewedChoices.transform.localPosition;
                    notebook.transform.localRotation = reviewedChoices.transform.localRotation;
                    notebook.transform.localScale = reviewedChoices.transform.localScale;
                    choices = notebook.GetComponent<PalmDialogueChoices>();
                }
                choices.leftHand = MatchHand(source, reviewedChoices.leftHand);
                choices.rightHand = MatchHand(source, reviewedChoices.rightHand);
                choices.leftPalm = MatchTransform(source, reviewedChoices.leftPalm);
                choices.rightPalm = MatchTransform(source, reviewedChoices.rightPalm);
                if (!choices.leftHand || !choices.rightHand || !choices.leftPalm || !choices.rightPalm ||
                    !choices.panel || choices.optionTargets == null || choices.optionTargets.Length != 4)
                    throw new InvalidOperationException("The merged notebook has unresolved hand or choice references.");
                PrefabUtility.SaveAsPrefabAsset(source, Source);
                AssetDatabase.SaveAssets();
                Debug.Log("[Daran] Merchant notebook and external coin purse merged into the established Man Creator prefab. V1 and V2 remain available.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(review);
                PrefabUtility.UnloadPrefabContents(source);
            }
        }

        public static void MergeAndValidateBatch()
        {
            Merge();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Main.unity");
            Zandea.Missions.Editor.DaranMissionValidator.ValidateOpenMain();
        }

        static XRDirectInteractor MatchHand(GameObject root, XRDirectInteractor example)
        {
            if (!example) return null;
            return root.GetComponentsInChildren<XRDirectInteractor>(true)
                .FirstOrDefault(hand => hand.name == example.name);
        }

        static Transform MatchTransform(GameObject root, Transform example)
        {
            if (!example) return null;
            var parts = root.GetComponentsInChildren<Transform>(true);
            var match = parts.FirstOrDefault(part => part.name == example.name);
            if (match) return match;
            // V2's palm reference is on a glove that the established prefab
            // does not contain. Its authored palm anchor serves the same role.
            string palmName = example.name.StartsWith("Left", StringComparison.OrdinalIgnoreCase)
                ? "L_Palm" : example.name.StartsWith("Right", StringComparison.OrdinalIgnoreCase)
                    ? "R_Palm" : null;
            return palmName == null ? null : parts.FirstOrDefault(part => part.name == palmName);
        }
    }
}
