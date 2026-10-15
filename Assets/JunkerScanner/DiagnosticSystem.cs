using System;
using UnityEngine;

namespace JunkerScanner
{
    [Serializable]
    public sealed class DiagnosticModule
    {
        public string displayName;
        public ScanStatus status = ScanStatus.Operational;
        public DiagnosticSeverity severity;
        public string message;
        public Transform location;
        public ScanHighlightTarget highlight;
        [Tooltip("When assigned, status comes from the actual occupant, including Missing when empty.")]
        public ComponentSlot slot;
        public DiagnosticReading Read() => new(displayName, slot ? slot.Status : status, severity, message,
            location ? location : slot ? slot.transform : null, highlight ? highlight : slot ? slot.scannerTarget : null);
    }

    public sealed class DiagnosticSystem : ScanInfoProvider
    {
        public DiagnosticModule[] modules = Array.Empty<DiagnosticModule>();
        public override void Populate(ScanInfo info)
        {
            foreach (var module in modules)
            {
                if (module == null) continue;
                var reading = module.Read(); info.Diagnostics.Add(reading);
                if (ScanStatusUtility.IsFault(reading.Status)) info.Status = ScanStatus.Fault;
            }
        }
        void OnDrawGizmosSelected()
        {
            foreach (var module in modules)
            {
                if (module == null) continue;
                var reading = module.Read();
                if (!reading.Location) continue;
                Gizmos.color = ScanStatusUtility.IsFault(reading.Status) ? Color.red : Color.green;
                Gizmos.DrawLine(transform.position, reading.Location.position);
                Gizmos.DrawWireSphere(reading.Location.position, .08f);
            }
        }
    }
}
