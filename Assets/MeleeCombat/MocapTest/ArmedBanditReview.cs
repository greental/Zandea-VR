using System;
using System.Reflection;
using KenshiVR.MeleeCombat;
using UnityEngine;

// Drives the real bandit animator, grip solver, and physical weapon in an isolated review scene.
public sealed class ArmedBanditReview : MonoBehaviour
{
    public StarvingBandit[] bandits;
    public Camera reviewCamera;
    public Transform[] labels;
    public float guardSeconds = 2f;
    public float attackGapSeconds = .45f;
    static readonly MethodInfo StartAttack = typeof(StarvingBandit).GetMethod("StartAttack", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo TickAttack = typeof(StarvingBandit).GetMethod("TickAttack", BindingFlags.Instance | BindingFlags.NonPublic);
    float stageStarted;
    float stageLength;
    int direction;
    bool attacking;
    bool paused;
    bool autoCycle = true;
    bool walking = true;
    int focus = -1;
    float priorTimeScale;

    void Start()
    {
        priorTimeScale = Time.timeScale;
        foreach (var bandit in bandits)
        {
            if (!bandit) continue;
            bandit.enabled = false;
            if (bandit.agent) bandit.agent.enabled = false;
            var driver = bandit.GetComponentInChildren<BanditMocapNpcDriver>();
            if (driver) driver.reviewLocomotionOverride = true;
            bandit.state = StarvingBandit.State.Approach;
        }
        stageStarted = Time.time;
        FrameCamera();
    }

    void OnDisable() { Time.timeScale = priorTimeScale; }

    void Update()
    {
        if (paused) return;
        float elapsed = Time.time - stageStarted;
        foreach (var bandit in bandits)
        {
            if (!bandit) continue;
            var driver = bandit.GetComponentInChildren<BanditMocapNpcDriver>();
            if (driver) driver.reviewLocomotion = !attacking && walking ? Vector2.up : Vector2.zero;
            if (attacking && bandit.state != StarvingBandit.State.Retreat)
                TickAttack.Invoke(bandit, null);
            else if (!attacking) bandit.state = StarvingBandit.State.Approach;
        }
        if (!attacking && elapsed >= guardSeconds) BeginAttack();
        else if (attacking && elapsed >= stageLength + attackGapSeconds)
        {
            attacking = false;
            if (autoCycle) direction = (direction + 1) & 7;
            stageStarted = Time.time;
        }
    }

    void LateUpdate()
    {
        if (labels == null || !reviewCamera) return;
        for (int i = 0; i < labels.Length && i < bandits.Length; i++)
        {
            var label = labels[i];
            if (!label || !bandits[i]) continue;
            label.rotation = Quaternion.LookRotation(reviewCamera.transform.position - label.position);
            var text = label.GetComponent<TextMesh>();
            if (!text) continue;
            var clip = CurrentClip(bandits[i]);
            string shortClip = clip.Replace("standing melee attack ", "").Replace("BoStaff_", "");
            var driver = bandits[i].GetComponentInChildren<BanditMocapNpcDriver>();
            if (attacking && driver && driver.usePresentedTwoHandStrikes)
                shortClip = "forward side strike";
            text.text = bandits[i].name.Replace("StarvingBandit_", "") + "\n" +
                (attacking ? "Attack " + (direction + 1) + ": " + shortClip :
                 walking ? "Walking guard" : "Standing guard");
        }
    }

    static string CurrentClip(StarvingBandit bandit)
    {
        var animator = bandit.GetComponentInChildren<Animator>();
        if (!animator) return "unknown";
        var clips = animator.IsInTransition(0) ? animator.GetNextAnimatorClipInfo(0) :
            animator.GetCurrentAnimatorClipInfo(0);
        return clips.Length > 0 && clips[0].clip ? clips[0].clip.name : "unknown";
    }

    void BeginAttack()
    {
        attacking = true;
        stageStarted = Time.time;
        stageLength = 0f;
        foreach (var bandit in bandits)
        {
            if (!bandit) continue;
            StartAttack.Invoke(bandit, new object[] { direction });
            var a = bandit.attacks[direction];
            stageLength = Mathf.Max(stageLength,
                (a.windupSeconds + a.strikeSeconds + a.followSeconds + a.recoverySeconds) /
                Mathf.Max(.1f, bandit.AttackAnimationSpeed));
        }
    }

    void FrameCamera()
    {
        if (!reviewCamera || bandits == null || bandits.Length == 0) return;
        if (focus < 0)
        {
            var center = bandits[bandits.Length / 2].transform.position;
            reviewCamera.transform.position = center + new Vector3(0f, 17f, -20f);
            reviewCamera.transform.LookAt(center + Vector3.up);
            reviewCamera.fieldOfView = 55f;
        }
        else
        {
            var target = bandits[Mathf.Clamp(focus, 0, bandits.Length - 1)].transform.position + Vector3.up * 1.25f;
            reviewCamera.transform.position = target + new Vector3(1.8f, 1.2f, -3.6f);
            reviewCamera.transform.LookAt(target);
            reviewCamera.fieldOfView = 46f;
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 310, Screen.height - 24), GUI.skin.box);
        GUILayout.Label("ARMED BANDIT REVIEW");
        GUILayout.Label((attacking ? "Attack " + (direction + 1) + " / 8" : walking ? "Walking guard" : "Standing guard") +
            (paused ? "  PAUSED" : ""));
        if (focus >= 0 && focus < bandits.Length && bandits[focus])
            GUILayout.Label("Playing: " + CurrentClip(bandits[focus]));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(paused ? "Resume" : "Pause")) { paused = !paused; Time.timeScale = paused ? 0f : priorTimeScale; }
        if (GUILayout.Button(autoCycle ? "Auto: on" : "Auto: off")) autoCycle = !autoCycle;
        if (GUILayout.Button(walking ? "Walk: on" : "Walk: off")) walking = !walking;
        GUILayout.EndHorizontal();
        GUILayout.Label("Choose attack direction:");
        GUILayout.BeginHorizontal();
        for (int i = 0; i < 8; i++)
            if (GUILayout.Button((i + 1).ToString())) { direction = i; attacking = false; stageStarted = Time.time - guardSeconds; }
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Overview")) { focus = -1; FrameCamera(); }
        GUILayout.Label("Focus a weapon:");
        for (int i = 0; i < bandits.Length; i++)
            if (bandits[i] && GUILayout.Button(bandits[i].name.Replace("StarvingBandit_", "")))
            { focus = i; FrameCamera(); }
        GUILayout.EndArea();
    }
}
