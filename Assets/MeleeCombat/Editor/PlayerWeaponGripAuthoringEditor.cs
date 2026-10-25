using System.Linq;
using JunkerBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    [CustomEditor(typeof(PlayerWeaponGripAuthoring))]
    public sealed class PlayerWeaponGripAuthoringEditor : UnityEditor.Editor
    {
        int weaponIndex;
        public override void OnInspectorGUI()
        {
            var fitter = (PlayerWeaponGripAuthoring)target;
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MyAssets/prefabs/weapons" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponentInChildren<PhysicalMeleeWeapon>(true))
                .ToArray();
            var labels = paths.Select(System.IO.Path.GetFileNameWithoutExtension).ToArray();
            int current = fitter.selectedWeapon ? System.Array.IndexOf(paths, AssetDatabase.GetAssetPath(fitter.selectedWeapon)) : -1;
            weaponIndex = EditorGUILayout.Popup("Weapon", Mathf.Max(0, current >= 0 ? current : weaponIndex), labels);
            if (paths.Length > 0)
            {
                var picked = AssetDatabase.LoadAssetAtPath<GameObject>(paths[Mathf.Clamp(weaponIndex, 0, paths.Length - 1)]);
                if (picked != fitter.selectedWeapon) { Undo.RecordObject(fitter, "Choose fitted weapon"); fitter.selectedWeapon = picked; Rebuild(fitter); }
            }
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Move and rotate Weapon grip preview under the visible right hand. Move Secondary Grip on the preview for two-handed weapons, then save.", MessageType.Info);
            if (GUILayout.Button("Create / Refresh Grip Preview")) Rebuild(fitter);
            if (GUILayout.Button("Save Current Grip To Player")) Save(fitter);
        }
        static void Rebuild(PlayerWeaponGripAuthoring fitter)
        {
            if (fitter.preview) Undo.DestroyObjectImmediate(fitter.preview);
            if (!fitter.selectedWeapon) return;
            var body = fitter.GetComponentInChildren<VRBodyController>(true);
            var hand = body && body.animator && body.animator.isHuman ? body.animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (!hand) { Debug.LogError("Player weapon fitting needs the humanoid right hand.", fitter); return; }
            fitter.preview = (GameObject)PrefabUtility.InstantiatePrefab(fitter.selectedWeapon, fitter.gameObject.scene);
            fitter.preview.name = "Weapon grip preview - " + fitter.selectedWeapon.name;
            Undo.RegisterCreatedObjectUndo(fitter.preview, "Create weapon grip preview");
            fitter.preview.transform.SetParent(hand, false);
            var weapon = fitter.preview.GetComponentInChildren<PhysicalMeleeWeapon>(true);
            var fit = weapon == null ? null : fitter.savedFits.Find(x => x.weaponId == weapon.weaponId);
            if (fit != null && weapon)
            {
                var grip = weapon.rightGrip ? weapon.rightGrip : weapon.grip ? weapon.grip : weapon.transform;
                Vector3 gripPosition = fitter.preview.transform.InverseTransformPoint(grip.position);
                Quaternion gripRotation = Quaternion.Inverse(fitter.preview.transform.rotation) * grip.rotation;
                Quaternion desired = Quaternion.Inverse(gripRotation * Quaternion.Euler(fit.rightEuler));
                fitter.preview.transform.SetLocalPositionAndRotation(fit.rightPosition - desired * gripPosition, desired);
                if (weapon.secondaryGrip)
                    weapon.secondaryGrip.SetLocalPositionAndRotation(fit.secondaryPosition, Quaternion.Euler(fit.secondaryEuler));
            }
            EditorUtility.SetDirty(fitter);
        }
        static void Save(PlayerWeaponGripAuthoring fitter)
        {
            if (!fitter.preview || !fitter.selectedWeapon) { Debug.LogError("Create a preview first.", fitter); return; }
            var weapon = fitter.preview.GetComponentInChildren<PhysicalMeleeWeapon>(true);
            if (!weapon) return;
            var fit = fitter.savedFits.Find(x => x.weaponId == weapon.weaponId);
            if (fit == null) { fit = new PlayerWeaponGripAuthoring.Fit { weaponId = weapon.weaponId }; fitter.savedFits.Add(fit); }
            Undo.RecordObject(fitter, "Save player weapon grip");
            fit.weaponPrefab = fitter.selectedWeapon;
            var grip = weapon.rightGrip ? weapon.rightGrip : weapon.grip ? weapon.grip : weapon.transform;
            Vector3 gripPosition = fitter.preview.transform.InverseTransformPoint(grip.position);
            Quaternion gripRotation = Quaternion.Inverse(fitter.preview.transform.rotation) * grip.rotation;
            Quaternion desired = fitter.preview.transform.localRotation;
            fit.rightPosition = fitter.preview.transform.localPosition + desired * gripPosition;
            fit.rightEuler = (Quaternion.Inverse(gripRotation) * Quaternion.Inverse(desired)).eulerAngles;
            fit.leftPosition = new Vector3(-fit.rightPosition.x, fit.rightPosition.y, fit.rightPosition.z);
            fit.leftEuler = fit.rightEuler;
            if (weapon.secondaryGrip)
            {
                fit.secondaryPosition = weapon.secondaryGrip.localPosition;
                fit.secondaryEuler = weapon.secondaryGrip.localEulerAngles;
            }
            EditorUtility.SetDirty(fitter);
            EditorSceneManager.MarkSceneDirty(fitter.gameObject.scene);
        }
    }
}
