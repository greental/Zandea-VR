using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.MeleeCombat
{
    // Records a real weapon clash so the arena's fallback scored hit respects
    // a player's held sword, even when the two rigidbodies separate quickly.
    public sealed class PracticeSwordGuard : MonoBehaviour
    {
        static PracticeSwordGuard active;
        [Min(.05f)] public float protectionSeconds = .45f;
        PhysicalMeleeWeapon ownWeapon;
        PhysicalMeleeWeapon lastBlockedWeapon;
        XRGrabInteractable grab;
        float lastBlockTime;

        void Awake()
        {
            ownWeapon = GetComponent<PhysicalMeleeWeapon>();
            grab = GetComponent<XRGrabInteractable>();
        }
        void OnEnable() => active = this;
        void OnDisable() { if (active == this) active = null; }
        void OnCollisionEnter(Collision collision) => Record(collision);
        void OnCollisionStay(Collision collision) => Record(collision);
        void Record(Collision collision)
        {
            if (!grab || !grab.isSelected) return;
            var incoming = collision.collider.GetComponentInParent<PhysicalMeleeWeapon>();
            if (!incoming || incoming == ownWeapon || !incoming.ownerRoot) return;
            lastBlockedWeapon = incoming;
            lastBlockTime = Time.time;
        }
        public static bool BlocksRecentStrike(PhysicalMeleeWeapon incoming) =>
            active && active.grab && active.grab.isSelected && incoming &&
            active.lastBlockedWeapon == incoming && Time.time - active.lastBlockTime <= active.protectionSeconds;
    }
}
