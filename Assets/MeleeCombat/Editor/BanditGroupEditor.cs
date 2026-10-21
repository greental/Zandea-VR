using System.Collections.Generic;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BanditGroup))]
public sealed class BanditGroupEditor : Editor
{
    PhysicalMeleeWeapon[] availableWeapons;
    StarvingBandit[] availableProfiles;
    string[] weaponNames;

    void OnEnable()
    {
        var choices = new List<(PhysicalMeleeWeapon weapon, StarvingBandit profile, string label)>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MeleeCombat/MocapTest/GeneratedVariants" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var profile = prefab ? prefab.GetComponent<StarvingBandit>() : null;
            if (!profile) continue;
            var generated = prefab.GetComponent<NpcGeneratedWeapon>();
            var weapon = generated && generated.weaponPrefab ? generated.weaponPrefab : profile.staff;
            if (weapon && weapon.GetComponent<Rigidbody>())
                choices.Add((weapon, profile, prefab.name.Replace("StarvingBandit_", "")));
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MyAssets/prefabs/weapons", "Assets/MeleeCombat" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var weapon = prefab ? prefab.GetComponent<PhysicalMeleeWeapon>() : null;
            if (weapon && weapon.GetComponent<Rigidbody>() && choices.All(choice => choice.weapon != weapon))
                choices.Add((weapon, null, prefab.name));
        }
        choices = choices.OrderBy(choice => choice.label).ToList();
        availableWeapons = choices.Select(choice => choice.weapon).ToArray();
        availableProfiles = choices.Select(choice => choice.profile).ToArray();
        weaponNames = new[] { "Keep saved weapon" }
            .Concat(choices.Select(choice => choice.label)).ToArray();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "authoredWeaponChoices", "authoredWeaponProfiles");
        var group = (BanditGroup)target;
        bool populated = group.authoredMembers != null && group.authoredMembers.Length > 0;
        if (populated)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Authored bandit weapons", EditorStyles.boldLabel);
            var choices = serializedObject.FindProperty("authoredWeaponChoices");
            var profiles = serializedObject.FindProperty("authoredWeaponProfiles");
            if (choices.arraySize != group.authoredMembers.Length) choices.arraySize = group.authoredMembers.Length;
            if (profiles.arraySize != group.authoredMembers.Length) profiles.arraySize = group.authoredMembers.Length;
            for (int i = 0; i < group.authoredMembers.Length; i++)
            {
                var member = group.authoredMembers[i];
                var choice = choices.GetArrayElementAtIndex(i);
                int current = System.Array.IndexOf(availableWeapons, choice.objectReferenceValue) + 1;
                int next = EditorGUILayout.Popup(member ? member.name : "Member " + (i + 1),
                    current, weaponNames);
                if (next != current)
                {
                    choice.objectReferenceValue = next == 0 ? null : availableWeapons[next - 1];
                    profiles.GetArrayElementAtIndex(i).objectReferenceValue = next == 0 ? null : availableProfiles[next - 1];
                }
            }
            EditorGUILayout.HelpBox("The selected prefab is equipped when Play starts. Its grip marker aligns to the bandit's hand automatically; no transform setup is needed.", MessageType.Info);
        }
        serializedObject.ApplyModifiedProperties();
        using (new EditorGUI.DisabledScope(Application.isPlaying || populated || !group.banditPrefab || EditorUtility.IsPersistent(group)))
            if (GUILayout.Button("Spawn editable bandits in scene")) Spawn(group);
    }

    static void Spawn(BanditGroup group)
    {
        Undo.RecordObject(group, "Spawn editable bandits");
        var created = new List<StarvingBandit>();
        for (int i = 0; i < Mathf.Max(1, group.groupSize); i++)
        {
            var prefab = group.banditPrefab;
            var options = group.weaponOptions != null && group.weaponOptions.Length > 0
                ? group.weaponOptions : group.weaponVariants;
            if (options != null && options.Length > 0)
                for (int j = 0; j < options.Length; j++)
                {
                    var candidate = options[(i + j) % options.Length];
                    var generated = candidate ? candidate.GetComponent<NpcGeneratedWeapon>() : null;
                    if (candidate && (candidate.staff || generated && generated.startEquipped &&
                        generated.weaponPrefab && generated.gripTarget))
                    { prefab = candidate; break; }
                }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, group.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "Spawn editable bandit");
            Undo.SetTransformParent(instance.transform, group.transform, "Parent editable bandit");
            Vector2 offset = i == 0 ? Vector2.zero : Random.insideUnitCircle * group.spawnRadius;
            Vector3 position = group.transform.position + group.transform.TransformDirection(new Vector3(offset.x, 0f, offset.y));
            foreach (var terrain in Terrain.activeTerrains)
            {
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (position.x < origin.x || position.x > origin.x + size.x ||
                    position.z < origin.z || position.z > origin.z + size.z) continue;
                position.y = terrain.SampleHeight(position) + origin.y;
                break;
            }
            instance.transform.position = position;
            instance.transform.rotation = group.transform.rotation;
            instance.name = i == 0 ? "Bandit Leader" : "Bandit Follower " + i;
            created.Add(instance.GetComponent<StarvingBandit>());
        }
        group.authoredMembers = created.ToArray();
        EditorUtility.SetDirty(group);
    }
}
