using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KenshiVR.CharacterCreator.Editor
{
    public static class RandomCharacterShowcase
    {
        const string ModelPath = "Assets/CharacterCreator/Models/male_creator.glb";
        const string CatalogPath = "Assets/CharacterCreator/CosmeticCatalog.asset";
        const string Output = "Assets/CharacterCreator/Showcase";
        const int Width = 960;
        const int Height = 1200;

        [MenuItem("Tools/Character Creator/Build Random Character Showcase")]
        public static void Build()
        {
            Directory.CreateDirectory(Output + "/Snapshots");

            var previous = SceneManager.GetActiveScene();
            var mode = string.IsNullOrEmpty(previous.path) ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            SceneManager.SetActiveScene(scene);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(CatalogPath);
            if (!model || !catalog) throw new InvalidOperationException("Creator model or cosmetic catalog is missing.");
            var cameraObject = new GameObject("Showcase Camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.13f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.13f, .15f, .18f);
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 30f;

            var lightObject = new GameObject("Showcase Key Light", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            lightObject.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.65f, .68f, .72f);

            var characters = new GameObject[6];
            var report = new StringBuilder("# Random character fitting snapshots\n\n");
            string[] families = { "desert", "raider", "scavenger", "desert", "raider", "scavenger" };
            HairStyle[] hairs = { HairStyle.Short, HairStyle.Medium, HairStyle.LongTiedBack,
                HairStyle.Short, HairStyle.LongTiedBack, HairStyle.Medium };
            BeardStyle[] beards = { BeardStyle.Stubble, BeardStyle.ShortBeard, BeardStyle.FullBeard,
                BeardStyle.CleanShaven, BeardStyle.FullBeard, BeardStyle.Stubble };
            string[] helmetOptions = { "spiked_helmet_a", "spiked_helmet_b", "bronze_spiked_helmet",
                "black_guard_helmet", "spiked_helmet_a", "spiked_helmet_b" };
            string[] torsoOptions = { "scavenger_vest", "armored_torso", "scavenger_vest",
                "armored_torso", "scavenger_vest", "armored_torso" };
            string[] gloveOptions = { "leather_glove_a", "leather_glove_b", "leather_glove_a",
                "leather_glove_b", "leather_glove_a", "leather_glove_b" };

            for (int i = 0; i < characters.Length; i++)
            {
                int number = i + 1;
                var random = new System.Random(842091 + i * 7919);
                var root = new GameObject("Character " + number + " - " + families[i]);
                root.transform.position = new Vector3(i * 3f, 0f, 0f);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.name = "Creator body";
                visual.transform.SetParent(root.transform, false);
                var appearance = root.AddComponent<CharacterAppearanceController>();
                appearance.visualRoot = visual.transform;
                var cosmetics = root.AddComponent<CharacterCosmeticsController>();
                cosmetics.appearance = appearance;
                cosmetics.skeletonRoot = visual.transform;
                cosmetics.catalog = catalog;
                appearance.Initialize();

                var data = new CharacterAppearanceData();
                CharacterAppearanceRandomizer.Body(data, random);
                CharacterAppearanceRandomizer.Face(data, appearance, random);
                CharacterAppearanceRandomizer.Colors(data, random);
                CharacterAppearanceRandomizer.Hair(data, catalog, random);
                CharacterAppearanceRandomizer.Beard(data, catalog, random);
                data.hairStyle = hairs[i];
                data.hairVariant = random.Next(Mathf.Max(1, catalog.HairVariantCount(data.hairStyle)));
                data.beardStyle = beards[i];
                data.beardVariant = random.Next(Mathf.Max(1, catalog.BeardVariantCount(data.beardStyle)));
                appearance.Apply(data);
                // One fitted item per body part; each glove prefab contains a matched pair.
                cosmetics.headwear = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(
                    "Assets/CharacterCreator/ImportedDownloads/Items/" + helmetOptions[i] + ".asset");
                cosmetics.torso = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(
                    "Assets/CharacterCreator/ImportedDownloads/Items/" + torsoOptions[i] + ".asset");
                cosmetics.legs = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(
                    "Assets/CharacterCreator/ImportedDownloads/Items/ragged_skirt_" + (char)('a' + i % 3) + ".asset");
                cosmetics.feet = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(
                    "Assets/CharacterCreator/ImportedDownloads/Items/wrapped_boot.asset");
                cosmetics.hands = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(
                    "Assets/CharacterCreator/ImportedDownloads/Items/" + gloveOptions[i] + ".asset");
                cosmetics.outerArmor = null;
                if (!cosmetics.headwear || !cosmetics.torso || !cosmetics.legs || !cosmetics.feet || !cosmetics.hands)
                    throw new InvalidOperationException("Character " + number + " has an incomplete outfit.");
                cosmetics.Refresh();
                characters[i] = root;

                report.Append("## Character ").Append(number).Append(" — ").Append(families[i]).AppendLine();
                report.Append("Hair: ").Append(catalog.ForHair(data.hairStyle, data.hairVariant)?.name ?? "none")
                    .Append("; beard: ").Append(catalog.ForBeard(data.beardStyle, data.beardVariant)?.name ?? "none").AppendLine();
                report.Append("Clothing: ").Append(cosmetics.headwear ? cosmetics.headwear.name + ", " : "")
                    .Append(cosmetics.torso ? cosmetics.torso.name + ", " : "")
                    .Append(cosmetics.legs ? cosmetics.legs.name + ", " : "")
                    .Append(cosmetics.feet ? cosmetics.feet.name + ", " : "")
                    .Append(cosmetics.hands ? cosmetics.hands.name + ", " : "")
                    .Append(cosmetics.outerArmor ? cosmetics.outerArmor.name : "").AppendLine();
                report.Append("Body: height ").Append(data.height.ToString("F2"))
                    .Append(", weight ").Append(data.weight.ToString("F2"))
                    .Append(", muscularity ").Append(data.muscularity.ToString("F2"))
                    .AppendLine(". Face sliders, skin tone, and eye color were also randomized.\n");
                File.WriteAllText(Output + "/Snapshots/character_" + number.ToString("00") + ".json", JsonUtility.ToJson(data, true));
            }

            EditorSceneManager.SaveScene(scene, Output + "/RandomCharacters.unity");
            for (int i = 0; i < characters.Length; i++)
            {
                foreach (var other in characters) other.SetActive(other == characters[i]);
                var center = characters[i].transform.position + new Vector3(0f, 1.04f, 0f);
                Render(camera, center + new Vector3(0f, .08f, 3.6f), center,
                    Output + "/Snapshots/character_" + (i + 1).ToString("00") + "_front.png");
                Render(camera, center + new Vector3(3.6f, .08f, 0f), center,
                    Output + "/Snapshots/character_" + (i + 1).ToString("00") + "_side.png");
                camera.orthographicSize = .45f;
                var head = characters[i].transform.position + new Vector3(0f, 1.65f, 0f);
                Render(camera, head + new Vector3(0f, .02f, 2.4f), head,
                    Output + "/Snapshots/character_" + (i + 1).ToString("00") + "_head.png");
                camera.orthographicSize = 1.13f;
                RenderPose(camera, characters[i], center, i + 1);
            }
            foreach (var character in characters) character.SetActive(true);
            camera.transform.position = new Vector3(7.5f, 1.5f, 13f);
            camera.transform.LookAt(new Vector3(7.5f, 1f, 0f));
            camera.orthographicSize = 5.5f;
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Output + "/Snapshots/README.md", report.ToString());
            AssetDatabase.Refresh();
            if (mode == NewSceneMode.Additive)
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }
            Debug.Log("Random character showcase: 6 characters and 24 fitting snapshots saved to " + Output);
        }

        static void RenderPose(Camera camera, GameObject character, Vector3 center, int number)
        {
            Transform left = null, right = null;
            foreach (var bone in character.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "CC_Base_L_Upperarm_050") left = bone;
                if (bone.name == "CC_Base_R_Upperarm_074") right = bone;
            }
            if (!left || !right) return;
            var leftRotation = left.localRotation;
            var rightRotation = right.localRotation;
            try
            {
                left.Rotate(Vector3.forward, 55f, Space.World);
                right.Rotate(Vector3.forward, -55f, Space.World);
                Render(camera, center + new Vector3(0f, .08f, 3.6f), center,
                    Output + "/Snapshots/character_" + number.ToString("00") + "_pose.png");
            }
            finally { left.localRotation = leftRotation; right.localRotation = rightRotation; }
        }

        static void Render(Camera camera, Vector3 position, Vector3 target, string path)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);
            var texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            camera.targetTexture = texture;
            try
            {
                texture.Create();
                camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
