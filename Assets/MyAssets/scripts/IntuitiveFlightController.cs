using UnityEngine;
using UnityEngine.XR.Content.Interaction;

/// <summary>
/// Flight model for intuitive HUD joystick: ship forward slerps toward the target direction
/// (from HOTASIntuitiveJoystick). Pitch + yaw only, no roll. Thrust along ship forward.
/// Use with HOTASIntuitiveJoystick; throttle from EngineLever or assign manually.
/// </summary>
public class IntuitiveFlightController : MonoBehaviour
{
    [Header("References")]
    public Rigidbody rb;
    public HOTASIntuitiveJoystick intuitiveJoystick;
    [Tooltip("Optional. If null, throttle stays 0 or use ThrottleValue for testing.")]
    public EngineLever throttleLever;
    [Tooltip("Manual throttle 0..1 when throttleLever is null (for testing).")]
    [Range(0f, 1f)]
    public float throttleValue;

    [Header("Slerp toward target direction")]
    [Tooltip("How fast the ship forward aligns to the HUD direction. Higher = snappier.")]
    public float slerpSpeed = 3f;
    [Tooltip("World up for orientation (no roll).")]
    public Vector3 worldUp = Vector3.up;

    [Header("Thrust")]
    public float maxForwardThrust = 40f;
    [Range(0f, 1f)]
    public float reverseThrustFactor = 0.35f;

    [Header("Damping & limits")]
    public float linearDamp = 2.5f;
    public float maxSpeed = 120f;

    float GetThrottle()
    {
        if (throttleLever != null)
        {
            float lever = throttleLever.normalizedValue;
            float t = Mathf.InverseLerp(-1f, 1f, lever);
            if (t > 0.4f && t < 0.6f) return 0f;
            if (t <= 0.4f) return -Mathf.InverseLerp(0.4f, 0f, t) * reverseThrustFactor;
            return Mathf.InverseLerp(0.6f, 1f, t);
        }
        return throttleValue;
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        // Thrust along current ship forward
        float throttle = GetThrottle();
        float thrust = throttle >= 0f ? throttle : throttle * reverseThrustFactor;
        rb.AddForce(rb.transform.forward * thrust * maxForwardThrust, ForceMode.Acceleration);

        // Slerp ship forward toward target direction (pitch + yaw, no roll)
        if (intuitiveJoystick != null)
        {
            Vector3 targetDir = intuitiveJoystick.GetTargetDirection();
            if (targetDir.sqrMagnitude < 0.01f) targetDir = rb.transform.forward;
            targetDir.Normalize();

            // Desired rotation: forward = targetDir, up = worldUp (pitch + yaw, no roll)
            Quaternion desiredRot = Quaternion.LookRotation(targetDir, worldUp);

            Quaternion newRot = Quaternion.Slerp(rb.rotation, desiredRot, slerpSpeed * Time.fixedDeltaTime);
            rb.MoveRotation(newRot);
        }

        // Linear damping
        if (rb.linearVelocity.sqrMagnitude > 0.01f)
            rb.AddForce(-rb.linearVelocity * linearDamp, ForceMode.Acceleration);

        // Speed limit
        if (rb.linearVelocity.magnitude > maxSpeed)
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
    }
}
