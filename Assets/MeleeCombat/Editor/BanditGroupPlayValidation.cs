using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
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
            if (members.Length < group.groupSize)
                throw new Exception($"Only {members.Length}/{group.groupSize} group members spawned");
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
