using UnityEngine;

/// <summary>
/// Intuitive HUD joystick: two 3D circles in front of the player. Input (WASD or stick) moves them;
/// the ship follows the vector from the near circle to the far circle (pitch + yaw, no roll).
/// Near circle is closer and moves less; far circle moves more (No Man's Sky style).
/// </summary>
public class HOTASIntuitiveJoystick : MonoBehaviour
{
    const string k_NearReticleName = "IntuitiveHUD_Near";
    const string k_FarReticleName = "IntuitiveHUD_Far";

    [Header("View reference")]
    [Tooltip("Camera or head transform. Reticles are placed in front of this.")]
    public Transform viewReference;

    [Header("Reticle objects (optional)")]
    [Tooltip("If assigned, use these; otherwise script creates two quads at runtime.")]
    public Transform nearReticle;
    public Transform farReticle;

    [Header("Distances (world units)")]
    [Tooltip("Distance of the near (closer, appears bigger) reticle.")]
    public float nearDistance = 2f;
    [Tooltip("Distance of the far (farther, appears smaller) reticle.")]
    public float farDistance = 5f;
    [Tooltip("Near reticle moves this fraction of the full deflection (e.g. 0.5 = half).")]
    [Range(0.1f, 1f)]
    public float nearSensitivity = 0.5f;

    [Header("Pitch/Yaw limits (degrees)")]
    public float maxPitchAngle = 80f;
    public float maxYawAngle = 80f;
    [Tooltip("Degrees per second per unit input (W/S/A/D = ±1).")]
    public float inputRate = 60f;

    [Header("Input")]
    [Tooltip("Use W/S for pitch, A/D for yaw. Same as keyboard flight.")]
    public bool useKeyboard = true;

    float _pitchAngleDeg;
    float _yawAngleDeg;
    bool _reticlesCreated;

    void Start()
    {
        if (viewReference == null)
            viewReference = Camera.main != null ? Camera.main.transform : transform;
    }

    void Update()
    {
        EnsureReticles();
        if (viewReference == null) return;

        if (useKeyboard)
        {
            float pitchInput = 0f;
            if (Input.GetKey(KeyCode.W)) pitchInput += 1f;
            if (Input.GetKey(KeyCode.S)) pitchInput -= 1f;
            float yawInput = 0f;
            if (Input.GetKey(KeyCode.D)) yawInput += 1f;
            if (Input.GetKey(KeyCode.A)) yawInput -= 1f;
            float dt = Time.deltaTime;
            _pitchAngleDeg = Mathf.Clamp(_pitchAngleDeg + pitchInput * inputRate * dt, -maxPitchAngle, maxPitchAngle);
            _yawAngleDeg = Mathf.Clamp(_yawAngleDeg + yawInput * inputRate * dt, -maxYawAngle, maxYawAngle);
        }

        UpdateReticlePositions();
    }

    void EnsureReticles()
    {
        if (viewReference == null) return;
        if (nearReticle != null && farReticle != null) { _reticlesCreated = true; return; }
        if (_reticlesCreated) return;

        if (nearReticle == null)
        {
            GameObject near = GameObject.CreatePrimitive(PrimitiveType.Quad);
            near.name = k_NearReticleName;
            near.transform.SetParent(viewReference, false);
            Destroy(near.GetComponent<Collider>());
            nearReticle = near.transform;
            nearReticle.localScale = new Vector3(0.3f, 0.3f, 0.3f);
            nearReticle.localRotation = Quaternion.Euler(0f, 180f, 0f); // face camera
        }
        if (farReticle == null)
        {
            GameObject far = GameObject.CreatePrimitive(PrimitiveType.Quad);
            far.name = k_FarReticleName;
            far.transform.SetParent(viewReference, false);
            Destroy(far.GetComponent<Collider>());
            farReticle = far.transform;
            farReticle.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            farReticle.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
        _reticlesCreated = true;
    }

    void UpdateReticlePositions()
    {
        if (nearReticle == null || farReticle == null || viewReference == null) return;

        // Camera local: +Z = forward, +Y = up, +X = right. Place in plane at z = distance, offset by pitch/yaw.
        float pitchRad = _pitchAngleDeg * Mathf.Deg2Rad;
        float yawRad = _yawAngleDeg * Mathf.Deg2Rad;
        float nearPitch = pitchRad * nearSensitivity;
        float nearYaw = yawRad * nearSensitivity;

        // Offset in the plane: x = yaw, y = pitch. Use tan so offset grows with angle.
        float nearX = Mathf.Tan(nearYaw) * nearDistance;
        float nearY = Mathf.Tan(nearPitch) * nearDistance;
        nearReticle.localPosition = new Vector3(nearX, nearY, nearDistance);

        float farX = Mathf.Tan(yawRad) * farDistance;
        float farY = Mathf.Tan(pitchRad) * farDistance;
        farReticle.localPosition = new Vector3(farX, farY, farDistance);
    }

    /// <summary>
    /// Direction the ship should point (world space). Vector from near to far reticle; no roll.
    /// </summary>
    public Vector3 GetTargetDirection()
    {
        if (farReticle == null || nearReticle == null || viewReference == null)
            return viewReference != null ? viewReference.forward : Vector3.forward;

        Vector3 delta = farReticle.position - nearReticle.position;
        if (delta.sqrMagnitude < 0.0001f)
            return viewReference.forward;
        return delta.normalized;
    }

    /// <summary>
    /// Current pitch/yaw state (degrees) for debugging or UI.
    /// </summary>
    public void GetPitchYaw(out float pitchDeg, out float yawDeg)
    {
        pitchDeg = _pitchAngleDeg;
        yawDeg = _yawAngleDeg;
    }
}
