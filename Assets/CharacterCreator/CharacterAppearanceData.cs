using System;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    [Serializable]
    public sealed class CharacterAppearanceData
    {
        [Range(.8f, 1.2f)] public float height = 1f;
        [Range(0f, 1f)] public float weight = .5f;
        [Range(0f, 1f)] public float muscularity = .5f;
        [Range(0f, 1f)] public float noseWidth = .5f;
        [Range(0f, 1f)] public float noseTip = .5f;
        [Range(0f, 1f)] public float jawWidth = .5f;
        [Range(0f, 1f)] public float earSize = .5f;
        [Range(0f, 1f)] public float foreheadShape = .5f;
        [Range(0f, 1f)] public float eyeSize = .5f;
        [Range(0f, 1f)] public float eyeSpacing = .5f;
        [Range(0f, 1f)] public float eyebrowHeight = .5f;
        [Range(0f, 1f)] public float eyebrowThickness = .5f;
        [Range(0f, 1f)] public float eyebrowAngle = .5f;
        [Range(0f, 1f)] public float eyebrowSpacing = .5f;
        [Range(0f, 1f)] public float eyeShape = .5f;
        [Range(0f, 1f)] public float eyeTilt = .5f;
        [Range(0f, 1f)] public float mouthWidth = .5f;
        [Range(0f, 1f)] public float lipFullness = .5f;
        public Color skinColor = new Color(.65f, .44f, .32f);
        public Color eyeColor = new Color(.25f, .35f, .4f);
        public HairStyle hairStyle;
        public int hairVariant;
        public BeardStyle beardStyle;
        public int beardVariant;
        public Color hairColor = new Color(.16f, .12f, .09f);
        public Color beardColor = new Color(.16f, .12f, .09f);
        public bool linkBeardColor = true;

        public CharacterAppearanceData Copy() => JsonUtility.FromJson<CharacterAppearanceData>(JsonUtility.ToJson(this));
    }

    public enum HairStyle { Bald, Short, Medium, LongTiedBack }
    public enum BeardStyle { CleanShaven, Stubble, ShortBeard, FullBeard }
}
