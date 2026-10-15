using UnityEngine;
using UnityEngine.Events;
using JunkerScanner;

namespace JunkerEquipment.Thermal
{
    public enum ThermalMode { Cut, Weld }
    public abstract class ThermalWorkTarget : ScanInfoProvider, IScanPathProvider
    {
        public ThermalPathProgress[] paths = System.Array.Empty<ThermalPathProgress>();
        public string materialId = "steel";
        [Min(0)] public float minimumPower = .5f;
        public UnityEvent completed = new();
        public System.Collections.Generic.IEnumerable<AuthoredCutPath> ScannerPaths
        {
            get { if(!Current || (!Accepts(ThermalMode.Cut) && !Accepts(ThermalMode.Weld))) yield break; foreach(var p in paths) if(p && p.path && !p.Complete) yield return p.path; }
        }
        int lastFrame=-1;
        public abstract bool Accepts(ThermalMode mode);
        public ThermalPathProgress Current { get { foreach(var p in paths) if(p && !p.Complete) return p; return null; } }
        public float Progress { get { float sum=0; foreach(var p in paths) if(p) sum+=p.Fraction; return paths.Length>0?sum/paths.Length:0; } }
        public void Show(object owner,bool value) { foreach(var p in paths) if(p && p.path) p.path.RequestVisibility(owner,value); }
        public bool Work(ThermalMode mode,Vector3 hit,float power,string compatibleMaterial,float seconds,float speed)
        {
            if(!isActiveAndEnabled || lastFrame==Time.frameCount || !Accepts(mode) || power<minimumPower ||
                (!string.IsNullOrEmpty(compatibleMaterial) && compatibleMaterial!=materialId)) return false;
            var current=Current;
            if(!current || !current.Advance(hit,seconds,speed)) return false;
            lastFrame=Time.frameCount;
            if(!Current) Finish(mode);
            return true;
        }
        protected void ResetPaths() { foreach(var p in paths) if(p) p.Clear(); }
        protected abstract void Finish(ThermalMode mode);
        public override void Populate(ScanInfo info)
        {
            info.Description=$"{materialId}: {Progress:P0}";
            if(Current && Current.Valid) info.Diagnostics.Add(new DiagnosticReading("Work location",info.Status,DiagnosticSeverity.Information,info.Hint,Current.path.controlPoints[0],null));
        }
    }
}
