using System.Collections.Generic;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BanditGroup))]
public sealed class BanditGroupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var group = (BanditGroup)target;
        bool populated = group.authoredMembers != null && group.authoredMembers.Length > 0;
        using (new EditorGUI.DisabledScope(Application.isPlaying || populated || !group.banditPrefab || EditorUtility.IsPersistent(group)))
            if (GUILayout.Button("Spawn editable bandits in scene")) Spawn(group);
        if (populated)
            EditorGUILayout.HelpBox("These scene bandits are used during Play mode. Edit their weapons, appearance, and positions in the Hierarchy.", MessageType.Info);
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
