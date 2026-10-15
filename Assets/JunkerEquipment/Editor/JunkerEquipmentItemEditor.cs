using UnityEditor;
using UnityEngine;

namespace JunkerEquipment.Editor
{
    [CustomEditor(typeof(JunkerEquipmentItem))]
    public sealed class JunkerEquipmentItemEditor : UnityEditor.Editor
    {
        void OnSceneGUI()
        {
            var item = (JunkerEquipmentItem)target;
            var grip = item.gripPose;
            if (!grip || !item.useGripTransformDirectly) return;
            Handles.color = Color.cyan;
            Handles.Label(grip.position + Vector3.up * HandleUtility.GetHandleSize(grip.position) * .13f,
                "Grip: hand attaches here; blue = forward");
            EditorGUI.BeginChangeCheck();
            var position = Handles.PositionHandle(grip.position, grip.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(grip, "Move weapon Grip");
                grip.position = position;
                PrefabUtility.RecordPrefabInstancePropertyModifications(grip);
            }
            EditorGUI.BeginChangeCheck();
            var rotation = Handles.RotationHandle(grip.rotation, grip.position);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(grip, "Rotate weapon Grip");
                grip.rotation = rotation;
                PrefabUtility.RecordPrefabInstancePropertyModifications(grip);
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var item = (JunkerEquipmentItem)target;
            if (!item.gripPose) return;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Grip is the hand attachment pose. Move and rotate that child in Prefab Mode. Its blue axis shows forward, and green shows up.", MessageType.Info);
            if (GUILayout.Button("Select Grip and frame it"))
            {
                Selection.activeTransform = item.gripPose;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }
    }
}
