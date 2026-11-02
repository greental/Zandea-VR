using JunkerBody;
using JunkerEquipment;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRKoombar.WeaponsStaging.Contracts;
using Zandea.FriendlyNPC;

namespace KenshiVR.MeleeCombat
{
    // One component per tracked hand. The direct interactor is suppressed only after
    // the hand is empty and has no valid grab target.
    [DefaultExecutionOrder(-500)]
    public sealed class PlayerMartialHand : MonoBehaviour
    {
        public enum HandState { Idle, Grabbing, Holding, MartialStance, ActivePunch, Recovery }
        public VRBodyController body;
        public JunkerGloveHand glove;
        public XRDirectInteractor interactor;
        public bool left;
        [Min(.03f)] public float fistRadius = .09f;
        [Min(.05f)] public float minimumTravel = .19f;
        [Min(.1f)] public float minimumSpeed = 1.55f;
        [Min(.1f)] public float punchCooldown = .42f;
        public HandState State { get; private set; }
        public bool InStance => State == HandState.MartialStance || State == HandState.ActivePunch || State == HandState.Recovery;
        public Vector3 Velocity { get; private set; }
        readonly Collider[] hits = new Collider[24];
        InteractionLayerMask normalLayers;
        JunkerEquipmentRig equipmentRig;
        Vector3 previous, strikeStart;
        float quietSeconds, recoveryUntil;
        bool sampled, ready;

        void Awake()
        {
            if (!glove) glove = GetComponent<JunkerGloveHand>();
            if (!body && glove) body = glove.body;
            if (glove) left = glove.left;
            if (!interactor && body)
            {
                var controller = left ? body.leftController : body.rightController;
                foreach (var candidate in body.xrOrigin.GetComponentsInChildren<XRDirectInteractor>(true))
                    if (controller && (candidate.transform == controller || candidate.transform.IsChildOf(controller)))
                    { interactor = candidate; break; }
            }
            if (interactor) normalLayers = interactor.interactionLayers;
            if (body) equipmentRig = body.GetComponentInChildren<JunkerEquipmentRig>(true);
        }

        void OnDisable()
        {
            if (interactor) interactor.interactionLayers = normalLayers;
            State = HandState.Idle;
            sampled = false;
        }

        void Update()
        {
            if (!glove || !glove.input || !interactor) return;
            if (FriendlyInputReservation.IsReserved(interactor))
            {
                LeaveStance(); State = HandState.Idle; return;
            }
            bool grip = (glove.input.gripValue.action?.ReadValue<float>() ?? 0f) > .7f;
            bool holding = interactor.hasSelection || interactor.isPerformingManualInteraction;
            if (holding || !grip)
            {
                LeaveStance();
                State = holding ? HandState.Holding : grip ? HandState.Grabbing : HandState.Idle;
                return;
            }
            if (!InStance)
            {
                // A hovered interactable wins if grip and trigger arrive together.
                if (interactor.interactablesHovered.Count > 0 ||
                    equipmentRig && equipmentRig.HasAvailableFloorPickup(interactor))
                { State = HandState.Grabbing; return; }
                normalLayers = interactor.interactionLayers;
                interactor.interactionLayers = 0;
                State = HandState.MartialStance;
                sampled = false;
                ready = true;
            }
        }

        void LeaveStance()
        {
            if (!InStance) return;
            interactor.interactionLayers = normalLayers;
            sampled = false;
            ready = false;
            Velocity = Vector3.zero;
        }

        public void TickTrackedImpact()
        {
            if (!glove) return;
            Vector3 current = glove.transform.position;
            if (!sampled) { previous = strikeStart = current; sampled = true; return; }
            Vector3 delta = current - previous;
            Velocity = delta / Mathf.Max(Time.deltaTime, .0001f);
            if (!InStance) { previous = current; strikeStart = current; return; }
            glove.SetCurl(1f, 1f);
            float speed = Velocity.magnitude;
            if (State == HandState.Recovery)
            {
                quietSeconds = speed < .55f ? quietSeconds + Time.deltaTime : 0f;
                if (Time.time >= recoveryUntil && quietSeconds >= .1f)
                { State = HandState.MartialStance; ready = true; strikeStart = current; }
            }
            else if (ready)
            {
                if (speed < .55f) strikeStart = current;
                if (speed >= minimumSpeed && Vector3.Distance(strikeStart, current) >= minimumTravel)
                {
                    State = HandState.ActivePunch;
                    TryPunch(previous, current, speed);
                }
                else State = HandState.MartialStance;
            }
            previous = current;
        }

