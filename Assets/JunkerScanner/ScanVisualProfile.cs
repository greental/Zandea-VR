using UnityEngine;

namespace JunkerScanner
{
    [CreateAssetMenu(menuName = "Junker/Scanner Visual Profile")]
    public sealed class ScanVisualProfile : ScriptableObject
    {
        public Color normal = new(.82f, .84f, .86f), interaction = new(.88f, .9f, .92f),
            salvage = new(.82f, .84f, .86f), cut = new(1, .4f, .05f),
            installation = Color.green, fault = Color.red;
        public Color ColorFor(ScanHighlightKind kind) => kind switch
        {
            ScanHighlightKind.Fault => fault, ScanHighlightKind.Cut => cut,
            ScanHighlightKind.Salvage => salvage, ScanHighlightKind.Interaction => interaction,
            ScanHighlightKind.InstallationTarget => installation, _ => normal
        };
    }
}
