using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using ScavengerNPC;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class UnifiedBoStaffBuilder
    {
        const string SourceNpc = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff.prefab";
        const string SourceArena = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffFullCombatArena.unity";
        const string SharedWeapon = "Assets/MyAssets/prefabs/weapons/Bo Staff.prefab";
        const string SharedNpc = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab";
        const string SharedArena = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffUnifiedCombatArena.unity";

        static UnifiedBoStaffBuilder()
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(SharedWeapon))
                EditorApplication.delayCall += BuildOnce;
        }

        static void BuildOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.IsAssetImportWorkerProcess() ||
                AssetDatabase.LoadAssetAtPath<GameObject>(SharedWeapon)) return;
            try { Build(); }
            catch (Exception error) { Debug.LogError("Unified Bo Staff build failed: " + error); }
        }

        [MenuItem("Tools/Melee Combat/Build unified Bo Staff prototype")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceNpc);
            if (!source) throw new InvalidOperationException("Source Bo Staff NPC prefab missing");
            var preview = source.GetComponentInChildren<MocapStaffPreview>(true);
            var embedded = source.GetComponentInChildren<PhysicalMeleeWeapon>(true);
            if (!preview || !embedded) throw new InvalidOperationException("Source staff and grip data missing");

            var weaponObject = UnityEngine.Object.Instantiate(embedded.gameObject);
            weaponObject.name = "Bo Staff";
            weaponObject.transform.SetParent(null);
            weaponObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var weapon = weaponObject.GetComponent<PhysicalMeleeWeapon>();
                weapon.npcGripTarget = null;
                weapon.ownerRoot = null;
                weapon.weaponId = "bo-staff";
                var right = Marker(weaponObject.transform, "Primary Grip Right", preview.rightGripLocal, preview.rightHandFromStaff);
                var left = Marker(weaponObject.transform, "Primary Grip Left", preview.leftGripLocal, preview.leftHandFromStaff);
                weapon.rightGrip = right;
                weapon.leftGrip = left;
                weapon.secondaryGrip = left;
                weapon.grip = right;
                var body = weaponObject.GetComponent<Rigidbody>();
                body.isKinematic = false;
                body.useGravity = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var grab = weaponObject.GetComponent<XRGrabInteractable>();
                if (!grab) grab = weaponObject.AddComponent<XRGrabInteractable>();
                grab.attachTransform = right;
                grab.useDynamicAttach = false;
                grab.throwOnDetach = false;
                grab.trackPosition = grab.trackRotation = false;
                PrefabUtility.SaveAsPrefabAsset(weaponObject, SharedWeapon);
            }
            finally { UnityEngine.Object.DestroyImmediate(weaponObject); }

            var shared = AssetDatabase.LoadAssetAtPath<GameObject>(SharedWeapon).GetComponent<PhysicalMeleeWeapon>();
            var npcObject = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(npcObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var oldWeapon = npcObject.GetComponentInChildren<PhysicalMeleeWeapon>(true);
                if (oldWeapon) UnityEngine.Object.DestroyImmediate(oldWeapon.gameObject);
                var brain = npcObject.GetComponent<StarvingBandit>();
                var grip = npcObject.GetComponentInChildren<MocapStaffPreview>(true);
                brain.staff = null;
                brain.positiveStrikingTip = null;
                brain.negativeStrikingTip = null;
                grip.staff = null;
                grip.positiveTip = null;
                grip.negativeTip = null;
                grip.driveVisualStaff = false;
                var motion = npcObject.GetComponent<BanditRigMotion>();
                if (motion) motion.staff = null;
                var equipment = npcObject.AddComponent<NpcGeneratedWeapon>();
                equipment.weaponPrefab = shared;
                equipment.gripTarget = brain.weaponGoal;
                equipment.bandit = brain;
                equipment.health = npcObject.GetComponent<ScavengerHealth>();
                npcObject.name = "StarvingBandit_BoStaff_Unified";
                PrefabUtility.SaveAsPrefabAsset(npcObject, SharedNpc);
            }
            finally { UnityEngine.Object.DestroyImmediate(npcObject); }

            var arena = EditorSceneManager.OpenScene(SourceArena);
            var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SharedNpc);
            int replaced = 0;
            foreach (var root in arena.GetRootGameObjects())
            {
                var old = root.GetComponent<StarvingBandit>();
                if (!old || !root.GetComponentInChildren<PhysicalMeleeWeapon>(true)) continue;
                var replacement = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab, arena);
                replacement.transform.SetPositionAndRotation(root.transform.position, root.transform.rotation);
                replacement.transform.localScale = root.transform.localScale;
                replacement.GetComponent<StarvingBandit>().target = old.target;
                replacement.GetComponent<NpcGeneratedWeapon>().startEquipped = replaced == 0;
                UnityEngine.Object.DestroyImmediate(root);
                replaced++;
            }
            if (replaced == 0) throw new InvalidOperationException("No Bo Staff NPCs found in source arena");
            EditorSceneManager.SaveScene(arena, SharedArena);
            AssetDatabase.SaveAssets();
            Debug.Log($"Unified Bo Staff: {SharedWeapon}, {SharedNpc}, {SharedArena}; NPCs: {replaced}");
        }

        static Transform Marker(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.localPosition = position;
            marker.localRotation = rotation;
            return marker;
        }
    }
}
