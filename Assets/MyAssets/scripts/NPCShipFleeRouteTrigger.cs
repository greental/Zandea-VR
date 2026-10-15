using UnityEngine;

/// <summary>
/// Trigger volume (e.g. canyon mouth): assigns a flee polyline to an <see cref="NPCShipAI"/> for its <b>next</b> Flee state.
/// Requires a trigger collider on this object.
/// </summary>
[RequireComponent(typeof(Collider))]
public class NPCShipFleeRouteTrigger : MonoBehaviour
{
    [SerializeField] private Transform[] fleeRoute;
    [Tooltip("If empty, any NPCShipAI entering the trigger receives the route. Otherwise only listed ships.")]
    [SerializeField] private NPCShipAI[] onlyTheseShips;

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null)
            c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (fleeRoute == null || fleeRoute.Length == 0)
            return;

        NPCShipAI ai = other.GetComponentInParent<NPCShipAI>();
        if (ai == null)
            return;

        if (onlyTheseShips != null && onlyTheseShips.Length > 0)
        {
            bool allowed = false;
            for (int i = 0; i < onlyTheseShips.Length; i++)
            {
                if (onlyTheseShips[i] == ai)
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
                return;
        }

        ai.SetFleeRouteOverride(fleeRoute);
    }
}
