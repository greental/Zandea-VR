using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KenshiVR.CharacterCreator
{
    // Only the HMD camera hides the face; mirrors and other cameras see the full character.
    public sealed class CreatorPlayerLocalHead : MonoBehaviour
    {
        public Camera localCamera;
        public Transform visualRoot;
        public CharacterCosmeticsController cosmetics;
        readonly Dictionary<Renderer, bool> previous = new();

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            Restore();
        }
        void Begin(ScriptableRenderContext _, Camera camera)
        {
            if (!localCamera || camera != localCamera || !visualRoot) return;
            foreach (var renderer in visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer || !IsHead(renderer)) continue;
                Hide(renderer);
            }
            if (cosmetics)
                foreach (var item in cosmetics.GetComponentsInChildren<AppearanceSpawnedItem>(true))
                    if (item && (item.slot == AppearanceSlot.Headwear || item.slot == AppearanceSlot.Hair ||
                                 item.slot == AppearanceSlot.Beard))
                        foreach (var renderer in item.GetComponentsInChildren<Renderer>(true)) Hide(renderer);
        }
        void End(ScriptableRenderContext _, Camera camera)
        {
            if (camera == localCamera) Restore();
        }
        void Restore()
        {
            foreach (var pair in previous) if (pair.Key) pair.Key.forceRenderingOff = pair.Value;
            previous.Clear();
        }
        void Hide(Renderer renderer)
        {
            if (!renderer || previous.ContainsKey(renderer)) return;
            previous[renderer] = renderer.forceRenderingOff;
            renderer.forceRenderingOff = true;
        }
        static bool IsHead(Renderer renderer)
        {
            var item = renderer.GetComponentInParent<AppearanceSpawnedItem>();
            if (item && (item.slot == AppearanceSlot.Headwear || item.slot == AppearanceSlot.Hair ||
                         item.slot == AppearanceSlot.Beard)) return true;
            switch (renderer.name)
            {
                case "Object_7": case "Object_9": case "Object_14":
                case "Object_16": case "Object_17": case "Object_18": case "Object_19":
                case "Object_21": case "Object_22": return true;
                default: return false;
            }
        }
    }
}
