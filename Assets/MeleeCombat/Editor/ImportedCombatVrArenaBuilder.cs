using System;
using System.Linq;
using JunkerBody;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class ImportedCombatVrArenaBuilder
{
    public const string Output = "Assets/MeleeCombat/MocapTest/GeneratedVariants/ImportedCombatVRArena.unity";
    const string Arena = "Assets/MeleeCombat/MocapTest/GeneratedVariants/ImportedCombatArena.unity";
    const string Player = "Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab";
    const string SwordMesh = "Assets/MyAssets/mesh/weapons/Generated/Gemini_Generated_Image_rm5g6jrm5g6jrm5g.glb";
    const string SwordPrefab = "Assets/MeleeCombat/MocapTest/GeneratedVariants/PlayerPracticeLongBlade.prefab";

    [MenuItem("Zandea/Melee Combat/Build VR imported combat arena")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SwordMesh);
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Player);
        if (!source || !playerPrefab) throw new InvalidOperationException("VR player or generated sword mesh is missing");
        var swordPrefab = BuildSword(source);
        var scene = EditorSceneManager.OpenScene(Arena, OpenSceneMode.Single);
        var axe = scene.GetRootGameObjects().Select(go => go.GetComponent<StarvingBandit>())
            .FirstOrDefault(brain => brain && brain.name == "Axe bandit");
        if (!axe) throw new InvalidOperationException("Saved Axe bandit missing from arena");
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "HookSword bandit" || root.name == "Duel health and winner" || root.name == "Duel camera")
                UnityEngine.Object.DestroyImmediate(root);

        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        player.name = "VR player - JunkerWeapon";
        player.transform.SetPositionAndRotation(new Vector3(0f, 0f, 2.5f), Quaternion.Euler(0f, 180f, 0f));
        Debug.Log($"VR_PLAYER_COMPONENTS {string.Join(",", player.GetComponents<Component>().Select(c => c ? c.GetType().Name : "Missing"))} childHealth={player.GetComponentsInChildren<ScavengerPlayerHealth>(true).Length}");
        var playerHealth = player.GetComponentInChildren<ScavengerPlayerHealth>(true);
        var body = player.GetComponentInChildren<VRBodyController>(true);
        if (!playerHealth) throw new InvalidOperationException("JunkerWeapon needs player health");
        Debug.Log($"VR_PLAYER_CHECK health={playerHealth != null} body={body != null} hmd={body && body.hmd != null}");
        axe.target = player.transform;
        axe.cycleAttackDirections = true;
        axe.initialAttackDelay = 3f;
        axe.staff.damagePerJoule = .08f;
        var scorer = axe.GetComponent<BanditDuelStrikeDamage>();
        if (!scorer) scorer = axe.gameObject.AddComponent<BanditDuelStrikeDamage>();
        scorer.brain = axe;
        scorer.damage = 7f;
        scorer.cleanHitChance = .8f;
        scorer.maximumRootDistance = 2f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(axe);
        PrefabUtility.RecordPrefabInstancePropertyModifications(axe.staff);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);

        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Practice sword table";
        table.transform.SetPositionAndRotation(new Vector3(-.55f, .5f, 1.75f), Quaternion.identity);
        table.transform.localScale = new Vector3(.7f, 1f, 1.15f);
        var sword = (GameObject)PrefabUtility.InstantiatePrefab(swordPrefab, scene);
        sword.name = "Player practice sword - generated long blade";
        sword.transform.SetPositionAndRotation(new Vector3(-.55f, 1.12f, 1.55f), Quaternion.Euler(90f, 0f, 0f));
        PrefabUtility.RecordPrefabInstancePropertyModifications(sword.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, Output, true)) throw new InvalidOperationException("Could not save VR arena");
        AssetDatabase.SaveAssets();
        Debug.Log($"VR_ARENA_BUILT {Output} player={player.name} bandit={axe.name} sword={sword.name}");
    }

    static GameObject BuildSword(GameObject source)
    {
        var root = new GameObject("PlayerPracticeLongBlade");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            visual.name = "Generated sword mesh";
            foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
            foreach (var shape in visual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(shape);
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Generated sword has no renderer");
            var bounds = GetBounds(renderers);
            int axis = bounds.size.x > bounds.size.y ? (bounds.size.x > bounds.size.z ? 0 : 2) :
                (bounds.size.y > bounds.size.z ? 1 : 2);
            visual.transform.localRotation = axis == 0 ? Quaternion.Euler(0f, 0f, 90f) :
                axis == 2 ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
            bounds = GetBounds(renderers);
            visual.transform.localScale = Vector3.one / Mathf.Max(.001f, bounds.size.y);
            bounds = GetBounds(renderers);
            visual.transform.position += root.transform.position + Vector3.up * .4f - bounds.center;
            bounds = GetBounds(renderers);
            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = 1.4f;
            rigidbody.useGravity = true;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.center = Vector3.up * .4f;
            collider.height = 1f;
            collider.radius = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * .3f, .025f, .09f);
            var grip = new GameObject("Grip - move to fit hand").transform;
            grip.SetParent(root.transform, false);
            var grab = root.AddComponent<XRGrabInteractable>();
            grab.attachTransform = grip;
            var weapon = root.AddComponent<PhysicalMeleeWeapon>();
            weapon.grip = grip;
            weapon.rightGrip = grip;
            weapon.leftGrip = grip;
            weapon.mass = rigidbody.mass;
            weapon.strikeType = PhysicalMeleeWeapon.StrikeType.Cutting;
            root.AddComponent<PracticeSwordGuard>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, SwordPrefab);
            if (!prefab) throw new InvalidOperationException("Could not save practice sword prefab");
            Debug.Log($"PRACTICE_SWORD_BUILT axis={axis} size={bounds.size} grip={grip.localPosition} colliderRadius={collider.radius:F3}");
            return prefab;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static Bounds GetBounds(Renderer[] renderers)
    {
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
