using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Runs after the tracked body and glove visuals have reached this frame's controller pose.
    [DefaultExecutionOrder(10700)]
    public sealed class PlayerMartialHandsLate : MonoBehaviour
    {
        PlayerMartialHand[] hands;
        void Awake() => hands = GetComponentsInChildren<PlayerMartialHand>(true);
        void LateUpdate()
        {
            foreach (var hand in hands)
                if (hand && hand.isActiveAndEnabled) hand.TickTrackedImpact();
        }
    }
}
