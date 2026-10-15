using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class ImportedCombatArenaValidation
{
    const string Key = "ImportedCombatArenaValidation.Running";
    static float started, startDistance, closestDistance, startingLeft, startingRight, maxGripError;
    static bool moved, struck, damaged, dodged, parried, playedImportedState;
    static ImportedCombatArenaValidation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene(ImportedCombatArenaBuilder.Output);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        var hud = Object.FindFirstObjectByType<BanditArenaTelemetry>();
        if (!hud || !hud.left || !hud.right) return;
        var left = hud.left;
        var right = hud.right;
        if (started == 0)
        {
            started = Time.realtimeSinceStartup;
            startDistance = Vector3.Distance(left.transform.position, right.transform.position);
            closestDistance = startDistance;
            startingLeft = left.health.Current;
            startingRight = right.health.Current;
        }
        closestDistance = Mathf.Min(closestDistance, Vector3.Distance(left.transform.position, right.transform.position));
        moved |= closestDistance < startDistance - .8f;
        struck |= left.state == StarvingBandit.State.Strike || right.state == StarvingBandit.State.Strike;
        damaged |= left.health.Current < startingLeft || right.health.Current < startingRight;
        dodged |= hud.LeftDodges + hud.RightDodges > 0;
        parried |= hud.LeftParries + hud.RightParries > 0;
        foreach (var fighter in new[] { left, right })
        {
            var driver = fighter.GetComponentInChildren<BanditMocapNpcDriver>();
            var animator = driver.GetComponent<Animator>();
            if (driver.useImportedCombatAttacks && animator.GetCurrentAnimatorStateInfo(0).IsName("AttackDirection" + fighter.CurrentAttack))
                playedImportedState = true;
            var grip = driver.grip;
            maxGripError = Mathf.Max(maxGripError, grip.primaryHandIsLeft ? grip.LeftGripError : grip.RightGripError);
        }
        if (Time.realtimeSinceStartup - started < 65f) return;
        var leftNav = left.GetComponent<NavMeshAgent>();
        var rightNav = right.GetComponent<NavMeshAgent>();
        bool nav = leftNav && rightNav && (leftNav.isOnNavMesh || left.health.IsDead) && (rightNav.isOnNavMesh || right.health.IsDead);
        bool pass = nav && moved && struck && damaged && dodged && parried && playedImportedState &&
            hud.LeftDirections >= 3 && hud.RightDirections >= 3 && maxGripError < .25f;
        Debug.Log($"IMPORTED_ARENA_{(pass ? "PASS" : "FAIL")} nav={nav} move={moved} strike={struck} damage={damaged} dodge={dodged} parry={parried} imported={playedImportedState} angles={hud.LeftDirections}/{hud.RightDirections} maxGrip={maxGripError:F3} health={left.health.Current:F0}/{right.health.Current:F0}");
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(pass ? 0 : 1);
    }
}
