using System;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class BanditMocapParryVariantsValidation
{
    const string Key = "BanditMocapParryVariantsValidation.Running";
    static string[] paths;
    static int index;
    static float started;
    static float guardStarted;
    static StarvingBandit defender;
    static GameObject threat;
    static Vector3 spawn;
    static BanditMocapParryVariantsValidation() { EditorApplication.update += Tick; }

    public static void Run()
    {
        paths = FindPaths();
        index = 0;
        EditorSceneManager.OpenScene("Assets/MeleeCombat/BanditDuel.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static string[] FindPaths() => AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MeleeCombat/MocapTest/GeneratedVariants" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => System.IO.Path.GetFileName(path).StartsWith("StarvingBandit_") &&
                !path.EndsWith("StarvingBandit_ShieldAndSaber.prefab"))
            .OrderBy(path => path).ToArray();
    static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (paths == null) paths = FindPaths();
        if (!defender)
        {
            if (!NavMesh.SamplePosition(Vector3.zero, out var hit, 20, NavMesh.AllAreas))
                throw new Exception("No duel NavMesh");
            spawn = hit.position;
            foreach (var old in UnityEngine.Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
                old.gameObject.SetActive(false);
            SpawnNext();
            return;
        }
        if (defender.state == StarvingBandit.State.Parry && guardStarted < 0)
            guardStarted = Time.realtimeSinceStartup;
        if (guardStarted >= 0 && Time.realtimeSinceStartup - guardStarted > .18f)
        {
            var grip = defender.GetComponentInChildren<MocapStaffPreview>();
            var body = grip.staff.GetComponent<Rigidbody>();
            float axis = grip.runtimeWeaponAxisWeight;
            Debug.Log($"PARRY_VARIANT {System.IO.Path.GetFileNameWithoutExtension(paths[index])} " +
                $"left={grip.LeftGripError:F3} right={grip.RightGripError:F3} " +
                $"reach={grip.ReachCorrection:F3} axis={axis:F2} axisLength={grip.runtimeWeaponAxisLocal.magnitude:F3} " +
                $"dynamic={!body.isKinematic} collider={grip.staff.GetComponent<CapsuleCollider>().enabled} " +
                $"joint={!!grip.staff.GetComponent<ConfigurableJoint>()} drive={grip.driveVisualStaff}");
            if (axis < .9f || grip.runtimeWeaponAxisLocal.sqrMagnitude < .0001f ||
                body.isKinematic || !grip.staff.GetComponent<CapsuleCollider>().enabled ||
                !grip.staff.GetComponent<ConfigurableJoint>() || grip.driveVisualStaff ||
                grip.LeftGripError > .08f || grip.RightGripError > .08f || grip.ReachCorrection > .3f)
            { Fail("Parry variant grip/physics failed: " + paths[index]); return; }
            defender.gameObject.SetActive(false);
            threat.SetActive(false);
            UnityEngine.Object.Destroy(defender.gameObject);
            UnityEngine.Object.Destroy(threat);
            defender = null;
            index++;
            if (index == paths.Length)
            {
                Debug.Log("PARRY_VARIANTS_PASS " + index);
                SessionState.SetBool(Key, false);
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
            }
            else SpawnNext();
        }
        else if (Time.realtimeSinceStartup - started > 2f)
            Fail("Parry variant never entered guard: " + paths[index]);
    }
    static void SpawnNext()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[index]);
        defender = UnityEngine.Object.Instantiate(prefab, spawn, Quaternion.identity).GetComponent<StarvingBandit>();
        threat = new GameObject("Validation incoming attacker");
        var attacker = threat.AddComponent<StarvingBandit>();
        attacker.enabled = false;
        attacker.state = StarvingBandit.State.Strike;
        threat.transform.position = spawn + Vector3.forward * 1.8f;
        defender.target = threat.transform;
        defender.parryChance = 1f;
        started = Time.realtimeSinceStartup;
        guardStarted = -1f;
    }
    static void Fail(string message)
    {
        Debug.LogError("PARRY_VARIANTS_FAIL " + message);
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(1);
    }
}
