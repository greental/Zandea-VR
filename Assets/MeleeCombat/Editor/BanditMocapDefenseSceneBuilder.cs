using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BanditMocapDefenseSceneBuilder
{
    const string Output = "Assets/MeleeCombat/MocapTest/GeneratedVariants/ShieldBlockDuel.unity";
    const string ParryOutput = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffParryDuel.unity";
    [MenuItem("Zandea/Melee Combat/Build mocap shield block duel")]
    public static void Build() => BuildDuel(false);
    [MenuItem("Zandea/Melee Combat/Build mocap Bo Staff parry duel")]
    public static void BuildParry() => BuildDuel(true);
    [MenuItem("Zandea/Melee Combat/Build all weapon parry duels")]
    public static void BuildAllParries()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MeleeCombat/MocapTest/GeneratedVariants" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("StarvingBandit_") || name == "StarvingBandit_ShieldAndSaber") continue;
            BuildDuel(true, name.Substring("StarvingBandit_".Length));
        }
    }
    static void BuildDuel(bool parry, string defenderWeapon = "BoStaff")
    {
        var scene = EditorSceneManager.OpenScene("Assets/MeleeCombat/BanditDuel.unity");
        foreach (var old in Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        foreach (var monitor in Object.FindObjectsByType<BanditDuelMonitor>(FindObjectsSortMode.None))
            Object.DestroyImmediate(monitor.gameObject);
        GameObject Spawn(string weapon, Vector3 position, Quaternion rotation)
        {
            var path = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_" + weapon + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) throw new System.InvalidOperationException("Missing " + path);
            var fighter = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            fighter.transform.SetPositionAndRotation(position, rotation);
            return fighter;
        }
        var shield = Spawn(parry ? defenderWeapon : "ShieldAndSaber", new Vector3(0, 0, -1.35f), Quaternion.identity);
        var staff = Spawn("BoStaff", new Vector3(0, 0, 1.35f), Quaternion.Euler(0, 180, 0));
        shield.GetComponent<StarvingBandit>().target = staff.transform;
        staff.GetComponent<StarvingBandit>().target = shield.transform;
        if (parry)
        {
            shield.GetComponent<StarvingBandit>().parryChance = 1f;
            shield.GetComponent<StarvingBandit>().dodgeIncomingNpcAttacks = false;
            staff.GetComponent<StarvingBandit>().parryIncomingNpcAttacks = false;
        }
        else shield.GetComponent<StarvingBandit>().blockChance = 1f;
        staff.GetComponent<StarvingBandit>().dodgeIncomingNpcAttacks = false;
        var camera = Object.FindFirstObjectByType<Camera>();
        camera.transform.position = new Vector3(3.5f, 2.25f, -3.8f);
        camera.transform.LookAt(new Vector3(0, 1, 0));
        var destination = parry && defenderWeapon != "BoStaff" ?
            "Assets/MeleeCombat/MocapTest/GeneratedVariants/" + defenderWeapon + "ParryDuel.unity" :
            parry ? ParryOutput : Output;
        EditorSceneManager.SaveScene(scene, destination);
        AssetDatabase.SaveAssets();
        Debug.Log((parry ? "MOCAP_PARRY_SCENE " : "MOCAP_SHIELD_SCENE ") + destination);
    }
}
