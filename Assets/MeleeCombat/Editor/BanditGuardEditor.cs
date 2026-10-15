using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    [CustomEditor(typeof(StarvingBandit))]
    public sealed class BanditGuardEditor : UnityEditor.Editor
    {
        bool authoring;
        int poseIndex = 1;
        static readonly string[] PoseNames = { "Guard reference", "Windup", "Early strike", "Impact", "Follow-through", "Recovery" };
        static StaffCombatPose SelectPose(StaffAttackDefinition a, int index) => index switch {
            0 => a.guardPose, 1 => a.windupPose, 2 => a.earlyStrikePose,
            3 => a.impactPose, 4 => a.followPose, _ => a.recoveryPose };
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var bandit = (StarvingBandit)target;
            EditorGUILayout.Space();
            if (bandit.guardAuthoringMode)
                EditorGUILayout.HelpBox("Saved guard pose is protected. The staff and hands are captured as the attack baseline when Play mode starts.", MessageType.Info);
            if (bandit.staff && bandit.weaponGoal && GUILayout.Button("Match grip goal to staff"))
            {
                Undo.RecordObject(bandit.weaponGoal, "Match bandit grip goal");
                bandit.weaponGoal.SetPositionAndRotation(bandit.staff.transform.position, bandit.staff.transform.rotation);
                EditorSceneManager.MarkSceneDirty(bandit.gameObject.scene);
            }
            if (GUILayout.Button("Zero finger rotations")) ZeroFingers(bandit);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Attack preview", EditorStyles.boldLabel);
            authoring = EditorGUILayout.Toggle("Attack Pose Authoring Mode", authoring);
            if (authoring && bandit.attacks != null && bandit.attacks.Length == 8)
            {
                bandit.previewDirection = EditorGUILayout.Popup("Author direction", bandit.previewDirection, StarvingBandit.ClockDirections);
                poseIndex = EditorGUILayout.Popup("Key pose", poseIndex, PoseNames);
                EditorGUILayout.HelpBox("Use the Scene view position and rotation handles to edit the selected key pose. Body and elbow fields are saved in the attack array above. Guard reference is protected.", MessageType.Info);
                if (GUILayout.Button("Reset selected attack to generated poses"))
                {
                    Undo.RecordObject(bandit, "Reset bandit attack pose");
                    bandit.GeneratePoseDefaults(bandit.previewDirection);
                    EditorUtility.SetDirty(bandit);
                    EditorSceneManager.MarkSceneDirty(bandit.gameObject.scene);
                }
                var a = bandit.attacks[bandit.previewDirection];
                if (a != null)
                {
                    var p = SelectPose(a, poseIndex);
                    EditorGUILayout.LabelField("Dominant hand", a.dominantRight ? "Right" : "Left");
                    EditorGUILayout.LabelField("Comfortable / maximum range", $"{a.comfortableRange:F2} / {a.comfortableRange + a.maximumApproachStep:F2} m");
                    if (poseIndex > 0)
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 midpoint = EditorGUILayout.Vector3Field("Grip midpoint offset", p.gripMidpointOffset);
                        Vector3 euler = EditorGUILayout.Vector3Field("Staff rotation", p.staffEuler);
                        float hip = EditorGUILayout.Slider("Hip yaw", p.hipYaw, -30, 30);
                        float torso = EditorGUILayout.Slider("Torso yaw", p.torsoYaw, -35, 35);
                        float pitch = EditorGUILayout.Slider("Torso pitch", p.torsoPitch, -25, 25);
                        float shoulder = EditorGUILayout.Slider("Shoulder yaw", p.shoulderYaw, -22, 22);
                        float lift = EditorGUILayout.Slider("Shoulder lift", p.shoulderLift, -15, 18);
                        float lunge = EditorGUILayout.Slider("Forward lunge", p.forwardLunge, -.05f, .2f);
                        float shift = EditorGUILayout.Slider("Weight shift", p.weightShift, -.08f, .08f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(bandit, "Edit bandit attack pose");
                            p.gripMidpointOffset = midpoint; p.staffEuler = euler; p.hipYaw = hip; p.torsoYaw = torso;
                            p.torsoPitch = pitch; p.shoulderYaw = shoulder; p.shoulderLift = lift; p.forwardLunge = lunge; p.weightShift = shift;
                            EditorUtility.SetDirty(bandit); EditorSceneManager.MarkSceneDirty(bandit.gameObject.scene);
                        }
                    }
                }
            }
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to preview without changing the saved guard pose.", MessageType.None);
                return;
            }
            bandit.previewDirection = EditorGUILayout.Popup("Clock direction", bandit.previewDirection, StarvingBandit.ClockDirections);
            bandit.previewPhase = (StarvingBandit.AttackPhase)EditorGUILayout.EnumPopup("Phase", bandit.previewPhase);
            bandit.previewProgress = EditorGUILayout.Slider("Phase progress", bandit.previewProgress, 0, 1);
            if (GUILayout.Button(bandit.previewEnabled ? "Stop preview and restore guard" : "Preview selected attack"))
            {
                if (bandit.previewEnabled) bandit.StopPreview();
                else { bandit.CaptureGuardForPreview(); bandit.previewEnabled = true; }
            }
            var motion = bandit.GetComponent<BanditRigMotion>();
            if (motion)
            {
                EditorGUILayout.LabelField("Grip error (left / right)",
                    $"{motion.LeftGripError * 1000f:F1} / {motion.RightGripError * 1000f:F1} mm");
                if (motion.LeftGripError > .025f || motion.RightGripError > .025f)
                    EditorGUILayout.HelpBox("A hand is more than 25 mm from its saved grip point. Shorten this path or adjust the body motion.", MessageType.Warning);
            }
            EditorGUILayout.LabelField("Reach correction", $"{bandit.ReachCorrectionDistance * 1000f:F1} mm");
            EditorGUILayout.LabelField("Rotation correction", $"{bandit.ReachCorrectionAngle:F1}°");
            EditorGUILayout.LabelField("Correction reason", bandit.ReachCorrectionReason);
            EditorGUILayout.LabelField("Residual reach error", $"{bandit.ReachError * 1000f:F1} mm");
            EditorGUILayout.LabelField("Tip position error", $"{bandit.CurrentTipError:F3} m");
            EditorGUILayout.LabelField("Orientation error", $"{bandit.CurrentOrientationError:F1}°");
            EditorGUILayout.LabelField("Desired attack arc", $"{bandit.DesiredAngularTravel:F1}°");
            EditorGUILayout.LabelField("Current achieved arc", $"{bandit.AchievedAngularTravel:F1}°");
            EditorGUILayout.LabelField("Desired tip path length", $"{bandit.DesiredTipTravel * 100f:F1} cm");
            EditorGUILayout.LabelField("Current achieved displacement", $"{bandit.AchievedTipTravel * 100f:F1} cm");
            if (bandit.ReachCorrectionDistance > .05f)
                EditorGUILayout.HelpBox("This path is shortened over 50 mm to keep both hands reachable. Tune its offsets and rotations.", MessageType.Warning);
            if (bandit.previewEnabled) Repaint();
        }
        void OnSceneGUI()
        {
            if (!authoring || poseIndex == 0) return;
            var bandit = (StarvingBandit)target;
            if (!bandit.staff || bandit.attacks == null || bandit.attacks.Length != 8) return;
            var a = bandit.attacks[Mathf.Clamp(bandit.previewDirection, 0, 7)];
            if (a == null) return;
            var p = SelectPose(a, poseIndex);
            bandit.GetTipLocals(out var plus, out var minus);
            Vector3 midpoint = (plus + minus) * .5f;
            Vector3 guardCenter = bandit.staff.transform.TransformPoint(midpoint);
            Vector3 worldCenter = guardCenter + bandit.transform.TransformVector(p.gripMidpointOffset);
            Quaternion worldRotation = bandit.staff.transform.rotation * Quaternion.Euler(p.staffEuler);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(worldCenter, worldRotation);
            Quaternion rotated = Handles.RotationHandle(worldRotation, moved);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(bandit, "Adjust staff attack pose");
                p.gripMidpointOffset = bandit.transform.InverseTransformVector(moved - guardCenter);
                p.staffEuler = (Quaternion.Inverse(bandit.staff.transform.rotation) * rotated).eulerAngles;
                EditorUtility.SetDirty(bandit); EditorSceneManager.MarkSceneDirty(bandit.gameObject.scene);
            }
            Handles.color = Color.yellow;
            Handles.DrawDottedLine(guardCenter, moved, 4);
            Handles.Label(moved, $"{StarvingBandit.ClockDirections[bandit.previewDirection]} {PoseNames[poseIndex]}");
        }
        static void ZeroFingers(StarvingBandit bandit)
        {
            var motion = bandit.GetComponent<BanditRigMotion>();
            if (!motion || !motion.visualRoot) return;
            string[] digits = { "Index", "Mid", "Ring", "Pinky", "Thumb" };
            int count = 0;
            foreach (var bone in motion.visualRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name.EndsWith("scaleCompensation")) continue;
                bool finger = false;
                foreach (string side in new[] { "L", "R" })
                foreach (string digit in digits)
                for (int segment = 1; segment <= 3; segment++)
                    if (bone.name.StartsWith("CC_Base_" + side + "_" + digit + segment + "_")) finger = true;
                if (!finger) continue;
                Undo.RecordObject(bone, "Zero bandit finger rotations");
                bone.localRotation = Quaternion.identity;
                PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
                count++;
            }
            if (count > 0) EditorSceneManager.MarkSceneDirty(bandit.gameObject.scene);
            Debug.Log("Zeroed " + count + " bandit finger bone local rotations.", bandit);
        }
    }
}
