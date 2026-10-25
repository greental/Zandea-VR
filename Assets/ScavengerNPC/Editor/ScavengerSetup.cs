using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using Object = UnityEngine.Object;

namespace ScavengerNPC.Editor
{
    public static class ScavengerSetup
    {
        const string Folder = "Assets/ScavengerNPC/Generated";
        const string Prefix = "Assets/ScavengerNPC/Models/Meshy_AI_Wasteland_Scavenger_biped_Animation_";
        static Transform Bone(GameObject model, string name) => model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "mixamorig:" + name);

        [MenuItem("Zandea/Scavenger NPC/Add player health to selected rig")]
        static void AddPlayerHealth()
        {
            if (!Selection.activeGameObject) { Debug.LogWarning("Select the player root containing the CharacterController."); return; }
            if (!Selection.activeGameObject.GetComponent<ScavengerPlayerHealth>()) Undo.AddComponent<ScavengerPlayerHealth>(Selection.activeGameObject);
        }
        [MenuItem("Zandea/Scavenger NPC/Create cover and peek markers")]
        static void AddCover()
        {
            var cover = new GameObject("Scavenger Cover").AddComponent<ScavengerCover>();
            Undo.RegisterCreatedObjectUndo(cover.gameObject, "Create NPC cover");
            if (SceneView.lastActiveSceneView) cover.transform.position = SceneView.lastActiveSceneView.pivot;
            cover.peek = new GameObject("Peek").transform; cover.peek.SetParent(cover.transform, false); cover.peek.localPosition = Vector3.right * 2;
            cover.peekPoints.Add(new ScavengerCover.PeekPoint { point = cover.peek, style = ScavengerCover.PeekStyle.Side });
            Selection.activeGameObject = cover.gameObject;
        }
        [MenuItem("Zandea/Scavenger NPC/Preview selected scavenger aim")]
        static void PreviewSelectedAim()
        {
            var pose = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<ScavengerPose>() : null;
            if (!pose) { Debug.LogWarning("Select a scavenger or one of its bones first."); return; }
            Undo.RecordObject(pose, "Preview scavenger aim");
            pose.previewAimInEditor = true;
            EditorUtility.SetDirty(pose);
            SceneView.RepaintAll();
        }

