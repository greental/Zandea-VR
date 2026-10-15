using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class JoystickController : MonoBehaviour
{
    [Header("References")]
    public Transform pivot;             // Empty object at hinge center
    public Transform visual;            // Joystick mesh that visually rotates
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable handle;   // The grab interactable object

    [Header("Limits")]
    public float maxPitch = 20f;
    public float maxRoll = 20f;
    public float returnSpeed = 10f;

    [Header("Sensitivity")]
    public float pitchSensitivity = 30f;
    public float rollSensitivity = 30f;

    private Quaternion neutralRot;
    private bool isHeld = false;

    private void Start()
    {
        neutralRot = visual.localRotation;

        handle.selectEntered.AddListener(OnGrab);
        handle.selectExited.AddListener(OnRelease);
    }

    private void OnDestroy()
    {
        handle.selectEntered.RemoveListener(OnGrab);
        handle.selectExited.RemoveListener(OnRelease);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        isHeld = true;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        isHeld = false;
    }

    private void Update()
    {
        if (isHeld)
            UpdateJoystickVisual();
        else
            ReturnToCenter();
    }

    private void UpdateJoystickVisual()
    {
        // vector from pivot to hand
        Vector3 dir = (handle.transform.position - pivot.position).normalized;

        // convert to pivot local space
        Vector3 localDir = pivot.InverseTransformDirection(dir);

        // calculate values
        // float pitch = Mathf.Clamp(-localDir.z * pitchSensitivity, -maxPitch, maxPitch);
        // float roll  = Mathf.Clamp(localDir.x * rollSensitivity, -maxRoll,  maxRoll);

        float pitch = -localDir.z * pitchSensitivity;
        float roll  = localDir.x * rollSensitivity;

        visual.localRotation = Quaternion.Euler(pitch, 0f, -roll);
    }

    private void ReturnToCenter()
    {
        visual.localRotation = Quaternion.Slerp(
            visual.localRotation,
            neutralRot,
            Time.deltaTime * returnSpeed
        );
    }
}
