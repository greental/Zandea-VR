using UnityEditor;
using UnityEngine;

namespace JunkerEquipment.Editor
{
    [CustomEditor(typeof(PlayerStartingItems))]
    public sealed class PlayerStartingItemsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
            var setup = (PlayerStartingItems)target;
            if (GUILayout.Button("Apply Starting Items Now"))
            {
                Sync(setup.leftHip, ref setup.leftHipItem, setup.leftHipPrefab);
                Sync(setup.rightHip, ref setup.rightHipItem, setup.rightHipPrefab);
                Sync(setup.leftBackpack, ref setup.leftBackpackItem, setup.leftBackpackPrefab);
                Sync(setup.rightBackpack, ref setup.rightBackpackItem, setup.rightBackpackPrefab);
                setup.Apply(); EditorUtility.SetDirty(setup);
            }
        }
        static void Sync(JunkerEquipmentSlot slot, ref JunkerEquipmentItem item, GameObject prefab)
        {
            if (!slot) return;
            if (item && (!prefab || PrefabUtility.GetCorrespondingObjectFromSource(item.gameObject) != prefab))
                Undo.DestroyObjectImmediate(item.gameObject);
            item = null;
            if (!prefab) { slot.startingItem = null; return; }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "Set starting slot item");
            instance.transform.SetParent(slot.transform, false);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            item = instance.GetComponentInChildren<JunkerEquipmentItem>(true);
            slot.startingItem = item; EditorUtility.SetDirty(slot);
        }
    }
}
