using KenshiVR.CharacterCreator;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    public static class AppearanceArchetypeSetup
    {
        const string Folder = "Assets/CharacterCreator/Archetypes";
        [InitializeOnLoadMethod]
        static void QueueDefaults() => EditorApplication.delayCall += CreateDefaults;

        [MenuItem("Tools/Character Creator/Create default NPC archetypes")]
        public static void CreateDefaults()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/CharacterCreator", "Archetypes");
            const string catalogPath = "Assets/CharacterCreator/CosmeticCatalog.asset";
            if (!AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(catalogPath))
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<CosmeticCatalog>(), catalogPath);
            Make("Starving Bandit", .88f, 1.02f, 0f, .08f, 0f, .12f,
                new[] { new WeightedHair { style = HairStyle.Bald, weight = 2 }, new WeightedHair { style = HairStyle.Short, weight = 3 } },
                new[] { new WeightedBeard { style = BeardStyle.CleanShaven, weight = 2 }, new WeightedBeard { style = BeardStyle.Stubble, weight = 3 } });
            Make("Heavy Guard", .98f, 1.16f, .5f, .8f, .82f, 1f,
                new[] { new WeightedHair { style = HairStyle.Short, weight = 4 }, new WeightedHair { style = HairStyle.Bald, weight = 1 } },
                new[] { new WeightedBeard { style = BeardStyle.CleanShaven, weight = 3 }, new WeightedBeard { style = BeardStyle.ShortBeard, weight = 1 } });
            Make("Merchant", .92f, 1.08f, .42f, .88f, .2f, .55f,
                new[] { new WeightedHair { style = HairStyle.Short, weight = 2 }, new WeightedHair { style = HairStyle.Medium, weight = 3 } },
                new[] { new WeightedBeard { style = BeardStyle.CleanShaven, weight = 2 }, new WeightedBeard { style = BeardStyle.ShortBeard, weight = 2 } });
            Make("Wanderer", .84f, 1.18f, .12f, .88f, .15f, .9f,
                new[] { new WeightedHair { style = HairStyle.Bald, weight = 1 }, new WeightedHair { style = HairStyle.Short, weight = 3 }, new WeightedHair { style = HairStyle.Medium, weight = 2 }, new WeightedHair { style = HairStyle.LongTiedBack, weight = 1 } },
                new[] { new WeightedBeard { style = BeardStyle.CleanShaven, weight = 2 }, new WeightedBeard { style = BeardStyle.Stubble, weight = 2 }, new WeightedBeard { style = BeardStyle.ShortBeard, weight = 1 }, new WeightedBeard { style = BeardStyle.FullBeard, weight = 1 } });
            AssetDatabase.SaveAssets();
        }
        static void Make(string name, float h0, float h1, float w0, float w1, float m0, float m1,
            WeightedHair[] hair, WeightedBeard[] beard)
        {
            string path = Folder + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<AppearanceArchetype>(path)) return;
            var asset = ScriptableObject.CreateInstance<AppearanceArchetype>();
            asset.height = new FloatRange { min = h0, max = h1 };
            asset.weight = new FloatRange { min = w0, max = w1 };
            asset.muscularity = new FloatRange { min = m0, max = m1 };
            asset.hair = hair; asset.beard = beard;
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
