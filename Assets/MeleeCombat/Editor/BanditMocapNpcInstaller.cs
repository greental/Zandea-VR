using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BanditMocapNpcInstaller
{
    const string Folder = "Assets/MeleeCombat/MocapTest/GeneratedVariants";
    const string AnimationFolder = "Assets/Kevin Iglesias/Human Animations/Animations/Male";

    [MenuItem("Tools/Melee Combat/Connect generated bandits to NPC animation")]
    public static void Install()
    {
        BoStaffDirectionalClipBuilder.Build();
        var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimationFolder })
            .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
            .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
        AnimationClip Clip(string name) => clips.TryGetValue(name, out var c) ? c :
            throw new InvalidOperationException("Missing animation " + name);
        var fingerPose = File.ReadAllLines("Assets/MeleeCombat/MocapTest/ClosedHandsPose.csv")
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .ToDictionary(parts => parts[0], parts => new Quaternion(
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture),
                float.Parse(parts[3], CultureInfo.InvariantCulture),
                float.Parse(parts[4], CultureInfo.InvariantCulture)));
        var movement = new (string name, Vector2 direction)[] {
            ("HumanM@Run01_Forward", new Vector2(0, 1)),
            ("HumanM@StrafeRun01_ForwardRight", new Vector2(.707f, .707f)),
            ("HumanM@StrafeRun01_Right", new Vector2(1, 0)),
            ("HumanM@StrafeRun01_BackwardRight", new Vector2(.707f, -.707f)),
            ("HumanM@Run01_Backward", new Vector2(0, -1)),
            ("HumanM@StrafeRun01_BackwardLeft", new Vector2(-.707f, -.707f)),
            ("HumanM@StrafeRun01_Left", new Vector2(-1, 0)),
            ("HumanM@StrafeRun01_ForwardLeft", new Vector2(-.707f, .707f))
        };
        int count = 0;
        foreach (var path in Directory.GetFiles(Folder, "StarvingBandit_*.prefab"))
        {
            var assetPath = path.Replace('\\', '/');
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var grip = root.GetComponentInChildren<MocapStaffPreview>(true);
                if (!grip || !grip.attack || grip.animations.Length < 2)
                    throw new InvalidOperationException("Missing attack or idle on " + assetPath);
                var controllerPath = Folder + "/" + Path.GetFileNameWithoutExtension(path) + ".controller";
                var previous = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (previous) AssetDatabase.DeleteAsset(controllerPath);
                var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
                controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
                var machine = controller.layers[0].stateMachine;
                var locomotion = machine.AddState("Locomotion");
                machine.defaultState = locomotion;
                var tree = new BlendTree { name = "Directional locomotion", blendType = BlendTreeType.FreeformCartesian2D,
                    blendParameter = "MoveX", blendParameterY = "MoveY", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(tree, controller);
                tree.AddChild(grip.animations[1], Vector2.zero);
                foreach (var entry in movement) tree.AddChild(Clip(entry.name), entry.direction);
                locomotion.motion = tree;
                bool directionalStaff = Path.GetFileNameWithoutExtension(path).EndsWith("_BoStaff");
                if (directionalStaff)
                {
                    var directions = BoStaffDirectionalClipBuilder.Names;
                    var playlist = grip.animations.ToList();
                    for (int direction = 0; direction < directions.Length; direction++)
                    {
                        var clip = BoStaffDirectionalClipBuilder.Load(directions[direction]);
                        if (!clip) throw new InvalidOperationException("Missing directional clip " + directions[direction]);
                        var state = machine.AddState("AttackDirection" + direction);
                        state.motion = clip;
                        var attack = root.GetComponent<StarvingBandit>().attacks[direction];
                        float duration = attack.windupSeconds + attack.strikeSeconds +
                            attack.followSeconds + attack.recoverySeconds;
                        state.speed = clip.length / Mathf.Max(.1f, duration);
                        if (!playlist.Contains(clip)) playlist.Add(clip);
                    }
                    grip.animations = playlist.ToArray();
                }
                else
                {
                    machine.AddState("Attack").motion = grip.attack;
                    if (grip.secondaryHandGrips)
                    {
                        var mirrored = machine.AddState("AttackMirrored");
                        mirrored.motion = grip.attack;
                        mirrored.mirror = true;
                    }
                }
                machine.AddState("Hit").motion = Clip("HumanM@CombatDamage01");
                var animator = grip.GetComponent<Animator>();
                var sourceAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(
                    "Assets/MeleeCombat/MocapTest/StarvingBandit_MocapAvatar.asset");
                animator.avatar = sourceAvatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                var poses = grip.fingers;
                for (int i = 0; i < poses.Length; i++)
                {
                    if (!poses[i].bone || !fingerPose.TryGetValue(poses[i].bone.name, out var closed)) continue;
                    poses[i].rotation = Quaternion.Slerp(Quaternion.identity, closed, .55f);
                }
                grip.fingers = poses;
                var driver = grip.GetComponent<BanditMocapNpcDriver>();
                if (!driver) driver = grip.gameObject.AddComponent<BanditMocapNpcDriver>();
                driver.grip = grip;
                driver.useDirectionalStaffAttacks = directionalStaff;
                driver.brain = root.GetComponent<StarvingBandit>();
                driver.brain.guardAuthoringMode = false;
                driver.brain.dodgeIncomingNpcAttacks = true;
                bool shield = Path.GetFileNameWithoutExtension(path).Contains("ShieldAndSaber");
                bool weaponParry = !shield;
                driver.brain.blockIncomingNpcAttacks = shield;
                driver.brain.parryIncomingNpcAttacks = weaponParry;
                if (shield || weaponParry) driver.brain.dodgeIncomingNpcAttacks = false;
                // The authored pose drives a joint target; the actual weapon remains dynamic.
                // Hand IK reads the actual rigidbody pose after each animation frame.
                if (weaponParry)
                {
                    var physical = grip.staff;
                    var rigidbody = physical.GetComponent<Rigidbody>();
                    var shape = physical.GetComponent<CapsuleCollider>();
                    var oldTarget = grip.desiredStaff.Find("Mocap physical grip target");
                    if (oldTarget) UnityEngine.Object.DestroyImmediate(oldTarget.gameObject);
                    var target = new GameObject("Mocap physical grip target").transform;
                    target.SetParent(grip.desiredStaff, false);
                    target.localPosition = physical.transform.InverseTransformPoint(physical.grip.position);
                    target.localRotation = Quaternion.Inverse(physical.transform.rotation) * physical.grip.rotation;
                    physical.npcGripTarget = target;
                    physical.enabled = true;
                    rigidbody.isKinematic = false;
                    rigidbody.useGravity = false;
                    shape.enabled = true;
                    grip.driveVisualStaff = false;
                }
                driver.agent = driver.brain.agent;
                driver.health = driver.brain.health;
                driver.ragdoll = root.GetComponent<ScavengerNPC.ScavengerRagdoll>();
                driver.enabled = true;
                grip.enabled = false;
                grip.applySavedFingerPose = true;
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                count++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (var path in Directory.GetFiles(Folder, "*Preview.unity").Concat(new[] { Folder + "/GeneratedWeaponGallery.unity" }))
        {
            var scene = EditorSceneManager.OpenScene(path.Replace('\\', '/'));
            foreach (var root in scene.GetRootGameObjects())
            {
                var grip = root.GetComponentInChildren<MocapStaffPreview>(true);
                if (!grip) continue;
                var driver = grip.GetComponent<BanditMocapNpcDriver>();
                grip.enabled = true;
                grip.applySavedFingerPose = true;
                if (driver) driver.enabled = false;
                // Scrub previews should remain deterministic even when the gameplay
                // Bo Staff prefab uses a dynamic weapon and joint.
                if (grip.staff && grip.staff.enabled)
                {
                    grip.staff.enabled = false;
                    grip.staff.GetComponent<CapsuleCollider>().enabled = false;
                    grip.staff.GetComponent<Rigidbody>().isKinematic = true;
                    grip.driveVisualStaff = true;
                }
            }
            EditorSceneManager.SaveScene(scene);
        }
        BoStaffDirectionalPreviewBuilder.Build();
        AssetDatabase.SaveAssets();
        Debug.Log("MOCAP_NPC_INSTALLED " + count + " prefabs; directional locomotion, attack, hit, ragdoll death; preview scenes preserved");
    }
}
