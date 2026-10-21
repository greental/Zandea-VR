using System.Collections;
using System.Collections.Generic;
using JunkerBody;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

namespace KenshiVR.MeleeCombat
{
    // Held physics loot must not push the player's locomotion body upward.
    public sealed class HeldLootCollisionGuard : MonoBehaviour
    {
        XRGrabInteractable grab;
        readonly List<(Collider item, Collider player)> ignored = new();
        Coroutine restore;
        readonly Dictionary<GameObject, int> layers = new();
        Collider[] playerShapes;
        const int HeldLayer = 2; // Ignore Raycast: remains physical against the world.

        public void Bind(XRGrabInteractable interactable)
        {
            if (grab) { grab.selectEntered.RemoveListener(OnGrab); grab.selectExited.RemoveListener(OnRelease); }
            grab = interactable;
            if (!grab) return;
            grab.selectEntered.AddListener(OnGrab);
            grab.selectExited.AddListener(OnRelease);
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            if (restore != null) { StopCoroutine(restore); restore = null; }
            var player = args.interactorObject.transform.GetComponentInParent<VRBodyController>();
            if (!player) player = FindFirstObjectByType<VRBodyController>();
            if (!player) return;
            var root = player.xrOrigin ? player.xrOrigin : player.transform.root;
            playerShapes = root.GetComponentsInChildren<Collider>(true);
            foreach (var gravity in root.GetComponentsInChildren<GravityProvider>(true))
                gravity.sphereCastLayerMask &= ~(1 << HeldLayer);
            Protect();
        }

        void FixedUpdate()
        {
            if (grab && grab.isSelected) Protect();
        }

        void Protect()
        {
            if (playerShapes == null) return;
            foreach (var item in GetComponentsInChildren<Collider>(true))
            {
                if (!layers.ContainsKey(item.gameObject)) layers.Add(item.gameObject, item.gameObject.layer);
                item.gameObject.layer = HeldLayer;
                foreach (var body in playerShapes)
                {
                    if (!item || !body || body.isTrigger || item == body || body.transform.IsChildOf(transform)) continue;
                    Physics.IgnoreCollision(item, body, true);
                    if (!ignored.Contains((item, body))) ignored.Add((item, body));
                }
            }
        }

        void OnRelease(SelectExitEventArgs args)
        {
            if (grab && grab.isSelected) return;
            if (restore != null) StopCoroutine(restore);
            restore = StartCoroutine(RestoreWhenClear());
        }

        IEnumerator RestoreWhenClear()
        {
            yield return new WaitForSeconds(.35f);
            // A dropped item can still overlap the body for several physics frames.
            while (OverlapsPlayer()) yield return new WaitForFixedUpdate();
            Restore();
            restore = null;
        }

        bool OverlapsPlayer()
        {
            foreach (var pair in ignored)
                if (pair.item && pair.player && pair.item.enabled && pair.player.enabled &&
                    pair.item.bounds.Intersects(pair.player.bounds)) return true;
            return false;
        }

        void Restore()
        {
            foreach (var pair in ignored)
                if (pair.item && pair.player) Physics.IgnoreCollision(pair.item, pair.player, false);
            ignored.Clear();
            foreach (var entry in layers) if (entry.Key) entry.Key.layer = entry.Value;
            layers.Clear();
            playerShapes = null;
        }

        void OnDisable()
        {
            if (restore != null) StopCoroutine(restore);
            restore = null;
            Restore();
        }

        void OnDestroy()
        {
            if (grab) { grab.selectEntered.RemoveListener(OnGrab); grab.selectExited.RemoveListener(OnRelease); }
            Restore();
        }
    }
}
