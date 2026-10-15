using System.Collections.Generic;
using UnityEngine;

/// <summary>Rolls the body artwork without tilting the physics hull or VR cockpit.</summary>
[RequireComponent(typeof(HoverBikeController))]
public sealed class HoverBikeCosmeticLean : MonoBehaviour
{
    [Range(0f, 25f)] public float maximumLean = 10f;
    [Min(.01f)] public float response = 5f;

    HoverBikeController bike;
    readonly List<Transform> visuals = new List<Transform>();
    readonly List<Vector3> restPositions = new List<Vector3>();
    readonly List<Quaternion> restRotations = new List<Quaternion>();
    float lean;

    void Awake()
    {
        bike = GetComponent<HoverBikeController>();
        // Independent body meshes use cached rest poses. The animated wheel is
        // composed separately, and its grips follow through the controller's sync.
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            var visual = renderer.transform;
            if (visual == transform || visual == bike.drivingSticks ||
                (bike.drivingSticks && visual.IsChildOf(bike.drivingSticks)) ||
                visual.GetComponentInParent<HoverBikeStick>() ||
                visual.GetComponentInParent<XRFlipSwitch>() ||
                visual.GetComponentInParent<SitInCockpit>() ||
                visual.GetComponentsInChildren<Collider>(true).Length != 0 ||
                visual.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                visual.childCount != 0)
                continue;

            visuals.Add(visual);
            restPositions.Add(visual.localPosition);
            restRotations.Add(visual.localRotation);
        }
    }

    // Called once by the controller after rebuilding the unleaned wheel pose.
    // Keeping this before grip synchronization avoids competing Update/LateUpdate
    // writers and prevents the previous frame's roll from accumulating on the wheel.
    internal void ApplyVisuals()
    {
        // Actual world yaw follows the turn even while it settles after release.
        // Positive yaw turns right, requiring negative roll around local forward.
        float yaw = bike.body ? Vector3.Dot(bike.body.angularVelocity, Vector3.up) * Mathf.Rad2Deg : 0f;
        float target = -Mathf.Clamp(yaw / Mathf.Max(1f, Mathf.Abs(bike.steeringDegreesPerSecond)), -1f, 1f) * maximumLean;
        lean = Mathf.Lerp(lean, target, 1f - Mathf.Exp(-response * Time.deltaTime));
        var roll = Quaternion.AngleAxis(lean, Vector3.forward);
        for (int i = 0; i < visuals.Count; i++)
        {
            var visual = visuals[i];
            if (!visual) continue;
            // Roll about the bike origin, independent of imported mesh axes/scale.
            Vector3 restPoint = transform.InverseTransformPoint(visual.parent.TransformPoint(restPositions[i]));
            Quaternion restRotation = Quaternion.Inverse(transform.rotation) * visual.parent.rotation * restRotations[i];
            visual.SetPositionAndRotation(transform.TransformPoint(roll * restPoint), transform.rotation * roll * restRotation);
        }

        if (bike.drivingSticks)
        {
            var wheel = bike.drivingSticks;
            Vector3 point = transform.InverseTransformPoint(wheel.position);
            Quaternion rotation = Quaternion.Inverse(transform.rotation) * wheel.rotation;
            wheel.SetPositionAndRotation(transform.TransformPoint(roll * point), transform.rotation * roll * rotation);
        }
    }

    void OnDisable()
    {
        lean = 0f;
        for (int i = 0; i < visuals.Count; i++)
        {
            if (!visuals[i]) continue;
            visuals[i].localPosition = restPositions[i];
            visuals[i].localRotation = restRotations[i];
        }
        if (bike)
        {
            bike.ApplyDrivingSticksVisual();
            if (bike.leftStick) bike.leftStick.SyncPose();
            if (bike.rightStick) bike.rightStick.SyncPose();
        }
    }
}
