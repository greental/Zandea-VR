using System;
using System.Collections.Generic;
using KenshiVR.CharacterCreator;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    [CustomEditor(typeof(CharacterAppearanceController))]
    public sealed class CharacterAppearanceControllerEditor : UnityEditor.Editor
    {
        static readonly (string field, AppearanceProperty property)[] Body = {
            ("height", AppearanceProperty.Height), ("weight", AppearanceProperty.Weight),
            ("muscularity", AppearanceProperty.Muscularity) };
        static readonly (string field, AppearanceProperty property)[] Face = {
            ("noseWidth", AppearanceProperty.NoseWidth), ("noseTip", AppearanceProperty.NoseTip),
            ("jawWidth", AppearanceProperty.JawWidth), ("earSize", AppearanceProperty.EarSize),
            ("foreheadShape", AppearanceProperty.ForeheadShape), ("eyeSize", AppearanceProperty.EyeSize),
            ("eyeSpacing", AppearanceProperty.EyeSpacing), ("eyebrowHeight", AppearanceProperty.EyebrowHeight),
            ("eyebrowThickness", AppearanceProperty.EyebrowThickness), ("eyebrowAngle", AppearanceProperty.EyebrowAngle),
            ("eyebrowSpacing", AppearanceProperty.EyebrowSpacing), ("eyeShape", AppearanceProperty.EyeShape),
            ("eyeTilt", AppearanceProperty.EyeTilt), ("mouthWidth", AppearanceProperty.MouthWidth),
            ("lipFullness", AppearanceProperty.LipFullness) };
        static readonly (string field, AppearanceProperty property)[] Colors = {
            ("skinColor", AppearanceProperty.SkinColor), ("eyeColor", AppearanceProperty.EyeColor) };

        void OnEnable() => Undo.undoRedoPerformed += Reapply;
        void OnDisable() => Undo.undoRedoPerformed -= Reapply;
        void Reapply()
        {
            if (target is CharacterAppearanceController controller && controller)
            { controller.Preview(); SceneView.RepaintAll(); }
        }
        public override void OnInspectorGUI()
        {
            var controller = (CharacterAppearanceController)target;
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("visualRoot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("characterController"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("vrBody"));
            bool referencesChanged = EditorGUI.EndChangeCheck();
            if (referencesChanged) serializedObject.ApplyModifiedProperties();
            controller.Initialize();
            if (referencesChanged) controller.Preview();
            RandomButton("Randomize all", () => Randomize(controller, "all"));
            var data = controller.Data;
            CommitAppearance(controller, data, Group("Body", data, Body, controller));
            if (RandomButton("Randomize body", () => Randomize(controller, "body"))) data = controller.Data;
            CommitAppearance(controller, data, Group("Face", data, Face, controller));
            if (RandomButton("Randomize face", () => Randomize(controller, "face"))) data = controller.Data;
            CommitAppearance(controller, data, Group("Colors", data, Colors, controller));
            if (RandomButton("Randomize skin and eye colors", () => Randomize(controller, "colors"))) data = controller.Data;
            if (RandomButton("Randomize skin tone", () => Randomize(controller, "skin"))) data = controller.Data;
            if (RandomButton("Randomize eye color", () => Randomize(controller, "eyes"))) data = controller.Data;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Hair", EditorStyles.boldLabel);
            DrawHairType(controller, data);
            data.hairColor = EditorGUILayout.ColorField("Hair Color", data.hairColor);
            CommitAppearance(controller, data, EditorGUI.EndChangeCheck());
            if (RandomButton("Randomize hair", () => Randomize(controller, "hair"))) data = controller.Data;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Beard", EditorStyles.boldLabel);
            DrawBeardType(controller, data);
            data.linkBeardColor = EditorGUILayout.Toggle("Link Beard Color", data.linkBeardColor);
            if (!data.linkBeardColor) data.beardColor = EditorGUILayout.ColorField("Beard Color", data.beardColor);
            CommitAppearance(controller, data, EditorGUI.EndChangeCheck());
            RandomButton("Randomize beard", () => Randomize(controller, "beard"));
            DrawExpression(controller);
            DrawClothing(controller);
            EditorGUILayout.Space();
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("morphBindings"), true);
            if (EditorGUI.EndChangeCheck()) { serializedObject.ApplyModifiedProperties(); controller.Preview(); }
            if (GUILayout.Button("Reapply preview")) Reapply();
            if (GUILayout.Button("Use current size as 100% baseline"))
            { Undo.RecordObject(controller, "Set character size baseline"); controller.ResetBaseline(); SceneView.RepaintAll(); }
        }

        static void DrawHairType(CharacterAppearanceController controller, CharacterAppearanceData data)
        {
            var cosmetics = controller.GetComponent<CharacterCosmeticsController>();
            var catalog = cosmetics ? cosmetics.catalog : null;
            if (!catalog)
            {
                data.hairStyle = (HairStyle)EditorGUILayout.EnumPopup("Hair Style", data.hairStyle);
                return;
            }
            var choices = new List<(string label, HairStyle style, int variant)>
            { ("Bald", HairStyle.Bald, 0) };
            AddHairChoices(choices, catalog, HairStyle.Short);
            AddHairChoices(choices, catalog, HairStyle.Medium);
            AddHairChoices(choices, catalog, HairStyle.LongTiedBack);
            int selected = choices.FindIndex(choice => choice.style == data.hairStyle &&
                choice.variant == data.hairVariant);
            if (selected < 0) selected = 0;
            int next = EditorGUILayout.Popup("Hair Type", selected,
                choices.ConvertAll(choice => choice.label).ToArray());
            data.hairStyle = choices[next].style;
            data.hairVariant = choices[next].variant;
        }

        static void AddHairChoices(List<(string label, HairStyle style, int variant)> choices,
            CosmeticCatalog catalog, HairStyle style)
        {
            int count = catalog.HairVariantCount(style);
            for (int variant = 0; variant < count; variant++)
            {
                var item = catalog.ForHair(style, variant);
                if (item) choices.Add((ObjectNames.NicifyVariableName(item.name), style, variant));
            }
        }

        static void DrawBeardType(CharacterAppearanceController controller, CharacterAppearanceData data)
        {
            var cosmetics = controller.GetComponent<CharacterCosmeticsController>();
            var catalog = cosmetics ? cosmetics.catalog : null;
            if (!catalog)
            {
                data.beardStyle = (BeardStyle)EditorGUILayout.EnumPopup("Beard Style", data.beardStyle);
                return;
            }
            var choices = new List<(string label, BeardStyle style, int variant)>
            { ("Clean Shaven", BeardStyle.CleanShaven, 0) };
            foreach (BeardStyle style in new[] { BeardStyle.Stubble, BeardStyle.ShortBeard, BeardStyle.FullBeard })
                for (int variant = 0; variant < catalog.BeardVariantCount(style); variant++)
                {
                    var item = catalog.ForBeard(style, variant);
                    if (item) choices.Add((ObjectNames.NicifyVariableName(item.name), style, variant));
                }
            int selected = choices.FindIndex(choice => choice.style == data.beardStyle &&
                choice.variant == data.beardVariant);
            if (selected < 0) selected = 0;
            int next = EditorGUILayout.Popup("Beard Type", selected,
                choices.ConvertAll(choice => choice.label).ToArray());
            data.beardStyle = choices[next].style;
            data.beardVariant = choices[next].variant;
        }
        static void CommitAppearance(CharacterAppearanceController controller, CharacterAppearanceData data, bool changed)
        {
            if (!changed) return;
            Undo.RecordObject(controller, "Edit character appearance");
            if (controller.visualRoot) Undo.RecordObject(controller.visualRoot, "Preview character appearance");
            if (controller.characterController) Undo.RecordObject(controller.characterController, "Preview character appearance");
            if (controller.vrBody) Undo.RecordObject(controller.vrBody, "Preview character appearance");
            controller.Apply(data);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            EditorUtility.SetDirty(controller);
            SceneView.RepaintAll();
        }
        static bool Group(string title, CharacterAppearanceData data, (string field, AppearanceProperty property)[] fields,
            CharacterAppearanceController controller)
        {
            EditorGUILayout.Space(); EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            foreach (var entry in fields)
            {
                bool works = controller.IsSupported(entry.property);
                var field = typeof(CharacterAppearanceData).GetField(entry.field);
                using (new EditorGUI.DisabledScope(!works))
                {
                    if (entry.property == AppearanceProperty.SkinColor || entry.property == AppearanceProperty.EyeColor)
                        field.SetValue(data, EditorGUILayout.ColorField(ObjectNames.NicifyVariableName(entry.field), (Color)field.GetValue(data)));
                    else field.SetValue(data, EditorGUILayout.Slider(ObjectNames.NicifyVariableName(entry.field),
                        (float)field.GetValue(data), entry.property == AppearanceProperty.Height ? .8f : 0f,
                        entry.property == AppearanceProperty.Height ? 1.2f : 1f));
                }
                if (!works) EditorGUILayout.LabelField("  " + entry.property + ": awaiting matching morph/material", EditorStyles.miniLabel);
            }
            return EditorGUI.EndChangeCheck();
        }
        static bool RandomButton(string label, Action action)
        {
            if (!GUILayout.Button(label)) return false;
            action();
            return true;
        }
        static void Randomize(CharacterAppearanceController controller, string group)
        {
            var random = new System.Random(Guid.NewGuid().GetHashCode());
            var cosmetics = controller.GetComponent<CharacterCosmeticsController>();
            var data = controller.Data;
            Undo.RecordObject(controller, "Randomize character " + group);
            if (controller.visualRoot) Undo.RecordObject(controller.visualRoot, "Randomize character size");
            if (controller.characterController) Undo.RecordObject(controller.characterController, "Randomize capsule size");
            if (controller.vrBody) Undo.RecordObject(controller.vrBody, "Randomize VR body size");
            if (group == "all" || group == "body") CharacterAppearanceRandomizer.Body(data, random);
            if (group == "all" || group == "face") CharacterAppearanceRandomizer.Face(data, controller, random);
            if (group == "all" || group == "colors") CharacterAppearanceRandomizer.Colors(data, random);
            if (group == "skin") CharacterAppearanceRandomizer.Skin(data, random);
            if (group == "eyes") CharacterAppearanceRandomizer.Eyes(data, random);
            if (group == "all" || group == "hair") CharacterAppearanceRandomizer.Hair(data, cosmetics ? cosmetics.catalog : null, random);
            if (group == "all" || group == "beard") CharacterAppearanceRandomizer.Beard(data, cosmetics ? cosmetics.catalog : null, random);
            controller.Apply(data);
            if (cosmetics && (group == "all" || group == "clothes")) RandomizeClothes(cosmetics, random, null);
            if (group == "all") RandomizeExpression(controller, random);
            EditorUtility.SetDirty(controller);
            if (cosmetics) EditorUtility.SetDirty(cosmetics);
            SceneView.RepaintAll();
        }
        static void RandomizeExpression(CharacterAppearanceController controller, System.Random random)
        {
            var expression = controller.GetComponent<NPCFacialExpressionController>();
            if (!expression) return;
            Undo.RecordObject(expression, "Randomize expression");
            expression.previewExpression = (ExpressionType)random.Next(0, 7);
            expression.previewIntensity = Mathf.Lerp(.6f, 1f, (float)random.NextDouble());
            expression.PreviewInEditor();
            EditorUtility.SetDirty(expression);
        }
        static bool HasClothingAssets()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AppearanceItemDefinition"))
            {
                var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item && item.prefab && item.slot >= AppearanceSlot.Headwear) return true;
            }
            return false;
        }
        static void RandomizeClothes(CharacterCosmeticsController cosmetics, System.Random random, AppearanceSlot? only)
        {
            if (!only.HasValue && cosmetics.catalog && cosmetics.catalog.wardrobeItems != null && cosmetics.catalog.wardrobeItems.Length > 0)
            {
                Undo.RecordObject(cosmetics, "Match clothing");
                CharacterClothingMatcher.Match(cosmetics, random);
                EditorUtility.SetDirty(cosmetics);
                SceneView.RepaintAll();
                return;
            }
            var choices = new Dictionary<AppearanceSlot, List<AppearanceItemDefinition>>();
            foreach (string guid in AssetDatabase.FindAssets("t:AppearanceItemDefinition"))
            {
                var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (!item || !item.prefab || item.slot < AppearanceSlot.Headwear) continue;
                if (!choices.TryGetValue(item.slot, out var list)) choices[item.slot] = list = new List<AppearanceItemDefinition>();
                list.Add(item);
            }
            Undo.RecordObject(cosmetics, "Randomize clothing");
            foreach (AppearanceSlot slot in Enum.GetValues(typeof(AppearanceSlot)))
            {
                if (slot < AppearanceSlot.Headwear || (only.HasValue && only.Value != slot)) continue;
                if (!choices.TryGetValue(slot, out var list) || list.Count == 0) continue;
                float total = 1f;
                foreach (var item in list) total += Mathf.Max(0f, item.randomWeight);
                float draw = (float)random.NextDouble() * total;
                AppearanceItemDefinition selected = null;
                foreach (var item in list)
                {
                    draw -= Mathf.Max(0f, item.randomWeight);
                    if (draw <= 0f) { selected = item; break; }
                }
                switch (slot)
                {
                    case AppearanceSlot.Headwear: cosmetics.headwear = selected; break;
                    case AppearanceSlot.Torso: cosmetics.torso = selected; break;
                    case AppearanceSlot.Legs: cosmetics.legs = selected; break;
                    case AppearanceSlot.Feet: cosmetics.feet = selected; break;
                    case AppearanceSlot.Hands: cosmetics.hands = selected; break;
                    case AppearanceSlot.OuterArmor: cosmetics.outerArmor = selected; break;
                }
            }
            cosmetics.Refresh();
            EditorUtility.SetDirty(cosmetics);
            SceneView.RepaintAll();
        }
        static void DrawExpression(CharacterAppearanceController controller)
        {
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Expressions", EditorStyles.boldLabel);
            var expression = controller.GetComponent<NPCFacialExpressionController>();
            if (!expression)
            {
                if (GUILayout.Button("Add expression controller"))
                { expression = Undo.AddComponent<NPCFacialExpressionController>(controller.gameObject); expression.visualRoot = controller.visualRoot; }
                return;
            }
            var so = new SerializedObject(expression); so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("previewExpression"));
            EditorGUILayout.PropertyField(so.FindProperty("previewIntensity"));
            so.ApplyModifiedProperties();
            RandomButton("Randomize expression", () => RandomizeExpression(controller,
                new System.Random(Guid.NewGuid().GetHashCode())));
            if (expression.previewExpression != ExpressionType.Neutral && !expression.HasMorph(expression.previewExpression))
                EditorGUILayout.LabelField("  Expression morph asset required", EditorStyles.miniLabel);
        }
        static void DrawClothing(CharacterAppearanceController controller)
        {
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Clothing and cosmetics", EditorStyles.boldLabel);
            var cosmetics = controller.GetComponent<CharacterCosmeticsController>();
            if (!cosmetics)
            {
                if (GUILayout.Button("Add clothing and cosmetics slots"))
                { cosmetics = Undo.AddComponent<CharacterCosmeticsController>(controller.gameObject);
                  cosmetics.appearance = controller; cosmetics.skeletonRoot = controller.visualRoot;
                  cosmetics.catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>("Assets/CharacterCreator/CosmeticCatalog.asset"); }
                return;
            }
            var so = new SerializedObject(cosmetics); so.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(so.FindProperty("skeletonRoot"));
            EditorGUILayout.PropertyField(so.FindProperty("catalog"));
            var data = controller.Data;
            if (cosmetics.catalog && data.hairStyle != HairStyle.Bald && !cosmetics.catalog.ForHair(data.hairStyle))
                EditorGUILayout.LabelField("  Hair style awaits a prefab in the catalog", EditorStyles.miniLabel);
            if (cosmetics.catalog && data.beardStyle != BeardStyle.CleanShaven && !cosmetics.catalog.ForBeard(data.beardStyle))
                EditorGUILayout.LabelField("  Beard style awaits a prefab in the catalog", EditorStyles.miniLabel);
            string[] slots = { "headwear", "torso", "legs", "feet", "hands", "outerArmor" };
            AppearanceSlot[] kinds = { AppearanceSlot.Headwear, AppearanceSlot.Torso, AppearanceSlot.Legs,
                AppearanceSlot.Feet, AppearanceSlot.Hands, AppearanceSlot.OuterArmor };
            bool hasClothingAssets = HasClothingAssets();
            if (!hasClothingAssets)
                EditorGUILayout.LabelField("Add clothing item prefabs to enable outfit randomization.", EditorStyles.miniLabel);
            for (int i = 0; i < slots.Length; i++)
            {
                DrawItemSlot(so.FindProperty(slots[i]), kinds[i]);
                var kind = kinds[i];
                using (new EditorGUI.DisabledScope(!hasClothingAssets))
                    RandomButton("Randomize " + kind, () => RandomizeClothes(cosmetics,
                        new System.Random(Guid.NewGuid().GetHashCode()), kind));
            }
            using (new EditorGUI.DisabledScope(!hasClothingAssets))
                RandomButton("Randomize all clothing", () => RandomizeClothes(cosmetics,
                    new System.Random(Guid.NewGuid().GetHashCode()), null));
            bool changed = EditorGUI.EndChangeCheck(); so.ApplyModifiedProperties();
            if (changed) cosmetics.Refresh();
        }

        static void DrawItemSlot(SerializedProperty property, AppearanceSlot slot)
        {
            var items = new List<AppearanceItemDefinition> { null };
            foreach (string guid in AssetDatabase.FindAssets("t:AppearanceItemDefinition",
                new[] { "Assets/CharacterCreator/ImportedDownloads/Items" }))
            {
                var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item && item.prefab && item.slot == slot && !items.Contains(item)) items.Add(item);
            }
            items.Sort(1, items.Count - 1, Comparer<AppearanceItemDefinition>.Create(
                (left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase)));
            var current = property.objectReferenceValue as AppearanceItemDefinition;
            if (current && !items.Contains(current)) items.Add(current);
            int selected = items.IndexOf(current);
            if (selected < 0) selected = 0;
            string[] names = items.ConvertAll(item => item ? ObjectNames.NicifyVariableName(item.name) : "None").ToArray();
            int next = EditorGUILayout.Popup(ObjectNames.NicifyVariableName(property.name), selected, names);
            if (next != selected) property.objectReferenceValue = items[next];
            var displayed = property.objectReferenceValue;
            var dropped = EditorGUILayout.ObjectField("Drop Item or Prefab", displayed,
                typeof(UnityEngine.Object), false);
            if (dropped != displayed && dropped is AppearanceItemDefinition droppedItem)
                property.objectReferenceValue = droppedItem;
            else if (dropped != displayed && dropped is GameObject droppedPrefab)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(droppedPrefab) ?? droppedPrefab;
                var match = items.Find(item => item && (item.prefab == droppedPrefab || item.prefab == source));
                if (match) property.objectReferenceValue = match;
                else Debug.LogWarning("No " + slot + " item definition uses prefab " + droppedPrefab.name);
            }
            else if (!dropped && displayed) property.objectReferenceValue = null;
            var assigned = property.objectReferenceValue as AppearanceItemDefinition;
            if (assigned && assigned.slot != slot)
                EditorGUILayout.HelpBox(assigned.name + " is assigned to " + assigned.slot +
                    "; it cannot spawn in the " + slot + " slot.", MessageType.Warning);
            else if (assigned && !assigned.prefab)
                EditorGUILayout.HelpBox(assigned.name + " has no prefab to spawn.", MessageType.Warning);
        }
    }
}
