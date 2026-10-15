using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Owned by the bike controller so this applies to existing bikes and variants.
internal sealed class HoverBikeHeldItemCollisions
{
    readonly List<XRBaseInputInteractor> hands = new List<XRBaseInputInteractor>();
    readonly List<Collider> shapes = new List<Collider>();
    readonly HashSet<Collider> heldShapes = new HashSet<Collider>();
    readonly HashSet<(Collider item, Collider bike)> wanted = new HashSet<(Collider, Collider)>();
    readonly Dictionary<(Collider item, Collider bike), bool> previous = new Dictionary<(Collider, Collider), bool>();
    readonly List<(Collider item, Collider bike)> expired = new List<(Collider, Collider)>();

    public void Refresh(SitInCockpit seat, Collider[] vehicleColliders)
    {
        if (!seat || !seat.isActiveAndEnabled || !seat.IsSeated || !seat.xrOrigin)
        {
            Restore();
            return;
        }

        heldShapes.Clear();
        wanted.Clear();
        seat.xrOrigin.GetComponentsInChildren<XRBaseInputInteractor>(false, hands);
        foreach (var hand in hands)
        {
            // Input interactors cover direct, ray and near/far hands, but not sockets.
            foreach (var selected in hand.interactablesSelected)
            {
                if (!(selected is XRGrabInteractable grab)) continue;
                grab.GetComponentsInChildren<Collider>(true, shapes);
                foreach (var shape in shapes) if (shape) heldShapes.Add(shape);
                // Also respect explicitly registered shapes outside the item's hierarchy.
                foreach (var shape in grab.colliders) if (shape) heldShapes.Add(shape);
            }
        }

        foreach (var item in heldShapes)
        {
            // Grabbing the bike's wheel or ignition must not change self-collision rules.
            if (System.Array.IndexOf(vehicleColliders, item) >= 0) continue;
            foreach (var hull in vehicleColliders)
            {
                if (!hull || hull == item || heldShapes.Contains(hull) ||
                    hull.transform.IsChildOf(seat.xrOrigin)) continue;
                var pair = (item, hull);
                wanted.Add(pair);
                if (!previous.ContainsKey(pair))
                    previous.Add(pair, Physics.GetIgnoreCollision(item, hull));
                // Reapply in case a held collider was disabled/re-enabled by equipment.
                Physics.IgnoreCollision(item, hull, true);
            }
        }

        expired.Clear();
        foreach (var pair in previous)
        {
            if (wanted.Contains(pair.Key)) continue;
            RestorePair(pair.Key, pair.Value);
            expired.Add(pair.Key);
        }
        foreach (var pair in expired) previous.Remove(pair);
    }

    public void Restore()
    {
        foreach (var pair in previous) RestorePair(pair.Key, pair.Value);
        previous.Clear();
        wanted.Clear();
        heldShapes.Clear();
        hands.Clear();
        shapes.Clear();
        expired.Clear();
    }

    static void RestorePair((Collider item, Collider bike) pair, bool ignored)
    {
        if (pair.item && pair.bike) Physics.IgnoreCollision(pair.item, pair.bike, ignored);
    }
}
