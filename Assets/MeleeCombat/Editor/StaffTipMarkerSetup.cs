using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class StaffTipMarkerSetup
    {
        const string ScenePath = "Assets/MeleeCombat/MeleePOC.unity";

        [InitializeOnLoadMethod]
        static void Register()
        {
            EditorApplication.delayCall += CalibrateLoadedScene;
            EditorSceneManager.sceneOpened += (_, _) => EditorApplication.delayCall += CalibrateLoadedScene;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += CalibrateLoadedScene;
            };
        }

        [MenuItem("Tools/Melee Combat/Calibrate staff tip markers")]
        public static void CalibrateLoadedScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                var bandit = root.GetComponentInChildren<StarvingBandit>(true);
                if (!bandit || !bandit.staff || (bandit.positiveStrikingTip && bandit.negativeStrikingTip)) continue;
                if (!StaffAxisCalibration.TryMeasure(bandit.staff.transform, out var positive, out var negative))
                {
                    Debug.LogWarning("Could not measure the Bo staff mesh. Tip markers need manual placement.", bandit);
                    continue;
                }
                Undo.RecordObject(bandit, "Calibrate staff tip references");
                if (!bandit.positiveStrikingTip)
                    bandit.positiveStrikingTip = CreateMarker(bandit.staff.transform, "PositiveStrikingTip", positive);
                if (!bandit.negativeStrikingTip)
                    bandit.negativeStrikingTip = CreateMarker(bandit.staff.transform, "NegativeStrikingTip", negative);
                PrefabUtility.RecordPrefabInstancePropertyModifications(bandit);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"Calibrated Bo staff tips in Rigidbody-local space: + {positive}, - {negative}; axis {(positive - negative).normalized}. Saved current guard scene without changing transforms.", bandit);
            }
        }

        static Transform CreateMarker(Transform parent, string name, Vector3 localPosition)
        {
            var marker = new GameObject(name).transform;
            Undo.RegisterCreatedObjectUndo(marker.gameObject, "Create staff tip marker");
            marker.SetParent(parent, false);
            marker.localPosition = localPosition;
            marker.localRotation = Quaternion.identity;
            marker.localScale = Vector3.one;
            return marker;
        }
    }
}
