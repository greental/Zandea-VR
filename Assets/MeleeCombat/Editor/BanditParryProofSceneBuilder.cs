using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class BanditParryProofSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/MeleeCombat/BanditParryProof.unity";
        const string VariantRoot = "Assets/MeleeCombat/MocapTest/GeneratedVariants/";

        [MenuItem("Zandea/Melee Combat/Build physical parry proof scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene("Assets/MeleeCombat/BanditDuel.unity");
            foreach (var old in Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            foreach (var old in Object.FindObjectsByType<BanditDuelMonitor>(FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);

            StarvingBandit Spawn(string variant, Vector3 position, Quaternion rotation)
            {
                var path = VariantRoot + "StarvingBandit_" + variant + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab) throw new System.InvalidOperationException("Missing " + path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetPositionAndRotation(position, rotation);
                return instance.GetComponent<StarvingBandit>();
            }

            var defender = Spawn("ShortSaber", new Vector3(0, 0, -1.15f), Quaternion.identity);
            var attacker = Spawn("BoStaff", new Vector3(0, 0, 1.15f), Quaternion.Euler(0, 180, 0));
            defender.name = "Defender - Short Saber";
            attacker.name = "Attacker - Bo Staff";
            defender.target = attacker.transform;
            attacker.target = defender.transform;
            defender.defensiveOnly = true;
            defender.parryIncomingNpcAttacks = true;
            defender.parryChance = 1f;
            var defenderHealth = defender.GetComponent<ScavengerNPC.ScavengerHealth>();
            defenderHealth.maxHealth = 10000f;
            defenderHealth.knockoutTrauma = 10000f;
            defender.dodgeIncomingNpcAttacks = false;
            defender.blockIncomingNpcAttacks = false;
            attacker.parryIncomingNpcAttacks = false;
            attacker.dodgeIncomingNpcAttacks = false;
            attacker.blockIncomingNpcAttacks = false;
            attacker.cycleAttackDirections = true;
            var proof = new GameObject("Parry proof results").AddComponent<BanditParryProof>();
            proof.defender = defender;
            proof.attacker = attacker;
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera)
            {
                camera.transform.position = new Vector3(3.4f, 2.3f, -3.7f);
                camera.transform.LookAt(new Vector3(0, 1.1f, 0));
            }
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("PARRY_PROOF_SCENE " + ScenePath);
        }
    }
}
