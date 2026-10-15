using UnityEngine;
using UnityEngine.Events;

namespace JunkerScanner
{
    public sealed class ServiceCompartment : ScanInfoProvider
    {
        public string compartmentName;
        [SerializeField] bool isOpen;
        public DiagnosticSystem[] diagnostics = System.Array.Empty<DiagnosticSystem>();
        public UnityEvent<bool> openChanged = new();
        public bool IsOpen => isOpen;
        [ContextMenu("Open compartment (scanner hook)")]
        public void Open() => SetOpen(true);
        [ContextMenu("Close compartment (scanner hook)")]
        public void Close() => SetOpen(false);
        public void SetOpen(bool value)
        { if (value == isOpen) return; isOpen = value; openChanged.Invoke(value); }
        public override void Populate(ScanInfo info)
        {
            if (!string.IsNullOrEmpty(compartmentName)) info.Name = compartmentName;
            foreach (var diagnostic in diagnostics) if (diagnostic) diagnostic.Populate(info);
            bool fault = info.Diagnostics.Exists(d => ScanStatusUtility.IsFault(d.Status));
            info.Status = fault ? ScanStatus.Fault : ScanStatus.Operational;
            if (!isOpen && fault) info.Description += "\nFault detected inside";
            if (string.IsNullOrEmpty(info.Hint)) info.Hint = isOpen ? "Inspect exposed components" : "Open to inspect";
        }
    }
}
