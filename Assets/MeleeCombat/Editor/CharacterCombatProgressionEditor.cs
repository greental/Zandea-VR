using ScavengerNPC;
using UnityEditor;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace KenshiVR.MeleeCombat.Editor
{
    [CustomEditor(typeof(CharacterCombatProgression))]
    public sealed class CharacterCombatProgressionEditor : UnityEditor.Editor
    {
        CharacterAttribute selected = CharacterAttribute.Strength;
        int manualValue = 50;
        float simulatedDamage = 10f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var progress = (CharacterCombatProgression)target;
            var attributes = progress.attributes;
            if (!attributes) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime attributes and XP", EditorStyles.boldLabel);
            foreach (CharacterAttribute attribute in System.Enum.GetValues(typeof(CharacterAttribute)))
                EditorGUILayout.LabelField(attribute.ToString(),
                    $"{attributes.Get(attribute)}  |  {progress.Experience(attribute):F1} / {progress.ExperienceToNext(attribute):F1} XP  |  lifetime {progress.LifetimeExperience(attribute):F1}");

            var player = progress.GetComponent<ScavengerPlayerHealth>();
            if (!player) player = progress.GetComponentInParent<ScavengerPlayerHealth>();
            var npc = progress.GetComponent<ScavengerHealth>();
            if (player) EditorGUILayout.LabelField("Health / trauma", $"{player.Current:F1} / {player.maxHealth:F1}  |  {player.Trauma:F1}");
            if (npc) EditorGUILayout.LabelField("Health / trauma", $"{npc.Current:F1} / {npc.maxHealth:F1}  |  {npc.Trauma:F1}");
            EditorGUILayout.LabelField("Stamina", "No stamina resource in current combat system");
            var totals = progress.Lifetime;
            EditorGUILayout.LabelField("Combat totals", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Punches attempted / landed", $"{totals.punchesAttempted} / {totals.punchesLanded}");
            EditorGUILayout.LabelField("Weapon hits / hits received", $"{totals.weaponHitsLanded} / {totals.validHitsReceived}");
            EditorGUILayout.LabelField("Damage dealt / received", $"{totals.damageDealt:F1} / {totals.damageReceived:F1}");
            EditorGUILayout.LabelField("Trauma dealt / received", $"{totals.consciousnessDamageDealt:F1} / {totals.consciousnessDamageReceived:F1}");
            EditorGUILayout.LabelField("Blocks / parries", $"{totals.successfulBlocks} / {totals.successfulParries}");
            EditorGUILayout.LabelField("Knockouts inflicted / suffered", $"{totals.knockoutsInflicted} / {totals.timesKnockedUnconscious}");
            EditorGUILayout.LabelField("Encounters survived", totals.combatEncountersSurvived.ToString());
            EditorGUILayout.LabelField("Incoming damage modifier", CharacterCombatMath.DamageTaken(attributes).ToString("F2"));
            EditorGUILayout.LabelField("Unarmed damage modifier", CharacterCombatMath.UnarmedDamage(attributes).ToString("F2"));
            EditorGUILayout.LabelField("Endurance trauma capacity", CharacterCombatMath.EnduranceTraumaCapacity(attributes).ToString("F2"));

            selected = (CharacterAttribute)EditorGUILayout.EnumPopup("Attribute", selected);
            manualValue = EditorGUILayout.IntSlider("Set value", manualValue, 1, attributes.runtimeMaximum);
            if (GUILayout.Button("Set selected runtime attribute")) attributes.SetRuntime(selected, manualValue);
            if (GUILayout.Button("Reset to starting profile")) progress.ResetToStartingValues();
            if (GUILayout.Button("Save progression")) progress.Save();
            if (GUILayout.Button("Load progression")) progress.Load();
            simulatedDamage = EditorGUILayout.FloatField("Simulated blunt hit", simulatedDamage);
            if (GUILayout.Button("Simulate incoming hit"))
            {
                var packet = new DamagePacket { BaseDamage = Mathf.Max(.1f, simulatedDamage),
                    DamageType = DamageType.Kinetic, PhysicalImpact = PhysicalImpactKind.Blunt,
                    HitPointWorld = progress.transform.position + Vector3.up, WeaponId = "debug simulation",
                    Timestamp = Time.time };
                if (player) player.ApplyDamage(packet);
                else if (npc) npc.ApplyDamage(packet);
            }
            EditorGUILayout.LabelField("Recent resolved hits", EditorStyles.boldLabel);
            var recent = progress.RecentHits;
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                var hit = recent[i];
                EditorGUILayout.LabelField($"{hit.time:F1} {hit.attacker} → {hit.defender}: {hit.attackType}");
                EditorGUILayout.LabelField($"raw {hit.rawDamage:F1}, health {hit.healthDamage:F1}, trauma {hit.consciousnessDamage:F1}, blocked {hit.blocked}, parried {hit.parried}, KO {hit.knockout}");
                EditorGUILayout.LabelField($"XP: Str {hit.strengthXp:F1}, Tou {hit.toughnessXp:F1}, End {hit.enduranceXp:F1}, MA {hit.martialArtsXp:F1}");
            }
            if (Application.isPlaying) Repaint();
        }
    }
}