        void TryPunch(Vector3 from, Vector3 to, float speed)
        {
            int count = Physics.OverlapCapsuleNonAlloc(from, to, fistRadius, hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var shape = hits[i];
                if (!shape || body && shape.transform.IsChildOf(body.xrOrigin)) continue;
                var target = shape.GetComponentInParent<IDamageable>();
                if (target == null || target.GetCombatState() == ShipCombatState.Destroyed) continue;
                Vector3 travel = to - from;
                Vector3 center = from + travel * Mathf.Clamp01(Vector3.Dot(shape.bounds.center - from, travel) /
                    Mathf.Max(travel.sqrMagnitude, .000001f));
                Vector3 contact = shape.ClosestPoint(center);
                if (Vector3.Distance(contact, center) > fistRadius + .02f) continue;
                float damage = Mathf.Clamp(4f + (speed - minimumSpeed) * 3.5f, 4f, 16f);
                damage *= CharacterCombatMath.DamageDealt(body.GetComponent<CharacterAttributes>(),
                    PhysicalMeleeWeapon.WeaponProficiencyClass.None);
                target.ApplyDamage(new DamagePacket {
                    BaseDamage = damage, DamageType = DamageType.Kinetic, PhysicalImpact = PhysicalImpactKind.Unarmed, HitPointWorld = contact,
                    HitNormalWorld = (contact - from).normalized, Source = body.gameObject,
                    WeaponId = left ? "left fist" : "right fist", Timestamp = Time.time
                });
                var martial = shape.GetComponentInParent<NpcMartialArts>();
                if (martial) martial.ReceiveStrikeStagger(Mathf.Clamp(speed / 3f, .8f, 1.5f));
                ready = false;
                recoveryUntil = Time.time + punchCooldown;
                quietSeconds = 0f;
                State = HandState.Recovery;
                break;
            }
        }

        public bool TryDefend(Vector3 impact, Vector3 attacker, out bool parry)
        {
            parry = false;
            if (!InStance || !body || !glove) return false;
            var arm = left ? body.leftArm : body.rightArm;
            Vector3 fist = glove.transform.position;
            Vector3 elbow = arm.lower.position;
            Vector3 segment = fist - elbow;
            Vector3 closest = elbow + segment * Mathf.Clamp01(Vector3.Dot(impact - elbow, segment) /
                Mathf.Max(segment.sqrMagnitude, .000001f));
            if (Vector3.Distance(impact, fist) > .2f && Vector3.Distance(impact, closest) > .16f) return false;
            if (fist.y < body.chest.position.y - .25f) return false;
            Vector3 incoming = Vector3.ProjectOnPlane(attacker - fist, Vector3.up);
            if (incoming.sqrMagnitude < .001f || Vector3.Dot(body.avatarRoot.forward, incoming.normalized) < .15f) return false;
            parry = Vector3.Dot(Velocity, incoming.normalized) > 1.2f;
            return true;
        }

        public bool TryIntercept(Vector3 attackFrom, Vector3 attackTo, float attackRadius,
            Vector3 attacker, out Vector3 impact)
        {
            impact = Vector3.zero;
            if (!InStance || !body || !glove) return false;
            var arm = left ? body.leftArm : body.rightArm;
            Vector3 fist = glove.transform.position;
            Vector3 elbow = arm.lower.position;
            Vector3 forearmMiddle = (fist + elbow) * .5f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 point = i == 0 ? fist : i == 1 ? forearmMiddle : elbow;
                Vector3 travel = attackTo - attackFrom;
                Vector3 closest = attackFrom + travel * Mathf.Clamp01(Vector3.Dot(point - attackFrom, travel) /
                    Mathf.Max(travel.sqrMagnitude, .000001f));
                if (Vector3.Distance(closest, point) > attackRadius + (i == 0 ? fistRadius : .11f)) continue;
                if (!TryDefend(closest, attacker, out _)) continue;
                impact = closest;
                return true;
            }
            return false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            GUI.Label(new Rect(left ? 12 : 180, 12, 165, 24), (left ? "Left: " : "Right: ") + State);
        }
#endif
    }
}
