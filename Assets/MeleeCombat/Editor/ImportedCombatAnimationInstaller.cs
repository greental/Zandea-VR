using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ImportedCombatAnimationInstaller
{
    const string Variants = "Assets/MeleeCombat/MocapTest/GeneratedVariants";
    const string Axe = "Assets/MeleeCombat/ImportedAnimations/ProMeleeAxePack/";
    const string Sword = "Assets/MeleeCombat/ImportedAnimations/GreatSword/";
    static readonly string[] OneHandAttacks = {
        "standing melee attack downward", "standing melee attack 360 high",
        "standing melee attack horizontal", "standing melee attack 360 low",
        "standing melee attack backhand", "standing melee combo attack ver. 1",
        "standing melee combo attack ver. 2", "standing melee combo attack ver. 3"
    };

    static AnimationClip Clip(string folder, string name)
    {
        var path = folder + name + ".fbx";
        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        return clip ? clip : throw new InvalidOperationException("Imported animation missing: " + path);
    }

    [MenuItem("Tools/Melee Combat/Install imported combat animations")]
    public static void Install()
    {
        int oneHand = 0, twoHand = 0;
        foreach (var file in Directory.GetFiles(Variants, "StarvingBandit_*.prefab"))
        {
            string path = file.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_BoStaff") || name.EndsWith("_PoleBlade")) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var grip = root.GetComponentInChildren<MocapStaffPreview>(true);
                var driver = root.GetComponentInChildren<BanditMocapNpcDriver>(true);
                var brain = root.GetComponent<StarvingBandit>();
                var animator = grip ? grip.GetComponent<Animator>() : null;
                var controller = animator ? animator.runtimeAnimatorController as AnimatorController : null;
                if (!grip || !driver || !brain || !controller)
                    throw new InvalidOperationException("Unconfigured generated bandit " + name);
                bool twoHands = grip.secondaryHandGrips;
                bool leftHand = grip.primaryHandIsLeft && !name.EndsWith("_ShieldAndSaber");
                var machine = controller.layers[0].stateMachine;
                var locomotion = machine.states.First(s => s.state.name == "Locomotion").state.motion as BlendTree;
                if (locomotion != null)
                {
                    var children = locomotion.children;
                    children[5].motion = Clip(Axe, "standing walk back");
                    locomotion.children = children;
                }
                var playlist = grip.animations.ToList();
                AnimationClip[] attackClips;
                if (twoHands)
                {
                    var slash = Clip(Sword, "Great Sword Slash");
                    attackClips = new[] { grip.attack, slash, grip.attack, slash,
                        grip.attack, slash, grip.attack, slash };
                    twoHand++;
                }
                else
                {
                    attackClips = OneHandAttacks.Select(n => Clip(Axe, n)).ToArray();
                    oneHand++;
                    // These clips were authored for a right-hand axe. Mirror the
                    // left-hand HookSword's animation, while keeping its IK grip.
                    var tree = machine.states.First(s => s.state.name == "Locomotion").state.motion as BlendTree;
                    if (tree != null)
                    {
                        var children = tree.children;
                        children[0].motion = Clip(Axe, "standing idle");
                        children[1].motion = Clip(Axe, "standing run forward");
                        children[3].motion = Clip(Axe, "standing walk right");
                        children[5].motion = Clip(Axe, "standing walk back");
                        children[7].motion = Clip(Axe, "standing walk left");
                        tree.children = children;
                    }
                }
                for (int i = 0; i < 8; i++)
                {
                    var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "AttackDirection" + i)
                        ?? machine.AddState("AttackDirection" + i);
                    state.motion = attackClips[i];
                    state.mirror = leftHand || (twoHands && i >= 4);
                    state.speed = 1f;
                    // Keep the NPC's phase clock aligned with the source clip.
                    // Combos remain one attack event until multi-hit scoring exists.
                    float duration = Mathf.Max(.6f, attackClips[i].length);
                    brain.attacks[i].windupSeconds = duration * .24f;
                    brain.attacks[i].strikeSeconds = duration * .36f;
                    brain.attacks[i].followSeconds = duration * .18f;
                    brain.attacks[i].recoverySeconds = duration * .22f;
                    if (!playlist.Contains(attackClips[i])) playlist.Add(attackClips[i]);
                }
                var guard = twoHands ? Clip(Sword, "Great Sword Blocking") : Clip(Axe, "standing block idle");
                var parry = twoHands ? guard : Clip(Axe, "standing block react large");
                SetState(machine, "Block", guard, twoHands);
                SetState(machine, "Parry", parry, twoHands || leftHand);
                foreach (var entry in new[] { ("HitLeft", "standing react large from left"),
                    ("HitRight", "standing react large from right"), ("HitGut", "standing react large gut") })
                    SetState(machine, entry.Item1, Clip(Axe, entry.Item2), false);
                driver.useImportedCombatAttacks = true;
                brain.useAnimatedAttackDisplacement = true;
                animator.applyRootMotion = true;
                driver.useImportedCombatGuards = true;
                driver.useImportedCombatReactions = true;
                grip.mirrorDirectionalPreview = leftHand;
                grip.directionalPreviewClips = leftHand ? attackClips : Array.Empty<AnimationClip>();
                if (!twoHands)
                {
                    // Fast single-hand swings can outrun the physical grip target
                    // between fixed physics steps. Keep a small compliant joint.
                    grip.staff.npcGripSlack = .02f;
                    grip.staff.npcAngularSlack = 6f;
                    grip.staff.positionSpring = 3000f;
                    grip.staff.positionDamper = 140f;
                    grip.staff.rotationSpring = 1200f;
                    grip.staff.rotationDamper = 90f;
                    grip.staff.maximumDriveForce = 2000f;
                }
                foreach (var clip in new[] { guard, parry, Clip(Axe, "standing react large from left"),
                    Clip(Axe, "standing react large from right"), Clip(Axe, "standing react large gut") })
                    if (!playlist.Contains(clip)) playlist.Add(clip);
                // Keep the rest of the named pack in the Inspector scrub list.
                // Utility motions such as disarm, jump, and unarmed actions are
                // previewable but are not fired by NPC AI without matching mechanics.
                if (!twoHands)
                    foreach (var animationFile in Directory.GetFiles(Axe, "*.fbx").OrderBy(p => p))
                    {
                        var extra = Clip(Axe, Path.GetFileNameWithoutExtension(animationFile));
                        if (!playlist.Contains(extra)) playlist.Add(extra);
                    }
                grip.animations = playlist.ToArray();
                EditorUtility.SetDirty(controller);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"IMPORTED_COMBAT_INSTALLED {name} hand={(twoHands ? "two" : leftHand ? "left" : "right")} attacks={attackClips.Length} preview={playlist.Count}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"IMPORTED_COMBAT_INSTALL_COMPLETE oneHand={oneHand} twoHand={twoHand}");
    }

    static void SetState(AnimatorStateMachine machine, string name, AnimationClip clip, bool mirror)
    {
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
        state.motion = clip;
        state.mirror = mirror;
    }
}
