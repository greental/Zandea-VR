using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    [DefaultExecutionOrder(-1000)]
    public sealed class CharacterStatArenaScenario : MonoBehaviour
    {
        public enum Matchup { WeakWeak, WeakVeteran, AverageAverage, VeteranVeteran, VeteranWeak }
        public Matchup matchup = Matchup.AverageAverage;
        public CharacterAttributeProfile weakBandit, averageFighter, veteranWarrior;
        public StarvingBandit left, right;
        public bool useSkillBasedAttackSelection = true;
        public bool comparisonCombatTuning = true;

        void Awake() => Apply();
        public void Apply()
        {
            var leftProfile = matchup == Matchup.WeakWeak || matchup == Matchup.WeakVeteran ?
                weakBandit : matchup == Matchup.VeteranVeteran || matchup == Matchup.VeteranWeak ?
                veteranWarrior : averageFighter;
            var rightProfile = matchup == Matchup.WeakWeak || matchup == Matchup.VeteranWeak ?
                weakBandit : matchup == Matchup.WeakVeteran || matchup == Matchup.VeteranVeteran ?
                veteranWarrior : averageFighter;
            Apply(left, leftProfile);
            Apply(right, rightProfile);
        }
        void Apply(StarvingBandit fighter, CharacterAttributeProfile profile)
        {
            if (!fighter) return;
            var attributes = fighter.GetComponent<CharacterAttributes>();
            if (attributes) attributes.ApplyProfile(profile);
            if (useSkillBasedAttackSelection) fighter.cycleAttackDirections = false;
            if (comparisonCombatTuning)
            {
                fighter.initialAttackDelay = .5f;
                fighter.ResetAttackSchedule();
                fighter.parryIncomingNpcAttacks = true;
                fighter.parryChance = .65f;
                fighter.blockIncomingNpcAttacks = true;
                fighter.blockChance = .25f;
                fighter.dodgeIncomingNpcAttacks = true;
                fighter.dodgeChance = .2f;
            }
        }
    }
}
