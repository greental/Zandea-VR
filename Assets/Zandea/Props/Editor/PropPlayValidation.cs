using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRKoombar.WeaponsStaging.Contracts;

namespace Zandea.Props.Editor
{
    [InitializeOnLoad]
    public static class PropPlayValidation
    {
        const string ScenePath = "Assets/Zandea/Props/ZandeaPropInteractions.unity";
        const string Request = "Temp/ZandeaProps.play-request";
        const string Report = "Temp/ZandeaProps.play-validation.txt";
        static string previousScene;
        static int phase, startFrame;
        static bool running;
        static string[] chestIds;
        static PropContainer chest;

        static PropPlayValidation()
        {
            running = SessionState.GetBool("ZandeaProps.running", false);
            previousScene = SessionState.GetString("ZandeaProps.previousScene", "");
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Mode;
        }
        [MenuItem("Tools/Zandea/Props/Run Representative Play Validation")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath))
                throw new InvalidOperationException("Save/import the prop test scene and exit Play Mode first.");
            previousScene = SceneManager.GetActiveScene().path;
            SessionState.SetString("ZandeaProps.previousScene", previousScene);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            running = true; phase = 0; startFrame = 0;
            SessionState.SetBool("ZandeaProps.running", true);
            File.WriteAllText(Report, "RUNNING\n");
            EditorApplication.isPlaying = true;
        }
        static void Tick()
        {
            if (File.Exists(Request) && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(Request);
                try { Begin(); } catch (Exception e) { File.WriteAllText(Report, "FAILED TO START: " + e); }
            }
            if (!running || !EditorApplication.isPlaying || Time.frameCount < 35) return;
            if (startFrame == 0) startFrame = Time.frameCount;
            try
            {
                if (phase == 0)
                {
                    var pot = Find("Test pot").GetComponent<BreakableProp>();
                    var stool = Find("Test stool").GetComponent<BreakableProp>();
                    var barrel = Find("Test barrel").GetComponent<BreakableProp>();
                    Require(pot && stool && barrel && !pot.IsBroken && !stool.IsBroken && !barrel.IsBroken,
                        "representative breakables start intact");
                    chest = Find("Test chest").GetComponent<PropContainer>();
                    Require(chest && chest.Contents.Count == 2, "chest creates two items once");
                    chestIds = chest.Contents.Select(i => i.InstanceId).ToArray();
                    chest.SetOpen(true);
                    Require(chest.GetComponentsInChildren<NpcWorldItem>().Length == 2,
                        "opened chest presents two reachable item objects");
                    chest.SetOpen(false);
                    phase = 1; startFrame = Time.frameCount;
                }
                else if (phase == 1 && Time.frameCount - startFrame >= 3)
                {
                    Require(chest.Contents.Select(i => i.InstanceId).SequenceEqual(chestIds),
                        "closing chest keeps item identities");
                    chest.SetOpen(true);
                    Require(chest.Contents.Select(i => i.InstanceId).SequenceEqual(chestIds),
                        "reopening chest does not regenerate contents");
                    var barrel = Find("Test barrel").GetComponent<BreakableProp>();
                    var barrelContents = barrel.GetComponent<PropContainer>();
                    Require(barrelContents.Contents.Count == 1, "barrel has one existing item");
                    var originalId = barrelContents.Contents[0].InstanceId;
                    barrel.ApplyDamage(new DamagePacket { BaseDamage = 30, DamageType = DamageType.Kinetic,
                        HitPointWorld = barrel.transform.position, HitNormalWorld = Vector3.up });
                    Require(barrel.IsBroken && barrelContents.Contents.Count == 0,
                        "melee damage breaks barrel and empties container once");
                    Require(UnityEngine.Object.FindObjectsByType<NpcWorldItem>(FindObjectsSortMode.None).Any(m =>
                        m.Item != null && m.Item.InstanceId == originalId), "barrel spills same item instance");
                    barrel.ApplyDamage(new DamagePacket { BaseDamage = 30, HitPointWorld = barrel.transform.position });
                    Require(barrelContents.Contents.Count == 0, "repeat damage cannot spill twice");
                    var pot = Find("Test pot").GetComponent<BreakableProp>();
                    pot.ApplyDamage(new DamagePacket { BaseDamage = 10, HitPointWorld = pot.transform.position });
                    Require(pot.IsBroken, "existing melee damage path breaks pot");
                    var stool = Find("Test stool").GetComponent<BreakableProp>();
                    stool.ApplyDamage(new DamagePacket { BaseDamage = 10, HitPointWorld = stool.transform.position });
                    Require(stool.IsBroken, "existing melee damage path breaks stool");
                    Require(UnityEngine.Object.FindObjectsByType<PropDebrisLifetime>(FindObjectsSortMode.None).Length <= 3,
                        "active debris burst cap is respected");
                    var rack = Find("Test rack").GetComponentInChildren<PropRackSocket>();
                    Require(rack && rack.CurrentItem != null && rack.CurrentItem.WorldObject,
                        "rack mounts one real world weapon with item identity");
                    var weapon = rack.CurrentItem;
                    rack.ItemTaken(weapon.WorldObject.GetComponent<PropItemPresentation>(), weapon);
                    Require(rack.CurrentItem == null && weapon.WorldObject, "taking weapon clears slot without destroying it");
                    var statue = Find("Test statue");
                    Require(!statue.GetComponent<Rigidbody>() && statue.GetComponent<Collider>(),
                        "statue remains static scenery");
                    File.WriteAllText(Report, "PASS: chest identity/open-close, barrel one-time spill, melee destruction, debris cap, rack removal, static statue.\nXR grab/release, lid feel, collisions, and Quest 2 performance require headset checks.\n");
                    running = false; SessionState.SetBool("ZandeaProps.running", false); EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                File.WriteAllText(Report, "FAIL: " + e + "\n");
                running = false; SessionState.SetBool("ZandeaProps.running", false); EditorApplication.isPlaying = false;
            }
        }
        static GameObject Find(string name)
        {
            var found = GameObject.Find(name);
            if (!found) throw new Exception("Missing " + name);
            return found;
        }
        static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
        static void Mode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode || string.IsNullOrEmpty(previousScene)) return;
            var test = SceneManager.GetSceneByPath(ScenePath);
            if (test.isLoaded) EditorSceneManager.CloseScene(test, true);
            var previous = SceneManager.GetSceneByPath(previousScene);
            if (previous.isLoaded) SceneManager.SetActiveScene(previous);
            previousScene = null;
            SessionState.SetString("ZandeaProps.previousScene", "");
        }
    }
}
