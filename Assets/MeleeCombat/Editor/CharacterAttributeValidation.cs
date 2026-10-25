using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ScavengerNPC;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class CharacterAttributeValidation
    {
        [MenuItem("Zandea/Melee Combat/Validate character attributes")]
        public static void Validate()
        {
            var weak = AssetDatabase.LoadAssetAtPath<CharacterAttributeProfile>(
                "Assets/MeleeCombat/Profiles/Weak Bandit.asset");
            var average = AssetDatabase.LoadAssetAtPath<CharacterAttributeProfile>(
                "Assets/MeleeCombat/Profiles/Average Fighter.asset");
            var veteran = AssetDatabase.LoadAssetAtPath<CharacterAttributeProfile>(
                "Assets/MeleeCombat/Profiles/Veteran Warrior.asset");
            Check(weak && average && veteran, "profile assets exist");
            Check(weak.values.strength == 15 && weak.values.staffProficiency == 10 &&
                veteran.values.strength == 75 && veteran.values.meleeAttack == 80,
                "requested profile values");
            var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab");
            Check(npcPrefab && npcPrefab.GetComponent<CharacterAttributes>() &&
                npcPrefab.GetComponent<CharacterAttributes>().profile == average,
                "unified NPC has average profile by default");
            EditorSceneManager.OpenScene(CharacterAttributeSetupBuilder.StatArena);
            var scenario = UnityEngine.Object.FindFirstObjectByType<CharacterStatArenaScenario>();
            Check(scenario && scenario.weakBandit == weak && scenario.averageFighter == average &&
                scenario.veteranWarrior == veteran && scenario.left && scenario.right,
                "stat arena profile references");
            EditorSceneManager.OpenScene("Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffDisarmArena.unity");
            Check(!UnityEngine.Object.FindFirstObjectByType<CharacterStatArenaScenario>(),
                "original disarm arena remains separate");
            EditorSceneManager.OpenScene("Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffUnifiedVRArena.unity");
            var player = UnityEngine.Object.FindFirstObjectByType<ScavengerPlayerHealth>();
            Check(player && player.GetComponent<CharacterAttributes>() &&
                player.GetComponent<CharacterAttributes>().profile == average,
                "VR player has the shared attribute component");
            // Opening scenes may unload editor asset wrappers. Reload before the
            // isolated runtime-copy checks below.
            weak = AssetDatabase.LoadAssetAtPath<CharacterAttributeProfile>(
                "Assets/MeleeCombat/Profiles/Weak Bandit.asset");
            average = AssetDatabase.LoadAssetAtPath<CharacterAttributeProfile>(
                "Assets/MeleeCombat/Profiles/Average Fighter.asset");
            veteran = AssetDatabase.LoadAssetAtPath<CharacterAttributeProfile>(
                "Assets/MeleeCombat/Profiles/Veteran Warrior.asset");
            var oneObject = new GameObject("Attribute validation one");
            var twoObject = new GameObject("Attribute validation two");
            var weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MyAssets/prefabs/weapons/Bo Staff.prefab");
            var weaponOne = UnityEngine.Object.Instantiate(weaponPrefab);
            var weaponTwo = UnityEngine.Object.Instantiate(weaponPrefab);
            try
            {
                var one = oneObject.AddComponent<CharacterAttributes>();
                var two = twoObject.AddComponent<CharacterAttributes>();
                one.ApplyProfile(weak);
                two.ApplyProfile(weak);
                one.SetRuntime(CharacterAttribute.Strength, 99);
                Check(one.Get(CharacterAttribute.Strength) == 99 &&
                    two.Get(CharacterAttribute.Strength) == 15 && weak.values.strength == 15,
                    "runtime values do not mutate profile or other characters");
                one.ApplyProfile(average);
                two.ApplyProfile(average);
                var firstWeapon = weaponOne.GetComponent<PhysicalMeleeWeapon>();
                var secondWeapon = weaponTwo.GetComponent<PhysicalMeleeWeapon>();
                Check(!firstWeapon.useAttributesForPlayerHandling && firstWeapon.useAttributesForNpcHandling,
                    "player opt-in and NPC handling defaults");
                Check(firstWeapon.proficiencyClass == PhysicalMeleeWeapon.WeaponProficiencyClass.Staff,
                    "shared staff proficiency class");
                float equal = CharacterCombatMath.DisarmChance(.05f, one, two, firstWeapon,
                    secondWeapon, 2.5f, 1.5f, .005f, .15f);
                one.ApplyProfile(weak);
                two.ApplyProfile(veteran);
                float weakAttack = CharacterCombatMath.DisarmChance(.05f, one, two, firstWeapon,
                    secondWeapon, 2.5f, 1.5f, .005f, .15f);
                float veteranAttack = CharacterCombatMath.DisarmChance(.05f, two, one, firstWeapon,
                    secondWeapon, 2.5f, 1.5f, .005f, .15f);
                Check(Mathf.Abs(equal - .05f) < .0001f && weakAttack < equal &&
                    veteranAttack > equal && veteranAttack <= .15f,
                    "baseline and asymmetric disarm resistance");
                Check(CharacterCombatMath.AttackSpeed(one, firstWeapon.proficiencyClass) < 1f &&
                    CharacterCombatMath.AttackSpeed(two, firstWeapon.proficiencyClass) > 1f &&
                    CharacterCombatMath.WeaponControl(one, 3f, firstWeapon.proficiencyClass) <
                    CharacterCombatMath.WeaponControl(two, 3f, firstWeapon.proficiencyClass) &&
                    CharacterCombatMath.DamageTaken(one) > CharacterCombatMath.DamageTaken(two),
                    "attack speed, heavy weapon control, and toughness scaling");
                Check(CharacterCombatMath.AttackSpeed(one,
                    PhysicalMeleeWeapon.WeaponProficiencyClass.None) >
                    CharacterCombatMath.AttackSpeed(one, firstWeapon.proficiencyClass),
                    "staff proficiency does not affect unrelated weapon classes");
                Debug.Log($"ATTRIBUTE_VALIDATION_PASS equalDisarm={equal:F3} weakVsVeteran={weakAttack:F3} veteranVsWeak={veteranAttack:F3} weakSpeed={CharacterCombatMath.AttackSpeed(one, firstWeapon.proficiencyClass):F2} veteranSpeed={CharacterCombatMath.AttackSpeed(two, firstWeapon.proficiencyClass):F2}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(oneObject);
                UnityEngine.Object.DestroyImmediate(twoObject);
                UnityEngine.Object.DestroyImmediate(weaponOne);
                UnityEngine.Object.DestroyImmediate(weaponTwo);
            }
        }
        static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
        }
    }
}
