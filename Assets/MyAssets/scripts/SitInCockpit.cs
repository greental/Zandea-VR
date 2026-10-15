using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using Unity.XR.CoreUtils;

/// <summary>
/// Pilot seat: parents XR Origin to the ship when seated. References auto-resolve when left null (single XROrigin / camera / mediator in scene).
/// </summary>
public class SitInCockpit : MonoBehaviour
{
    [Header("References")]
    public Transform xrOrigin;        
    public Camera mainCamera;
    public Transform seatAnchor;      
    public LocomotionMediator locomotionMediator;

    [Header("Settings")]
    public float seatYOffset = 0f;
    public float sitDistance = 0.7f;
    public bool lockMovementWhenSeated = true;

    private bool isSeated = false;
    private bool insideSeatZone = false;
    private readonly HashSet<Collider> playerCollidersInSeatZone = new HashSet<Collider>();

    /// <summary>True while the pilot is seated (XR origin parented to ship).</summary>
    public bool IsSeated => isSeated;

    [Header("Input")]
    [Tooltip("Bound to sit/stand: OnEnable subscribes to performed; OnSitStandPressed toggles Sit vs StandUp when inside the seat trigger.")]
    public InputActionReference sitStandAction;

    void Awake()
    {
        ResolvePlayerRigRefs();
        if (seatAnchor == null)
            seatAnchor = FindSeatAnchorTransform();
    }

    static Transform OriginToTransform(XROrigin xro)
    {
        if (xro == null) return null;
        return xro.transform;
    }

    static Transform NormalizeToXROriginRoot(Transform candidate)
    {
        if (candidate == null)
            return null;

        XROrigin xro = candidate.GetComponentInParent<XROrigin>();
        return xro != null ? xro.transform : candidate;
    }

    void ResolvePlayerRigRefs()
    {
        var origins = FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (origins.Length > 1)
            Debug.LogError("[SitInCockpit] Scene has " + origins.Length + " XROrigin components — use exactly one. Seating / rig resolution may be wrong.", this);

        if (xrOrigin == null && origins.Length > 0)
        {
            var xro = origins[0];
            xrOrigin = OriginToTransform(xro);
        }

        xrOrigin = NormalizeToXROriginRoot(xrOrigin);

        if (mainCamera == null)
        {
            var xro = origins.Length > 0 ? origins[0] : FindFirstObjectByType<XROrigin>(FindObjectsInactive.Exclude);
            if (xro != null && xro.Camera != null)
                mainCamera = xro.Camera;
            else if (Camera.main != null)
                mainCamera = Camera.main;
        }

        if (locomotionMediator == null)
            locomotionMediator = FindFirstObjectByType<LocomotionMediator>(FindObjectsInactive.Exclude);
    }

    Transform FindSeatAnchorTransform()
    {
        foreach (var hint in new[] { "SeatAnchor", "PilotChairPos", "seatAnchor", "Seat", "ChairAnchor" })
        {
            var t = FindChildByNameRecursive(transform.root, hint);
            if (t != null) return t;
        }
        return null;
    }

    static Transform FindChildByNameRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var f = FindChildByNameRecursive(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    private void OnEnable()
    {
        if (sitStandAction != null && sitStandAction.action != null)
        {
            sitStandAction.action.Enable();
            sitStandAction.action.performed += OnSitStandPressed;
        }
    }

    private void OnDisable()
    {
        if (sitStandAction != null && sitStandAction.action != null)
        {
            sitStandAction.action.performed -= OnSitStandPressed;
            sitStandAction.action.Disable();
        }

        playerCollidersInSeatZone.Clear();
        insideSeatZone = false;
    }

    private void Update()
    {
        // Nothing needed here anymore – parenting removes drift entirely
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayerCollider(other))
            return;

        playerCollidersInSeatZone.Add(other);
        insideSeatZone = playerCollidersInSeatZone.Count > 0;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null)
            return;

