using System.Text;
using TMPro;
using UnityEngine;

namespace JunkerScanner
{
    public sealed class CompactScannerPresenter : ScannerPresenter
    {
        public TMP_Text label;
        public GameObject indicator;
        [Range(1, 12)] public int maxDiagnosticRows = 5;
        readonly StringBuilder text = new();
        public override void Present(bool scanning, ScanInfo info)
        {
            if (indicator) indicator.SetActive(scanning);
            if (!label) return;
            label.gameObject.SetActive(scanning);
            if (!scanning) { label.text = ""; return; }
            if (info == null) { label.text = "SCANNER ONLINE"; return; }
            text.Clear().Append(info.Name).Append('\n');
            if (!string.IsNullOrEmpty(info.Category)) text.Append(info.Category).Append(" · ");
            text.Append(ScanStatusUtility.Label(info.Status));
            if (info.Condition.HasValue) text.Append("   ").Append(Mathf.RoundToInt(info.Condition.Value)).Append('%');
            if (!string.IsNullOrEmpty(info.Description)) text.Append('\n').Append(info.Description);
            int rows = 0;
            // Faults first, retaining authored order within each group.
            for (int pass = 0; pass < 2; pass++)
                foreach (var diagnostic in info.Diagnostics)
                {
                    if (ScanStatusUtility.IsFault(diagnostic.Status) != (pass == 0) || rows >= maxDiagnosticRows) continue;
                    text.Append('\n').Append(diagnostic.Name).Append("   ").Append(ScanStatusUtility.Label(diagnostic.Status)); rows++;
                }
            if (info.Diagnostics.Count > rows) text.Append("\n+").Append(info.Diagnostics.Count - rows).Append(" systems");
            if (!string.IsNullOrEmpty(info.Hint)) text.Append('\n').Append(info.Hint);
            label.text = text.ToString();
        }
    }
}
