using KenshiVR.CharacterCreator;
using UnityEngine;

namespace Zandea.FriendlyNPC
{
    [CreateAssetMenu(menuName = "Zandea/Friendly NPC/Voice profile")]
    public sealed class VoiceProfile : ScriptableObject
    {
        public string providerId = "kokoro";
        public string providerVoiceId;
        [TextArea] public string voiceDescription;
        [Range(.7f, 1.3f)] public float speechRate = 1f;
        public ExpressionType defaultExpression = ExpressionType.Neutral;
        [TextArea] public string licenseNotes;
    }
}
