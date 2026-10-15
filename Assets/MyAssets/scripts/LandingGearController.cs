using UnityEngine;

/// <summary>
/// Controls deploy/fold animation for multiple landing gear pivots.
/// Assign each gear pivot and its local deployed/folded rotation in inspector.
/// </summary>
public class LandingGearController : MonoBehaviour
{
    [System.Serializable]
    public class GearPart
    {
        [Tooltip("Friendly name for inspector readability.")]
        public string name = "Gear";
        [Tooltip("Pivot transform for this landing gear piece.")]
        public Transform pivot;
        [Tooltip("Local euler rotation when gear is deployed (down).")]
        public Vector3 deployedLocalEuler;
        [Tooltip("Local euler rotation when gear is folded into body.")]
        public Vector3 foldedLocalEuler = new Vector3(90f, 0f, 0f);
    }

    [Header("Gear setup (Razara has 3 pieces)")]
    public GearPart[] gearParts = new GearPart[3];

    [Header("State")]
    [Tooltip("True = deployed (landing mode). False = folded into hull (flight mode).")]
    public bool gearDeployed = true;

    [Header("Animation")]
    [Tooltip("Degrees per second for gear rotation.")]
    public float rotateSpeedDegPerSec = 120f;
    [Tooltip("If true, all gear parts animate together; if false, each part can finish at different times.")]
    public bool synchronizeCompletion = true;

    [Header("Debug / Testing")]
    [Tooltip("Optional keyboard test key for toggling gear while in play mode.")]
    public KeyCode testToggleKey = KeyCode.G;
    [Tooltip("Enable key test toggle in play mode.")]
    public bool enableTestToggleKey = false;

    public bool IsMoving { get; private set; }
    public float DeployFraction { get; private set; } // 0 = fully folded, 1 = fully deployed

    void Start()
    {
        // Snap to current state on startup so there is no random intermediate pose.
        ApplyStateImmediate(gearDeployed);
    }

    void Update()
    {
        if (enableTestToggleKey && Application.isPlaying && Input.GetKeyDown(testToggleKey))
            ToggleGear();

        AnimateTowardsTarget(Time.deltaTime);
    }

    /// <summary>Toggle between deployed and folded.</summary>
    public void ToggleGear()
    {
        SetGearDeployed(!gearDeployed);
    }

    /// <summary>Deploy or fold landing gear; animation happens in Update.</summary>
    public void SetGearDeployed(bool deployed)
    {
        gearDeployed = deployed;
    }

    /// <summary>Force immediate state without animation (useful in editor setup).</summary>
    public void ApplyStateImmediate(bool deployed)
    {
        gearDeployed = deployed;
        Quaternion allTarget = Quaternion.identity;

        for (int i = 0; i < gearParts.Length; i++)
        {
            var part = gearParts[i];
            if (part == null || part.pivot == null) continue;
            Vector3 targetEuler = deployed ? part.deployedLocalEuler : part.foldedLocalEuler;
            Quaternion target = Quaternion.Euler(targetEuler);
            part.pivot.localRotation = target;
            allTarget = target;
        }

        IsMoving = false;
        DeployFraction = deployed ? 1f : 0f;
    }

    void AnimateTowardsTarget(float dt)
    {
        if (gearParts == null || gearParts.Length == 0)
        {
            IsMoving = false;
            return;
        }

        bool anyMoving = false;
        float maxRemaining = 0f;

        for (int i = 0; i < gearParts.Length; i++)
        {
            var part = gearParts[i];
            if (part == null || part.pivot == null) continue;

            Vector3 targetEuler = gearDeployed ? part.deployedLocalEuler : part.foldedLocalEuler;
            Quaternion target = Quaternion.Euler(targetEuler);
            Quaternion current = part.pivot.localRotation;

            float angle = Quaternion.Angle(current, target);
            maxRemaining = Mathf.Max(maxRemaining, angle);

            if (angle > 0.01f)
            {
                float maxStep = Mathf.Max(0f, rotateSpeedDegPerSec) * dt;
                part.pivot.localRotation = Quaternion.RotateTowards(current, target, maxStep);
                anyMoving = true;
            }
            else
            {
                part.pivot.localRotation = target;
            }
        }

        IsMoving = anyMoving;

        // Approximate deployment progress from worst-case remaining angle.
        // 90 deg is typical fold span; this keeps a useful normalized status for HUD.
        float t = 1f - Mathf.Clamp01(maxRemaining / 90f);
        DeployFraction = gearDeployed ? t : (1f - t);

        if (synchronizeCompletion && !anyMoving)
        {
            // Ensure exact final value after all parts finish.
            DeployFraction = gearDeployed ? 1f : 0f;
        }
    }
}
