using UnityEngine;

namespace JunkerScanner
{
    public sealed class CuttableAccessPoint : ScanInfoProvider
    {
        public bool cuttingRequired = true;
        public string requiredAction = "Thermal cutting", materialRating;
        [Min(0)] public float difficulty = 1;
        [TextArea] public string scannerDescription = "Fasteners destroyed";
        [SerializeField] bool opened;
        public AuthoredCutPath[] paths = System.Array.Empty<AuthoredCutPath>();
        public bool NeedsCutting => cuttingRequired && !opened;
        public bool IsOpened => opened;
        public void SetOpened(bool value) { opened = value; }
        public override void Populate(ScanInfo info)
        {
            info.Description = scannerDescription;
            info.Status = opened ? ScanStatus.Operational : ScanStatus.Locked;
            info.Hint = NeedsCutting ? requiredAction + " required" : opened ? "Access open" : null;
        }
    }
}
