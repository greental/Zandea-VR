using System;
using System.IO;
using System.Linq;
using JunkerBody;
using JunkerEquipment;
using KenshiVR.Bandaging;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    [InitializeOnLoad]
    public static class CreatorPlayerPlayValidation
    {
        const string Request = "Temp/CreatorPlayer.play-request";
        const string Result = "Temp/CreatorPlayer.play.txt";
        const string PrefabPath = "Assets/CharacterCreator/Player/VR player - Man Creator.prefab";
        const string ArmorPath = "Assets/CharacterCreator/ImportedDownloads/Items/armored_torso.asset";
        const string Running = "CreatorPlayerPlayValidation.Running";
        static GameObject original, actor;
        static int phase, frame;

        static CreatorPlayerPlayValidation() => EditorApplication.update += Tick;

        [MenuItem("Tools/Character Creator/Validate Man player in Play Mode")]
        public static void Run() { File.WriteAllText(Request, "run"); Tick(); }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;
            if (!SessionState.GetBool(Running, false))
            {
                if (!File.Exists(Request)) return;
                File.Delete(Request);
                SessionState.SetBool(Running, true);
                phase = 0;
                if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
            }
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (phase == 0)
                {
                    var source = UnityEngine.Object.FindObjectsByType<VRBodyController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Single(body => body.gameObject.scene.path == "Assets/Main.unity");
                    original = source.transform.root.gameObject;
                    Vector3 position = original.transform.position;
                    Quaternion rotation = original.transform.rotation;
                    original.SetActive(false);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                    if (!prefab) throw new Exception("Generated player prefab is missing");
                    actor = UnityEngine.Object.Instantiate(prefab, position, rotation);
                    phase = 1; frame = Time.frameCount;
                    return;
                }
                if (Time.frameCount < frame + 15) return;
                var body = actor.GetComponentInChildren<VRBodyController>(true);
                var wounds = actor.GetComponentInChildren<PlayerWounds>(true);
                var rig = actor.GetComponentInChildren<JunkerEquipmentRig>(true);
                var appearance = actor.GetComponentInChildren<CharacterAppearanceController>(true);
                var cosmetics = actor.GetComponentInChildren<CharacterCosmeticsController>(true);
                var gait = actor.GetComponentInChildren<CreatorPlayerGait>(true);
                Check(body && body.isActiveAndEnabled && body.animator && body.animator.isActiveAndEnabled,
                    "creator tracked body and animator activate");
                Check(wounds && wounds.body == body && wounds.zones.Count >= 8,
                    "wounds bind to creator arms and legs");
                Check(rig && rig.body == body && new[] { rig.meleeSheath, rig.rightHipSheath,
                    rig.leftBackSheath, rig.rightBackSheath }.All(anchor => anchor &&
                    rig.slots.Contains(anchor.GetComponent<JunkerEquipmentSlot>())) &&
                    actor.GetComponentInChildren<BackpackInventory>(true),
                    "backpack and four additional item slots survive the conversion");
                Check(appearance && cosmetics && gait && gait.isActiveAndEnabled,
                    "appearance, armor, and leg gait systems activate");
                float headError = Vector3.Distance(body.head.position, body.headTarget.position);
                float leftHandError = Vector3.Distance(body.leftArm.tip.position, body.leftArm.target.position);
                float rightHandError = Vector3.Distance(body.rightArm.tip.position, body.rightArm.target.position);
                float leftFootError = Vector3.Distance(body.leftLeg.tip.position, body.leftLeg.target.position);
                float rightFootError = Vector3.Distance(body.rightLeg.tip.position, body.rightLeg.target.position);
                Check(headError < .03f && leftFootError < .1f && rightFootError < .1f,
                    $"IK errors head={headError:F3} feet={leftFootError:F3}/{rightFootError:F3}");
                // Main's desktop fallback controllers begin over one metre ahead of
                // the shoulders, beyond either human arm. Exercise each chain at a
                // reachable tracked-hand pose instead of counting that as an IK error.
                foreach (var arm in new[] { body.leftArm, body.rightArm })
                {
                    var reachable = arm.upper.position + body.avatarRoot.forward * .34f;
                    VRBodyController.SolveTwoBone(arm.upper, arm.lower, arm.tip,
                        reachable, arm.hint.position);
                    Check(Vector3.Distance(arm.tip.position, reachable) < .03f,
                        "creator arm IK misses a reachable tracked-hand target");
                }
                var data = appearance.Data.Copy();
                data.weight = .9f;
                appearance.Apply(data);
                var skin = body.avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .First(renderer => renderer.name == "Object_10");
                int shape = skin.sharedMesh.GetBlendShapeIndex("WeightHeavy");
                Check(shape >= 0 && skin.GetBlendShapeWeight(shape) > .7f,
                    "player body responds to an appearance edit");
                var armor = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(ArmorPath);
                cosmetics.outerArmor = armor;
                cosmetics.Refresh();
                var worn = actor.GetComponentsInChildren<AppearanceSpawnedItem>(true)
                    .FirstOrDefault(item => item.definition == armor);
                Check(worn && worn.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(renderer =>
                    renderer.bones.Length > 0 && renderer.bones.All(bone => bone && bone.IsChildOf(body.avatarRoot))),
                    "armored torso binds to the creator player's deforming skeleton");
                File.WriteAllText(Result, "PASS creator player: runtime head/leg IK, reachable two-arm solve, wounds, backpack, shared item slots, appearance morph, and skinned armor binding. " +
                    $"Default desktop hand targets were outside arm reach ({leftHandError:F3}/{rightHandError:F3} m); headset reach remains to test.");
                Finish();
            }
            catch (Exception error)
            {
                File.WriteAllText(Result, "FAIL " + error);
                Debug.LogException(error);
                Finish();
            }
        }

        static void Check(bool condition, string label)
        { if (!condition) throw new Exception(label); }

        static void Finish()
        {
            if (actor) UnityEngine.Object.Destroy(actor);
            if (original) original.SetActive(true);
            actor = original = null;
            SessionState.SetBool(Running, false);
            EditorApplication.isPlaying = false;
        }
    }
}
