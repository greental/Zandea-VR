using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Small arena HUD so the player can tell which defensive and attack states
    // actually occurred while the two NPCs fight.
    public sealed class BanditArenaTelemetry : MonoBehaviour
    {
        public StarvingBandit left, right;
        public int LeftDodges => dodges[0];
        public int RightDodges => dodges[1];
        public int LeftParries => parries[0];
        public int RightParries => parries[1];
        public int LeftBlocks => blocks[0];
        public int RightBlocks => blocks[1];
        public int LeftHits => hits[0];
        public int RightHits => hits[1];
        public float LeftDamage => damageDealt[0];
        public float RightDamage => damageDealt[1];
        public int LeftRecoveries => recoveries[0];
        public int RightRecoveries => recoveries[1];
        public int LeftAttacks => AttackCount(0);
        public int RightAttacks => AttackCount(1);
        public int QualifyingClashes => WeaponDisarmResolver.Active ? WeaponDisarmResolver.Active.QualifyingClashes : 0;
        public int DisarmAttempts => WeaponDisarmResolver.Active ? WeaponDisarmResolver.Active.ProbabilityRolls : 0;
        public int DisarmSuccesses => WeaponDisarmResolver.Active ? WeaponDisarmResolver.Active.SuccessfulDisarms : 0;
        public float CombatDuration => finalDuration > 0 ? finalDuration : Time.time - started;
        public string Winner => !left || !right ? "Unknown" :
            left.health.IsDead && right.health.IsDead ? "Draw" :
            left.health.IsDead ? right.name : right.health.IsDead ? left.name : "Ongoing";
        public int LeftDirections => SeenDirections(0);
        public int RightDirections => SeenDirections(1);
        readonly int[,] attacks = new int[2, 8];
        readonly int[] dodges = new int[2], parries = new int[2];
        readonly int[] blocks = new int[2], hits = new int[2], recoveries = new int[2];
        readonly float[] damageDealt = new float[2], previousHealth = new float[2];
        readonly PhysicalMeleeWeapon[] previousWeapon = new PhysicalMeleeWeapon[2];
        readonly bool[] initialized = new bool[2];
        float started, finalDuration;
        readonly bool[] announcedAll = new bool[2];
        readonly StarvingBandit.State[] previous = new StarvingBandit.State[2];
        GUIStyle style;

        void Update()
        {
            if (started == 0) started = Time.time;
            Record(0, left);
            Record(1, right);
            if (finalDuration == 0 && left && right && left.health && right.health &&
                (left.health.IsDead || right.health.IsDead)) finalDuration = Time.time - started;
        }
        void Record(int side, StarvingBandit bandit)
        {
            if (!bandit) return;
            var equipment = bandit.GetComponent<NpcGeneratedWeapon>();
            var currentWeapon = equipment ? equipment.Instance : bandit.staff;
            if (!initialized[side])
            {
                previousHealth[side] = bandit.health ? bandit.health.Current : 0;
                previousWeapon[side] = currentWeapon;
                initialized[side] = true;
            }
            if (bandit.health && bandit.health.Current < previousHealth[side])
            {
                int attackerSide = 1 - side;
                hits[attackerSide]++;
                damageDealt[attackerSide] += previousHealth[side] - bandit.health.Current;
            }
            if (bandit.health) previousHealth[side] = bandit.health.Current;
            if (!previousWeapon[side] && currentWeapon) recoveries[side]++;
            previousWeapon[side] = currentWeapon;
            var current = bandit.state;
            if (current == previous[side]) return;
            if (current == StarvingBandit.State.Anticipate)
            {
                attacks[side, Mathf.Clamp(bandit.CurrentAttack, 0, 7)]++;
                if (SeenDirections(side) == 8 && !announcedAll[side])
                {
                    announcedAll[side] = true;
                    Debug.Log($"ARENA_ALL_DIRECTIONS {bandit.name}");
                }
            }
            else if (current == StarvingBandit.State.Dodge) dodges[side]++;
            else if (current == StarvingBandit.State.Parry) parries[side]++;
            else if (current == StarvingBandit.State.Block) blocks[side]++;
            previous[side] = current;
        }
        int SeenDirections(int side)
        {
            int count = 0;
            for (int i = 0; i < 8; i++) if (attacks[side, i] > 0) count++;
            return count;
        }
        int AttackCount(int side)
        {
            int count = 0;
            for (int i = 0; i < 8; i++) count += attacks[side, i];
            return count;
        }
        void OnGUI()
        {
            if (!left || !right) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white } };
            Draw(left, 0, 18);
            Draw(right, 1, Screen.width - Mathf.Min(390, Screen.width * .38f) - 18);
        }
        void Draw(StarvingBandit fighter, int side, float x)
        {
            float width = Mathf.Min(390, Screen.width * .38f);
            GUI.Box(new Rect(x, 78, width, 65), GUIContent.none);
            GUI.Label(new Rect(x + 10, 81, width - 20, 25),
                $"{fighter.state}   angle {fighter.CurrentAttack * 45}°", style);
            GUI.Label(new Rect(x + 10, 108, width - 20, 25),
                $"Angles {SeenDirections(side)}/8   Dodges {dodges[side]}   Parries {parries[side]}", style);
        }
    }
}