        playerCollidersInSeatZone.Remove(other);
        insideSeatZone = playerCollidersInSeatZone.Count > 0;
    }

    bool IsPlayerCollider(Collider other)
    {
        if (other == null)
            return false;

        if (other.CompareTag("Player"))
            return true;

        if (xrOrigin != null && (other.transform == xrOrigin || other.transform.IsChildOf(xrOrigin)))
            return true;

        return other.GetComponentInParent<XROrigin>() != null;
    }

    private void OnSitStandPressed(InputAction.CallbackContext ctx)
    {
        if (isSeated)
            StandUp();
        else
            TrySit();
    }


    private void TrySit()
    {
        if (!insideSeatZone)
        {
            Debug.Log("Not inside seat zone.");
            return;
        }

        Sit();
    }

    /// <summary>Seats the resolved player rig without requiring it to enter the seat trigger first.</summary>
    public bool StartSeated()
    {
        if (isSeated)
            return true;

        ResolvePlayerRigRefs();
        if (seatAnchor == null)
            seatAnchor = FindSeatAnchorTransform();

        if (xrOrigin == null || mainCamera == null || seatAnchor == null)
        {
            Debug.LogWarning("[SitInCockpit] Cannot start seated: XR Origin, main camera, or seat anchor is missing.", this);
            return false;
        }

        Sit();
        return isSeated;
    }


    // ============================================================
    //                          SIT
    // ============================================================
    private void Sit()
    {
        Transform camT = mainCamera.transform;

        // --- 1. Compute desired head world position ---
        Vector3 desiredHeadPos =
            seatAnchor.position +
            seatAnchor.up * seatYOffset;

        // --- 2. Align XR Origin fully to seat so when ship is tilted we're not at an awkward angle ---
        xrOrigin.rotation = seatAnchor.rotation;

        // --- 3. Move XR Origin so camera lands exactly at desired spot ---
        Vector3 offset = desiredHeadPos - camT.position;
        xrOrigin.position += offset;

        // --- 4. Parent XR Origin to the SHIP ---
        FlightController shipFlight = transform.root.GetComponentInChildren<FlightController>(true);
        Rigidbody shipBody = shipFlight != null ? shipFlight.rb : null;
        if (shipBody == null && seatAnchor != null)
            shipBody = seatAnchor.GetComponentInParent<Rigidbody>();
        Transform shipRoot = shipBody != null ? shipBody.transform : transform.root;
        xrOrigin.SetParent(shipRoot, worldPositionStays: true);

        // --- 5. Disable body colliders only (hands stay enabled so we can grab the stick) ---
        SetPlayerBodyCollidersEnabled(false);

        // --- 6. Freeze locomotion ---
        if (lockMovementWhenSeated)
            FreezeLocomotion(true);

        isSeated = true;

        Debug.Log("[SIT] Seated + Parent to Ship");
    }


    // ============================================================
    //                        STAND UP
    // ============================================================
    public void StandUp()
    {
        isSeated = false;

        // Unparent XR Origin BACK to world
        xrOrigin.SetParent(null, worldPositionStays: true);

        // Re-enable body colliders when leaving seat (hands were never disabled)
        SetPlayerBodyCollidersEnabled(true);

        // Flatten XR Origin to world horizontal so player isn't left tilted (rotation no longer dirtied by seat)
        Vector3 fwd = xrOrigin.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f)
            fwd = xrOrigin.up;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f)
            fwd = Vector3.forward;
        xrOrigin.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        if (lockMovementWhenSeated)
            FreezeLocomotion(false);

        Debug.Log("[STAND] Unseated + Unparented");
    }


    // ============================================================
    //     BODY COLLIDERS ONLY (disable when seated; hands stay on for grab)
    // ============================================================
    private void SetPlayerBodyCollidersEnabled(bool enabled)
    {
        if (xrOrigin == null) return;
        // Body only: CharacterController and Colliders on the rig root (and Camera Offset if present).
        // Do NOT touch hand/controller hierarchy so joystick-hand collision still works for grab.
        var cc = xrOrigin.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = enabled;
        foreach (var c in xrOrigin.GetComponents<Collider>())
            c.enabled = enabled;
        var cameraOffset = xrOrigin.childCount > 0 ? xrOrigin.GetChild(0) : null;
        if (cameraOffset != null)
        {
            foreach (var c in cameraOffset.GetComponents<Collider>())
                c.enabled = enabled;
        }
    }

    // ============================================================
    //                    LOCOMOTION FREEZE
    // ============================================================
    private void FreezeLocomotion(bool freeze)
    {
        if (locomotionMediator != null)
        {
            foreach (var provider in locomotionMediator.GetComponentsInChildren<LocomotionProvider>())
                provider.enabled = !freeze;
        }
    }
}
