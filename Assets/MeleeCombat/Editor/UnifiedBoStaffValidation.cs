using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class UnifiedBoStaffValidation
    {
        [MenuItem("Zandea/Melee Combat/Validate unified Bo Staff prototype")]
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
            var shaft = weaponObject.GetComponent<CapsuleCollider>();
            if (shaft.direction != 1 || shaft.radius < .03f ||
                !weaponObject.transform.Find("PositiveStrikingTip") ||
                !weaponObject.transform.Find("NegativeStrikingTip"))
                throw new InvalidOperationException("Physical shaft collider or striking tips are misconfigured");
            bool hasMesh = false;
            foreach (var filter in weaponObject.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh) { hasMesh = true; break; }
            if (!hasMesh) throw new InvalidOperationException("Shared Bo Staff mesh is missing");
            if (MissingScripts(weaponObject)) throw new InvalidOperationException("Shared Bo Staff has missing scripts");
            var equipment = npcObject.GetComponent<NpcGeneratedWeapon>();
            var brain = npcObject.GetComponent<StarvingBandit>();
            var grip = npcObject.GetComponentInChildren<MocapStaffPreview>(true);
            if (!equipment || !brain || !grip || equipment.weaponPrefab != weapon || !equipment.gripTarget ||
                npcObject.GetComponentInChildren<PhysicalMeleeWeapon>(true))
                throw new InvalidOperationException("Unified NPC must reference, not embed, the shared staff");
            if (MissingScripts(npcObject)) throw new InvalidOperationException("Unified NPC has missing scripts");
            var scene = EditorSceneManager.OpenScene(arenaPath);
            int armed = 0, unarmed = 0, players = 0, damageDrivers = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (MissingScripts(root)) throw new InvalidOperationException("VR arena has missing scripts");
                if (root.GetComponentInChildren<XROrigin>(true)) players++;
                var npc = root.GetComponent<NpcGeneratedWeapon>();
                if (!npc) continue;
                if (npc.weaponPrefab != weapon) throw new InvalidOperationException("Arena NPC uses another weapon prefab");
                if (root.GetComponent<BanditDuelStrikeDamage>()) damageDrivers++;
                if (npc.startEquipped) armed++; else unarmed++;
            }
            if (armed < 1 || unarmed < 1 || players < 1 || damageDrivers < 2)
                throw new InvalidOperationException("VR arena needs a player, armed and unarmed NPCs, and strike damage drivers");
            Debug.Log($"Unified Bo Staff validation passed: {armed} armed NPC, {unarmed} unarmed NPC, {players} player rig, one shared prefab");
        }

        static bool MissingScripts(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0) return true;
            return false;
        }
    }
}
