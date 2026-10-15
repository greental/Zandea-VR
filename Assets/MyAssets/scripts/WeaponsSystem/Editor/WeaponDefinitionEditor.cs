#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VRKoombar.WeaponsStaging.Data;

namespace VRKoombar.WeaponsStaging.Editor
{
    [CustomEditor(typeof(WeaponDefinition))]
    internal sealed class WeaponDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawScriptField();
            EditorGUILayout.Space(4);

            DrawIdentityAndProjectile();
            EditorGUILayout.Space(4);

            DrawAudioSection();
            EditorGUILayout.Space(4);

            DrawImpactSection();

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

        private void DrawIdentityAndProjectile()
        {
            EditorGUILayout.LabelField("Weapon", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.weaponId)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.damageType)));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Projectile", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(WeaponDefinition.projectilePrefab)),
                new GUIContent(
                    "Projectile Prefab",
                    "Assign a prefab asset from the Project window (with Rigidbody, Collider, WeaponProjectile)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.projectileSpeed)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.fireRate)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.weaponHeatPerShot)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.ammoPerMagazine)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.reloadSeconds)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.shieldInteraction)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hullInteraction)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.subsystemEffects)), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.requiresDeployedHardpoint)));
        }

        private void DrawAudioSection()
        {
            EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);

            SerializedProperty useGatling = serializedObject.FindProperty(nameof(WeaponDefinition.useGatlingStyleFireAudio));
            EditorGUILayout.PropertyField(useGatling, new GUIContent("Use Gatling Audio"));

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.fireSoundVolume)));

            if (useGatling.boolValue)
            {
                EditorGUILayout.HelpBox("Gatling enabled: uses Start / Loop / End (single Fire Sound is hidden).", MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.gatlingFireSoundStart)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.gatlingFireSoundLoop)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.gatlingFireSoundEnd)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.gatlingFireTailDelay)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.gatlingProjectileSpinUpSeconds)));
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.fireSound)));
            }
        }

        private void DrawImpactSection()
        {
            EditorGUILayout.LabelField("Impact VFX", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hitImpactEffectPrefab)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hitImpactEffectScale)));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Scorch Mark", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hitScorchMarkPrefab)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hitScorchMarkUsesUpAsNormal)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hitScorchMarkScale)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hitScorchMarkSurfaceOffset)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hitScorchMarkRandomRollDegrees)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hitScorchMarkLifetimeSeconds)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WeaponDefinition.hitScorchMarkParentToHitObject)));
        }
    }
}
#endif
