using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JunkerEquipment
{
    [DefaultExecutionOrder(11000)]
    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
    public sealed class JunkerPowerCell : MonoBehaviour
    {
        public string cellType = "R4";
        [Min(1)] public int capacity = 24;
        [Min(0)] public int remainingShots = 24;
        public JunkerPistolBatterySocket Socket { get; private set; }
        XRGrabInteractable grab;
        Rigidbody body;
        VRInventoryItem inventoryItem;
        public bool IsStoredInInventory => inventoryItem && inventoryItem.CurrentContainer;
        Collider[] colliders;
        bool pendingInsert;
        JunkerPistolBatterySocket justRemovedFrom;
        float sameSocketBlockedUntil;
        RigidbodyInterpolation looseInterpolation;
        bool capturedLooseInterpolation;
        float restoreLoosePhysicsUntil;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
            inventoryItem = GetComponent<VRInventoryItem>();
            if (!capturedLooseInterpolation) CaptureLooseInterpolation();
            colliders = GetComponentsInChildren<Collider>(true);
            if (grab.colliders.Count == 0)
                foreach (var shape in colliders) if (shape) grab.colliders.Add(shape);
            grab.selectEntered.AddListener(Selected);
            grab.selectExited.AddListener(Released);
            var parentSocket = GetComponentInParent<JunkerPistolBatterySocket>();
            if (parentSocket && parentSocket.startingCell == this) parentSocket.MountStartingCell(this);
        }

        void OnDestroy()
        {
            if (grab) { grab.selectEntered.RemoveListener(Selected); grab.selectExited.RemoveListener(Released); }
            if (Socket) Socket.Forget(this);
        }

        void Selected(SelectEnterEventArgs _)
        {
            pendingInsert = false;
            if (Socket)
            {
                justRemovedFrom = Socket;
                sameSocketBlockedUntil = Time.time + .35f;
                Socket.Remove(this);
            }
        }

        void Released(SelectExitEventArgs args)
        {
            if (grab.isSelected) return;
            // Restore the loose baseline before inventory captures it. XRI may have cached
            // the kinematic/no-gravity state of an installed or previously stored cell.
            if (!Socket)
            {
                body.isKinematic = false; body.useGravity = true; body.interpolation = looseInterpolation;
                restoreLoosePhysicsUntil = Time.time + .75f;
            }
            if (inventoryItem && inventoryItem.TryStoreOnRelease(args)) { pendingInsert = false; return; }
            pendingInsert = !args.isCanceled;
        }

        void LateUpdate()
        {
            if (IsStoredInInventory) { pendingInsert = false; return; }
            RestoreLoosePhysicsAfterRelease();
            EnforceMountedPose();
            // Prevent an accidental immediate snap-back, but never bar the same
            // cell indefinitely if the player brings it back to the socket.
            if (justRemovedFrom && justRemovedFrom.socketAnchor &&
                (Time.time >= sameSocketBlockedUntil ||
                 Vector3.Distance(transform.position, justRemovedFrom.socketAnchor.position) > .08f))
                justRemovedFrom = null;
            if (!pendingInsert || grab.isSelected) return;
            pendingInsert = false;
            JunkerPistolBatterySocket nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (var socket in FindObjectsByType<JunkerPistolBatterySocket>(FindObjectsSortMode.None))
            {
                if (!socket.CanInsert(this)) continue;
                if (socket == justRemovedFrom) continue;
                float distance = DistanceTo(socket.socketAnchor.position);
                if (distance < nearestDistance && distance <= socket.insertionRadius)
                { nearest = socket; nearestDistance = distance; }
            }
            if (nearest) nearest.Insert(this);
        }

        public float DistanceTo(Vector3 point)
        {
            float distance = Vector3.Distance(transform.position, point);
            if (colliders == null) return distance;
            foreach (var shape in colliders)
                if (shape && shape.enabled) distance = Mathf.Min(distance, Vector3.Distance(shape.ClosestPoint(point), point));
            return distance;
        }

        public bool ConsumeShot()
        {
            if (remainingShots <= 0) return false;
            remainingShots--;
            return true;
        }

        internal void SetMounted(JunkerPistolBatterySocket socket)
        {
            Socket = socket;
            justRemovedFrom = null;
            if (!body) body = GetComponent<Rigidbody>();
            if (!capturedLooseInterpolation) CaptureLooseInterpolation();
            if (!grab) grab = GetComponent<XRGrabInteractable>();
            if (colliders == null) colliders = GetComponentsInChildren<Collider>(true);
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            foreach (var shape in colliders) if (shape) shape.isTrigger = !socket.IsOpen;
            // Preserve world scale when moving under a scaled pistol/socket.
            transform.SetParent(socket.socketAnchor, true);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.Euler(socket.mountedEuler);
            SetLatchOpen(socket.IsOpen);
        }

        void CaptureLooseInterpolation()
        {
            looseInterpolation = body.interpolation;
            capturedLooseInterpolation = true;
        }

        void EnforceMountedPose()
        {
            if (!Socket || !Socket.socketAnchor || grab.isSelected) return;
            if (transform.parent != Socket.socketAnchor) transform.SetParent(Socket.socketAnchor, true);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.Euler(Socket.mountedEuler);
        }

        public void SetLoose()
        {
            Socket = null;
            restoreLoosePhysicsUntil = Time.time + .75f;
            transform.SetParent(null, true);
            foreach (var shape in colliders) if (shape) shape.isTrigger = false;
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = looseInterpolation;
            grab.enabled = true;
        }
        void FixedUpdate() { RestoreLoosePhysicsAfterRelease(); }
        void RestoreLoosePhysicsAfterRelease()
        {
            if (Socket || IsStoredInInventory || !body || !grab || grab.isSelected || Time.time > restoreLoosePhysicsUntil) return;
            // XRI can restore the kinematic/no-gravity state cached while the cell was mounted.
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = looseInterpolation;
        }

        internal void SetLatchOpen(bool open)
        {
            if (!Socket) return;
            if (colliders != null) foreach (var shape in colliders) if (shape) shape.isTrigger = !open;
            if (grab) grab.enabled = open;
            transform.localPosition = Vector3.zero;
            Physics.SyncTransforms();
        }
    }
}
