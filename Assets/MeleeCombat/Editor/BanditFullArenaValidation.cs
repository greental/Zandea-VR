using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class BanditFullArenaValidation
{
    const string Key = "BanditFullArenaValidation.Running";
    static float started;
    static float initialDistance;
    static float closestDistance;
    static bool sawMovement, sawStrike, sawDamage, sawDodge, sawParry;
    static float leftHealth, rightHealth;

    static BanditFullArenaValidation() { EditorApplication.update += Tick; }

    public static void Run()
    {
        EditorSceneManager.OpenScene(BanditMocapArenaBuilder.Output);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        var telemetry = Object.FindFirstObjectByType<BanditArenaTelemetry>();
        if (!telemetry || !telemetry.left || !telemetry.right) return;
        var left = telemetry.left;
        var right = telemetry.right;
        if (started == 0)
        {
            started = Time.realtimeSinceStartup;
            initialDistance = Vector3.Distance(left.transform.position, right.transform.position);
            closestDistance = initialDistance;
            leftHealth = left.health.Current;
            rightHealth = right.health.Current;
        }
        float distance = Vector3.Distance(left.transform.position, right.transform.position);
        closestDistance = Mathf.Min(closestDistance, distance);
        sawMovement |= closestDistance < initialDistance - .8f;
        sawStrike |= left.state == StarvingBandit.State.Strike || right.state == StarvingBandit.State.Strike;
        sawDamage |= left.health.Current < leftHealth || right.health.Current < rightHealth;
        sawDodge |= telemetry.LeftDodges + telemetry.RightDodges > 0;
        sawParry |= telemetry.LeftParries + telemetry.RightParries > 0;
        if (Time.realtimeSinceStartup - started < 38f) return;
        var la = left.GetComponent<NavMeshAgent>();
        var ra = right.GetComponent<NavMeshAgent>();
        bool navValid = la && ra && (la.isOnNavMesh || left.health.IsDead) && (ra.isOnNavMesh || right.health.IsDead);
        bool pass = navValid && sawMovement && sawStrike && sawDamage && sawDodge && sawParry &&
            telemetry.LeftDirections >= 4 && telemetry.RightDirections >= 4;
        Debug.Log($"FULL_ARENA_{(pass ? "PASS" : "FAIL")} nav={la?.isOnNavMesh}/{ra?.isOnNavMesh} moved={sawMovement} closest={closestDistance:F2} strike={sawStrike} damage={sawDamage} dodge={sawDodge} parry={sawParry} angles={telemetry.LeftDirections}/{telemetry.RightDirections} health={left.health.Current:F0}/{right.health.Current:F0}");
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(pass ? 0 : 1);
    }
}
