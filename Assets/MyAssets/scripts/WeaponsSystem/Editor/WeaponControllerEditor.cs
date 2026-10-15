#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRKoombar.WeaponsStaging.Core;

namespace VRKoombar.WeaponsStaging.Editor
{
    [CustomEditor(typeof(WeaponController))]
    internal sealed class WeaponControllerEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> LocalConfigFields = new()
        {
            "weaponId",
            "damageType",
            "projectilePrefab",
            "projectileSpeed",
            "fireRate",
            "weaponHeatPerShot",
            "overheatThreshold",
            "resumeHeatThreshold",
            "heatCoolingPerSecond",
            "useHeatLockout",
            "ammoPerMagazine",
            "reloadSeconds",
            "shieldInteraction",
            "hullInteraction",
            "requiresDeployedHardpoint",
            "fireSound",
            "fireSoundVolume",
            "useGatlingStyleFireAudio",
            "gatlingFireSoundStart",
            "gatlingFireSoundLoop",
            "gatlingFireSoundEnd",
            "gatlingFireTailDelay",
            "gatlingProjectileSpinUpSeconds",
            "hitImpactEffectPrefab",
            "hitImpactEffectScale",
            "hitScorchMarkPrefab",
            "hitScorchMarkUsesUpAsNormal",
            "hitScorchMarkScale",
            "hitScorchMarkSurfaceOffset",
            "hitScorchMarkRandomRollDegrees",
            "hitScorchMarkLifetimeSeconds",
            "hitScorchMarkParentToHitObject",
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty useLegacy = serializedObject.FindProperty("useLegacyWeaponDefinition");
            SerializedProperty definition = serializedObject.FindProperty("definition");

            DrawScriptField();
            EditorGUILayout.PropertyField(useLegacy, new GUIContent("Use Legacy Weapon Definition"));
            EditorGUILayout.PropertyField(definition, new GUIContent("Legacy Weapon Definition"));

            if (useLegacy.boolValue && definition.objectReferenceValue != null)
            {
                EditorGUILayout.HelpBox("Legacy mode is ON: firing/damage/audio/vfx values are read from WeaponDefinition. Local inspector weapon values are hidden.", MessageType.Info);
            }
            else if (useLegacy.boolValue && definition.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Legacy mode is ON but no WeaponDefinition is assigned. Assign one, or turn legacy mode OFF to use local inspector values.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Inspector-driven mode: local WeaponController values are active.", MessageType.None);
            }

            SerializedProperty prop = serializedObject.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (prop.name == "m_Script" || prop.name == "useLegacyWeaponDefinition" || prop.name == "definition")
                {
                    continue;
                }

                if (useLegacy.boolValue && LocalConfigFields.Contains(prop.name))
                {
                    continue;
                }

                EditorGUILayout.PropertyField(prop, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptField()
        {
            SerializedProperty scriptProp = serializedObject.FindProperty("m_Script");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(scriptProp);
            }
        }
    }
}
#endif
