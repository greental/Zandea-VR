using UnityEngine;
namespace JunkerEquipment
{
    public sealed class JunkerTarget : MonoBehaviour
    {
        public float health=100;
        public TextMesh label;
        public int Hits { get; private set; }
        public void ApplyDamage(float damage) { health=Mathf.Max(0,health-damage); Hits++; if(label) label.text=$"HITS {Hits}\n{health:F0} HP"; }
    }
}
