using System.IO;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class BanditMocapShieldValidation
{
    const string Key = "BanditMocapShieldValidation.Running";
    static StarvingBandit bandit;
    static MocapStaffPreview grip;
    static float started;
    static int phase;
    static BanditMocapShieldValidation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/MeleeCombat/BanditDuel.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (!bandit)
        {
            if (!NavMesh.SamplePosition(Vector3.zero, out var hit, 20, NavMesh.AllAreas))
                throw new System.Exception("No duel NavMesh");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_ShieldAndSaber.prefab");
            var instance = Object.Instantiate(prefab, hit.position, Quaternion.identity);
            bandit = instance.GetComponent<StarvingBandit>();
            grip = instance.GetComponentInChildren<MocapStaffPreview>();
            foreach (var other in Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
                if (other != bandit) other.gameObject.SetActive(false);
            var threat = new GameObject("Shield incoming attacker").AddComponent<StarvingBandit>();
            threat.enabled = false;
            threat.state = StarvingBandit.State.Anticipate;
            threat.transform.position = hit.position + Vector3.forward * 1.8f;
            bandit.target = threat.transform;
            bandit.blockChance = 1;
            started = Time.realtimeSinceStartup;
            return;
        }
        if (phase == 0 && bandit.state == StarvingBandit.State.Block)
        {
            phase = 1;
            started = Time.realtimeSinceStartup;
        }
        else if (phase == 0 && Time.realtimeSinceStartup - started > 2f)
            throw new System.Exception("Shield bandit did not enter block");
        else if (phase == 1 && Time.realtimeSinceStartup - started > .25f)
        {
            Debug.Log($"SHIELD_CHECK state={bandit.state} weight={grip.runtimeWeaponOffsetWeight:F2} leftGrip={grip.LeftGripError:F3} reach={grip.ReachCorrection:F3} agentStopped={bandit.agent.isStopped}");
            if (bandit.state != StarvingBandit.State.Block || grip.runtimeWeaponOffsetWeight < .8f ||
                grip.LeftGripError > .05f || grip.ReachCorrection > .2f)
                throw new System.Exception("Shield guard pose or grip failed");
            var camera = Camera.main;
            if (camera)
            {
                camera.transform.position = bandit.transform.position + bandit.transform.forward * 3f + Vector3.up * 1.9f;
                camera.transform.LookAt(bandit.transform.position + Vector3.up * 1.1f);
                var texture = new RenderTexture(1280, 900, 24);
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                var shot = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                shot.Apply();
                File.WriteAllBytes(Path.GetFullPath("shield-block.png"), shot.EncodeToPNG());
                RenderTexture.active = null;
                camera.targetTexture = null;
                Object.Destroy(shot);
                Object.Destroy(texture);
            }
            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
