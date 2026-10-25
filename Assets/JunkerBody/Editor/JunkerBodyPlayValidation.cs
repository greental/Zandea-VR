using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace JunkerBody.Editor
{
    [InitializeOnLoad]
    public static class JunkerBodyPlayValidation
    {
        const string Flag = "JunkerBody.PlayValidation";
        static JunkerBodyPlayValidation() { EditorApplication.playModeStateChanged += Changed; }
        [MenuItem("Zandea/JunkerBody/Run synthetic Play Mode validation")]
        public static void Start()
        {
            if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != "Assets/Scenes/JunkerBody.unity")
                throw new InvalidOperationException("Play validation requires only JunkerBody open to isolate runtime side effects.");
            // Preserve current JunkerBody edits before entering the explicitly requested test.
            if (SceneManager.GetActiveScene().isDirty && !EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                throw new InvalidOperationException("Could not save JunkerBody before validation.");
            SessionState.SetBool(Flag, true);
            EditorApplication.isPlaying = true;
        }
        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
            {
                SessionState.SetBool(Flag, false);
                new GameObject("JunkerBody temporary validation").AddComponent<JunkerBodyValidationRunner>();
            }
        }
    }
    [DefaultExecutionOrder(20000)]
    public sealed class JunkerBodyValidationRunner : MonoBehaviour
    {
        VRBodyController body;
        JunkerSprint sprint;
        InputDevice device;
        ButtonControl click;
        int frame, stage;
        float walkMeasured, standingPelvis, stageTime;
        float leftLowerLength, leftHandLength;
        Vector3 calibrationHead, calibrationLeftRelative, calibrationRightRelative;
        Vector3 roomOrigin;
        readonly StringBuilder report = new StringBuilder("Unity Play Mode validation using synthetic tracking and movement input.\n");
        void Start()
        {
            try
            {
                body = FindFirstObjectByType<VRBodyController>(); sprint = body.GetComponent<JunkerSprint>();
                // The Alert clip has its own authored bone translations; compare against
                // that animation pose, not the model import's rest pose.
                var idleClip = body.animator.runtimeAnimatorController.animationClips.Single(c => c.name == "JunkerIdle");
                idleClip.SampleAnimation(body.avatarRoot.gameObject, 0f);
                leftLowerLength = body.leftArm.lower.localPosition.magnitude;
                leftHandLength = body.leftArm.tip.localPosition.magnitude;
                foreach (var driver in body.xrOrigin.GetComponentsInChildren<TrackedPoseDriver>(true)) driver.enabled = false;
                body.leftOpticalTracking = null; body.rightOpticalTracking = null;
                sprint.moveProvider.leftHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.ManualValue;
                sprint.moveProvider.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.ManualValue;
                sprint.moveProvider.rightHandMoveInput.manualValue = Vector2.zero;
                // Match the OpenXR Oculus Touch layout: the control is named
                // thumbstickClicked and carries the Primary2DAxisClick usage.
                InputSystem.RegisterLayout("{\"name\":\"JunkerValidationController\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"thumbstickClicked\",\"layout\":\"Button\",\"usages\":[\"Primary2DAxisClick\"]}]}");
                device = InputSystem.AddDevice("JunkerValidationController"); InputSystem.SetDeviceUsage(device, "LeftHand");
                click = device.GetChildControl<ButtonControl>("thumbstickClicked");
                Pose(body.standingHeight); SetInput(false, false);
            }
            catch (Exception e) { Finish(e); }
        }
        void Pose(float height, float x = 0f, float yaw = 0f)
        {
            body.hmd.SetPositionAndRotation(body.xrOrigin.TransformPoint(new Vector3(x, height, 0)), body.xrOrigin.rotation * Quaternion.Euler(0, yaw, 0));
            body.leftController.SetPositionAndRotation(body.hmd.position + body.xrOrigin.TransformVector(new Vector3(-0.25f, -0.4f, 0.25f)), body.xrOrigin.rotation);
            body.rightController.SetPositionAndRotation(body.hmd.position + body.xrOrigin.TransformVector(new Vector3(0.25f, -0.4f, 0.25f)), body.xrOrigin.rotation);
        }
        void SetInput(bool moving, bool pressed)
        {
            sprint.moveProvider.leftHandMoveInput.manualValue = moving ? Vector2.up : Vector2.zero;
            using (StateEvent.From(device, out var inputEvent))
            { click.WriteValueIntoEvent(pressed ? 1f : 0f, inputEvent); InputSystem.QueueEvent(inputEvent); }
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        void CheckTracking()
        {
            Check(Vector3.Distance(body.head.position, body.headTarget.position) < 0.002f, "Head alignment error");
            foreach (var arm in new[] { body.leftArm, body.rightArm })
            {
                float reach = Vector3.Distance(arm.upper.position, arm.lower.position) + Vector3.Distance(arm.lower.position, arm.tip.position);
                float targetDistance = Vector3.Distance(arm.upper.position, arm.target.position);
                float error = Vector3.Distance(arm.tip.position, arm.target.position);
                Check(error < Mathf.Max(0.005f, targetDistance - reach + 0.005f), "Arm IK error: " + error);
                Check(Quaternion.Angle(arm.tip.rotation, arm.target.rotation) < 0.1f, "Wrist rotation error");
            }
        }
        void LateUpdate()
        {
            if (!body || !device.added) return;
            // Use simulated time: XR startup stalls are capped by maximumDeltaTime and
            // must not count as a full second of gravity/animation evaluation.
            stageTime += Time.deltaTime;
            if (++frame < 30 || stageTime < (stage == 0 ? 1.5f : 1f)) return;
            frame = 0; stageTime = 0f;
            try
            {
                CheckTracking();
                switch (stage++)
                {
                    case 0:
                        Check(sprint.animator.GetFloat("Speed") < 0.05f, "Idle blend failed"); standingPelvis = body.xrOrigin.InverseTransformPoint(body.hips.position).y;
                        Check(Vector3.Distance(body.avatarRoot.localScale, Vector3.one * 1.4f) < 0.0001f, "User mesh scale changed");
                        var cc = body.xrOrigin.GetComponent<CharacterController>();
                        Check(!body.xrOrigin.GetComponent<LockHeight>().enabled, "Competing height writer is enabled");
                        Check(Mathf.Abs(cc.height - body.standingHeight) < 0.02f, "Capsule does not follow HMD height");
                        Check(Mathf.Abs(cc.center.y - cc.height * 0.5f - cc.skinWidth) < 0.002f, "Capsule bottom is not floor-relative");
                        var gravity = sprint.moveProvider.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionMediator>().GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>();
                        Check(Physics.Raycast(body.xrOrigin.position + Vector3.up * 0.3f, Vector3.down, out var ground, 20f, gravity.sphereCastLayerMask, QueryTriggerInteraction.Ignore), "Ground probe did not hit scene floor");
                        float floorError = Vector3.Dot(body.xrOrigin.position - ground.point, Vector3.up);
                        Check(Mathf.Abs(floorError) < 0.03f, "Tracking origin floor height error: " + floorError + " origin=" + body.xrOrigin.position + " CC grounded=" + cc.isGrounded + " Gravity grounded=" + gravity.isGrounded + " useGravity=" + gravity.useGravity + " gravityState=" + gravity.locomotionState);
                        Check(Mathf.Abs(body.leftArm.lower.localPosition.magnitude / leftLowerLength - body.armLengthMultiplier) < 0.02f, "Arm length adjustment mismatch: ratio=" + (body.leftArm.lower.localPosition.magnitude / leftLowerLength) + " expected=" + body.armLengthMultiplier);
                        Check(Mathf.Abs(body.leftArm.tip.localPosition.magnitude / leftHandLength - body.armLengthMultiplier) < 0.02f, "Forearm length adjustment failed");
                        report.AppendLine("PASS uniform 1.4 scale, arm length adjustment, XRI capsule ownership, ground alignment error=" + floorError.ToString("F4") + " m.");
                        Capture("standing"); report.AppendLine("PASS idle, head and both arm/wrist IK."); Pose(body.standingHeight, 0, 20); break;
                    case 1:
                        Check(Quaternion.Angle(body.avatarRoot.rotation, body.xrOrigin.rotation) < 1f, "Body ignored yaw dead zone");
                        report.AppendLine("PASS torso yaw dead zone."); roomOrigin = body.xrOrigin.position; Pose(body.standingHeight, 0.2f); break;
                    case 2:
                        Check(sprint.animator.GetFloat("Speed") < 0.05f, "Room-scale movement triggered walking");
                        Check(Vector3.ProjectOnPlane(body.xrOrigin.position - roomOrigin, body.xrOrigin.up).magnitude < 0.005f, "Body moved XR Origin");
                        report.AppendLine("PASS room-scale alignment without locomotion."); Pose(body.standingHeight); SetInput(true, false); break;
                    case 3:
                        walkMeasured = sprint.MeasuredSpeed;
                        Check(walkMeasured > 0.1f && !sprint.IsSprinting && sprint.animator.GetFloat("Speed") > 0.2f, "Walking failed, speed=" + walkMeasured);
                        report.AppendLine("PASS walking: " + walkMeasured.ToString("F2") + " m/s."); SetInput(true, true); break;
                    case 4:
                        report.AppendLine("Sprint input diagnostic: focused=" + Application.isFocused + " deviceEnabled=" + device.enabled + " rawClick=" + click.ReadValue() + " actionEnabled=" + sprint.sprintAction.action.enabled + " pressed=" + sprint.sprintAction.action.IsPressed() + " controls=" + string.Join(",", sprint.sprintAction.action.controls.Select(c=>c.path)));
                        Check(sprint.IsSprinting && sprint.MeasuredSpeed > walkMeasured * 1.3f && sprint.animator.GetFloat("Speed") > 1.2f, "Sprint failed: " + sprint.MeasuredSpeed);
                        report.AppendLine("PASS held click sprint and run blend: " + sprint.MeasuredSpeed.ToString("F2") + " m/s."); SetInput(true, false); break;
                    case 5:
                        Check(!sprint.IsSprinting && Mathf.Abs(sprint.moveProvider.moveSpeed - sprint.walkSpeed) < 0.01f, "Sprint release failed");
                        report.AppendLine("PASS release returns to walking."); SetInput(false, true); break;
                    case 6:
                        Check(!sprint.IsSprinting && sprint.animator.GetFloat("Speed") < 0.05f, "Neutral stick with click should idle: sprint=" + sprint.IsSprinting + " blend=" + sprint.animator.GetFloat("Speed") + " measured=" + sprint.MeasuredSpeed);
                        report.AppendLine("PASS neutral stick prevents sprint and returns to idle."); SetInput(false, false); Pose(1.1f); break;
                    case 7:
                        Check(body.CrouchAmount > 0.4f && body.xrOrigin.InverseTransformPoint(body.hips.position).y < standingPelvis - 0.3f, "Physical crouch failed");
                        foreach (var leg in new[] { body.leftLeg, body.rightLeg }) Check(Vector3.Distance(leg.tip.position, leg.target.position) < 0.02f, "Crouched foot lost its animated target");
                        Capture("crouching"); report.AppendLine("PASS crouch pelvis lowering and grounded foot targets."); Pose(body.standingHeight);
                        body.leftController.position = body.hmd.position + body.xrOrigin.TransformVector(new Vector3(-0.15f, 0.25f, 0.1f));
                        body.rightController.position = body.hmd.position + body.xrOrigin.TransformVector(new Vector3(0.15f, 0.25f, 0.1f)); break;
                    case 8:
                        report.AppendLine("PASS overhead arm IK.");
                        body.leftController.position = body.hmd.position + body.xrOrigin.TransformVector(new Vector3(0.15f, -0.25f, 0.12f));
                        body.rightController.position = body.hmd.position + body.xrOrigin.TransformVector(new Vector3(-0.15f, -0.25f, 0.12f)); break;
                    case 9:
                        report.AppendLine("PASS crossed/chest arm IK."); Pose(body.standingHeight, -0.2f, 100f); break;
                    case 10:
                        report.AppendLine("PASS changed tracking pose/recenter alignment."); Pose(body.standingHeight);
                        body.hmd.rotation = body.xrOrigin.rotation * Quaternion.Euler(65f, 0f, 0f); break;
                    case 11:
                        CaptureFirstPerson();
                        Pose(body.standingHeight);
                        calibrationHead = body.xrOrigin.InverseTransformPoint(body.hmd.position);
                        calibrationLeftRelative = body.leftController.position - body.hmd.position;
                        calibrationRightRelative = body.rightController.position - body.hmd.position;
                        body.GetComponent<JunkerTrackingCalibration>().floorOffset = -0.1f;
                        break;
                    case 12:
                        var actualHead = body.xrOrigin.InverseTransformPoint(body.hmd.position);
                        Check(Mathf.Abs(actualHead.y - calibrationHead.y + 0.1f) < 0.002f, "Common tracking floor correction was not applied");
                        Check(Vector3.Distance(body.leftController.position - body.hmd.position, calibrationLeftRelative) < 0.002f, "Left tracking separated from HMD");
                        Check(Vector3.Distance(body.rightController.position - body.hmd.position, calibrationRightRelative) < 0.002f, "Right tracking separated from HMD");
                        body.GetComponent<JunkerTrackingCalibration>().floorOffset = 0f;
                        report.AppendLine("PASS optional floor correction moves HMD and controllers together."); break;
                    case 13:
                        Check(Mathf.Abs(body.xrOrigin.InverseTransformPoint(body.hmd.position).y - calibrationHead.y) < 0.002f, "Floor correction accumulated offsets");
                        report.AppendLine("PASS floor correction restores without accumulated offset.");
                        Finish(null); break;
                }
            }
            catch (Exception e) { Finish(e); }
        }
        void CaptureFirstPerson()
        {
            var camera = body.hmd.GetComponent<Camera>();
            var visibility = body.avatarRoot.GetComponentInChildren<JunkerLocalHead>();
            bool observedHeadless = false;
            void Observe(UnityEngine.Rendering.ScriptableRenderContext context, Camera renderingCamera)
            { if (renderingCamera == camera) observedHeadless = visibility.body.sharedMesh == visibility.headlessMesh; }
            var target = new RenderTexture(1000, 1000, 24);
            var previous = RenderTexture.active; var oldTarget = camera.targetTexture;
            var texture = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += Observe;
            try {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0); texture.Apply();
                File.WriteAllBytes("Temp/JunkerBody-firstperson.png", texture.EncodeToPNG());
                Check(observedHeadless, "Player camera did not select headless mesh");
                Check(visibility.body.sharedMesh == visibility.fullMesh, "Full mesh not restored after player camera render");
                report.AppendLine("PASS local camera head removal, full-mesh restoration and first-person capture.");
            }
            finally {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= Observe;
                RenderTexture.active = previous; camera.targetTexture = oldTarget;
                Destroy(texture); target.Release(); Destroy(target);
            }
        }
        void Capture(string label)
        {
            var go = new GameObject("Temporary validation camera");
            var camera = go.AddComponent<Camera>();
            camera.transform.position = body.hips.position + body.xrOrigin.TransformVector(new Vector3(0.4f, 0.5f, 3f));
            camera.transform.LookAt(body.hips.position, body.xrOrigin.up);
            camera.nearClipPlane = 0.05f; camera.farClipPlane = 100f;
            var target = new RenderTexture(900, 1000, 24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(900, 1000, TextureFormat.RGB24, false);
            try {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 900, 1000), 0, 0); texture.Apply();
                File.WriteAllBytes("Temp/JunkerBody-" + label + ".png", texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; camera.targetTexture = null; Destroy(texture); target.Release(); Destroy(target); Destroy(go); }
        }
        void Finish(Exception error)
        {
            enabled = false;
            if (device != null && device.added) InputSystem.RemoveDevice(device);
            report.AppendLine(error == null ? "PASS all synthetic Play Mode checks. Quest hardware fit, grabbing and visual comfort still require headset testing." : "FAIL " + error);
            File.WriteAllText("Temp/JunkerBody.playvalidation", report.ToString());
            EditorApplication.isPlaying = false;
        }
    }
}
