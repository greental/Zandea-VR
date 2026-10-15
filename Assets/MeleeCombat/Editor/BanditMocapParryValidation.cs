using System.IO;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class BanditMocapParryValidation
{
    const string Key = "BanditMocapParryValidation.Running";
    static StarvingBandit bandit;
    static MocapStaffPreview grip;
    static float started;
    static int phase;
    static Vector3 impulseDirection;
    static BanditMocapParryValidation() { EditorApplication.update += Tick; }
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
                "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff.prefab");
            var instance = Object.Instantiate(prefab, hit.position, Quaternion.identity);
            bandit = instance.GetComponent<StarvingBandit>();
            grip = instance.GetComponentInChildren<MocapStaffPreview>();
            foreach (var other in Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
                if (other != bandit) other.gameObject.SetActive(false);
            var threat = new GameObject("Parry incoming attacker").AddComponent<StarvingBandit>();
            threat.enabled = false;
            threat.state = StarvingBandit.State.Strike;
            threat.transform.position = hit.position + Vector3.forward * 1.8f;
            bandit.target = threat.transform;
            bandit.parryChance = 1;
            started = Time.realtimeSinceStartup;
            return;
        }
        if (phase == 0 && bandit.state == StarvingBandit.State.Parry)
        {
            phase = 1;
            started = Time.realtimeSinceStartup;
        }
        else if (phase == 0 && Time.realtimeSinceStartup - started > 2f)
            throw new System.Exception("Staff bandit did not enter parry");
        else if (phase == 1 && Time.realtimeSinceStartup - started > .18f)
        {
            Debug.Log($"PARRY_CHECK state={bandit.state} weight={grip.runtimeWeaponAxisWeight:F2} left={grip.LeftGripError:F3} right={grip.RightGripError:F3} reach={grip.ReachCorrection:F3}");
            if (bandit.state != StarvingBandit.State.Parry || grip.runtimeWeaponAxisWeight < .9f ||
                grip.LeftGripError > .05f || grip.RightGripError > .05f || grip.ReachCorrection > .2f)
                throw new System.Exception("Parry pose or two-hand grip failed");
            var body = grip.staff.GetComponent<Rigidbody>();
            if (body.isKinematic || !grip.staff.GetComponent<CapsuleCollider>().enabled ||
                !grip.staff.GetComponent<ConfigurableJoint>() || grip.driveVisualStaff)
                throw new System.Exception("Bo Staff physical joint is not active");
            impulseDirection = bandit.transform.right;
            body.AddForce(impulseDirection * 4f, ForceMode.Impulse);
            phase = 2;
            started = Time.realtimeSinceStartup;
        }
        else if (phase == 2 && Time.realtimeSinceStartup - started > .08f)
        {
            float displacement = Vector3.Distance(grip.staff.transform.position, grip.desiredStaff.position);
            Debug.Log($"PARRY_PHYSICS displacement={displacement:F3} left={grip.LeftGripError:F3} right={grip.RightGripError:F3}");
            if (displacement < .005f || grip.LeftGripError > .06f || grip.RightGripError > .06f)
                throw new System.Exception("Staff did not yield to impulse or hands lost grip");
            var camera = Camera.main;
            if (camera)
            {
                Capture(camera, bandit.transform.position + bandit.transform.forward * 3 + Vector3.up * 1.9f, "parry-front.png");
                Capture(camera, bandit.transform.position + bandit.transform.right * 3 + Vector3.up * 1.9f, "parry-side.png");
            }
            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
    static void Capture(Camera camera, Vector3 position, string file)
    {
        camera.transform.position = position;
        camera.transform.LookAt(bandit.transform.position + Vector3.up * 1.1f);
        var texture = new RenderTexture(1280, 900, 24);
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        var shot = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        shot.Apply();
        File.WriteAllBytes(Path.GetFullPath(file), shot.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.Destroy(shot);
        Object.Destroy(texture);
    }
}
