using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class MeleePocBuilder
    {
        const string MalePath = "Assets/MyAssets/mesh/Charecters/male/male.glb";
        const string StaffPath = "Assets/MyAssets/mesh/weapons/BO_Staff.glb";
        const string XrPath = "Assets/MyAssets/Player/XR Origin (VR) Variant.prefab";
        const string ScenePath = "Assets/MeleeCombat/MeleePOC.unity";
        const string NavPath = "Assets/MeleeCombat/MeleePOCNavMesh.asset";

        [InitializeOnLoadMethod]
        static void BuildMissingSceneOnReload()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !File.Exists(ScenePath)) Build();
            };
        }

        [MenuItem("Tools/Melee Combat/Build POC Scene")]
        public static void Build()
        {
            var male = AssetDatabase.LoadAssetAtPath<GameObject>(MalePath);
            var staffModel = AssetDatabase.LoadAssetAtPath<GameObject>(StaffPath);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(XrPath);
            if (!male || !staffModel || !player) { Debug.LogError("Melee POC requires the male GLB, Bo staff GLB, and XR prefab."); return; }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Arena floor"; floor.transform.position = new Vector3(0, -.15f, 0);
            floor.transform.localScale = new Vector3(12, .3f, 12);
            var navigation = new GameObject("Navigation").AddComponent<NavMeshSurface>();
            navigation.collectObjects = CollectObjects.Children;
            navigation.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            floor.transform.SetParent(navigation.transform, true);
            BakeNavigation(navigation);
            var xr = (GameObject)PrefabUtility.InstantiatePrefab(player);
            xr.transform.position = new Vector3(0, 0, -2.4f);
            if (!xr.GetComponent<ScavengerPlayerHealth>()) xr.AddComponent<ScavengerPlayerHealth>();
            var enemy = new GameObject("Starving Bandit - male rig");
            enemy.transform.position = new Vector3(0, 0, 1.5f);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(male);
            model.name = "Animated male rig";
            model.transform.SetParent(enemy.transform, false);
            FitVisual(model.transform, 1.75f);
            var capsule = enemy.AddComponent<CapsuleCollider>();
            capsule.height = 1.75f; capsule.radius = .27f; capsule.center = Vector3.up * .875f;
            var agent = enemy.AddComponent<NavMeshAgent>();
            agent.radius = .28f; agent.height = 1.75f; agent.stoppingDistance = 1.3f;
            var health = enemy.AddComponent<ScavengerHealth>(); health.maxHealth = 55;
            BuildRagdoll(enemy, model.transform, capsule);
            var goal = new GameObject("Animated grip goal").transform;
            goal.SetParent(enemy.transform, false);
            goal.localPosition = new Vector3(.05f, 1.25f, .3f);
            goal.localRotation = Quaternion.Euler(-10, 0, 0);
            var staff = CreateWeapon("Bandit Bo staff", staffModel, 1.9f, .034f, 2.1f);
            staff.transform.SetPositionAndRotation(goal.position, goal.rotation);
            staff.transform.SetParent(enemy.transform, true);
            staff.ownerRoot = enemy.transform;
            staff.npcGripTarget = goal;
            staff.minimumImpactSpeed = 1.8f;
            var brain = enemy.AddComponent<StarvingBandit>();
            brain.staff = staff; brain.health = health; brain.agent = agent; brain.weaponGoal = goal;
            var motion = enemy.AddComponent<BanditRigMotion>();
            motion.visualRoot = model.transform; motion.staff = staff; motion.brain = brain; motion.agent = agent;
            // A player sword uses a collider authored from primitives until a sword mesh is supplied.
            var sword = new GameObject("Player practice sword");
            sword.transform.position = new Vector3(.55f, 1.05f, -1.8f);
            var rb = sword.AddComponent<Rigidbody>(); rb.mass = 1.6f;
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handle.name = "Handle"; handle.transform.SetParent(sword.transform, false);
            handle.transform.localScale = new Vector3(.035f, .13f, .035f);
            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade"; blade.transform.SetParent(sword.transform, false);
            blade.transform.localPosition = Vector3.up * .47f;
            blade.transform.localScale = new Vector3(.065f, .7f, .018f);
            var swordGrip = new GameObject("Grip").transform;
            swordGrip.SetParent(sword.transform, false);
            var grab = sword.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            grab.attachTransform = swordGrip;
            var physicalSword = sword.AddComponent<PhysicalMeleeWeapon>();
            physicalSword.grip = swordGrip; physicalSword.strikeType = PhysicalMeleeWeapon.StrikeType.Blunt;
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            Debug.Log("Built " + ScenePath + " with male rig motion and a baked NavMesh.");
        }

        [MenuItem("Tools/Melee Combat/Upgrade POC motion and navigation")]
        public static void UpgradeExistingScene()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                StarvingBandit brain = null;
                GameObject floor = null;
                NavMeshSurface navigation = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!brain) brain = root.GetComponent<StarvingBandit>();
                    if (root.name == "Arena floor") floor = root;
                    if (root.name == "Navigation")
                    {
                        navigation = root.GetComponent<NavMeshSurface>();
                        var nestedFloor = root.transform.Find("Arena floor");
                        if (nestedFloor) floor = nestedFloor.gameObject;
                    }
                }
                if (!brain || !floor) { Debug.LogError("Melee POC scene is missing the bandit or arena floor."); return; }
                if (!navigation) navigation = new GameObject("Navigation").AddComponent<NavMeshSurface>();
                navigation.collectObjects = CollectObjects.Children;
                navigation.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                floor.transform.SetParent(navigation.transform, true);
                BakeNavigation(navigation);
                var rig = brain.transform.Find("Animated male rig");
                var motion = brain.GetComponent<BanditRigMotion>();
                if (!motion) motion = brain.gameObject.AddComponent<BanditRigMotion>();
                motion.visualRoot = rig; motion.staff = brain.staff; motion.brain = brain; motion.agent = brain.agent;
                brain.attackRange = 1.5f;
                brain.runUntilRange = 3.1f;
                brain.runSpeed = 3.2f;
                brain.chasseSpeed = .85f;
                brain.agent.stoppingDistance = 1.3f;
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("Upgraded " + ScenePath + " with male rig motion and a baked NavMesh.");
            }
            finally
            {
                if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }
        }

        static void BakeNavigation(NavMeshSurface surface)
        {
            surface.BuildNavMesh();
            var data = surface.navMeshData;
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);
            if (existing) { EditorUtility.CopySerialized(data, existing); Object.DestroyImmediate(data); }
            else AssetDatabase.CreateAsset(data, NavPath);
            surface.RemoveData();
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);
            surface.AddData();
        }

        static PhysicalMeleeWeapon CreateWeapon(string name, GameObject source, float length, float radius, float mass)
        {
            var root = new GameObject(name);
            var rb = root.AddComponent<Rigidbody>(); rb.mass = mass;
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
            visual.name = "Bo staff mesh"; visual.transform.SetParent(root.transform, false);
            // The source mesh runs along Z, but its two imported node rotations make
            // that axis X in Unity. Bring it back to the physical capsule's Z axis.
            visual.transform.localRotation = Quaternion.Euler(0, -90, 0);
            FitVisual(visual.transform, length);
            var shape = root.AddComponent<CapsuleCollider>();
            shape.direction = 3; shape.height = length; shape.radius = radius;
            var grip = new GameObject("Grip").transform;
            grip.SetParent(root.transform, false);
            var weapon = root.AddComponent<PhysicalMeleeWeapon>();
            weapon.grip = grip; weapon.mass = mass;
            return weapon;
        }
        static void FitVisual(Transform visual, float desiredLength)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest < .001f) return;
            visual.localScale = Vector3.one * (desiredLength / longest);
            // Keep source orientation; recentre its visual geometry around the physics root.
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            visual.position -= bounds.center - (visual.parent.position + (desiredLength == 1.75f ? Vector3.up * desiredLength * .5f : Vector3.zero));
        }

        public static void BuildRagdoll(GameObject root, Transform model, Collider rootCapsule)
        {
            string[] names = {
                "CC_Base_Hip_02", "CC_Base_Spine01_034", "CC_Base_Spine02_035", "CC_Base_Head_038",
                "CC_Base_L_Thigh_04", "CC_Base_L_Calf_05", "CC_Base_R_Thigh_00", "CC_Base_R_Calf_021",
                "CC_Base_L_Forearm_051", "CC_Base_L_Hand_055", "CC_Base_R_Forearm_077", "CC_Base_R_Hand_081"
            };
            var bones = new List<Rigidbody>();
            var colliders = new List<Collider>();
            foreach (var name in names)
            {
                Transform bone = null;
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == name) { bone = t; break; }
                if (!bone) continue;
                var body = bone.gameObject.AddComponent<Rigidbody>();
                body.mass = name.Contains("Hip") ? 8f : name.Contains("Spine") ? 5f : 1.5f;
                body.isKinematic = true; body.interpolation = RigidbodyInterpolation.None;
                var shape = bone.gameObject.AddComponent<SphereCollider>();
                float worldRadius = name.Contains("Hip") || name.Contains("Spine") ? .16f : name.Contains("Head") ? .12f : .08f;
                shape.radius = worldRadius / Mathf.Max(.001f, bone.lossyScale.x);
                bones.Add(body); colliders.Add(shape);
            }
            foreach (var body in bones)
            {
                var parent = body.transform.parent;
                while (parent && !parent.GetComponent<Rigidbody>()) parent = parent.parent;
                if (!parent) continue;
                var joint = body.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent.GetComponent<Rigidbody>();
                joint.enablePreprocessing = false;
            }
            var ragdoll = root.AddComponent<ScavengerRagdoll>();
            ragdoll.animator = model.GetComponentInChildren<Animator>();
            ragdoll.capsule = rootCapsule;
            ragdoll.bones = bones.ToArray(); ragdoll.boneColliders = colliders.ToArray();
        }
    }
}
