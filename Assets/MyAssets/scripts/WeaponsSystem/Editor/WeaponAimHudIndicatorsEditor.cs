#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VRKoombar.WeaponsStaging.Core;

namespace VRKoombar.WeaponsStaging.Editor
{
    [CustomEditor(typeof(WeaponAimHudIndicators))]
    internal sealed class WeaponAimHudIndicatorsEditor : UnityEditor.Editor
    {
        private const string PreviewCountName = "_inspectorPreviewDotCount";
        private const string PreviewNormName = "_inspectorPreviewNormalized";
        private const string PreviewAnchoredName = "_inspectorPreviewAnchored";
        private const string PreviewHalfName = "_inspectorPreviewParentHalf";

        private double _nextRepaintTime;

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying || target == null)
            {
                return;
            }

            double t = EditorApplication.timeSinceStartup;
            if (t < _nextRepaintTime)
            {
                return;
            }

            _nextRepaintTime = t + 0.05;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            SerializedProperty spriteProp = serializedObject.FindProperty("dotSprite");

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Live dot preview (Play Mode)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "In Play Mode, PIP dots are UI objects under Dots Parent named AimDot_0, AimDot_1, … Search the Hierarchy under your HUD Canvas.",
                MessageType.None);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode and keep this inspector open. Preview reads serialized runtime data each frame.",
                    MessageType.Info);
                return;
            }

            serializedObject.Update();
            SerializedProperty countProp = serializedObject.FindProperty(PreviewCountName);
            int count = countProp != null ? countProp.intValue : 0;
            EditorGUILayout.LabelField($"Active dots (preview): {count}", EditorStyles.miniLabel);

            DrawPreviewRect(serializedObject, spriteProp.objectReferenceValue as Sprite);
        }

        private static void DrawPreviewRect(SerializedObject so, Sprite dotSprite)
        {
            const float previewHeight = 140f;
            Rect r = GUILayoutUtility.GetRect(10f, previewHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.12f, 1f));

            SerializedProperty countProp = so.FindProperty(PreviewCountName);
            SerializedProperty normProp = so.FindProperty(PreviewNormName);
            SerializedProperty anchoredProp = so.FindProperty(PreviewAnchoredName);
            SerializedProperty halfProp = so.FindProperty(PreviewHalfName);

            int count = countProp != null ? countProp.intValue : 0;
            if (count <= 0 || normProp == null || anchoredProp == null)
            {
                GUI.Label(
                    new Rect(r.x + 8f, r.y + 8f, r.width - 16f, 48f),
                    "No preview data yet. Ensure Play Mode, a ship with WeaponControllers + muzzles, and Hud View Camera set.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            Vector2 parentHalf = halfProp != null ? halfProp.vector2Value : new Vector2(400f, 400f);

            for (int i = 0; i < count; i++)
            {
                Vector2 n;
                if (normProp.isArray && i < normProp.arraySize)
                {
                    n = normProp.GetArrayElementAtIndex(i).vector2Value;
                }
                else if (anchoredProp.isArray && i < anchoredProp.arraySize)
                {
                    Vector2 ap = anchoredProp.GetArrayElementAtIndex(i).vector2Value;
                    float hx = Mathf.Max(1f, parentHalf.x);
                    float hy = Mathf.Max(1f, parentHalf.y);
                    n = new Vector2(ap.x / hx, ap.y / hy);
                }
                else
                {
                    continue;
                }

                Vector2 center = new Vector2(
                    r.x + r.width * (0.5f + n.x * 0.42f),
                    r.y + r.height * (0.5f - n.y * 0.42f));
                float dotRadius = dotSprite != null ? 10f : 5f;
                Rect dr = new Rect(center.x - dotRadius, center.y - dotRadius, dotRadius * 2f, dotRadius * 2f);

                if (dotSprite != null && dotSprite.texture != null)
                {
                    Rect tr = dotSprite.textureRect;
                    Texture2D tex = dotSprite.texture;
                    Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                    GUI.DrawTextureWithTexCoords(dr, tex, uv, true);
                }
                else
                {
                    EditorGUI.DrawRect(dr, new Color(1f, 0.85f, 0.15f, 0.95f));
                }
            }
        }
    }
}
#endif
