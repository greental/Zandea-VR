using System;
using System.IO;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BoStaffDirectionalValidation
{
    const string Key = "BoStaffDirectionalValidation.Running";
    static readonly float[] Samples = { .20f, .42f, .65f };
    static BoStaffDirectionalPreview preview;
    static int direction, sample;
    static float changedAt;
    static readonly Vector3[] impact = new Vector3[8];
    static BoStaffDirectionalValidation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        direction = sample = 0;
        EditorSceneManager.OpenScene("Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffDirectionalPreview.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (!preview)
        {
            preview = UnityEngine.Object.FindFirstObjectByType<BoStaffDirectionalPreview>();
            if (!preview) { Fail("Preview rig missing"); return; }
            preview.playing = false;
            SetSample();
            return;
        }
        if (Time.realtimeSinceStartup - changedAt < .06f) return;
        var grip = preview.grip;
        var animator = preview.GetComponent<Animator>();
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (!animator.GetCurrentAnimatorStateInfo(0).IsName("AttackDirection" + direction))
        { Fail("Wrong animator state for direction " + direction); return; }
        float left = grip.LeftGripError, right = grip.RightGripError;
        float reach = grip.ReachCorrection;
        Vector3 tip = preview.transform.root.InverseTransformPoint(grip.positiveTip.position);
        Vector3 otherTip = preview.transform.root.InverseTransformPoint(grip.negativeTip.position);
        Debug.Log($"DIRECTION_CHECK direction={direction} time={Samples[sample]:F2} " +
            $"left={left:F3} right={right:F3} reach={reach:F3} tip={tip} otherTip={otherTip} " +
            $"feet={leftFoot.position.y - preview.transform.root.position.y:F2}/{rightFoot.position.y - preview.transform.root.position.y:F2}");
        if (left > .06f || right > .06f || reach > .25f)
        { Fail("Grip failed for direction " + direction + " sample " + sample); return; }
        if (sample == 1) impact[direction] = tip;
        if (sample == 1) Capture(direction);
        sample++;
        if (sample == Samples.Length) { sample = 0; direction++; }
        if (direction == 8)
        {
            int distinct = 0;
            for (int i = 0; i < impact.Length; i++)
            {
                bool unique = true;
                for (int j = 0; j < i; j++)
                    if (Vector3.Distance(impact[i], impact[j]) < .08f) { unique = false; break; }
                if (unique) distinct++;
            }
            Debug.Log("DIRECTION_PASS directions=8 distinctImpactPositions=" + distinct);
            if (distinct < 5) { Fail("Directional paths are too similar"); return; }
            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
        else SetSample();
    }
    static void SetSample()
    {
        preview.direction = direction;
        preview.normalizedTime = Samples[sample];
        changedAt = Time.realtimeSinceStartup;
    }
    static void Capture(int index)
    {
        var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
        if (!camera) return;
        camera.transform.position = preview.transform.root.position + new Vector3(0, 1.8f, 3.5f);
        camera.transform.LookAt(preview.transform.root.position + Vector3.up * 1.1f);
        var target = new RenderTexture(900, 700, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(900, 700, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 900, 700), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.GetFullPath("direction-" + index + ".png"), image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(target);
    }
    static void Fail(string message)
    {
        Debug.LogError("DIRECTION_FAIL " + message);
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(1);
    }
}