        [MenuItem("Zandea/Scavenger NPC/Build enemy prefabs and test scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            if (Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path) && SceneManager.GetActiveScene().rootCount == 0)
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/BuildWorkspace.unity");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefix + "Walking_withSkin.glb");
            if (!source) throw new InvalidOperationException("Repaired GLBs have not imported. Run tools/repair_scavenger_skin.py first.");
            var root = new GameObject("Scavenger JKR-88");
            try
            {
                var model = Object.Instantiate(source, root.transform); model.name = "Visual";
                foreach (var a in model.GetComponentsInChildren<Animation>()) Object.DestroyImmediate(a);
                var animator = model.GetComponent<Animator>(); if (!animator) animator = model.AddComponent<Animator>();
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var walk = Clip("Walking", "Walk", model, true);
                var run = Clip("Running", "Run", model, true);
                var idle = Idle(model);
                Clip("Dead", "Death", model, false);
                string controllerPath = Folder + "/Scavenger.controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                foreach (var state in controller.layers[0].stateMachine.states) controller.layers[0].stateMachine.RemoveState(state.state);
                controller.parameters = new[] { new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float } };
                var tree = new BlendTree { name = "Locomotion", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
                tree.AddChild(idle, 0); tree.AddChild(walk, 1.3f); tree.AddChild(run, 3.5f);
                AssetDatabase.AddObjectToAsset(tree, controller);
                var locomotion = controller.layers[0].stateMachine.AddState("Locomotion"); locomotion.motion = tree;
                controller.layers[0].stateMachine.defaultState = locomotion;
                animator.runtimeAnimatorController = controller;

                var agent = root.AddComponent<NavMeshAgent>(); agent.height = 1.7f; agent.radius = .26f; agent.stoppingDistance = .15f; agent.angularSpeed = 360; agent.acceleration = 14;
                var capsule = root.AddComponent<CapsuleCollider>(); capsule.height = 1.7f; capsule.radius = .25f; capsule.center = Vector3.up * .85f;
                var enemy = root.AddComponent<ScavengerEnemy>(); enemy.animator = animator; enemy.eyes = Bone(model, "Head");
                var pose = root.AddComponent<ScavengerPose>(); pose.enemy = enemy; pose.hips = Bone(model, "Hips"); pose.head = Bone(model, "Head");
                pose.rightUpper = Bone(model, "RightArm"); pose.rightLower = Bone(model, "RightForeArm"); pose.rightHand = Bone(model, "RightHand");
                pose.leftUpper = Bone(model, "LeftArm"); pose.leftLower = Bone(model, "LeftForeArm"); pose.leftHand = Bone(model, "LeftHand");
                pose.rightThigh = Bone(model, "RightUpLeg"); pose.rightShin = Bone(model, "RightLeg"); pose.rightFoot = Bone(model, "RightFoot");
                pose.leftThigh = Bone(model, "LeftUpLeg"); pose.leftShin = Bone(model, "LeftLeg"); pose.leftFoot = Bone(model, "LeftFoot");
                pose.hitSpine = Bone(model, "Spine2");
                var cellAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/weapons/R4-PowerCell.prefab");
                var belt = root.AddComponent<ScavengerAmmoBelt>();
                belt.waist = pose.hips;
                belt.cellPrefab = cellAsset ? cellAsset.GetComponent<JunkerEquipment.JunkerPowerCell>() : null;
                // The hand local +Y points along the palm; rotate it to the weapon's +Z.
                pose.handGripRotation = Quaternion.Euler(90, 0, 0); pose.leftGripRotation = Quaternion.Euler(90, 0, 0);
                Ragdoll(root, model, animator, pose, capsule);
                Weapon(root, pose, enemy, false);
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Scavenger_JKR88.prefab");
                Object.DestroyImmediate(pose.gun.gameObject);
                Weapon(root, pose, enemy, true); root.name = "Scavenger PKO";
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Scavenger_PKO.prefab");
                var ammoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/weapons/JKR-88 1.prefab");
                if (ammoPrefab)
                {
                    Object.DestroyImmediate(pose.gun.gameObject);
                    var ammoGun = (GameObject)PrefabUtility.InstantiatePrefab(ammoPrefab, pose.rightHand);
                    var pistol = ammoGun.GetComponent<JunkerEquipment.JunkerPistol>();
                    if (!pistol || !pistol.muzzle) throw new InvalidOperationException("JKR-88 1 prefab needs a configured JunkerPistol and muzzle.");
                    var driver = ammoGun.AddComponent<ScavengerWeapon>();
                    driver.owner = enemy; driver.pistol = pistol; driver.muzzle = pistol.muzzle; driver.range = pistol.range;
                    enemy.weapon = driver; pose.gun = ammoGun.transform;
                    ammoGun.transform.localPosition = pose.gunGripPosition;
                    ammoGun.transform.localRotation = Quaternion.Euler(pose.gunGripEuler);
                    var body = ammoGun.GetComponent<Rigidbody>();
                    if (body) { body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.None; }
                    root.name = "Scavenger JKR-88 Ammo";
                    PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Scavenger_JKR88_Ammo.prefab");
                }
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            BuildRestartSign();
            Demo();
            File.WriteAllText("Library/Scavenger-build.txt", "PASS: repaired model, idle/walk/run/death clips, armed prefabs, ragdolls, baked test scene.\n");
            Debug.Log("Scavenger NPC prefabs and test scene built in " + Folder);
        }
        static void Save(Object asset, string path)
        {
            var old = AssetDatabase.LoadMainAssetAtPath(path);
            if (old) { EditorUtility.CopySerialized(asset, old); Object.DestroyImmediate(asset); }
            else AssetDatabase.CreateAsset(asset, path);
        }
        static AnimationClip Clip(string sourceName, string name, GameObject model, bool loop)
        {
            var original = AssetDatabase.LoadAllAssetsAtPath(Prefix + sourceName + "_withSkin.glb").OfType<AnimationClip>().OrderBy(c => c.name.Length).First();
            var clip = Object.Instantiate(original); clip.name = name; clip.legacy = false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var t = string.IsNullOrEmpty(binding.path) ? model.transform : model.transform.Find(binding.path);
                if (!t) throw new InvalidOperationException("Unresolved clip bone " + binding.path);
                if ((t.name == "mixamorig:Hips" || t == model.transform || t.name == "target_character") && binding.propertyName.StartsWith("m_LocalPosition."))
                {
                    int axis = binding.propertyName.EndsWith("x") ? 0 : binding.propertyName.EndsWith("y") ? 1 : 2;
                    if (axis != 1 || t.name != "mixamorig:Hips") AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, clip.length, t.localPosition[axis]));
                }
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            string path = Folder + "/" + name + ".anim"; Save(clip, path); return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }
        static AnimationClip Idle(GameObject model)
        {
            var clip = new AnimationClip { name = "Idle", legacy = false };
            foreach (var bone in model.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("mixamorig:")))
            {
                string path = AnimationUtility.CalculateTransformPath(bone, model.transform);
                for (int i = 0; i < 3; i++) clip.SetCurve(path, typeof(Transform), "m_LocalPosition." + "xyz"[i], AnimationCurve.Constant(0, 2, bone.localPosition[i]));
                for (int i = 0; i < 4; i++) clip.SetCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[i], AnimationCurve.Constant(0, 2, bone.localRotation[i]));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings);
            Save(clip, Folder + "/Idle.anim"); return AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Idle.anim");
        }
        static void Weapon(GameObject root, ScavengerPose pose, ScavengerEnemy enemy, bool pko)
        {
            string path = pko ? "Assets/MyAssets/mesh/weapons/PKO/PKO.fbx" : "Assets/MyAssets/mesh/weapons/JKR/JKR-88.glb";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) throw new InvalidOperationException("Missing weapon model: " + path);
            // Keep the weapon in the rig hierarchy even when the Animator is previewed
            // in Edit mode, the pose component is disabled, or the NPC becomes a ragdoll.
            var gun = new GameObject(pko ? "PKO" : "JKR-88"); gun.transform.SetParent(pose.rightHand, false);
            var visual = Object.Instantiate(asset, gun.transform); visual.name = "Weapon visual";
            visual.transform.localRotation = Quaternion.Euler(0, pko ? -90 : 90, 0);
            var renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            visual.transform.localScale *= .32f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            visual.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.min.z + .04f);
            bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(gun.transform, false);
            muzzle.localPosition = new Vector3(bounds.center.x, bounds.center.y + .015f, bounds.max.z + .01f);
            var weapon = gun.AddComponent<ScavengerWeapon>(); weapon.owner = enemy; weapon.muzzle = muzzle;
            weapon.audioSource = gun.AddComponent<AudioSource>(); weapon.audioSource.spatialBlend = 1; weapon.audioSource.playOnAwake = false;
            weapon.shot = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MyAssets/Audio/Weapons/pko/mrfriends-pistol-shot-233473.mp3");
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere); flash.name = "Flash"; flash.transform.SetParent(muzzle, false); flash.transform.localScale = new Vector3(.025f, .025f, .07f);
            Object.DestroyImmediate(flash.GetComponent<Collider>()); flash.SetActive(false); weapon.flash = flash;
            enemy.weapon = weapon; pose.gun = gun.transform;
            pose.gunGripPosition = new Vector3(0, .04f, -.03f); pose.gunGripEuler = new Vector3(-90, 0, 0);
            gun.transform.position = pose.rightHand.TransformPoint(pose.gunGripPosition); gun.transform.rotation = pose.rightHand.rotation * Quaternion.Euler(pose.gunGripEuler);
        }
        static void Ragdoll(GameObject root, GameObject model, Animator animator, ScavengerPose pose, Collider capsule)
        {
            string[] names = { "Hips", "Spine", "Spine1", "Spine2", "Head", "LeftArm", "LeftForeArm", "RightArm", "RightForeArm", "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg" };
            var bodies = new List<Rigidbody>(); var colliders = new List<Collider>();
            foreach (string name in names)
            {
                var bone = Bone(model, name); var body = bone.gameObject.AddComponent<Rigidbody>(); body.isKinematic = true; body.mass = name == "Hips" ? 10 : 3;
                // Physics interpolation writes old Rigidbody poses over the Animator's
                // bone transforms between fixed steps. Enable it only once ragdoll starts.
                body.interpolation = RigidbodyInterpolation.None; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var shape = bone.gameObject.AddComponent<CapsuleCollider>(); shape.direction = 1;
                float length = bone.childCount > 0 ? bone.GetChild(0).localPosition.magnitude : .2f;
                shape.radius = name.Contains("Leg") ? .065f : name.Contains("Arm") ? .045f : .10f;
                shape.height = Mathf.Max(shape.radius * 2, length); shape.center = Vector3.up * length * .5f;
                bodies.Add(body); colliders.Add(shape);
            }
            foreach (var body in bodies.Skip(1))
            {
                var parent = body.transform.parent; while (parent && !parent.GetComponent<Rigidbody>()) parent = parent.parent;
                if (!parent) continue;
                var joint = body.gameObject.AddComponent<CharacterJoint>(); joint.connectedBody = parent.GetComponent<Rigidbody>();
                joint.axis = Vector3.right; joint.swingAxis = Vector3.forward; joint.enableProjection = true;
                joint.lowTwistLimit = new SoftJointLimit { limit = -25 }; joint.highTwistLimit = new SoftJointLimit { limit = 25 };
                joint.swing1Limit = new SoftJointLimit { limit = 45 }; joint.swing2Limit = new SoftJointLimit { limit = 30 };
            }
            var ragdoll = root.AddComponent<ScavengerRagdoll>(); ragdoll.animator = animator; ragdoll.pose = pose; ragdoll.capsule = capsule;
            ragdoll.bones = bodies.ToArray(); ragdoll.boneColliders = colliders.ToArray();
        }
        static GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = position; go.transform.localScale = size; return go;
        }
        [MenuItem("Zandea/Scavenger NPC/Build shootable restart sign")]
        public static void BuildRestartSign()
        {
            Directory.CreateDirectory(Folder);
            var root = new GameObject("Shoot to restart");
            try
            {
                root.AddComponent<ScavengerRestartSign>();
                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "Sign panel";
                panel.transform.SetParent(root.transform, false);
                panel.transform.localScale = new Vector3(1.8f, .6f, .08f);
                string materialPath = Folder + "/RestartSign.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (!material)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    material.color = new Color(.035f, .075f, .09f);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                panel.GetComponent<Renderer>().sharedMaterial = material;
                var label = new GameObject("SHOOT TO RESTART").AddComponent<TextMesh>();
                label.transform.SetParent(root.transform, false);
                label.transform.localPosition = new Vector3(0, 0, .052f);
                label.text = "SHOOT TO RESTART";
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.characterSize = .13f;
                label.fontSize = 48;
                label.color = new Color(.25f, .95f, 1f);
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/ScavengerRestartSign.prefab");
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Zandea/Scavenger NPC/Place restart sign in active scene")]
        static void PlaceRestartSign()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/ScavengerRestartSign.prefab");
            if (!prefab) { BuildRestartSign(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/ScavengerRestartSign.prefab"); }
            var sign = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
            sign.transform.position = (SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.pivot : Vector3.zero) + Vector3.up * 1.4f;
            if (SceneView.lastActiveSceneView && SceneView.lastActiveSceneView.camera)
            {
                var towardCamera = SceneView.lastActiveSceneView.camera.transform.position - sign.transform.position;
                towardCamera.y = 0;
                if (towardCamera.sqrMagnitude > .01f) sign.transform.rotation = Quaternion.LookRotation(towardCamera);
            }
            Undo.RegisterCreatedObjectUndo(sign, "Place shootable restart sign");
            Selection.activeGameObject = sign;
            EditorSceneManager.MarkSceneDirty(sign.scene);
        }
        static void Demo()
        {
            var previous = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(previous.path))
                throw new InvalidOperationException("Enemy prefabs are built. Save the current untitled scene before generating the additive demo scene.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                Box("Ground", new Vector3(0, -.15f, 0), new Vector3(30, .3f, 30));
                Box("Cover wall", new Vector3(0, 1, 2), new Vector3(3, 2, .6f));
                var cover = new GameObject("Cover - protected side").AddComponent<ScavengerCover>(); cover.transform.position = new Vector3(0, 0, 3);
                cover.peek = new GameObject("Peek right").transform; cover.peek.SetParent(cover.transform); cover.peek.position = new Vector3(2.3f, 0, 2.9f);
                var leftPeek = new GameObject("Peek left").transform; leftPeek.SetParent(cover.transform); leftPeek.position = new Vector3(-2.3f, 0, 2.9f);
                cover.peekPoints.Add(new ScavengerCover.PeekPoint { point = cover.peek, style = ScavengerCover.PeekStyle.Side });
                cover.peekPoints.Add(new ScavengerCover.PeekPoint { point = leftPeek, style = ScavengerCover.PeekStyle.Side });
                Box("Half cover wall", new Vector3(-6, .55f, 2.5f), new Vector3(2.2f, 1.1f, .5f));
                var halfCover = new GameObject("Half cover - over peek").AddComponent<ScavengerCover>(); halfCover.transform.position = new Vector3(-6, 0, 3.5f);
                var overPeek = new GameObject("Over peek").transform; overPeek.SetParent(halfCover.transform, false);
                halfCover.peekPoints.Add(new ScavengerCover.PeekPoint { point = overPeek, style = ScavengerCover.PeekStyle.Over, sightHeight = 1.5f });
                var surface = new GameObject("Navigation").AddComponent<NavMeshSurface>(); surface.collectObjects = CollectObjects.All; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                // Build only this scene's environment, before adding actors.
                surface.collectObjects = CollectObjects.Children;
                foreach (var go in scene.GetRootGameObjects().Where(g => g != surface.gameObject).ToArray()) go.transform.SetParent(surface.transform);
                surface.BuildNavMesh();
                string navPath = Folder + "/ScavengerDemoNavMesh.asset";
                Save(surface.navMeshData, navPath);
                surface.RemoveData(); surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath); surface.AddData();
                var player = GameObject.CreatePrimitive(PrimitiveType.Capsule); player.name = "Test player target"; player.transform.position = new Vector3(0, 1, -6); player.AddComponent<ScavengerPlayerHealth>();
                var signPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/ScavengerRestartSign.prefab");
                if (signPrefab)
                {
                    var sign = (GameObject)PrefabUtility.InstantiatePrefab(signPrefab);
                    sign.transform.position = new Vector3(0, 1.4f, -8);
                }
                var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.transform.position = new Vector3(6, 4, -8); camera.transform.LookAt(new Vector3(0, 1, 3)); camera.gameObject.AddComponent<AudioListener>();
                var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = Quaternion.Euler(50, -30, 0);
                var npc = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Scavenger_JKR88.prefab"));
                npc.transform.position = new Vector3(4, 0, 6); npc.transform.rotation = Quaternion.Euler(0, 180, 0);
                var brain = npc.GetComponent<ScavengerEnemy>(); brain.target = player.transform; brain.targetRoot = player.transform; brain.idleAction = ScavengerEnemy.Action.Standing;
                EditorSceneManager.SaveScene(scene, Folder + "/ScavengerDemo.unity");
            }
            finally { EditorSceneManager.CloseScene(scene, true); if (previous.IsValid()) SceneManager.SetActiveScene(previous); }
        }
    }
}
