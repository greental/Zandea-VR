using UnityEngine;

namespace JunkerScanner
{
    [DisallowMultipleComponent]
    public class ScannableObject : MonoBehaviour, IScannable
    {
        public string displayName, category;
        [TextArea] public string description;
        public string interactionHint;
        public ScanStatus status;
        public int scannerPriority;
        public ScanHighlightKind highlightKind;
        public ScanHighlightTarget[] highlights = System.Array.Empty<ScanHighlightTarget>();
        [Tooltip("Optional compartment whose closed cover prevents inspection of this object.")]
        public ServiceCompartment accessCompartment;
        [Tooltip("Ordered data sources; later providers can override basic information.")]
        public ScanInfoProvider[] providers = System.Array.Empty<ScanInfoProvider>();
        public virtual bool CanScan => isActiveAndEnabled && (!accessCompartment || accessCompartment.IsOpen);
        public int Priority => scannerPriority;
        public virtual ScanInfo Inspect()
        {
            var info = new ScanInfo { Name = string.IsNullOrEmpty(displayName) ? name : displayName,
                Category = category, Description = description, Hint = interactionHint, Status = status };
            foreach (var provider in providers) if (provider && provider.isActiveAndEnabled) provider.Populate(info);
            return info;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, .06f);
        }
    }
}
