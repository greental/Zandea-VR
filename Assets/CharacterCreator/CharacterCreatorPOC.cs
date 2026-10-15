using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KenshiVR.CharacterCreator
{
    public sealed class CharacterCreatorPOC : MonoBehaviour
    {
        public CharacterAppearanceController character;
        public Transform turntable;
        public Canvas canvas;
        CharacterAppearanceData current;
        Font font;
        RectTransform content;
        string PresetPath => Path.Combine(Application.persistentDataPath, "character-creator-preset.json");

        void Start()
        {
            if (!character) { Debug.LogError("Character creator needs a character.", this); return; }
            current = character.Data;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!FindFirstObjectByType<EventSystem>())
            {
                var events = new GameObject("UI Event System", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform);
            }
            BuildUI();
        }

        void BuildUI()
        {
            var root = new GameObject("Character Creator World UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            root.transform.position = new Vector3(1.55f, 1.45f, 1.4f);
            root.transform.rotation = Quaternion.Euler(0, -28, 0);
            root.transform.localScale = Vector3.one * .0015f;
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(650, 1250);
            var bg = root.AddComponent<Image>(); bg.color = new Color(.08f, .09f, .11f, .94f);
            content = rect;
            AddText("CHARACTER CREATOR", 0, 42, 26);
            var properties = new[] { AppearanceProperty.Height, AppearanceProperty.Weight, AppearanceProperty.Muscularity,
                AppearanceProperty.NoseWidth, AppearanceProperty.JawWidth, AppearanceProperty.EarSize,
                AppearanceProperty.ForeheadShape, AppearanceProperty.EyeSize, AppearanceProperty.EyeSpacing };
            for (int i = 0; i < properties.Length; i++) AddSlider(properties[i], 62 + i * 72);
            AddColor("Skin color", AppearanceProperty.SkinColor, 724);
            AddColor("Eye color", AppearanceProperty.EyeColor, 804);
            AddButton("Rotate left", 894, 0, () => Rotate(-25));
            AddButton("Rotate right", 894, 1, () => Rotate(25));
            AddButton("Randomize", 948, 0, Randomize);
            AddButton("Reset", 948, 1, () => SetData(new CharacterAppearanceData()));
            AddButton("Save preset", 1002, 0, Save);
            AddButton("Load preset", 1002, 1, Load);
            AddButton("Next hair", 1056, 0, CycleHair);
            AddButton("Next beard", 1056, 1, CycleBeard);
            AddButton("Match outfit", 1110, 0, MatchOutfit);
        }

        RectTransform Item(string name, float y, float height, float x = 0, float width = 600)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(content, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
        Text AddText(string text, float y, float height, int size = 19)
        {
            var go = Item(text, y, height).gameObject;
            var label = go.AddComponent<Text>(); label.font = font; label.fontSize = size; label.color = Color.white;
            label.text = text; label.alignment = TextAnchor.MiddleLeft; return label;
        }
        void AddSlider(AppearanceProperty property, float y)
        {
            bool works = character.IsSupported(property);
            string label = property + (works ? "" : "  •  MORPH REQUIRED");
            var text = AddText(label, y, 27); if (!works) text.color = new Color(.75f, .65f, .55f);
            var track = Item(property + " slider", y + 32, 20).gameObject;
            var image = track.AddComponent<Image>(); image.color = new Color(.3f, .33f, .36f);
            var slider = track.AddComponent<Slider>(); slider.targetGraphic = image;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(track.transform, false);
            var fillRect = (RectTransform)fill.transform; fillRect.anchorMin = new Vector2(0, 0); fillRect.anchorMax = new Vector2(1, 1); fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = new Color(.78f, .59f, .32f);
            slider.fillRect = fillRect; slider.minValue = property == AppearanceProperty.Height ? .8f : 0f;
            slider.maxValue = property == AppearanceProperty.Height ? 1.2f : 1f;
            slider.SetValueWithoutNotify(Get(property)); slider.interactable = works;
            slider.onValueChanged.AddListener(value => { Set(property, value); character.Apply(current); });
        }
        void AddColor(string label, AppearanceProperty property, float y)
        {
            AddText(label, y, 25);
            string[] channels = { "R", "G", "B" };
            for (int i = 0; i < 3; i++)
            {
                int channel = i;
                var go = Item(label + channels[i], y + 35, 20, (i - 1) * 205, 185).gameObject;
                var image = go.AddComponent<Image>(); image.color = Color.gray;
                var slider = go.AddComponent<Slider>(); slider.targetGraphic = image;
                var knob = new GameObject("Handle", typeof(RectTransform), typeof(Image)); knob.transform.SetParent(go.transform, false);
                var rect = (RectTransform)knob.transform; rect.sizeDelta = new Vector2(16, 28);
                knob.GetComponent<Image>().color = Color.white; slider.handleRect = rect;
                slider.SetValueWithoutNotify(property == AppearanceProperty.SkinColor ? current.skinColor[channel] : current.eyeColor[channel]);
                slider.interactable = character.IsSupported(property);
                slider.onValueChanged.AddListener(value => { var color = property == AppearanceProperty.SkinColor ? current.skinColor : current.eyeColor; color[channel] = value;
                    if (property == AppearanceProperty.SkinColor) current.skinColor = color; else current.eyeColor = color; character.Apply(current); });
            }
        }
        void AddButton(string title, float y, int column, Action action)
        {
            var go = Item(title, y, 44, column == 0 ? -153 : 153, 290).gameObject;
            var image = go.AddComponent<Image>(); image.color = new Color(.28f, .34f, .39f);
            var button = go.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => action());
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)); label.transform.SetParent(go.transform, false);
            var rect = (RectTransform)label.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = label.GetComponent<Text>(); text.font = font; text.fontSize = 19; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.text = title;
        }
        float Get(AppearanceProperty property) => property switch {
            AppearanceProperty.Height => current.height, AppearanceProperty.Weight => current.weight,
            AppearanceProperty.Muscularity => current.muscularity, AppearanceProperty.NoseWidth => current.noseWidth,
            AppearanceProperty.JawWidth => current.jawWidth, AppearanceProperty.EarSize => current.earSize,
            AppearanceProperty.ForeheadShape => current.foreheadShape, AppearanceProperty.EyeSize => current.eyeSize,
            AppearanceProperty.EyeSpacing => current.eyeSpacing, AppearanceProperty.NoseTip => current.noseTip,
            AppearanceProperty.EyebrowHeight => current.eyebrowHeight,
            AppearanceProperty.EyebrowThickness => current.eyebrowThickness,
            AppearanceProperty.EyebrowAngle => current.eyebrowAngle,
            AppearanceProperty.EyebrowSpacing => current.eyebrowSpacing,
            AppearanceProperty.EyeShape => current.eyeShape, AppearanceProperty.EyeTilt => current.eyeTilt,
            AppearanceProperty.MouthWidth => current.mouthWidth, AppearanceProperty.LipFullness => current.lipFullness,
            _ => .5f };
        void Set(AppearanceProperty property, float value)
        {
            switch (property) {
                case AppearanceProperty.Height: current.height = value; break;
                case AppearanceProperty.Weight: current.weight = value; break;
                case AppearanceProperty.Muscularity: current.muscularity = value; break;
                case AppearanceProperty.NoseWidth: current.noseWidth = value; break;
                case AppearanceProperty.JawWidth: current.jawWidth = value; break;
                case AppearanceProperty.EarSize: current.earSize = value; break;
                case AppearanceProperty.ForeheadShape: current.foreheadShape = value; break;
                case AppearanceProperty.EyeSize: current.eyeSize = value; break;
                case AppearanceProperty.EyeSpacing: current.eyeSpacing = value; break;
                case AppearanceProperty.NoseTip: current.noseTip = value; break;
                case AppearanceProperty.EyebrowHeight: current.eyebrowHeight = value; break;
                case AppearanceProperty.EyebrowThickness: current.eyebrowThickness = value; break;
                case AppearanceProperty.EyebrowAngle: current.eyebrowAngle = value; break;
                case AppearanceProperty.EyebrowSpacing: current.eyebrowSpacing = value; break;
                case AppearanceProperty.EyeShape: current.eyeShape = value; break;
                case AppearanceProperty.EyeTilt: current.eyeTilt = value; break;
                case AppearanceProperty.MouthWidth: current.mouthWidth = value; break;
                case AppearanceProperty.LipFullness: current.lipFullness = value; break;
            }
        }
        void Rotate(float degrees) { if (turntable) turntable.Rotate(0, degrees, 0, Space.World); }
        void SetData(CharacterAppearanceData data) { current = data; character.Apply(current); if (canvas) Destroy(canvas.gameObject); BuildUI(); }
        void Randomize()
        {
            var random = new System.Random(Guid.NewGuid().GetHashCode());
            var data = new CharacterAppearanceData();
            var cosmetics = character.GetComponent<CharacterCosmeticsController>();
            CharacterAppearanceRandomizer.Body(data, random);
            CharacterAppearanceRandomizer.Face(data, character, random);
            CharacterAppearanceRandomizer.Colors(data, random);
            CharacterAppearanceRandomizer.Hair(data, cosmetics ? cosmetics.catalog : null, random);
            CharacterAppearanceRandomizer.Beard(data, cosmetics ? cosmetics.catalog : null, random);
            SetData(data);
        }
        void CycleHair()
        {
            var cosmetics = character.GetComponent<CharacterCosmeticsController>();
            var catalog = cosmetics ? cosmetics.catalog : null;
            if (!catalog) return;
            for (int i = 0; i < 12; i++)
            {
                int count = catalog.HairVariantCount(current.hairStyle);
                if (current.hairStyle != HairStyle.Bald && current.hairVariant + 1 < count) current.hairVariant++;
                else { current.hairStyle = (HairStyle)(((int)current.hairStyle + 1) % Enum.GetValues(typeof(HairStyle)).Length); current.hairVariant = 0; }
                if (current.hairStyle == HairStyle.Bald || catalog.HairVariantCount(current.hairStyle) > 0) break;
            }
            character.Apply(current);
        }
        void CycleBeard()
        {
            var cosmetics = character.GetComponent<CharacterCosmeticsController>();
            var catalog = cosmetics ? cosmetics.catalog : null;
            if (!catalog) return;
            for (int i = 0; i < 12; i++)
            {
                int count = catalog.BeardVariantCount(current.beardStyle);
                if (current.beardStyle != BeardStyle.CleanShaven && current.beardVariant + 1 < count) current.beardVariant++;
                else { current.beardStyle = (BeardStyle)(((int)current.beardStyle + 1) % Enum.GetValues(typeof(BeardStyle)).Length); current.beardVariant = 0; }
                if (current.beardStyle == BeardStyle.CleanShaven || catalog.BeardVariantCount(current.beardStyle) > 0) break;
            }
            character.Apply(current);
        }
        void MatchOutfit()
        {
            var cosmetics = character.GetComponent<CharacterCosmeticsController>();
            CharacterClothingMatcher.Match(cosmetics);
        }
        void Save() { File.WriteAllText(PresetPath, JsonUtility.ToJson(current, true)); Debug.Log("Saved character preset: " + PresetPath); }
        void Load() { if (File.Exists(PresetPath)) SetData(JsonUtility.FromJson<CharacterAppearanceData>(File.ReadAllText(PresetPath))); else Debug.LogWarning("No character preset at " + PresetPath); }
    }
}
