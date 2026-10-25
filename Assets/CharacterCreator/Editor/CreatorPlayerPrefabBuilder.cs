using System;
using System.IO;
using System.Linq;
using System.Text;
using JunkerBody;
using JunkerEquipment;
using KenshiVR.Bandaging;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    [InitializeOnLoad]
    public static class CreatorPlayerPrefabBuilder
    {
        const string MainPath = "Assets/Main.unity";
        const string ModelPath = "Assets/CharacterCreator/Models/male_creator.glb";
        const string AuditRequest = "Temp/CreatorPlayer.audit-request";
        const string AuditResult = "Temp/CreatorPlayer.audit.txt";
        const string BuildRequest = "Temp/CreatorPlayer.build-request";
        const string BuildResult = "Temp/CreatorPlayer.build.txt";
        const string OutputFolder = "Assets/CharacterCreator/Player";
        const string PrefabPath = OutputFolder + "/VR player - Man Creator.prefab";

        static CreatorPlayerPrefabBuilder() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (File.Exists(BuildRequest))
            {
                File.Delete(BuildRequest);
                try { Build(); }
                catch (Exception error) { File.WriteAllText(BuildResult, "FAIL " + error); Debug.LogException(error); }
                return;
            }
            if (!File.Exists(AuditRequest)) return;
            File.Delete(AuditRequest);
            try { Audit(); }
            catch (Exception error) { File.WriteAllText(AuditResult, "FAIL " + error); Debug.LogException(error); }
        }

        [MenuItem("Zandea/Character Creator/Build Man player prefab from Main")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build in Edit Mode");
            var originalBody = UnityEngine.Object.FindObjectsByType<VRBodyController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(value => value.gameObject.scene.path == MainPath);
            var source = PrefabUtility.GetOutermostPrefabInstanceRoot(originalBody.gameObject);
            if (!source) throw new InvalidOperationException("Main player must be a prefab instance");
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!modelAsset) throw new InvalidOperationException("Creator model is missing");
            if (!AssetDatabase.IsValidFolder(OutputFolder)) AssetDatabase.CreateFolder("Assets/CharacterCreator", "Player");
            GameObject copy = null;
            try
            {
                copy = UnityEngine.Object.Instantiate(source);
                copy.name = "VR player - Man Creator";
                if (PrefabUtility.IsAnyPrefabInstanceRoot(copy))
                    PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var body = copy.GetComponentInChildren<VRBodyController>(true);
                if (!body || !body.avatarRoot) throw new InvalidOperationException("Copied player has no VR body");
                var oldVisual = body.avatarRoot;
                var oldMesh = oldVisual.Find("target_character");
                if (!oldMesh || !oldMesh.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any())
                    throw new InvalidOperationException("Junker target_character visual subtree is missing");
                var oldFeet = (body.leftLeg.tip.position + body.rightLeg.tip.position) * .5f;
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                model.name = "Man customizable body";
                model.transform.SetParent(oldVisual.parent, false);
                model.transform.SetSiblingIndex(oldVisual.GetSiblingIndex());
                model.transform.SetPositionAndRotation(oldVisual.position, oldVisual.rotation);
                model.transform.localScale = Vector3.one;
                Transform Bone(string name) => model.GetComponentsInChildren<Transform>(true)
                    .Single(value => value.name == name);
                var hips = Bone("CC_Base_Hip_02");
                var chest = Bone("CC_Base_Spine02_035");
                var head = Bone("CC_Base_Head_038");
                var leftUpper = Bone("CC_Base_L_Upperarm_050");
                var leftLower = Bone("CC_Base_L_Forearm_051");
                var leftHand = Bone("CC_Base_L_Hand_055");
                var rightUpper = Bone("CC_Base_R_Upperarm_074");
                var rightLower = Bone("CC_Base_R_Forearm_077");
                var rightHand = Bone("CC_Base_R_Hand_081");
                var leftThigh = Bone("CC_Base_L_Thigh_04");
                var leftCalf = Bone("CC_Base_L_Calf_05");
                var leftFoot = Bone("CC_Base_L_Foot_06");
                var rightThigh = Bone("CC_Base_R_Thigh_00");
                var rightCalf = Bone("CC_Base_R_Calf_021");
                var rightFoot = Bone("CC_Base_R_Foot_022");
                model.transform.position += oldFeet - (leftFoot.position + rightFoot.position) * .5f;
                body.avatarRoot = model.transform;
                body.hips = hips; body.chest = chest; body.head = head;
                Bind(body.leftArm, leftUpper, leftLower, leftHand, model.transform, body.leftController);
                Bind(body.rightArm, rightUpper, rightLower, rightHand, model.transform, body.rightController);
                Bind(body.leftLeg, leftThigh, leftCalf, leftFoot, model.transform, null);
                Bind(body.rightLeg, rightThigh, rightCalf, rightFoot, model.transform, null);
                body.armLengthMultiplier = 1f; // CC_Base limb offsets live in scale-compensation parents.
                body.meshScale = 1f;
                body.playerHeight = Mathf.Max(1.5f, Vector3.Distance(
                    (leftFoot.position + rightFoot.position) * .5f, head.position) + .12f);
                body.headRotationOffset = (Quaternion.Inverse(model.transform.rotation) * head.rotation).eulerAngles;
                var face = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(renderer => renderer.name == "Object_9");
                var eye = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(renderer => renderer.name == "Object_16" || renderer.name == "Object_18")
                    .Select(renderer => renderer.bounds.center).ToArray();
                var eyePosition = eye.Length == 2 ? (eye[0] + eye[1]) * .5f : head.position + model.transform.up * .09f;
                body.headPositionOffset = Quaternion.Inverse(model.transform.rotation) * (head.position - eyePosition);
                var animator = model.GetComponentInChildren<Animator>(true);
                if (!animator) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/JunkerBody/JunkerLocomotion.controller");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                body.animator = animator;
                var sprint = body.GetComponent<JunkerSprint>();
                if (sprint) sprint.animator = animator;
                // The creator rig is Humanoid, so use the same full walk/run cycle as
                // Junker instead of the old small procedural foot shuffle.
                var appearance = body.gameObject.AddComponent<CharacterAppearanceController>();
                appearance.visualRoot = model.transform;
                appearance.vrBody = body;
                var cosmetics = body.gameObject.AddComponent<CharacterCosmeticsController>();
                cosmetics.appearance = appearance;
                cosmetics.skeletonRoot = model.transform;
                cosmetics.catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>("Assets/CharacterCreator/CosmeticCatalog.asset");
                var wearable = body.gameObject.AddComponent<PlayerWearableClothing>();
                wearable.body = body;
                wearable.cosmetics = cosmetics;
                var localHead = body.gameObject.AddComponent<CreatorPlayerLocalHead>();
                localHead.localCamera = body.hmd ? body.hmd.GetComponent<Camera>() : null;
                localHead.visualRoot = model.transform;
                localHead.cosmetics = cosmetics;
                foreach (var glove in body.GetComponentsInChildren<JunkerGloveHand>(true))
                    foreach (var renderer in glove.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                UnityEngine.Object.DestroyImmediate(oldMesh.gameObject);
                appearance.Initialize();
                appearance.Apply(appearance.Data);
                cosmetics.Refresh();
                var saved = PrefabUtility.SaveAsPrefabAsset(copy, PrefabPath);
                if (!saved) throw new InvalidOperationException("Prefab save failed");
                AssetDatabase.SaveAssets();
                ValidateAsset(saved);
                File.WriteAllText(BuildResult, "PASS " + PrefabPath + "\n" +
                    "Creator bones wired: head, chest, hips, both arms and both legs. Appearance and armor slots present.\n" +
                    "Source Main player unchanged: " + source.name);
            }
            finally { if (copy) UnityEngine.Object.DestroyImmediate(copy); }
        }

        static void ValidateAsset(GameObject prefab)
        {
            var body = prefab.GetComponentInChildren<VRBodyController>(true);
            var sprint = prefab.GetComponentInChildren<JunkerSprint>(true);
            var rig = prefab.GetComponentInChildren<JunkerEquipmentRig>(true);
            var wounds = prefab.GetComponentInChildren<PlayerWounds>(true);
            var appearance = prefab.GetComponentInChildren<CharacterAppearanceController>(true);
            var cosmetics = prefab.GetComponentInChildren<CharacterCosmeticsController>(true);
            var wearable = prefab.GetComponentInChildren<PlayerWearableClothing>(true);
            var head = prefab.GetComponentInChildren<CreatorPlayerLocalHead>(true);
            if (!body || !body.enabled || !body.animator || !body.animator.runtimeAnimatorController ||
                !sprint || sprint.animator != body.animator || !rig || rig.body != body ||
                !wounds || wounds.body != body || !appearance || appearance.vrBody != body ||
                appearance.visualRoot != body.avatarRoot || !cosmetics || cosmetics.skeletonRoot != body.avatarRoot ||
                !cosmetics.catalog || !wearable || wearable.cosmetics != cosmetics || wearable.body != body ||
                !head || head.visualRoot != body.avatarRoot ||
                head.cosmetics != cosmetics)
                throw new InvalidOperationException("Saved player is missing a retained system or creator binding");
            if (!body.leftArm.Valid || !body.rightArm.Valid || !body.leftLeg.Valid || !body.rightLeg.Valid ||
                !body.head.IsChildOf(body.avatarRoot) || !body.hips.IsChildOf(body.avatarRoot) ||
                !body.chest.IsChildOf(body.avatarRoot) ||
                new[] { body.leftArm, body.rightArm, body.leftLeg, body.rightLeg }.Any(limb =>
                    !limb.lower.IsChildOf(limb.upper) || !limb.tip.IsChildOf(limb.lower) ||
                    Vector3.Distance(limb.upper.position, limb.lower.position) < .08f ||
                    Vector3.Distance(limb.lower.position, limb.tip.position) < .08f))
                throw new InvalidOperationException("Saved creator IK chain is incomplete");
            if (prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Count(renderer => renderer.transform.IsChildOf(body.avatarRoot)) < 10 ||
                prefab.GetComponentsInChildren<Transform>(true).Any(transform => transform.name == "target_character"))
                throw new InvalidOperationException("Creator skin is missing or Junker skin remains");
            if (!prefab.GetComponentInChildren<BackpackInventory>(true) ||
                !prefab.GetComponentInChildren<CharacterController>(true))
                throw new InvalidOperationException("Backpack or locomotion capsule was lost");
        }

        static void Bind(VRBodyController.Limb limb, Transform upper, Transform lower, Transform tip,
            Transform root, Transform controller)
        {
            limb.upper = upper; limb.lower = lower; limb.tip = tip;
            if (controller) limb.wristRotationOffset =
                (Quaternion.Inverse(controller.rotation) * tip.rotation).eulerAngles;
        }

        static RuntimeAnimatorController EnsureController()
        {
            const string clipPath = OutputFolder + "/ManPlayerRest.anim";
            const string controllerPath = OutputFolder + "/ManPlayerLocomotion.controller";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (!clip)
            {
                clip = new AnimationClip { name = "ManPlayerRest" };
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
                var state = controller.layers[0].stateMachine.AddState("Locomotion");
                state.motion = clip;
                controller.layers[0].stateMachine.defaultState = state;
            }
            return controller;
        }

        [MenuItem("Zandea/Character Creator/Audit Main player conversion")]
        public static void Audit()
        {
            var body = UnityEngine.Object.FindObjectsByType<VRBodyController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(value => value.gameObject.scene.path == MainPath);
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(body.gameObject);
            if (!root) root = body.transform.root.gameObject;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) throw new InvalidOperationException("Creator model asset missing");
            var report = new StringBuilder();
            report.AppendLine("MAIN PLAYER " + root.name + " source=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root));
            report.AppendLine("root " + root.transform.position + " avatar=" + body.avatarRoot.name +
                " local=" + body.avatarRoot.localPosition + " scale=" + body.avatarRoot.localScale);
            report.AppendLine("OLD AVATAR CHILDREN");
            foreach (var child in body.avatarRoot.GetComponentsInChildren<Transform>(true))
                if (child.parent == body.avatarRoot || child.GetComponent<MonoBehaviour>())
                    report.AppendLine(Path(body.avatarRoot, child) + " components=" +
                        string.Join(",", child.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name)));
            report.AppendLine("OUTSIDE REFERENCES INTO OLD AVATAR");
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component || component.transform.IsChildOf(body.avatarRoot)) continue;
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.NextVisible(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is Component referenced &&
                        referenced.transform.IsChildOf(body.avatarRoot))
                        report.AppendLine(component.GetType().Name + " " + Path(root.transform, component.transform) +
                            "." + property.propertyPath + " -> " + Path(body.avatarRoot, referenced.transform));
            }
            report.AppendLine("CREATOR MODEL");
            var animator = model.GetComponentInChildren<Animator>(true);
            report.AppendLine("animator=" + (animator ? animator.name : "none") + " avatar=" +
                (animator && animator.avatar ? animator.avatar.name : "none"));
            foreach (var bone in model.GetComponentsInChildren<Transform>(true))
                if (bone.name.StartsWith("CC_Base_") && (bone.name.Contains("Hip_") || bone.name.Contains("Spine02_") ||
                    bone.name.Contains("Head_") || bone.name.Contains("Upperarm_") || bone.name.Contains("Forearm_") ||
                    bone.name.Contains("Hand_") || bone.name.Contains("Thigh_") || bone.name.Contains("Calf_") ||
                    bone.name.Contains("Foot_")) && !bone.name.EndsWith("scaleCompensation"))
                    report.AppendLine(bone.name + " pos=" + bone.position + " local=" + bone.localPosition);
            File.WriteAllText(AuditResult, report.ToString());
        }

        static string Path(Transform root, Transform child)
        {
            if (root == child) return ".";
            string path = child.name;
            for (var current = child.parent; current && current != root; current = current.parent)
                path = current.name + "/" + path;
            return path;
        }
    }
}
