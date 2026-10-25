using System;
using ScavengerNPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class CharacterAttributeSetupBuilder
    {
        const string Directory = "Assets/MeleeCombat/Profiles";
        const string Npc = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab";
        const string DisarmArena = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffDisarmArena.unity";
        public const string StatArena = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffStatCombatArena.unity";
        const string VrArena = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffUnifiedVRArena.unity";

        [MenuItem("Zandea/Melee Combat/Set up character attribute profiles")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Directory)) AssetDatabase.CreateFolder("Assets/MeleeCombat", "Profiles");
            var weak = Profile("Weak Bandit", new CharacterAttributeValues {
                strength = 15, dexterity = 20, meleeAttack = 15, meleeDefense = 15,
                toughness = 15, staffProficiency = 10 });
            var average = Profile("Average Fighter", CharacterAttributeValues.Average);
            var veteran = Profile("Veteran Warrior", new CharacterAttributeValues {
                strength = 75, dexterity = 70, meleeAttack = 80, meleeDefense = 75,
                toughness = 70, staffProficiency = 80 });
            const string sharedWeapon = "Assets/MyAssets/prefabs/weapons/Bo Staff.prefab";
            var staff = PrefabUtility.LoadPrefabContents(sharedWeapon);
            try
            {
                var weapon = staff.GetComponent<PhysicalMeleeWeapon>();
                if (!weapon) throw new InvalidOperationException("Shared staff weapon component missing");
                weapon.proficiencyClass = PhysicalMeleeWeapon.WeaponProficiencyClass.Staff;
                PrefabUtility.SaveAsPrefabAsset(staff, sharedWeapon);
            }
            finally { PrefabUtility.UnloadPrefabContents(staff); }
            var npc = PrefabUtility.LoadPrefabContents(Npc);
            try
            {
                var attributes = npc.GetComponent<CharacterAttributes>();
                if (!attributes) attributes = npc.AddComponent<CharacterAttributes>();
                attributes.profile = average;
                PrefabUtility.SaveAsPrefabAsset(npc, Npc);
            }
            finally { PrefabUtility.UnloadPrefabContents(npc); }
            var scene = EditorSceneManager.OpenScene(DisarmArena);
            var telemetry = UnityEngine.Object.FindFirstObjectByType<BanditArenaTelemetry>();
            if (!telemetry || !telemetry.left || !telemetry.right)
                throw new InvalidOperationException("Disarm arena needs two NPCs");
            var scenario = UnityEngine.Object.FindFirstObjectByType<CharacterStatArenaScenario>();
            if (!scenario) scenario = new GameObject("Character stat matchup").AddComponent<CharacterStatArenaScenario>();
            scenario.weakBandit = weak;
            scenario.averageFighter = average;
            scenario.veteranWarrior = veteran;
            scenario.left = telemetry.left;
            scenario.right = telemetry.right;
            EditorSceneManager.SaveScene(scene, StatArena);
            scene = EditorSceneManager.OpenScene(VrArena);
            var player = UnityEngine.Object.FindFirstObjectByType<ScavengerPlayerHealth>();
            if (player)
            {
                var attributes = player.GetComponent<CharacterAttributes>();
                if (!attributes) attributes = player.gameObject.AddComponent<CharacterAttributes>();
                attributes.profile = average;
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Character profiles configured for unified NPC, player, and disarm arena");
        }

        static CharacterAttributeProfile Profile(string name, CharacterAttributeValues values)
        {
            var path = Directory + "/" + name + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAttributeProfile>(path);
            if (profile) return profile; // Preserve later Inspector tuning.
            profile = ScriptableObject.CreateInstance<CharacterAttributeProfile>();
            profile.values = values;
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }
    }
}
