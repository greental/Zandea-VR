using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    public sealed class NpcWorldItem : MonoBehaviour
    {
        public NpcItemInstance Item { get; private set; }
        public void Bind(NpcItemInstance item) { Item = item; }
    }
}
