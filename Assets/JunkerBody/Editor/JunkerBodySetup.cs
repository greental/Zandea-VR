using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using Unity.XR.CoreUtils;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JunkerBody.Editor
{
    public static class JunkerBodySetup
    {
        const string ScenePath = "Assets/Scenes/JunkerBody.unity";
        const string Folder = "Assets/JunkerBody";
        const string Source = "Assets/MyAssets/mesh/Charecters/Junker/Meshy_AI_Rustbound_Sentinel_biped (1)/Meshy_AI_Rustbound_Sentinel_biped/";
        static T GetOrAdd<T>(GameObject go) where T : Component { var component = go.GetComponent<T>(); return component ? component : go.AddComponent<T>(); }
        static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
        static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        static Transform Target(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (!child) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            return child;
        }
        static void SaveAsset(UnityEngine.Object asset, string path)
        {
            var old = AssetDatabase.LoadMainAssetAtPath(path);
            if (old) { EditorUtility.CopySerialized(asset, old); UnityEngine.Object.DestroyImmediate(asset); }
            else AssetDatabase.CreateAsset(asset, path);
        }
        static AnimationClip CopyClip(string sourceName, string name, Transform root)
        {
            string path = Source + "Meshy_AI_Rustbound_Sentinel_biped_Animation_" + sourceName + "_withSkin.glb";
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().OrderBy(c => c.name.Length).First();
            var clip = UnityEngine.Object.Instantiate(source);
            clip.name = name; clip.legacy = false;
            // GLB clips are transform animations on the same skeleton. Keep bone paths and
            // vertical gait motion, but remove horizontal trajectory from hips/root.
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform)) continue;
                var t = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                if (!t) throw new InvalidOperationException("Unresolved animation path: " + binding.path);
                if ((t == root || t.name == "target_character" || t.name == "mixamorig:Hips") && binding.propertyName.StartsWith("m_LocalPosition."))
                {
                    int axis = binding.propertyName.EndsWith(".x") ? 0 : binding.propertyName.EndsWith(".y") ? 1 : 2;
                    if (axis != 1 || t.name != "mixamorig:Hips")
                        AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, clip.length, t.localPosition[axis]));
                }
                if ((t == root || t.name == "target_character") && binding.propertyName.StartsWith("m_LocalRotation."))
                    AnimationUtility.SetEditorCurve(clip, binding, null);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true; settings.loopBlend = true;
            settings.keepOriginalPositionXZ = true; settings.keepOriginalPositionY = true; settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            SaveAsset(clip, Folder + "/" + name + ".anim");
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + name + ".anim");
        }
        static VRBodyController.Limb Limb(Transform root, Transform targets, string side, bool leg)
        {
            string prefix = "mixamorig:" + side;
            var limb = new VRBodyController.Limb {
                upper = Find(root, prefix + (leg ? "UpLeg" : "Arm")),
                lower = Find(root, prefix + (leg ? "Leg" : "ForeArm")),
                tip = Find(root, prefix + (leg ? "Foot" : "Hand")),
                target = Target(targets, side + (leg ? "FootTarget" : "WristTarget")),
                hint = Target(targets, side + (leg ? "KneeHint" : "ElbowHint")),
                hintOffset = leg ? new Vector3(0, 0, 0.7f) : new Vector3(side == "Left" ? -0.45f : 0.45f, -0.35f, -0.25f),
                wristPositionOffset = new Vector3(0f, -0.02f, -0.06f)
            };
            if (!leg)
            {
                Vector3 fingerDirection = limb.tip.GetChild(0).position - limb.tip.position;
                Quaternion aligned = Quaternion.FromToRotation(fingerDirection, root.forward) * limb.tip.rotation;
                limb.wristRotationOffset = (Quaternion.Inverse(root.rotation) * aligned).eulerAngles;
            }
            return limb;
        }
        [MenuItem("Zandea/JunkerBody/Configure scene")]
        public static void Configure()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Configure in Edit Mode.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var origin = Components<XROrigin>(scene).Single();
            var root = origin.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Meshy_AI_Rustbound_Sentinel_biped_Animation_Casual_Walk"));
            if (!root) throw new InvalidOperationException("Existing Junker model not found.");
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var rig = Target(origin.transform, "JunkerBody");
            root.SetParent(rig, true);
            var targets = Target(rig, "TrackingTargets");
            var body = GetOrAdd<VRBodyController>(rig.gameObject);
            body.xrOrigin = origin.Origin.transform;
            body.hmd = origin.Camera.transform;
            body.leftController = Find(origin.transform, "LeftInteractor");
            body.rightController = Find(origin.transform, "RightInteractor");
            body.avatarRoot = root;
            body.hips = Find(root, "mixamorig:Hips");
            body.chest = Find(root, "mixamorig:Spine2");
            body.head = Find(root, "mixamorig:Head");
            body.headTarget = Target(targets, "HeadTarget");
            FitAvatar(body);
            body.headRotationOffset = (Quaternion.Inverse(root.rotation) * body.head.rotation).eulerAngles;
            body.leftArm = Limb(root, targets, "Left", false); body.rightArm = Limb(root, targets, "Right", false);
            body.leftLeg = Limb(root, targets, "Left", true); body.rightLeg = Limb(root, targets, "Right", true);
            body.leftOpticalTracking = Find(origin.transform, "Left Hand Tracking").GetComponent<XRHandTrackingEvents>();
            body.rightOpticalTracking = Find(origin.transform, "Right Hand Tracking").GetComponent<XRHandTrackingEvents>();
            body.leftOpticalWrist = Find(origin.transform, "L_Wrist"); body.rightOpticalWrist = Find(origin.transform, "R_Wrist");
            body.opticalHandVisuals = body.leftOpticalTracking.GetComponentsInChildren<Renderer>(true)
                .Concat(body.rightOpticalTracking.GetComponentsInChildren<Renderer>(true)).ToArray();
            var animator = GetOrAdd<Animator>(root.gameObject);
            body.animator = animator;
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var idle = CopyClip("Alert", "JunkerIdle", root);
            var walk = CopyClip("Casual_Walk", "JunkerWalk", root);
            var run = CopyClip("Running", "JunkerRun", root);
            string controllerPath = Folder + "/JunkerLocomotion.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
                var tree = new BlendTree { name = "Idle Walk Run", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
                tree.AddChild(idle, 0); tree.AddChild(walk, 1); tree.AddChild(run, 2);
                AssetDatabase.AddObjectToAsset(tree, controller);
                var state = controller.layers[0].stateMachine.AddState("Locomotion"); state.motion = tree;
                controller.layers[0].stateMachine.defaultState = state;
                EditorUtility.SetDirty(controller);
            }
            var avatar = AvatarBuilder.BuildGenericAvatar(root.gameObject, ""); avatar.name = "JunkerAvatar";
            if (!avatar.isValid) throw new InvalidOperationException("Generic avatar invalid.");
            SaveAsset(avatar, Folder + "/JunkerAvatar.asset");
            animator.avatar = AssetDatabase.LoadAssetAtPath<Avatar>(Folder + "/JunkerAvatar.asset");
            animator.runtimeAnimatorController = controller;
            var sprint = GetOrAdd<JunkerSprint>(rig.gameObject);
            sprint.moveProvider = origin.GetComponentsInChildren<ContinuousMoveProvider>(true).Single();
            sprint.xrOrigin = origin.Origin.transform; sprint.animator = animator;
            sprint.walkSpeed = sprint.moveProvider.moveSpeed; sprint.sprintSpeed = sprint.walkSpeed * 1.8f;
            sprint.crouchSpeed = sprint.walkSpeed * .4f;
            BuildHeadVisibility(body, origin.Camera);
            EditorUtility.SetDirty(body); EditorUtility.SetDirty(sprint); EditorUtility.SetDirty(animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(origin);
            EditorSceneManager.MarkSceneDirty(scene);
            foreach (var guid in AssetDatabase.FindAssets("", new[] { Folder }))
                AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
            Validate(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
            File.WriteAllText("Temp/JunkerBody.result", "CONFIGURED and saved JunkerBody.\n" + File.ReadAllText("Temp/JunkerBody.validation"));
        }
        [MenuItem("Zandea/JunkerBody/Inspect fit and floor")]
        public static void InspectFit()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var body = Components<VRBodyController>(scene).Single();
            var origin = Components<XROrigin>(scene).Single();
            var cc = origin.GetComponent<CharacterController>();
            var text = new StringBuilder();
            text.AppendLine($"Playing={EditorApplication.isPlaying} Paused={EditorApplication.isPaused} Dirty={scene.isDirty}");
            text.AppendLine($"Origin {origin.transform.position:F4} scale={origin.transform.lossyScale:F4} requested={origin.RequestedTrackingOriginMode} current={origin.CurrentTrackingOriginMode} deviceYOffset={origin.CameraYOffset}");
            for(var t = body.hmd; t; t=t.parent) text.AppendLine($"{t.name}: local={t.localPosition:F4} scale={t.localScale:F4}");
            text.AppendLine($"CC enabled={cc.enabled} height={cc.height} radius={cc.radius} center={cc.center:F4} skin={cc.skinWidth} step={cc.stepOffset} minMove={cc.minMoveDistance} bottomLocal={cc.center.y - cc.height/2}");
            var locked = origin.GetComponent<LockHeight>();
            text.AppendLine($"LockHeight enabled={locked.enabled} height={locked.fixedHeight}");
            foreach (var c in origin.GetComponentsInChildren<Collider>(true)) text.AppendLine($"Player collider {c.name} layer={c.gameObject.layer} trigger={c.isTrigger} active={c.gameObject.activeInHierarchy} enabled={c.enabled}");
            text.AppendLine($"Avatar scale={body.avatarRoot.localScale:F4} standingEyeHeight={body.standingHeight} headOffset={body.headPositionOffset:F4}");
            text.AppendLine($"Avatar world eye estimate={body.head.position:F4} HMD={body.hmd.position:F4}");
            foreach(var collider in Components<Collider>(scene).Where(c=>!c.transform.IsChildOf(origin.transform)))
                if(collider.Raycast(new Ray(origin.transform.position+Vector3.up*5f,Vector3.down),out var hit,20f)) text.AppendLine($"Ground hit {collider.name}: {hit.point:F4} bounds={collider.bounds}");
            File.WriteAllText("Temp/JunkerBody.fit",text.ToString());
        }
        static void FitAvatar(VRBodyController body)
        {
            // Overall size is explicitly user-controlled. Eye/crouch height must not
            // silently shrink the user's chosen mesh size.
            body.avatarRoot.localScale = Vector3.one * body.meshScale;
            var front = Find(body.avatarRoot, "headfront");
            var crown = Find(body.avatarRoot, "mixamorig:HeadTop_End");
            float eyeRise;
            Vector3 eye;
            eyeRise = Vector3.Dot(crown.position - body.head.position, body.avatarRoot.up) * 0.55f;
            eye = front.position + body.avatarRoot.up * eyeRise;
            body.headPositionOffset = Quaternion.Inverse(body.avatarRoot.rotation) * (body.head.position - eye);
            body.headPositionOffset.x = 0f;
        }
        [MenuItem("Zandea/JunkerBody/Apply avatar size and eye offset")]
        public static void FitStandingHeight()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Fit in Edit Mode.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) throw new InvalidOperationException("Open JunkerBody first.");
            var body = Components<VRBodyController>(scene).Single();
            Undo.RecordObjects(new UnityEngine.Object[] { body, body.avatarRoot }, "Apply Junker avatar size");
            FitAvatar(body); EditorUtility.SetDirty(body); EditorSceneManager.MarkSceneDirty(scene);
        }
        [MenuItem("Zandea/JunkerBody/Apply player fit and floor correction")]
        public static void ApplyPlayerFitAndFloor()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Apply fit in Edit Mode.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) throw new InvalidOperationException("Open JunkerBody first.");
            var body = Components<VRBodyController>(scene).Single();
            var origin = Components<XROrigin>(scene).Single();
            var cc = origin.GetComponent<CharacterController>();
            var locked = origin.GetComponent<LockHeight>();
            var gravity = origin.GetComponentInChildren<GravityProvider>(true);
            Undo.RecordObjects(new UnityEngine.Object[] { body, body.avatarRoot, cc, locked, gravity }, "Junker player fit and floor correction");
            body.meshScale = 1.4f; body.playerHeight = 1.78f;
            body.standingHeight = 1.66f; body.armLengthMultiplier = 1.1f;
            FitAvatar(body);
            // Let the existing XRI manipulator own its capsule; LockHeight otherwise
            // replaces its HMD-height capsule with a 0.6 m capsule every LateUpdate.
            locked.enabled = false;
            cc.skinWidth = 0.02f; cc.minMoveDistance = 0f;
            cc.height = body.playerHeight;
            cc.center = new Vector3(0f, cc.height * 0.5f + cc.skinWidth, 0f);
            // Radius + cast distance should end at the origin floor, not 4 cm below it.
            gravity.sphereCastDistanceBuffer = -gravity.sphereCastRadius;
            var calibration = GetOrAdd<JunkerTrackingCalibration>(body.gameObject);
            calibration.xrOrigin = origin; calibration.trackingOffset = body.hmd.parent;
            calibration.leftController = body.leftController;
            calibration.rightController = body.rightController;
            foreach (var obj in new UnityEngine.Object[] { locked, cc, gravity })
            { EditorUtility.SetDirty(obj); PrefabUtility.RecordPrefabInstancePropertyModifications(obj); }
            EditorUtility.SetDirty(body); EditorUtility.SetDirty(calibration);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            InspectFit();
        }
        static void BuildHeadVisibility(VRBodyController body, Camera camera)
        {
            var renderer = body.avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>().First(t => t.name == "Mesh_0");
            var original = renderer.sharedMesh;
            var mesh = UnityEngine.Object.Instantiate(original); mesh.name = "JunkerLocalBody";
            var headBones = new HashSet<int>();
            for (int i = 0; i < renderer.bones.Length; i++)
                if (renderer.bones[i] == body.head || renderer.bones[i].IsChildOf(body.head) || renderer.bones[i].name == "mixamorig:Neck") headBones.Add(i);
            var weights = original.GetAllBoneWeights(); var counts = original.GetBonesPerVertex();
            var hidden = new bool[original.vertexCount]; int index = 0;
            for (int v = 0; v < hidden.Length; v++)
            {
                float weight = 0;
                for (int j = 0; j < counts[v]; j++) { var w = weights[index++]; if (headBones.Contains(w.boneIndex)) weight += w.weight; }
                hidden[v] = weight >= 0.25f;
            }
            weights.Dispose(); counts.Dispose();
            int removed = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var triangles = original.GetTriangles(s); var keep = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                    if (hidden[triangles[i]] || hidden[triangles[i+1]] || hidden[triangles[i+2]]) removed++;
                    else { keep.Add(triangles[i]); keep.Add(triangles[i+1]); keep.Add(triangles[i+2]); }
                mesh.SetTriangles(keep, s, false);
            }
            if (removed == 0) throw new InvalidOperationException("No head triangles removed.");
            SaveAsset(mesh, Folder + "/JunkerLocalBody.asset");
            var visibility = GetOrAdd<JunkerLocalHead>(renderer.gameObject);
            visibility.localCamera = camera; visibility.body = renderer; visibility.fullMesh = original;
            visibility.headlessMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/JunkerLocalBody.asset");
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.updateWhenOffscreen = true;
            var shadow = Target(renderer.transform.parent, "JunkerFullBodyShadow");
            var shadowRenderer = GetOrAdd<SkinnedMeshRenderer>(shadow.gameObject);
            shadowRenderer.sharedMesh = original; shadowRenderer.sharedMaterials = renderer.sharedMaterials;
            shadowRenderer.bones = renderer.bones; shadowRenderer.rootBone = renderer.rootBone;
            shadowRenderer.localBounds = renderer.localBounds; shadowRenderer.updateWhenOffscreen = true;
            shadowRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            // Hide only duplicate visual renderers; tracking, colliders, animators and interactors remain enabled.
            foreach (var hand in new[] { body.leftController, body.rightController })
                foreach (var r in hand.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(r); }
            EditorUtility.SetDirty(visibility);
        }
        static void Validate(Scene scene)
        {
            var bodies = Components<VRBodyController>(scene);
            if (bodies.Length != 1) throw new InvalidOperationException("Expected exactly one body controller.");
            var body = bodies[0];
            if (!body.leftArm.Valid || !body.rightArm.Valid || !body.leftLeg.Valid || !body.rightLeg.Valid) throw new InvalidOperationException("Incomplete IK references.");
            foreach (var limb in new[] { body.leftArm, body.rightArm, body.leftLeg, body.rightLeg })
                if (!limb.lower.IsChildOf(limb.upper) || !limb.tip.IsChildOf(limb.lower) || limb.target.IsChildOf(body.avatarRoot)) throw new InvalidOperationException("Invalid IK parenting.");
            if (body.hmd.IsChildOf(body.avatarRoot) || !body.animator.avatar.isValid || !body.animator.runtimeAnimatorController) throw new InvalidOperationException("Invalid tracking/animator.");
            foreach (var t in Components<Transform>(scene)) if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0) throw new InvalidOperationException("Missing script: " + t.name);
            if (Components<ContinuousMoveProvider>(scene).Length != 1) throw new InvalidOperationException("Unexpected movement provider count.");
            var sb = new StringBuilder("PASS: references, bone chains, target parenting, valid Generic avatar, Animator, no missing scripts, one continuous movement provider.\n");
            foreach (var clip in body.animator.runtimeAnimatorController.animationClips)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(Transform) && binding.path.Length > 0 && !body.avatarRoot.Find(binding.path)) throw new InvalidOperationException("Unresolved clip binding: " + binding.path);
                sb.AppendLine($"{clip.name}: {clip.length:F2}s, legacy={clip.legacy}, curves={AnimationUtility.GetCurveBindings(clip).Length}");
            }
            File.WriteAllText("Temp/JunkerBody.validation", sb.ToString());
        }
        [MenuItem("Zandea/JunkerBody/Validate saved scene")]
        public static void ValidateSaved()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath); bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            else if (SceneManager.sceneCount == 1 && !scene.isDirty)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else throw new InvalidOperationException("Reload validation requires a saved JunkerBody as the sole open scene, or JunkerBody closed.");
            Validate(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
            File.WriteAllText("Temp/JunkerBody.result", File.ReadAllText("Temp/JunkerBody.validation"));
        }
    }
}
