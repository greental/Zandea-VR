using System;
using System.Collections.Generic;
using ScavengerNPC;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    public readonly struct DisarmClash
    {
        public readonly PhysicalMeleeWeapon Attacker, Defender;
        public readonly float RelativeSpeed, Impulse;
        public readonly Vector3 ContactNormal;
        public DisarmClash(PhysicalMeleeWeapon attacker, PhysicalMeleeWeapon defender,
            float relativeSpeed, float impulse, Vector3 contactNormal)
        {
            Attacker = attacker; Defender = defender; RelativeSpeed = relativeSpeed;
            Impulse = impulse; ContactNormal = contactNormal;
        }
    }

    // Future attributes can implement this on either combatant. With no provider the
    // chance stays at the configured baseline.
    public interface IDisarmChanceModifier
    {
        float ModifyDisarmChance(in DisarmClash clash, float chance);
    }

    // Player combatants can supply this same contract when player attack intent exists.
    public interface IDisarmCombatant
    {
        Transform CombatantRoot { get; }
        bool CanClash { get; }
        bool IsStriking { get; }
        bool Opposes(IDisarmCombatant other);
        bool ReleaseForDisarm(PhysicalMeleeWeapon weapon);
    }

    public sealed class WeaponDisarmResolver : MonoBehaviour
    {
        public enum ProbabilityMode { Normal, ForcedTest, Disabled }
        [Header("Qualifying clash")]
        [Min(0)] public float minimumRelativeSpeed = 1.5f;
        [Min(0)] public float minimumImpulse = .4f;
        [Min(0)] public float pairCooldown = 1.25f;
        [Header("Disarm chance")]
        [Range(0, 1)] public float baselineChance = .05f;
        public ProbabilityMode mode = ProbabilityMode.Normal;
        public bool useSeededRandom;
        public int randomSeed = 7319;
        [Header("Release")]
        [Min(0)] public float releaseImpulse = 1.2f;
        [Min(0)] public float maximumReleaseImpulse = 2.5f;
        [Header("Diagnostics")]
        public bool logAccepted, logRejected, logRolls, logTransitions;

        public static WeaponDisarmResolver Active { get; private set; }
        public int QualifyingClashes { get; private set; }
        public int ProbabilityRolls { get; private set; }
        public int SuccessfulDisarms { get; private set; }
        readonly Dictionary<ulong, PairState> pairs = new();
        System.Random seededRandom;

        struct PairState { public bool touching; public float lastRoll; }

        void OnEnable()
        {
            if (Active && Active != this) { Debug.LogError("Only one WeaponDisarmResolver may run in a scene", this); enabled = false; return; }
            Active = this;
            seededRandom = new System.Random(randomSeed);
        }
        void OnDisable()
        {
            if (Active == this) Active = null;
            pairs.Clear();
        }

        static ulong Key(PhysicalMeleeWeapon a, PhysicalMeleeWeapon b)
        {
            uint first = (uint)a.GetInstanceID(), second = (uint)b.GetInstanceID();
            if (first > second) (first, second) = (second, first);
            return ((ulong)first << 32) | second;
        }

        public void NotifySeparation(PhysicalMeleeWeapon a, PhysicalMeleeWeapon b)
        {
            if (!a || !b || !pairs.TryGetValue(Key(a, b), out var pair)) return;
            pair.touching = false;
            pairs[Key(a, b)] = pair;
        }

        // Called only on weapon collision entry. Public to permit deterministic
        // Play Mode tests with measured impact inputs and no headset.
        public bool Evaluate(PhysicalMeleeWeapon a, PhysicalMeleeWeapon b,
            float relativeSpeed, float impulse, Vector3 contactNormal)
        {
            if (!a || !b || a == b) return false;
            ulong key = Key(a, b);
            pairs.TryGetValue(key, out var pair);
            if (pair.touching) { Reject("continued contact"); return false; }
            pair.touching = true;
            pairs[key] = pair;
            if (relativeSpeed < minimumRelativeSpeed || impulse < minimumImpulse)
            { Reject($"minor contact speed={relativeSpeed:F2} impulse={impulse:F2}"); return false; }
            if (pairCooldown > 0 && pair.lastRoll > 0 && Time.time - pair.lastRoll < pairCooldown)
            { Reject("pair cooldown"); return false; }
            if (a.State == PhysicalMeleeWeapon.HoldState.World ||
                b.State == PhysicalMeleeWeapon.HoldState.World || !a.Owner || !b.Owner ||
                a.Owner == b.Owner || !a.ownerRoot || !b.ownerRoot)
            { Reject("ownership transition or world weapon"); return false; }
            var actorA = a.Owner as IDisarmCombatant;
            var actorB = b.Owner as IDisarmCombatant;
            if (actorA == null || actorB == null || !actorA.CanClash || !actorB.CanClash ||
                !actorA.Opposes(actorB) || !actorB.Opposes(actorA))
            { Reject("dead, ragdolled, or non-opposing combatants"); return false; }
            bool strikingA = actorA.IsStriking;
            bool strikingB = actorB.IsStriking;
            if (!strikingA && !strikingB) { Reject("no active strike"); return false; }
            // When both attack at once, the faster weapon is treated as the striker.
            bool aAttacks = strikingA && (!strikingB || a.GetComponent<Rigidbody>().linearVelocity.sqrMagnitude >=
                b.GetComponent<Rigidbody>().linearVelocity.sqrMagnitude);
            var attacker = aAttacks ? a : b;
            var defender = aAttacks ? b : a;
            if (logAccepted) Debug.Log($"Disarm clash {attacker.name} -> {defender.name}: speed={relativeSpeed:F2}, impulse={impulse:F2}", this);
            QualifyingClashes++;
            var effectiveMode = mode;
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            effectiveMode = ProbabilityMode.Normal;
#endif
            float chance = effectiveMode == ProbabilityMode.Disabled ? 0 : effectiveMode == ProbabilityMode.ForcedTest ? 1 : baselineChance;
            var clash = new DisarmClash(attacker, defender, relativeSpeed, impulse, contactNormal);
            if (effectiveMode == ProbabilityMode.Normal)
            {
                chance = Modify(attacker.ownerRoot, clash, chance);
                chance = Modify(defender.ownerRoot, clash, chance);
            }
            chance = Mathf.Clamp01(chance);
            float roll = useSeededRandom ? (float)seededRandom.NextDouble() : UnityEngine.Random.value;
            ProbabilityRolls++;
            pair.lastRoll = Time.time > 0 ? Time.time : float.Epsilon;
            pairs[key] = pair;
            if (logRolls) Debug.Log($"Disarm roll={roll:F3}, chance={chance:F3}", this);
            if (roll >= chance) return false;
            var defenderActor = defender.Owner as IDisarmCombatant;
            if (defenderActor == null) { Reject("defender equipment changed"); return false; }
            Vector3 direction = defender.transform.position - attacker.transform.position;
            direction = (direction.sqrMagnitude > .001f ? direction.normalized : contactNormal.normalized) + Vector3.up * .35f;
            if (!defenderActor.ReleaseForDisarm(defender)) { Reject("defender equipment changed"); return false; }
            if (defender.State != PhysicalMeleeWeapon.HoldState.World) return false;
            var body = defender.GetComponent<Rigidbody>();
            if (body && direction.sqrMagnitude > .001f)
                body.AddForce(direction.normalized * Mathf.Min(releaseImpulse, maximumReleaseImpulse), ForceMode.Impulse);
            SuccessfulDisarms++;
            if (logTransitions) Debug.Log($"Disarmed {defenderActor.CombatantRoot.name}: {defender.name} is now world-owned", this);
            return true;
        }

        static float Modify(Transform actor, in DisarmClash clash, float chance)
        {
            foreach (var behaviour in actor.GetComponents<MonoBehaviour>())
                if (behaviour is IDisarmChanceModifier modifier)
                    chance = modifier.ModifyDisarmChance(clash, chance);
            return chance;
        }
        void Reject(string reason)
        {
            if (logRejected) Debug.Log("Disarm clash rejected: " + reason, this);
        }
    }
}
