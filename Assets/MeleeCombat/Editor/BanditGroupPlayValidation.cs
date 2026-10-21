using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using KenshiVR.CharacterCreator;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BanditGroupPlayValidation
{
    const string Request = "Temp/BanditGroupPlayValidation.request";
    const string Result = "Temp/BanditGroupPlayValidation.result";
    const string Running = "BanditGroupPlayValidation.Running";
    const string StartedPlay = "BanditGroupPlayValidation.StartedPlay";
    static BanditGroup group;
    static GameObject target;
    static int startFrame;
    static int phase;
    static float attackDeadline;

    static BanditGroupPlayValidation() => EditorApplication.update += Tick;

    [MenuItem("Tools/Melee Combat/Validate bandit group in Play mode")]
    public static void Run() { File.WriteAllText(Request, "run"); Tick(); }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (!SessionState.GetBool(Running, false))
        {
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            SessionState.SetBool(Running, true);
            SessionState.SetBool(StartedPlay, !EditorApplication.isPlaying);
            phase = 0;
            if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        }
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (phase == 0)
            {
                group = UnityEngine.Object.FindObjectsByType<BanditGroup>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).FirstOrDefault();
                if (!group) throw new Exception("No bandit group in the loaded scene");
                group.gameObject.SetActive(true);
                startFrame = Time.frameCount;
                phase = 1;
                return;
            }
            if (Time.frameCount < startFrame + (phase == 1 ? 12 : 30)) return;
            var members = UnityEngine.Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None)
                .Where(bandit => bandit.combatGroup == group).OrderBy(bandit => bandit.name).ToArray();
            bool authored = group.authoredMembers != null && group.authoredMembers.Length > 0;
            int expectedCount = authored ? group.authoredMembers.Count(member => member) : group.groupSize;
            if (members.Length != expectedCount)
                throw new Exception($"Expected {expectedCount} members, found {members.Length}");
            foreach (var member in members)
                if (!member.staff || member.staff.State != PhysicalMeleeWeapon.HoldState.HeldByNpc)
                {
                    var generated = member.GetComponent<NpcGeneratedWeapon>();
                    throw new Exception(member.name + " spawned without an equipped weapon: " +
                        $"staff={(member.staff ? member.staff.name : "null")}, " +
                        $"generated={(generated ? generated.name : "none")}, " +
                        $"instance={(generated && generated.Instance ? generated.Instance.name : "null")}, " +
                        $"start={(generated && generated.startEquipped)}, " +
                        $"prefab={(generated && generated.weaponPrefab ? generated.weaponPrefab.name : "null")}, " +
                        $"grip={(generated && generated.gripTarget ? generated.gripTarget.name : "null")}, " +
                        $"state={(member.staff ? member.staff.State.ToString() : "none")}");
                }
            if (authored && group.authoredWeaponChoices != null)
                for (int i = 0; i < group.authoredMembers.Length && i < group.authoredWeaponChoices.Length; i++)
                {
                    var choice = group.authoredWeaponChoices[i];
                    var authoredMember = group.authoredMembers[i];
                    if (choice && authoredMember && (!authoredMember.staff || authoredMember.staff.name != choice.name))
                        throw new Exception(authoredMember.name + " did not equip its selected weapon " + choice.name);
                }
            if (!authored && members.Select(member => member.staff.name).Distinct().Count() < 2)
                throw new Exception("The editable group still has one weapon type: " +
                    string.Join(", ", members.Select(member => member.staff.name)));
            if (!authored && (members.Select(member => member.GetComponent<CharacterAppearanceController>()?.Data.weight ?? -1f)
                .Distinct().Count() < 2 ||
                members.Select(member => member.GetComponent<CharacterAppearanceController>()?.Data.noseWidth ?? -1f)
                .Distinct().Count() < 2))
                throw new Exception("The editable group did not vary body weight and face shape");
            if (!authored && members.Any(member => { var cosmetics = member.GetComponent<CharacterCosmeticsController>();
                    return !cosmetics || !cosmetics.headwear; }))
                throw new Exception("At least one editable member has no chosen headwear");
            if (!authored && members.Any(member => !member.GetComponentsInChildren<AppearanceSpawnedItem>(true)
                .Any(item => item.slot == AppearanceSlot.Headwear && item.gameObject.activeInHierarchy &&
                    item.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled && !renderer.forceRenderingOff))))
                throw new Exception("At least one editable member has no rendered headgear");
            var leader = members.FirstOrDefault(member => member.name == "Bandit Leader");
            if (!leader) throw new Exception("No bandit leader spawned");
            foreach (var member in members)
                if (member != leader && Vector3.Distance(member.groupDestination, leader.transform.position) > group.followDistance * 4f)
                    throw new Exception(member.name + " has no leader-relative follow destination");
            if (phase == 1)
            {
                target = new GameObject("Bandit group validation target");
                target.transform.position = leader.transform.position + leader.transform.forward * 4f + Vector3.up * 1.5f;
                group.player = target.transform;
                startFrame = Time.frameCount;
                phase = 2;
                return;
            }
            if (members.Any(member => !member.groupEngaged || member.target != target.transform))
                throw new Exception("Leader and followers did not engage the nearby player together: " +
                    string.Join(", ", members.Select(member => member.name + " engaged=" +
                        member.groupEngaged + " target=" + (member.target ? member.target.name : "none"))));
            if (phase == 2)
            {
                phase = 3;
                attackDeadline = Time.time + 10f;
                return;
            }
            if (leader.state != StarvingBandit.State.Anticipate && leader.state != StarvingBandit.State.Strike &&
                leader.state != StarvingBandit.State.FollowThrough)
            {
                if (Time.time >= attackDeadline)
                    throw new Exception($"Leader did not start an attack: state={leader.state}, " +
                        $"distance={Vector3.Distance(leader.transform.position, target.transform.position):F2}");
                return;
            }
            File.WriteAllText(Result, $"PASS bandit group Play validation: {members.Length} armed members, follow destinations, shared engagement, leader attack");
            Finish();
        }
        catch (Exception error)
        {
            File.WriteAllText(Result, "FAIL bandit group Play validation\n" + error);
            Debug.LogException(error);
            Finish();
        }
    }

    static void Finish()
    {
        if (target) UnityEngine.Object.Destroy(target);
        target = null;
        group = null;
        SessionState.SetBool(Running, false);
        bool startedPlay = SessionState.GetBool(StartedPlay, false);
        SessionState.SetBool(StartedPlay, false);
        if (startedPlay) EditorApplication.isPlaying = false;
    }
}
