using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class MocapTestBuilder
{
    const string Folder = "Assets/MeleeCombat/MocapTest";
    const string Source = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";
    const string AttackPath = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Combat/Polearm/HumanM@AttackPolearm01.fbx";
    public static void BuildAndValidate() { Build(); Validate(); }

    [MenuItem("Zandea/Melee Combat/Build isolated mocap test")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/MeleeCombat", "MocapTest");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        var clip = AssetDatabase.LoadAllAssetsAtPath(AttackPath).OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == "HumanM@AttackPolearm01");
        if (!source || !clip) throw new InvalidOperationException("Original prefab or imported polearm attack is missing.");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = "StarvingBandit_MocapTest";
        try
        {
            var brain = instance.GetComponent<StarvingBandit>();
            var motion = instance.GetComponent<BanditRigMotion>();
            var agent = instance.GetComponent<NavMeshAgent>();
            var animator = instance.GetComponentInChildren<Animator>(true);
            if (!brain || !motion || !animator || !brain.staff) throw new InvalidOperationException("Original bandit dependencies are incomplete.");
            brain.enabled = false;
            motion.enabled = false;
            if (agent) agent.enabled = false;
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is NpcGeneratedWeapon) behaviour.enabled = false;

            // This experimental staff is a visual probe; its original physics integration is retained but disabled.
            var staff = brain.staff;
            staff.enabled = false;
            foreach (var behaviour in staff.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
            var body = staff.GetComponent<Rigidbody>();
            if (body) { body.isKinematic = true; body.useGravity = false; }
            foreach (var collider in staff.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

            var right = Find(instance.transform, "CC_Base_R_Hand_081");
            var left = Find(instance.transform, "CC_Base_L_Hand_055");
            var authoredRight = staff.transform.InverseTransformPoint(right.position);
            var authoredLeft = staff.transform.InverseTransformPoint(left.position);
            var authoredRightRotation = Quaternion.Inverse(staff.transform.rotation) * right.rotation;
            var authoredLeftRotation = Quaternion.Inverse(staff.transform.rotation) * left.rotation;
            var authoredFingers = animator.GetComponentsInChildren<Transform>(true)
                .Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^CC_Base_[LR]_(Index|Mid|Ring|Pinky|Thumb)[123]_\d+$"))
                .Select(t => new MocapStaffPreview.FingerPose { bone = t, rotation = t.localRotation }).ToArray();
            // A posed guard is not an Avatar reference pose. Restore the imported skeleton before
            // constructing this experiment's Avatar; keep the authored grip as separate local data.
            var restModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CharacterCreator/Models/male_creator.glb");
            var restBones = restModel.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("CC_Base_"))
                .ToDictionary(t => t.name);
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
                if (restBones.TryGetValue(bone.name, out var rest))
                { bone.localPosition = rest.localPosition; bone.localRotation = rest.localRotation; bone.localScale = rest.localScale; }

            var avatarPath = Folder + "/StarvingBandit_MocapAvatar.asset";
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
            var freshAvatar = MakeAvatar(animator.gameObject);
            if (!avatar) { avatar = freshAvatar; AssetDatabase.CreateAsset(avatar, avatarPath); }
            else { EditorUtility.CopySerialized(freshAvatar, avatar); UnityEngine.Object.DestroyImmediate(freshAvatar); EditorUtility.SetDirty(avatar); }
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("CC rig did not produce a valid Humanoid Avatar; inspect Avatar mapping before continuing.");
            animator.avatar = avatar;
            animator.enabled = true;
            animator.applyRootMotion = false;

            var controllerPath = Folder + "/MocapAttack.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Select(x => x.state).FirstOrDefault(x => x.name == "Attack") ?? machine.AddState("Attack");
            state.motion = clip;
            state.iKOnFeet = false;
            machine.defaultState = state;
            var layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;
            animator.runtimeAnimatorController = controller;

            if (!right || !left) throw new InvalidOperationException("Saved CC hand bones were not found.");
            var preview = animator.gameObject.AddComponent<MocapStaffPreview>();
            preview.attack = clip;
            preview.staff = staff;
            preview.positiveTip = brain.positiveStrikingTip;
            preview.negativeTip = brain.negativeStrikingTip;
            preview.rightGripLocal = authoredRight;
            preview.leftGripLocal = authoredLeft;
            preview.rightHandFromStaff = authoredRightRotation;
            preview.leftHandFromStaff = authoredLeftRotation;
            preview.fingers = authoredFingers;
            preview.desiredStaff = brain.weaponGoal;
            preview.attachment = MocapStaffPreview.Attachment.BothHands;
            preview.playing = true;

            var prefabPath = Folder + "/StarvingBandit_MocapTest.prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            UnityEngine.Object.DestroyImmediate(instance);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Preview floor";
            floor.transform.localScale = new Vector3(4, 1, 4);
            var test = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
            test.transform.position = Vector3.zero;
            var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummy.name = "Red preview dummy";
            // The right-hand-led tip crosses the centerline at about 2.05 m on this clip.
            dummy.transform.position = new Vector3(0, 1, 2.05f);
            dummy.transform.localScale = new Vector3(.48f, 1f, .48f);
            dummy.GetComponent<Collider>().enabled = false;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            if (shader)
            {
                var matPath = Folder + "/PreviewDummy.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
                mat.shader = shader;
                mat.SetColor("_BaseColor", new Color(.9f, .08f, .08f));
                dummy.GetComponent<Renderer>().sharedMaterial = mat;
            }
            var scenePreview = test.GetComponentInChildren<MocapStaffPreview>(true);
            scenePreview.target = dummy.transform;
            PrefabUtility.RecordPrefabInstancePropertyModifications(scenePreview);
            var light = new GameObject("Preview light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(3, 2, 4), Quaternion.LookRotation(new Vector3(-3, -.9f, -3.2f)));
            EditorSceneManager.SaveScene(scene, Folder + "/MocapCombatTest.unity");
            AssetDatabase.SaveAssets();
            Debug.Log($"Mocap test built. Clip {clip.name}, {clip.length:F3}s, Humanoid Avatar valid={avatar.isValid}. Open {Folder}/MocapCombatTest.unity and scrub the MocapStaffPreview component in Play mode.");
        }
        catch { if (instance) UnityEngine.Object.DestroyImmediate(instance); throw; }
    }

    [MenuItem("Zandea/Melee Combat/Validate isolated mocap test")]
    public static void Validate()
    {
        var scene = EditorSceneManager.OpenScene(Folder + "/MocapCombatTest.unity", OpenSceneMode.Single);
        var preview = UnityEngine.Object.FindFirstObjectByType<MocapStaffPreview>();
        if (!preview || !preview.attack || !preview.staff) throw new InvalidOperationException("Mocap test scene is incomplete.");
        if (!preview.target || !preview.positiveTip || !preview.negativeTip)
            throw new InvalidOperationException("Mocap test target or tip references are missing.");
        var animator = preview.GetComponent<Animator>();
        if (!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
            throw new InvalidOperationException("Experimental Humanoid Avatar is invalid.");
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (!hips || !chest || !right || !left || !rightFoot) throw new InvalidOperationException("Retargeted bones are missing.");
        foreach (MocapStaffPreview.Attachment mode in Enum.GetValues(typeof(MocapStaffPreview.Attachment)))
        {
            preview.attachment = mode;
            Vector3 firstHips = default, firstChest = default, firstRight = default, firstLeft = default, firstFoot = default;
            float hipTravel = 0, chestTravel = 0, rightTravel = 0, leftTravel = 0, footTravel = 0;
            float minTipDistance = float.MaxValue, minBodyRadialDistance = float.MaxValue, maxLeftGripError = 0, maxRightGripError = 0;
            float maxCorrection = 0, maxReach = 0;
            Vector3 closestTip = default;
            string tipSamples = "";
            for (int i = 0; i <= 40; i++)
            {
                preview.normalizedTime = i / 40f;
                preview.Sample();
                maxCorrection = Mathf.Max(maxCorrection, preview.LeftCorrection, preview.RightCorrection);
                maxReach = Mathf.Max(maxReach, preview.ReachCorrection);
                if (i == 0)
                {
                    firstHips = hips.position; firstChest = chest.position;
                    firstRight = right.position; firstLeft = left.position; firstFoot = rightFoot.position;
                }
                hipTravel = Mathf.Max(hipTravel, Vector3.Distance(firstHips, hips.position));
                chestTravel = Mathf.Max(chestTravel, Vector3.Distance(firstChest, chest.position));
                rightTravel = Mathf.Max(rightTravel, Vector3.Distance(firstRight, right.position));
                leftTravel = Mathf.Max(leftTravel, Vector3.Distance(firstLeft, left.position));
                footTravel = Mathf.Max(footTravel, Vector3.Distance(firstFoot, rightFoot.position));
                if (mode == MocapStaffPreview.Attachment.RawAnimation) continue;
                var staffTransform = preview.staff.transform;
                var tip = preview.tracePositiveTip ? preview.positiveTip : preview.negativeTip;
                if (tip && preview.target)
                {
                    var distance = Vector3.Distance(tip.position, preview.target.position);
                    if (distance < minTipDistance) { minTipDistance = distance; closestTip = tip.position; }
                    if (tip.position.y >= .5f && tip.position.y <= 1.5f)
                    {
                        var radial = Vector2.Distance(new Vector2(tip.position.x, tip.position.z), new Vector2(preview.target.position.x, preview.target.position.z));
                        minBodyRadialDistance = Mathf.Min(minBodyRadialDistance, radial);
                    }
                    if (i % 10 == 0) tipSamples += $" t{i / 40f:F2}={tip.position}";
                }
                maxRightGripError = Mathf.Max(maxRightGripError, Vector3.Distance(right.position, staffTransform.TransformPoint(preview.rightGripLocal)));
                maxLeftGripError = Mathf.Max(maxLeftGripError, Vector3.Distance(left.position, staffTransform.TransformPoint(preview.leftGripLocal)));
            }
            Debug.Log($"MOCAP_VALIDATION mode={mode} clip={preview.attack.name} duration={preview.attack.length:F3}s avatarValid={animator.avatar.isValid} hips={hipTravel:F3}m chest={chestTravel:F3}m rightHand={rightTravel:F3}m leftHand={leftTravel:F3}m rightFoot={footTravel:F3}m minTipToDummyCenter={minTipDistance:F3}m minBodyRadialDistance={minBodyRadialDistance:F3}m bodyCross={minBodyRadialDistance < .24f} closestTip={closestTip} maxRightGripError={maxRightGripError:F3}m maxLeftGripError={maxLeftGripError:F3}m{tipSamples}");
            Debug.Log($"GRIP_CORRECTION mode={mode} maxWristCorrection={maxCorrection:F4} maxReachCorrection={maxReach:F4} gripSpacing={Vector3.Distance(preview.leftGripLocal, preview.rightGripLocal):F4}");
        }
    }

    public static void RenderValidationFrames()
    {
        EditorSceneManager.OpenScene(Folder + "/MocapCombatTest.unity", OpenSceneMode.Single);
        var preview = UnityEngine.Object.FindFirstObjectByType<MocapStaffPreview>();
        var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
        if (!preview || !camera) throw new InvalidOperationException("Mocap scene camera or preview is missing.");
        camera.enabled = true;
        var output = Path.Combine(Application.dataPath, "../MocapRenderValidation");
        Directory.CreateDirectory(output);
        var texture = new RenderTexture(1280, 720, 24);
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var skins = preview.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var baked = new List<(SkinnedMeshRenderer source, Mesh mesh, GameObject display)>();
        foreach (var skin in skins)
        {
            var display = new GameObject("Validation baked mesh", typeof(MeshFilter), typeof(MeshRenderer));
            display.transform.SetParent(skin.transform, false);
            var mesh = new Mesh();
            display.GetComponent<MeshFilter>().sharedMesh = mesh;
            display.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            baked.Add((skin, mesh, display));
        }
        try
        {
            camera.targetTexture = texture;
            foreach (MocapStaffPreview.Attachment mode in Enum.GetValues(typeof(MocapStaffPreview.Attachment)))
            {
                preview.attachment = mode;
                foreach (float time in new[] { 0f, .25f, .5f, .75f })
                {
                    preview.normalizedTime = time;
                    preview.Sample();
                    foreach (var item in baked)
                    {
                        item.source.BakeMesh(item.mesh);
                        item.source.enabled = false;
                    }
                    camera.Render();
                    RenderTexture.active = texture;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    image.Apply();
                    var path = Path.Combine(output, $"{mode}_{time:F2}.png");
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    Debug.Log("MOCAP_RENDER " + path);
                }
            }
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(image);
            foreach (var item in baked)
            {
                item.source.enabled = true;
                UnityEngine.Object.DestroyImmediate(item.display);
                UnityEngine.Object.DestroyImmediate(item.mesh);
            }
        }
    }

    static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    static Avatar MakeAvatar(GameObject root)
    {
        var map = new Dictionary<string, string> {
            {"Hips","CC_Base_Hip_02"}, {"Spine","CC_Base_Spine01_034"}, {"Chest","CC_Base_Spine02_035"},
            {"Neck","CC_Base_NeckTwist01_036"}, {"Head","CC_Base_Head_038"},
            {"LeftShoulder","CC_Base_L_Clavicle_049"}, {"LeftUpperArm","CC_Base_L_Upperarm_050"}, {"LeftLowerArm","CC_Base_L_Forearm_051"}, {"LeftHand","CC_Base_L_Hand_055"},
            {"RightShoulder","CC_Base_R_Clavicle_073"}, {"RightUpperArm","CC_Base_R_Upperarm_074"}, {"RightLowerArm","CC_Base_R_Forearm_077"}, {"RightHand","CC_Base_R_Hand_081"},
            {"LeftUpperLeg","CC_Base_L_Thigh_04"}, {"LeftLowerLeg","CC_Base_L_Calf_05"}, {"LeftFoot","CC_Base_L_Foot_06"},
            {"RightUpperLeg","CC_Base_R_Thigh_00"}, {"RightLowerLeg","CC_Base_R_Calf_021"}, {"RightFoot","CC_Base_R_Foot_022"}
        };
        var skeleton = root.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone {
            name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
        }).ToArray();
        var human = map.Select(pair => new HumanBone { humanName = pair.Key, boneName = pair.Value, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        var description = new HumanDescription { human = human, skeleton = skeleton,
            upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
            armStretch = .05f, legStretch = .05f, feetSpacing = 0f };
        return AvatarBuilder.BuildHumanAvatar(root, description);
    }
}

[CustomEditor(typeof(MocapStaffPreview))]
public sealed class MocapStaffPreviewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var preview = (MocapStaffPreview)target;
        float before = preview.normalizedTime;
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
        {
            if (!Mathf.Approximately(before, preview.normalizedTime)) preview.playing = false;
            preview.Sample();
            SceneView.RepaintAll();
        }
        if (preview.animations != null && preview.animations.Length > 0)
        {
            var names = preview.animations.Select(c => c ? c.name : "Missing clip").ToArray();
            int selected = EditorGUILayout.Popup("Preview animation", Mathf.Clamp(preview.animationIndex, 0, names.Length - 1), names);
            if (selected != preview.animationIndex)
            {
                Undo.RecordObject(preview, "Select combat animation");
                preview.animationIndex = selected;
                preview.normalizedTime = 0;
                preview.Sample();
                EditorUtility.SetDirty(preview);
                SceneView.RepaintAll();
            }
        }
        if (GUILayout.Button(preview.playing ? "Pause attack" : "Play attack")) preview.playing = !preview.playing;
        if (GUILayout.Button("Sample selected attack frame"))
        {
            ((MocapStaffPreview)target).Sample();
            SceneView.RepaintAll();
        }
        EditorGUILayout.LabelField("Grip error (left / right)", $"{preview.LeftGripError * 1000:F1} / {preview.RightGripError * 1000:F1} mm");
        EditorGUILayout.LabelField("Arm correction (left / right)", $"{preview.LeftCorrection * 100:F1} / {preview.RightCorrection * 100:F1} cm");
        EditorGUILayout.HelpBox("Enter Play mode to preview. Scrubbing pauses the attack. BothHands preserves the animated hand vector and uses the existing arm IK to hold the actual staff. RawAnimation disables grip corrections. Desired Staff is the future physics drive target; Drive Visual Staff currently moves the rigid visual directly.", MessageType.Info);
    }
}
