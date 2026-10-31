using UnityEngine;
namespace Zandea.FriendlyNPC
{
    [CreateAssetMenu(menuName = "Zandea/Economy/Currency")]
    public sealed class CurrencyDefinition : ScriptableObject
    {
        public string id = "ZEK";
        public string singularName = "Zek";
        public string pluralName = "Zeks";
        public string symbol = "Z";
        public string Format(int amount) => amount + " " + (Mathf.Abs(amount) == 1 ? singularName : pluralName);
        public static string FormatZeks(int amount) => amount + " " + (Mathf.Abs(amount) == 1 ? "Zek" : "Zeks");
    }
}
