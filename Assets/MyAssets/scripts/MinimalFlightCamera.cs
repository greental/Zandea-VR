using UnityEngine;

/// <summary>
/// Optional: follow the MinimalFlight ship from behind so you can see pitch/roll.
/// Assign the ship transform in the inspector; camera stays behind and looks at ship.
/// </summary>
public class MinimalFlightCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 8f;
    public float height = 2f;
    public float smoothTime = 0.15f;

    Vector3 velocity;

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 back = -target.forward;
        back.y = 0f;
        if (back.sqrMagnitude < 0.01f) back = -target.up;
        back.Normalize();
        Vector3 desired = target.position + back * distance + Vector3.up * height;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
        transform.LookAt(target.position + target.forward * 2f);
    }
}
