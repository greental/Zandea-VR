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
        public int LeftDirections => SeenDirections(0);
        public int RightDirections => SeenDirections(1);
        readonly int[,] attacks = new int[2, 8];
        readonly int[] dodges = new int[2], parries = new int[2];
        readonly bool[] announcedAll = new bool[2];
        readonly StarvingBandit.State[] previous = new StarvingBandit.State[2];
        GUIStyle style;

        void Update()
        {
            Record(0, left);
            Record(1, right);
        }
        void Record(int side, StarvingBandit bandit)
        {
            if (!bandit) return;
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
            previous[side] = current;
        }
        int SeenDirections(int side)
        {
            int count = 0;
            for (int i = 0; i < 8; i++) if (attacks[side, i] > 0) count++;
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
