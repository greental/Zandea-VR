using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GeneratedMocapVariantBuilder
{
    const string Output = "Assets/MeleeCombat/MocapTest/GeneratedVariants";
    const string Source = "Assets/MeleeCombat/MocapTest/StarvingBandit_MocapTest.prefab";
    const string Generated = "Assets/MyAssets/mesh/weapons/Generated/Gemini_Generated_Image_";
    const string Clips = "Assets/Kevin Iglesias/Human Animations/Animations/Male";

    struct Variant
    {
        public string name, mesh, attack, idle;
        public Vector3 axis;
        public float length;
        public bool bothHands, leftHand, shield;
        public Variant(string name, string mesh, Vector3 axis, float length, string attack, string idle,
            bool bothHands = false, bool leftHand = false, bool shield = false)
        { this.name = name; this.mesh = mesh; this.axis = axis; this.length = length; this.attack = attack;
          this.idle = idle; this.bothHands = bothHands; this.leftHand = leftHand; this.shield = shield; }
    }
    static readonly Variant[] Variants = {
        new Variant("BoStaff", null, Vector3.up, 2f, "HumanM@AttackPolearm01", "HumanM@CombatIdlePolearm01", bothHands:true),
        new Variant("CurvedCleaver", "5yoj435yoj435yoj", Vector3.left, .85f, "HumanM@Attack1H01_R", "HumanM@CombatIdle1H01"),
        new Variant("PoleBlade", "7a0nqg7a0nqg7a0n", Vector3.up, 1.65f, "HumanM@AttackPolearm01", "HumanM@CombatIdlePolearm01", bothHands:true),
        new Variant("HeavyCleaver", "cmp4cscmp4cscmp4", Vector3.left, 1.35f, "HumanM@Attack2H01", "HumanM@CombatIdle2H01", bothHands:true),
        new Variant("HookSword", "ctvo3tctvo3tctvo", Vector3.left, .95f, "HumanM@Attack1H01_L", "HumanM@CombatIdle1H01", leftHand:true),
        new Variant("RustedDoubleAxe", "icncchicncchicnc", Vector3.up, 1.35f, "HumanM@Attack2H01", "HumanM@CombatIdle2H01", bothHands:true),
        new Variant("BroadCleaver", "j8jvw8j8jvw8j8jv", Vector3.up, 1.25f, "HumanM@Attack2H01", "HumanM@CombatIdle2H01", bothHands:true),
        new Variant("ShortSaber", "qh6xjiqh6xjiqh6x", Vector3.left, .9f, "HumanM@Attack1H01_R", "HumanM@CombatIdle1H01"),
        new Variant("LongBlade", "rm5g6jrm5g6jrm5g", Vector3.up, 1.5f, "HumanM@Attack2H01", "HumanM@CombatIdle2H01", bothHands:true),
        new Variant("SpikedDoubleAxe", "scu0f9scu0f9scu0", Vector3.up, 1.35f, "HumanM@Attack2H01", "HumanM@CombatIdle2H01", bothHands:true),
        new Variant("SawCleaver", "v24ohsv24ohsv24o", Vector3.left, 1.4f, "HumanM@Attack2H01", "HumanM@CombatIdle2H01", bothHands:true),
        new Variant("Axe", "xl1dq9xl1dq9xl1d", Vector3.up, .9f, "HumanM@Attack1H01_R", "HumanM@CombatIdle1H01"),
        new Variant("SpikedMace", "xopexxxopexxxope", Vector3.up, .8f, "HumanM@Attack1H01_R", "HumanM@CombatIdle1H01"),
        new Variant("ShieldAndSaber", null, Vector3.up, .65f, "HumanM@AttackShield01", "HumanM@CombatIdle1H01", leftHand:true, shield:true)
    };

    [MenuItem("Tools/Melee Combat/Build generated weapon bandits")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder("Assets/MeleeCombat/MocapTest", "GeneratedVariants");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        if (!source) throw new InvalidOperationException("Build the original isolated Bo Staff test first.");
        var clips = LoadClips();
        var prefabs = new List<GameObject>();
        foreach (var variant in Variants)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "StarvingBandit_" + variant.name;
            try
            {
                var preview = instance.GetComponentInChildren<MocapStaffPreview>(true);
                if (!preview) throw new InvalidOperationException("Missing preview on " + variant.name);
                preview.attack = Clip(clips, variant.attack);
                preview.animations = Playlist(clips, variant).ToArray();
                preview.animationIndex = 0;
                preview.primaryHandIsLeft = variant.leftHand;
                preview.secondaryHandGrips = variant.bothHands;
                preview.attachment = variant.bothHands ? MocapStaffPreview.Attachment.BothHands : MocapStaffPreview.Attachment.PrimaryHand;
                preview.playing = true;
                preview.showTrajectory = false;
                var weapon = preview.staff.transform;
                weapon.name = variant.name + " weapon anchor";
                if (variant.mesh != null || variant.shield)
                {
                    foreach (var renderer in weapon.GetComponentsInChildren<Renderer>(true))
                        UnityEngine.Object.DestroyImmediate(renderer.gameObject);
                    var grip = variant.leftHand ? preview.leftGripLocal : preview.rightGripLocal;
                    var direction = (variant.leftHand ? preview.leftGripLocal - preview.rightGripLocal :
                        preview.rightGripLocal - preview.leftGripLocal).normalized;
                    if (variant.shield) AddTemporaryShield(weapon, grip);
                    else AddGenerated(weapon, variant.mesh, variant.axis, variant.length, grip, direction, variant.bothHands);
                    var tip = new GameObject("Animated striking tip").transform;
                    tip.SetParent(weapon, false);
                    tip.localPosition = grip + direction * variant.length * .75f;
                    preview.positiveTip = tip;
                    if (variant.shield)
                    {
                        var secondary = new GameObject("Animated right-hand saber").transform;
                        secondary.SetParent(instance.transform, false);
                        AddGenerated(secondary, "qh6xjiqh6xjiqh6x", Vector3.left, .9f,
                            Vector3.zero, (preview.rightGripLocal - preview.leftGripLocal).normalized, false);
                        preview.secondaryVisualWeapon = secondary;
                        preview.secondaryWeaponFromRightHand = Quaternion.Inverse(preview.rightHandFromStaff);
                    }
                }
                // Keep the original NPC, health, ragdoll, navigation, and appearance
                // components. Only the obsolete procedural animation driver conflicts
                // with the imported full-body clip preview.
                var oldAnimation = instance.GetComponent<BanditRigMotion>();
                if (oldAnimation) UnityEngine.Object.DestroyImmediate(oldAnimation);
                var brain = instance.GetComponent<StarvingBandit>();
                if (brain) brain.enabled = true;
                var agent = instance.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent) agent.enabled = true;
                var path = Output + "/StarvingBandit_" + variant.name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                prefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                BuildScene(path, variant.name);
                Debug.Log($"GENERATED_VARIANT {variant.name} mesh={variant.mesh ?? (variant.shield ? "temporary shield + generated saber" : "BO_Staff")} clips={preview.animations.Length} attack={preview.attack.name}");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        BuildGallery(prefabs);
        AssetDatabase.SaveAssets();
    }

    static Dictionary<string, AnimationClip> LoadClips()
    {
        var map = new Dictionary<string, AnimationClip>();
        foreach (var file in Directory.GetFiles(Clips, "*.fbx", SearchOption.AllDirectories))
        {
            var path = file.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            if (clip) map[name] = clip;
        }
        return map;
    }
    static AnimationClip Clip(Dictionary<string, AnimationClip> clips, string name)
    {
        if (!clips.TryGetValue(name, out var clip)) throw new InvalidOperationException("Missing clip " + name);
        return clip;
    }
    static IEnumerable<AnimationClip> Playlist(Dictionary<string, AnimationClip> clips, Variant variant)
    {
        yield return Clip(clips, variant.attack);
        yield return Clip(clips, variant.idle);
        foreach (var name in new[] { "HumanM@CombatIdle01", "HumanM@CombatDamage01", "HumanM@Death01", "HumanM@Idle01" })
            yield return Clip(clips, name);
        foreach (var pair in clips.OrderBy(x => x.Key))
            if (pair.Key.Contains("@Run") || pair.Key.Contains("@StrafeRun")) yield return pair.Value;
    }
    static void AddGenerated(Transform parent, string id, Vector3 axis, float length, Vector3 desiredGrip,
        Vector3 desiredDirection, bool bothHands)
    {
        var path = Generated + id + ".glb";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!asset) throw new InvalidOperationException("Missing generated weapon " + path);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("No renderer in " + path);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        float span = Mathf.Abs(axis.x) > .5f ? bounds.size.x : bounds.size.y;
        var grip = bounds.center;
        float fraction = bothHands ? .26f : .15f;
        float along = axis.x < -.5f ? bounds.max.x - span * fraction :
            (axis.x > .5f ? bounds.min.x : bounds.min.y) + span * fraction;
        if (Mathf.Abs(axis.x) > .5f) grip.x = along; else grip.y = along;
        float scale = length / span;
        var rotation = Quaternion.FromToRotation(axis, desiredDirection);
        model.transform.SetParent(parent, false);
        model.transform.localScale = Vector3.one * scale;
        model.transform.localRotation = rotation;
        model.transform.localPosition = desiredGrip - rotation * (grip * scale);
        model.name = "Generated " + id;
    }
    static void AddTemporaryShield(Transform parent, Vector3 grip)
    {
        var shield = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shield.name = "Temporary shield visual";
        shield.transform.SetParent(parent, false);
        shield.transform.localPosition = grip + new Vector3(0, 0, .10f);
        shield.transform.localRotation = Quaternion.Euler(90, 0, 0);
        shield.transform.localScale = new Vector3(.33f, .035f, .33f);
        var collider = shield.GetComponent<Collider>();
        if (collider) UnityEngine.Object.DestroyImmediate(collider);
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader)
        {
            var matPath = Output + "/TemporaryShield.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, matPath); }
            material.SetColor("_BaseColor", new Color(.17f, .15f, .13f));
            shield.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
    static void BuildScene(string path, string name)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AddEnvironment();
        var bandit = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
        bandit.transform.position = Vector3.zero;
        SetPreviewOnly(bandit);
        EditorSceneManager.SaveScene(scene, Output + "/" + name + "Preview.unity");
    }
    static void BuildGallery(List<GameObject> prefabs)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AddEnvironment();
        for (int i = 0; i < prefabs.Count; i++)
        {
            var bandit = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
            bandit.transform.position = new Vector3((i % 7 - 3) * 2.35f, 0, (i / 7) * 3.4f);
            SetPreviewOnly(bandit);
        }
        var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
        camera.transform.position = new Vector3(0, 4.5f, 19f);
        camera.transform.LookAt(new Vector3(0, 1, 1.7f));
        camera.fieldOfView = 55;
        EditorSceneManager.SaveScene(scene, Output + "/GeneratedWeaponGallery.unity");
    }
    static void AddEnvironment()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Preview floor";
        floor.transform.localScale = new Vector3(5, 1, 5);
        var light = new GameObject("Preview light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50, -30, 0);
        var camera = new GameObject("Preview camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(3, 2, 4);
        camera.transform.LookAt(new Vector3(0, 1, 0));
    }
    static void SetPreviewOnly(GameObject bandit)
    {
        // The preview scenes have no NavMesh or player. Keep these components on
        // the prefab, while the scene instances focus on inspecting animation.
        var brain = bandit.GetComponent<StarvingBandit>();
        if (brain) brain.enabled = false;
        var agent = bandit.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent) agent.enabled = false;
    }
}


