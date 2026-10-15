using UnityEngine;
using UnityEngine.Rendering;

namespace JunkerBody
{
    // Only the player's camera receives the headless mesh; other cameras keep the full avatar.
    public sealed class JunkerLocalHead : MonoBehaviour
    {
        public Camera localCamera;
        public SkinnedMeshRenderer body;
        public Mesh fullMesh, headlessMesh;
        void OnEnable() { RenderPipelineManager.beginCameraRendering += Begin; RenderPipelineManager.endCameraRendering += End; }
        void Begin(ScriptableRenderContext context, Camera camera) { if (body) body.sharedMesh = camera == localCamera ? headlessMesh : fullMesh; }
        void End(ScriptableRenderContext context, Camera camera) { if (body) body.sharedMesh = fullMesh; }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            if (body) body.sharedMesh = fullMesh;
        }
    }
}
