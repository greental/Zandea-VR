using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace KenshiVR.MeleeCombat
{
    [Serializable]
    public struct CombatHitRecord
    {
        public float time;
        public string attacker, defender, attackType, weaponId;
        public float rawDamage, healthDamage, consciousnessDamage;
        public bool blocked, parried, staggered, knockout, surprise;
        public float strengthXp, toughnessXp, enduranceXp, martialArtsXp;
        // Body region is intentionally absent: the current damage pipeline does not report it.
    }

    [Serializable]
    public sealed class CombatLifetimeStats
    {
        public int punchesAttempted, punchesLanded, weaponHitsLanded, validHitsReceived;
        public int kicksLanded;
        public int successfulBlocks, successfulParries, knockoutsInflicted, timesKnockedUnconscious;
        public int combatEncountersSurvived;
        public float damageDealt, damageReceived, consciousnessDamageDealt, consciousnessDamageReceived;
    }

    [Serializable]
    sealed class CharacterProgressionSave
    {
        public string characterId;
        public CharacterAttributeValues attributes;
        public float[] xp;
        public float[] lifetimeXp;
        public CombatLifetimeStats lifetime;
    }

    // One optional observer per character. Health receivers remain the sole damage authority.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterAttributes))]
    public sealed class CharacterCombatProgression : MonoBehaviour
    {
        public CharacterAttributes attributes;
        public string persistentCharacterId;
        public bool progressionEnabled = true;
        public bool detailedLogging;
        [Range(1, 128)] public int recentHitLimit = 32;
        [Min(1f)] public float encounterWindowSeconds = 120f;
        [Min(1f)] public float xpBudgetPerWindow = 25f;
        [Min(1f)] public float baseXpToNext = 30f;
        [Min(1f)] public float xpPerCurrentPoint = 6f;
        [SerializeField, HideInInspector] float[] xp = new float[8];
        [SerializeField, HideInInspector] float[] lifetimeXp = new float[8];
        [SerializeField, HideInInspector] CombatLifetimeStats lifetime = new CombatLifetimeStats();
        [SerializeField, HideInInspector] List<CombatHitRecord> recentHits = new List<CombatHitRecord>(32);
        float windowStarted, windowXp;

        public CombatLifetimeStats Lifetime => lifetime;
        public IReadOnlyList<CombatHitRecord> RecentHits => recentHits;
        public float Experience(CharacterAttribute attribute) => xp != null && (int)attribute < xp.Length ? xp[(int)attribute] : 0f;
        public float LifetimeExperience(CharacterAttribute attribute) => lifetimeXp != null && (int)attribute < lifetimeXp.Length ? lifetimeXp[(int)attribute] : 0f;
        public float ExperienceToNext(CharacterAttribute attribute) => baseXpToNext + attributes.Get(attribute) * xpPerCurrentPoint;
        public string SavePath => string.IsNullOrWhiteSpace(persistentCharacterId) ? null :
            Path.Combine(Application.persistentDataPath, "character-progress-" + SafeId(persistentCharacterId) + ".json");

        void Awake()
        {
            if (!attributes) attributes = GetComponent<CharacterAttributes>();
            EnsureArrays();
        }

        void Start()
        {
            if (!string.IsNullOrWhiteSpace(persistentCharacterId)) Load();
        }

        void OnApplicationPause(bool paused) { if (paused) Save(); }
        void OnApplicationQuit() => Save();

        void EnsureArrays()
        {
            if (xp == null || xp.Length < 8) Array.Resize(ref xp, 8);
            if (lifetimeXp == null || lifetimeXp.Length < 8) Array.Resize(ref lifetimeXp, 8);
            if (lifetime == null) lifetime = new CombatLifetimeStats();
            if (recentHits == null) recentHits = new List<CombatHitRecord>(recentHitLimit);
        }

        static string SafeId(string id)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) id = id.Replace(invalid, '_');
            return id;
        }

        public void Save()
        {
            if (SavePath == null || !attributes) return;
            EnsureArrays();
            var data = new CharacterProgressionSave { characterId = persistentCharacterId,
                attributes = attributes.RuntimeValues, xp = xp, lifetimeXp = lifetimeXp, lifetime = lifetime };
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(data, true)); }
            catch (Exception exception) { Debug.LogWarning("Character progression save failed: " + exception.Message, this); }
        }

        public bool Load()
        {
            if (SavePath == null || !File.Exists(SavePath) || !attributes) return false;
            try
            {
                var data = JsonUtility.FromJson<CharacterProgressionSave>(File.ReadAllText(SavePath));
                if (data == null || data.characterId != persistentCharacterId) return false;
                foreach (CharacterAttribute attribute in Enum.GetValues(typeof(CharacterAttribute)))
                    attributes.SetRuntime(attribute, data.attributes.Get(attribute));
                xp = data.xp;
                lifetimeXp = data.lifetimeXp;
                lifetime = data.lifetime;
                EnsureArrays();
                return true;
            }
            catch (Exception exception) { Debug.LogWarning("Character progression load failed: " + exception.Message, this); return false; }
        }

        public void ResetToStartingValues()
        {
            if (attributes) attributes.ResetToDefaults();
            xp = new float[8];
            lifetimeXp = new float[8];
            lifetime = new CombatLifetimeStats();
            recentHits.Clear();
            windowXp = 0f;
            Save();
        }

        public void RecordEncounterSurvived()
        {
            lifetime.combatEncountersSurvived++;
            Award(CharacterAttribute.Toughness, 3f);
            Award(CharacterAttribute.Endurance, 3f);
        }

        public float RecordCarryingTraining(float meaningfulLoadDistanceXp) =>
            Award(CharacterAttribute.Strength, meaningfulLoadDistanceXp);

        public static CharacterCombatProgression Find(Transform root)
        {
            if (!root) return null;
            var own = root.GetComponent<CharacterCombatProgression>();
            if (own) return own;
            own = root.GetComponentInParent<CharacterCombatProgression>();
            return own ? own : root.GetComponentInChildren<CharacterCombatProgression>(true);
        }

        public static void RecordPunchAttempt(Transform attacker)
        {
            var progress = Find(attacker);
            if (progress) progress.lifetime.punchesAttempted++;
        }

        // Called once by an accepting health receiver, after mitigation and state transitions.
        public static void RecordHit(in DamagePacket packet, Transform defender, float healthDamage,
            float traumaDamage, bool blocked, bool parried, bool staggered, bool knockout,
            bool defenderWasConscious, bool defenderWasHostile)
        {
            var victim = Find(defender);
            var attacker = packet.Source ? Find(packet.Source.transform) : null;
            if (!victim && !attacker) return;
            var hit = new CombatHitRecord {
                time = packet.Timestamp > 0f ? packet.Timestamp : Time.time,
                attacker = packet.Source ? packet.Source.name : "environment",
                defender = defender.name,
                attackType = packet.PhysicalImpact == PhysicalImpactKind.Unarmed ?
                    (packet.WeaponId != null && (packet.WeaponId.IndexOf("foot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        packet.WeaponId.IndexOf("kick", StringComparison.OrdinalIgnoreCase) >= 0) ? "kick" : "punch") :
                    packet.PhysicalImpact == PhysicalImpactKind.Blunt && packet.WeaponId != null &&
                    packet.WeaponId.IndexOf("staff", StringComparison.OrdinalIgnoreCase) >= 0 ? "Bo Staff" : "weapon",
                weaponId = packet.WeaponId,
                rawDamage = packet.BaseDamage, healthDamage = healthDamage,
                consciousnessDamage = traumaDamage, blocked = blocked, parried = parried,
                staggered = staggered, knockout = knockout, surprise = packet.SurpriseAttack
            };
            if (victim)
            {
                victim.lifetime.validHitsReceived++;
                victim.lifetime.damageReceived += healthDamage;
                victim.lifetime.consciousnessDamageReceived += traumaDamage;
                if (blocked && !parried) victim.lifetime.successfulBlocks++;
                if (parried) victim.lifetime.successfulParries++;
                if (knockout) victim.lifetime.timesKnockedUnconscious++;
                if (defenderWasConscious && !blocked && healthDamage >= 1f)
                {
                    hit.toughnessXp = victim.Award(CharacterAttribute.Toughness, healthDamage * .35f);
                    hit.enduranceXp = victim.Award(CharacterAttribute.Endurance, traumaDamage * .15f);
                }
                if (defenderWasConscious && parried && packet.PhysicalImpact == PhysicalImpactKind.Unarmed)
                    hit.martialArtsXp = victim.Award(CharacterAttribute.MartialArts, .5f);
            }
            if (attacker && attacker != victim)
            {
                if (hit.attackType == "punch") attacker.lifetime.punchesLanded++;
                else if (hit.attackType == "kick") attacker.lifetime.kicksLanded++;
                else attacker.lifetime.weaponHitsLanded++;
                attacker.lifetime.damageDealt += healthDamage;
                attacker.lifetime.consciousnessDamageDealt += traumaDamage;
                if (knockout) attacker.lifetime.knockoutsInflicted++;
                if (defenderWasConscious && defenderWasHostile && healthDamage >= 1f)
                {
                    if (packet.PhysicalImpact == PhysicalImpactKind.Unarmed)
                    {
                        hit.strengthXp = attacker.Award(CharacterAttribute.Strength, healthDamage * .25f);
                        hit.martialArtsXp = attacker.Award(CharacterAttribute.MartialArts, healthDamage * .45f);
                    }
                    else if (packet.PhysicalImpact == PhysicalImpactKind.Blunt)
                        hit.strengthXp = attacker.Award(CharacterAttribute.Strength, healthDamage * .10f);
                    hit.enduranceXp = attacker.Award(CharacterAttribute.Endurance, .25f);
                }
            }
            if (victim) victim.AddRecent(hit);
            if (attacker && attacker != victim) attacker.AddRecent(hit);
        }

        void AddRecent(CombatHitRecord hit)
        {
            recentHits.Add(hit);
            while (recentHits.Count > recentHitLimit) recentHits.RemoveAt(0);
            if (detailedLogging) Debug.Log($"{hit.attacker} -> {hit.defender}: {hit.attackType} {hit.healthDamage:F1} health, {hit.consciousnessDamage:F1} trauma", this);
        }

        float Award(CharacterAttribute attribute, float amount)
        {
            if (!progressionEnabled || !attributes || amount <= 0f) return 0f;
            EnsureArrays();
            if (Time.time - windowStarted >= encounterWindowSeconds)
            { windowStarted = Time.time; windowXp = 0f; }
            float granted = Mathf.Min(amount, Mathf.Max(0f, xpBudgetPerWindow - windowXp));
            if (granted <= 0f) return 0f;
            windowXp += granted;
            int index = (int)attribute;
            xp[index] += granted;
            lifetimeXp[index] += granted;
            while (xp[index] >= ExperienceToNext(attribute) && attributes.Get(attribute) < attributes.runtimeMaximum)
            {
                xp[index] -= ExperienceToNext(attribute);
                attributes.SetRuntime(attribute, attributes.Get(attribute) + 1);
            }
            return granted;
        }
    }
}
