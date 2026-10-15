using System.Collections.Generic;
using UnityEngine;

namespace JunkerScanner
{
    // Replaceable backend. Each renderer should belong to one target; multiple scanners can share a target.
    public class ScanHighlightTarget : MonoBehaviour
    {
        public Renderer[] renderers = System.Array.Empty<Renderer>();
        readonly Dictionary<object, (ScanHighlightKind kind, ScanVisualProfile profile)> requests = new();
        readonly List<(Renderer renderer, int index, MaterialPropertyBlock original)> saved = new();
        MaterialPropertyBlock working;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");
        public virtual void Request(object owner, ScanHighlightKind kind, ScanVisualProfile profile)
        {
            if (owner == null || !profile || !isActiveAndEnabled) return;
            if (requests.Count == 0) Capture();
            requests[owner] = (kind, profile); Apply();
        }
        public virtual void Release(object owner)
        { if (owner == null || !requests.Remove(owner)) return; if (requests.Count == 0) Restore(); else Apply(); }
        void Capture()
        {
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, i);
                    saved.Add((renderer, i, block));
                }
            }
        }
        void Apply()
        {
            working ??= new MaterialPropertyBlock();
            ScanHighlightKind kind = 0; ScanVisualProfile profile = null;
            foreach (var request in requests.Values)
                if (!profile || request.kind >= kind) { kind = request.kind; profile = request.profile; }
            if (!profile) return;
            foreach (var entry in saved)
            {
                if (!entry.renderer) continue;
                entry.renderer.GetPropertyBlock(working, entry.index);
                // Preserve renderer-wide values when the material has no per-slot block.
                if (working.isEmpty) entry.renderer.GetPropertyBlock(working);
                working.SetColor(BaseColor, profile.ColorFor(kind)); working.SetColor(ColorId, profile.ColorFor(kind));
                entry.renderer.SetPropertyBlock(working, entry.index);
            }
        }
        void Restore()
        {
            foreach (var entry in saved) if (entry.renderer)
                entry.renderer.SetPropertyBlock(entry.original.isEmpty ? null : entry.original, entry.index);
            saved.Clear();
        }
        protected virtual void OnDisable() { requests.Clear(); Restore(); }
    }
}
