using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class UnifiedBoStaffValidation
    {
        [MenuItem("Tools/Melee Combat/Validate unified Bo Staff prototype")]
        public static void Validate()
        {
            const string weaponPath = "Assets/MyAssets/prefabs/weapons/Bo Staff.prefab";
            const string npcPath = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab";
            const string arenaPath = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffUnifiedVRArena.unity";
            var weaponObject = AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
            var npcObject = AssetDatabase.LoadAssetAtPath<GameObject>(npcPath);
            if (!weaponObject || !npcObject) throw new InvalidOperationException("Unified prefabs are missing");
            var weapon = weaponObject.GetComponent<PhysicalMeleeWeapon>();
            var body = weaponObject.GetComponent<Rigidbody>();
            var grab = weaponObject.GetComponent<XRGrabInteractable>();
            if (!weapon || !body || !grab || !weapon.rightGrip || !weapon.leftGrip ||
                !weaponObject.GetComponentInChildren<Renderer>() || !weaponObject.GetComponent<CapsuleCollider>())
                throw new InvalidOperationException("Shared Bo Staff components are incomplete");
            if (body.isKinematic || !body.useGravity || grab.useDynamicAttach)
                throw new InvalidOperationException("World physics or static grip configuration is wrong");
            var equipment = npcObject.GetComponent<NpcGeneratedWeapon>();
            var brain = npcObject.GetComponent<StarvingBandit>();
            var grip = npcObject.GetComponentInChildren<MocapStaffPreview>(true);
            if (!equipment || !brain || !grip || equipment.weaponPrefab != weapon || !equipment.gripTarget ||
                npcObject.GetComponentInChildren<PhysicalMeleeWeapon>(true))
                throw new InvalidOperationException("Unified NPC must reference, not embed, the shared staff");
            var scene = EditorSceneManager.OpenScene(arenaPath);
            int armed = 0, unarmed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                var npc = root.GetComponent<NpcGeneratedWeapon>();
                if (!npc) continue;
                if (npc.weaponPrefab != weapon) throw new InvalidOperationException("Arena NPC uses another weapon prefab");
                if (npc.startEquipped) armed++; else unarmed++;
            }
            if (armed < 1 || unarmed < 1) throw new InvalidOperationException("Arena needs armed and unarmed NPCs");
            Debug.Log($"Unified Bo Staff validation passed: {armed} armed NPC, {unarmed} unarmed NPC, one shared prefab");
        }
    }
}
