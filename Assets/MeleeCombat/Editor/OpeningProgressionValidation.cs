using System;
using System.IO;
using System.Linq;
using ScavengerNPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zandea.Opening;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class OpeningProgressionValidation
    {
        const string Main = "Assets/Main.unity";
        const string Test = "Assets/Scenes/Zandea/OpeningCaptureTest.unity";

        // Batch validation only: it opens scenes and deliberately does not save them.
        public static void Run()
        {
            var average = AssetDatabase.LoadAssetAtPath<CharacterAttributeProfile>("Assets/MeleeCombat/Profiles/Average Fighter.asset");
            Check(average && average.values.Get(CharacterAttribute.Endurance) == 50,
                "Legacy seven-attribute profile lost neutral Endurance");
            var unrelated = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab");
            Check(unrelated && unrelated.GetComponent<CharacterAttributes>() &&
                unrelated.GetComponent<CharacterAttributes>().profile == average,
                "Shared NPC prefab profile changed");
            CheckScene(Main, true);
            CheckScene(Test, false);
            CheckSaveRoundTrip();
            Debug.Log("OPENING_PROGRESSION_VALIDATION_PASS");
        }

        static void CheckScene(string path, bool persistent)
        {
            EditorSceneManager.OpenScene(path);
            var director = UnityEngine.Object.FindObjectsByType<OpeningDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(d => d.gameObject.scene.path == path);
            Check(director.kaelProfile && director.leaderProfile && director.ordinarySlaverProfile,
                path + " has missing profiles");
            Check(director.persistPlayerProgress == persistent, path + " persistence mode incorrect");
            Check(director.slavers.Length == 3, path + " does not have three slavers");
            director.ApplyOpeningProfiles();
            director.ApplyOpeningProfiles();
            var player = director.player.GetComponent<CharacterAttributes>();
            Check(player && player.Get(CharacterAttribute.Strength) == 8 &&
                player.Get(CharacterAttribute.Toughness) == 10 && player.Get(CharacterAttribute.Endurance) == 10 &&
                player.Get(CharacterAttribute.MartialArts) == 5, path + " Kael stats incorrect");
            Check(director.player.GetComponents<CharacterAttributes>().Length == 1 &&
                director.player.GetComponents<CharacterCombatProgression>().Length == 1,
                path + " duplicates player attributes or progression");
            for (int i = 0; i < 3; i++)
            {
                var slaver = director.slavers[i];
                var attributes = slaver.GetComponent<CharacterAttributes>();
                var profile = i == 0 ? director.leaderProfile : director.ordinarySlaverProfile;
                Check(attributes && attributes.profile == profile &&
                    slaver.GetComponents<CharacterAttributes>().Length == 1 &&
                    slaver.GetComponents<CharacterCombatProgression>().Length == 1,
                    path + " slaver " + i + " profile or component count incorrect");
                Check(attributes.Get(CharacterAttribute.Strength) == (i == 0 ? 30 : 22) &&
                    attributes.Get(CharacterAttribute.Toughness) == (i == 0 ? 32 : 25) &&
                    attributes.Get(CharacterAttribute.Endurance) == (i == 0 ? 30 : 25) &&
                    attributes.Get(CharacterAttribute.MartialArts) == (i == 0 ? 24 : 16),
                    path + " slaver " + i + " values incorrect");
            }
            var ordinary = director.slavers[1];
            var ordinaryStats = ordinary.GetComponent<CharacterAttributes>();
            var ordinaryHealth = ordinary.GetComponent<ScavengerHealth>();
            var playerHealth = director.playerHealth;
            float bestPunch = 16f * CharacterCombatMath.UnarmedDamage(player) * CharacterCombatMath.DamageTaken(ordinaryStats);
            float slaverThreshold = ordinaryHealth.knockoutTrauma *
                CharacterCombatMath.EnduranceTraumaCapacity(ordinaryStats, ordinaryHealth.enduranceTraumaScale);
            Check(2f * bestPunch * director.slaverUnarmedTraumaMultiplier < slaverThreshold,
                path + " two maximum torso punches reach ordinary slaver trauma threshold");
            float staffHit = director.openingStaffBaseDamage *
                CharacterCombatMath.DamageDealt(ordinaryStats, PhysicalMeleeWeapon.WeaponProficiencyClass.Staff) *
                CharacterCombatMath.DamageTaken(player) * playerHealth.bluntSubdualMultiplier;
            float kaelThreshold = playerHealth.knockoutTrauma *
                CharacterCombatMath.EnduranceTraumaCapacity(player, playerHealth.enduranceTraumaScale);
            Check(3f * staffHit >= kaelThreshold,
                path + " three clean staff hits fall short of Kael trauma threshold");
            Debug.Log($"OPENING_BALANCE_CALC {path} punch={bestPunch:F2} slaverTraumaThreshold={slaverThreshold:F2} " +
                $"staff={staffHit:F2} kaelTraumaThreshold={kaelThreshold:F2}");
        }

        static void CheckSaveRoundTrip()
        {
            var root = new GameObject("progression validation temporary");
            string path = null;
            try
            {
                var attributes = root.AddComponent<CharacterAttributes>();
                attributes.ResetToDefaults();
                var progress = root.AddComponent<CharacterCombatProgression>();
                progress.attributes = attributes;
                progress.persistentCharacterId = "zandea-validation-" + Guid.NewGuid().ToString("N");
                path = progress.SavePath;
                attributes.SetRuntime(CharacterAttribute.Strength, 47);
                progress.RecordCarryingTraining(8f);
                CharacterCombatProgression.RecordPunchAttempt(root.transform);
                progress.Save();
                attributes.SetRuntime(CharacterAttribute.Strength, 3);
                Check(progress.Load() && attributes.Get(CharacterAttribute.Strength) == 47 &&
                    progress.LifetimeExperience(CharacterAttribute.Strength) > 0f &&
                    progress.Lifetime.punchesAttempted == 1,
                    "Character progression save/load round trip failed");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (path != null && File.Exists(path)) File.Delete(path);
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
