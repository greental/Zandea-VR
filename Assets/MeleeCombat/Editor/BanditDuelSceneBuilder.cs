using KenshiVR.CharacterCreator;
using ScavengerNPC;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class BanditDuelSceneBuilder
    {
        const string ScenePath = "Assets/MeleeCombat/BanditDuel.unity";
        const string NavPath = "Assets/MeleeCombat/BanditDuelNavMesh.asset";
        const string PrefabPath = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";
        const string ItemPath = "Assets/CharacterCreator/ImportedDownloads/Items/";

        [MenuItem("Zandea/Melee Combat/Build bandit duel scene")]
        public static void Build()
        {
            CombinedBanditBuilder.Build();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) { Debug.LogError("Combined bandit prefab is missing"); return; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var arena = new GameObject("Arena navigation");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Duel floor";
            floor.transform.SetParent(arena.transform, false);
            floor.transform.localPosition = new Vector3(0, -.15f, 0);
            floor.transform.localScale = new Vector3(12, .3f, 12);
            var floorRenderer = floor.GetComponent<Renderer>();
            const string floorMaterialPath = "Assets/MeleeCombat/BanditDuelFloor.mat";
            var floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(floorMaterialPath);
            if (!floorMaterial)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) throw new System.InvalidOperationException("URP Lit shader missing for duel floor");
                floorMaterial = new Material(shader);
                AssetDatabase.CreateAsset(floorMaterial, floorMaterialPath);
            }
            floorMaterial.SetColor("_BaseColor", new Color(.38f, .32f, .25f));
            floorRenderer.sharedMaterial = floorMaterial;
            var surface = arena.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);
            if (existing) { EditorUtility.CopySerialized(surface.navMeshData, existing); Object.DestroyImmediate(surface.navMeshData); }
            else AssetDatabase.CreateAsset(surface.navMeshData, NavPath);
            surface.RemoveData();
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);
            surface.AddData();

            var armor = ScriptableObject.CreateInstance<AppearanceArchetype>();
            EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<AppearanceArchetype>("Assets/CharacterCreator/Archetypes/Starving Bandit.asset"), armor);
            armor.name = "Armored Duel Bandit";
            armor.height = new FloatRange { min = 1, max = 1 }; // keep the authored hand-to-staff reach
            armor.headwear = Items("spiked_helmet_a", "spiked_helmet_b", "black_guard_helmet");
            armor.torso = Items("armored_torso");
            armor.feet = Items("wrapped_boot");
            armor.hands = Items("leather_glove_a", "leather_glove_b");
            const string archetypePath = "Assets/MeleeCombat/ArmoredDuelBandit.asset";
            var old = AssetDatabase.LoadAssetAtPath<AppearanceArchetype>(archetypePath);
            if (old) { EditorUtility.CopySerialized(armor, old); Object.DestroyImmediate(armor); armor = old; }
            else AssetDatabase.CreateAsset(armor, archetypePath);

            var one = Create(prefab, "Ash", new Vector3(0, 0, -1.35f), Quaternion.identity, 17321, armor);
            var two = Create(prefab, "Rust", new Vector3(0, 0, 1.35f), Quaternion.Euler(0, 180, 0), 88403, armor);
            one.GetComponent<StarvingBandit>().target = two.transform;
            two.GetComponent<StarvingBandit>().target = one.transform;
            var monitor = new GameObject("Duel health and winner").AddComponent<BanditDuelMonitor>();
            monitor.left = one.GetComponent<ScavengerHealth>();
            monitor.right = two.GetComponent<ScavengerHealth>();
            monitor.captureSnapshots = true;

            var camera = new GameObject("Duel camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(2.7f, 2.25f, -3.8f);
            camera.transform.LookAt(new Vector3(0, 1, 0));
            var light = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.7f;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Built bandit duel scene: " + ScenePath);
        }

        static WeightedItem[] Items(params string[] names)
        {
            var result = new WeightedItem[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(ItemPath + names[i] + ".asset");
                if (!item) throw new System.InvalidOperationException("Missing armor item: " + names[i]);
                result[i] = new WeightedItem { item = item, weight = 1 };
            }
            return result;
        }

        static GameObject Create(GameObject prefab, string name, Vector3 position, Quaternion rotation, int seed, AppearanceArchetype archetype)
        {
            var fighter = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            fighter.name = name;
            fighter.transform.SetPositionAndRotation(position, rotation);
            var brain = fighter.GetComponent<StarvingBandit>();
            brain.guardAuthoringMode = false;
            brain.previewEnabled = false;
            brain.showTrajectory = false;
            var npc = fighter.GetComponent<NPCCharacterAppearance>();
            npc.seed = seed;
            npc.archetype = archetype;
            npc.Generate(seed);
            var health = fighter.GetComponent<ScavengerHealth>();
            health.maxHealth = 55;
            var strikeDamage = fighter.GetComponent<BanditDuelStrikeDamage>();
            if (!strikeDamage) strikeDamage = fighter.AddComponent<BanditDuelStrikeDamage>();
            strikeDamage.brain = brain;
            strikeDamage.cleanHitChance = .8f;
            if (!fighter.GetComponent<ScavengerRagdoll>())
                throw new System.InvalidOperationException("Duel fighter has no death ragdoll");
            return fighter;
        }
    }
}
