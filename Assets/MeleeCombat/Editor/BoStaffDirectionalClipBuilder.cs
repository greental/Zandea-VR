using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BoStaffDirectionalClipBuilder
{
    public const string Folder = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffDirectional";
    public static readonly string[] Names = { "Overhead", "DiagonalRight", "HorizontalRight", "LowRight",
        "Thrust", "LowLeft", "HorizontalLeft", "DiagonalLeft" };

    [MenuItem("Tools/Melee Combat/Build directional Bo Staff clips")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/MeleeCombat/MocapTest/GeneratedVariants", "BoStaffDirectional");
        var swing = Source("HumanM@Attack2H01");
        var thrust = Source("HumanM@AttackPolearm01");
        Create("Overhead", swing, new Dictionary<string, float> {
            ["Left Arm Down-Up"] = .30f, ["Right Arm Down-Up"] = .30f,
            ["Chest Front-Back"] = -.10f, ["UpperChest Front-Back"] = -.07f });
        Create("DiagonalRight", swing, new Dictionary<string, float> {
            ["Chest Twist Left-Right"] = -.25f, ["Spine Twist Left-Right"] = -.12f,
            ["Right Arm Down-Up"] = .12f, ["Left Arm Front-Back"] = -.10f });
        Create("HorizontalRight", swing, new Dictionary<string, float> {
            ["Chest Twist Left-Right"] = .28f, ["Spine Twist Left-Right"] = .14f,
            ["Left Arm Down-Up"] = -.32f, ["Right Arm Down-Up"] = .14f,
            ["Right Shoulder Front-Back"] = .15f });
        Create("LowRight", swing, new Dictionary<string, float> {
            ["Spine Front-Back"] = .18f, ["Chest Front-Back"] = .18f,
            ["Left Arm Down-Up"] = -.38f, ["Right Arm Down-Up"] = -.14f,
            ["Chest Twist Left-Right"] = .20f });
        Create("Thrust", thrust, new Dictionary<string, float> {
            ["Chest Front-Back"] = -.07f,
            ["Left Shoulder Front-Back"] = .06f, ["Right Shoulder Front-Back"] = .06f,
            ["Left Arm Front-Back"] = .07f, ["Right Arm Front-Back"] = .07f });
        Create("LowLeft", swing, new Dictionary<string, float> {
            ["Spine Front-Back"] = .18f, ["Chest Front-Back"] = .18f,
            ["Left Arm Down-Up"] = -.12f, ["Right Arm Down-Up"] = -.38f,
            ["Chest Twist Left-Right"] = -.31f });
        Create("HorizontalLeft", swing, new Dictionary<string, float> {
            ["Chest Twist Left-Right"] = -.32f, ["Spine Twist Left-Right"] = -.16f,
            ["Left Arm Down-Up"] = .14f, ["Right Arm Down-Up"] = -.32f,
            ["Left Shoulder Front-Back"] = .16f });
        Create("DiagonalLeft", swing, new Dictionary<string, float> {
            ["Chest Twist Left-Right"] = -.26f, ["Spine Twist Left-Right"] = -.13f,
            ["Left Arm Down-Up"] = .23f, ["Right Arm Down-Up"] = .18f,
            ["Right Arm Front-Back"] = -.10f });
        AssetDatabase.SaveAssets();
        Debug.Log("BO_STAFF_DIRECTIONAL_CLIPS " + Names.Length);
    }
    public static AnimationClip Load(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/BoStaff_" + name + ".anim");
    static AnimationClip Source(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets(name + " t:AnimationClip"))
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid))
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            if (clip) return clip;
        }
        throw new InvalidOperationException("Missing imported clip " + name);
    }
    static void Create(string name, AnimationClip source, Dictionary<string, float> offsets)
    {
        var idle = Source("HumanM@CombatIdlePolearm01");
        var idleCurves = AnimationUtility.GetCurveBindings(idle)
            .Where(b => b.path == "")
            .ToDictionary(b => b.propertyName, b => AnimationUtility.GetEditorCurve(idle, b));
        string path = Folder + "/BoStaff_" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (!clip)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.name = "BoStaff_" + name;
        clip.frameRate = source.frameRate;
        clip.wrapMode = WrapMode.Once;
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var original = AnimationUtility.GetEditorCurve(source, binding);
            bool shaped = offsets.TryGetValue(binding.propertyName, out var amount);
            idleCurves.TryGetValue(binding.propertyName, out var idleCurve);
            bool grounded = IsLowerBody(binding.propertyName) && idleCurve != null;
            if (shaped || grounded)
            {
                var keys = new List<Keyframe>();
                int frames = Mathf.CeilToInt(source.length * source.frameRate);
                for (int frame = 0; frame <= frames; frame++)
                {
                    float t = Mathf.Min(source.length, frame / source.frameRate);
                    float u = t / source.length;
                    // Smooth windup and recovery preserve continuity with combat idle.
                    float envelope = Mathf.Sin(Mathf.PI * u);
                    envelope *= envelope;
                    float value = original.Evaluate(t);
                    if (grounded)
                    {
                        float idleTime = (t / source.length) * idle.length;
                        float idleWeight = binding.propertyName == "RootT.y" ? 1f : .72f;
                        value = Mathf.Lerp(value, idleCurve.Evaluate(idleTime), idleWeight);
                    }
                    if (shaped) value += amount * envelope;
                    keys.Add(new Keyframe(t, Mathf.Clamp(value, -1f, 1f)));
                }
                var changed = new AnimationCurve(keys.ToArray());
                for (int i = 0; i < changed.length; i++) changed.SmoothTangents(i, 0);
                AnimationUtility.SetEditorCurve(clip, binding, changed);
            }
            else AnimationUtility.SetEditorCurve(clip, binding, original);
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            AnimationUtility.SetObjectReferenceCurve(clip, binding,
                AnimationUtility.GetObjectReferenceCurve(source, binding));
        AnimationUtility.SetAnimationEvents(clip, AnimationUtility.GetAnimationEvents(source));
        EditorUtility.SetDirty(clip);
    }
    static bool IsLowerBody(string property) => property == "RootT.y" ||
        property.StartsWith("LeftFootT.") || property.StartsWith("RightFootT.") ||
        property.StartsWith("LeftFootQ.") || property.StartsWith("RightFootQ.") ||
        property.StartsWith("Left Upper Leg") || property.StartsWith("Right Upper Leg") ||
        property.StartsWith("Left Lower Leg") || property.StartsWith("Right Lower Leg") ||
        property.StartsWith("Left Foot ") || property.StartsWith("Right Foot ") ||
        property.StartsWith("Left Toes") || property.StartsWith("Right Toes");
}
