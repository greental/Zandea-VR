using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEngine;

namespace Zandea.Opening
{
    /// <summary>The one Inspector target for the opening encounter.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class OpeningTargetBinding : MonoBehaviour
    {
        [Header("Opening target")]
        [Tooltip("Assign the active XR player from this scene. The group and every slaver use its HMD as their target.")]
        public VRBodyController targetPlayer;
        public BanditGroup group;
        public StarvingBandit[] slavers;
        public OpeningDirector director;

        void Awake() => Apply();

        public void Apply()
        {
            if (!targetPlayer || !targetPlayer.hmd || !group || slavers == null || !director)
            { Debug.LogError("Opening target binding is incomplete.", this); return; }
            group.player = targetPlayer.hmd;
            director.player = targetPlayer;
            foreach (var slaver in slavers)
                if (slaver) slaver.target = targetPlayer.hmd;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying || !targetPlayer || !targetPlayer.hmd) return;
            UnityEditor.EditorApplication.delayCall += ApplyInEditor;
        }

        void ApplyInEditor()
        {
            if (!this || Application.isPlaying || !targetPlayer || !targetPlayer.hmd) return;
            Apply();
            if (group) UnityEditor.EditorUtility.SetDirty(group);
            if (director) UnityEditor.EditorUtility.SetDirty(director);
            if (slavers != null)
                foreach (var slaver in slavers) if (slaver) UnityEditor.EditorUtility.SetDirty(slaver);
        }
#endif
    }
}
