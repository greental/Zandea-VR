using System;
using System.IO;
using System.Linq;
using System.Text;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A request file lets an already-open editor perform the pass without replacing its scene.
[InitializeOnLoad]
public static class ArmedBanditPass
{
    const string Request = "Temp/ArmedBanditPass.request";
    const string Result = "Temp/ArmedBanditPass.result";
    const string Folder = "Assets/MeleeCombat/MocapTest/GeneratedVariants";
    static ArmedBanditPass() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string command;
        try { command = File.ReadAllText(Request).Trim(); File.Delete(Request); }
        catch (IOException) { return; }
        if (command == "refresh") { AssetDatabase.Refresh(); return; }
        try { Run(); } catch (Exception e) { File.WriteAllText(Result, e.ToString()); Debug.LogException(e); }
    }
    static float AlignGeneratedHilt(string variant, Transform model, Transform weapon, MocapStaffPreview grip)
    {
        var filter = model.GetComponentInChildren<MeshFilter>();
        if (!filter || !filter.sharedMesh) throw new Exception("Missing blade mesh: " + variant);
        var bounds = filter.sharedMesh.bounds;
        bool vertical = variant == "BroadCleaver";
        var hilt = bounds.center;
        if (vertical) hilt.y = bounds.max.y - bounds.size.y * .15f;
        else hilt.x = bounds.max.x - bounds.size.x * .15f;
        Vector3 wrist = grip.primaryHandIsLeft ? grip.leftGripLocal : grip.rightGripLocal;
        Vector3 delta = weapon.TransformPoint(wrist) - filter.transform.TransformPoint(hilt);
        model.position += delta;
        if (Vector3.Distance(filter.transform.TransformPoint(hilt), weapon.TransformPoint(wrist)) > .002f)
            throw new Exception("Blade hilt did not meet wrist: " + variant);
        return delta.magnitude;
    }
    [MenuItem("Zandea/Melee Combat/Apply armed bandit grip and contact pass")]
    public static void Run()
    {
        var report = new StringBuilder();
        foreach (var path in Directory.GetFiles(Folder, "StarvingBandit_*.prefab").OrderBy(p => p))
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Configure(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.AppendLine("CONFIGURED " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        const string legacyPath = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";
        var legacy = PrefabUtility.LoadPrefabContents(legacyPath);
        try
        {
            var brain = legacy.GetComponent<StarvingBandit>();
            var weapon = brain ? brain.staff : null;
            if (!weapon || !StaffAxisCalibration.TryMeasure(weapon.transform, out var positive, out var negative))
                throw new Exception("Legacy Bo Staff geometry unavailable");
            MeasureVisibleTips(weapon.transform, ref positive, ref negative);
            brain.positiveStrikingTip = Marker(weapon.transform, "PositiveStrikingTip", positive);
            brain.negativeStrikingTip = Marker(weapon.transform, "NegativeStrikingTip", negative);
            var capsule = weapon.GetComponent<CapsuleCollider>();
            if (!capsule) capsule = weapon.gameObject.AddComponent<CapsuleCollider>();
            var delta = positive - negative;
            int axis = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ?
                (Mathf.Abs(delta.x) > Mathf.Abs(delta.z) ? 0 : 2) :
                (Mathf.Abs(delta.y) > Mathf.Abs(delta.z) ? 1 : 2);
            capsule.direction = axis;
            capsule.center = (positive + negative) * .5f;
            capsule.height = delta.magnitude;
            capsule.radius = .045f;
            capsule.enabled = true;
            PrefabUtility.SaveAsPrefabAsset(legacy, legacyPath);
            report.AppendLine("CONFIGURED " + legacyPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(legacy); }
        AssetDatabase.SaveAssets();
        File.WriteAllText(Result, report.ToString());
        Debug.Log("ARMED_BANDIT_PASS_COMPLETE\n" + report);
    }
    public static void Configure(GameObject root)
    {
        var grip = root.GetComponentInChildren<MocapStaffPreview>(true);
        var driver = root.GetComponentInChildren<BanditMocapNpcDriver>(true);
        var brain = root.GetComponent<StarvingBandit>();
        if (!grip || !driver || !brain) return;
        if (!root.name.Contains("BoStaff") && grip.armedFingerPoseVersion == 0)
        {
            var fingers = grip.fingers;
            for (int i = 0; i < fingers.Length; i++)
                fingers[i].rotation = Quaternion.SlerpUnclamped(Quaternion.identity, fingers[i].rotation, 1.6f);
            grip.fingers = fingers;
            grip.armedFingerPoseVersion = 1;
        }
        if (grip.staff) ReorientGeneratedWeapon(root.name, grip, grip.staff.transform);
        driver.stabilizeAttackFeet = root.name.EndsWith("_SpikedMace");
        driver.presentLongBladeGuard = root.name.EndsWith("_LongBlade");
        var combatController = grip.GetComponent<Animator>().runtimeAnimatorController as AnimatorController;
        if (combatController && driver.useDirectionalStaffAttacks)
        {
            var states = combatController.layers[0].stateMachine.states.Select(s => s.state).ToDictionary(s => s.name);
            // The authored low directions crossed the torso and read as a retreating
            // floor sweep. Reuse the clean side strikes until a grounded low attack exists.
            states["AttackDirection3"].motion = states["AttackDirection2"].motion;
            states["AttackDirection5"].motion = states["AttackDirection6"].motion;
            states["AttackDirection3"].speed = states["AttackDirection2"].speed;
            states["AttackDirection5"].speed = states["AttackDirection6"].speed;
            EditorUtility.SetDirty(combatController);
        }
        // Clip selection belongs to ImportedCombatAnimationInstaller. This grip
        // pass must not replace its locomotion or directional attack states.
        var animator = grip.GetComponent<Animator>();
        var clip = grip.animations.FirstOrDefault(c => c && c.name.Contains(grip.secondaryHandGrips ?
            (root.name.Contains("BoStaff") || root.name.Contains("PoleBlade") ? "CombatIdlePolearm" : "CombatIdle2H") : "CombatIdle1H"));
        if (!clip) throw new Exception("Missing armed idle: " + root.name);
        // Sample on a disposable copy; never bake sampled transforms into the actual prefab.
        var sample = UnityEngine.Object.Instantiate(grip.gameObject);
        try
        {
            var sampleAnimator = sample.GetComponent<Animator>();
            sampleAnimator.enabled = true;
            clip.SampleAnimation(sample, clip.length * .25f);
            driver.armedGuardRotations = BanditMocapNpcDriver.GuardBones.Select(b => {
                var bone = sampleAnimator.GetBoneTransform(b);
                return bone ? bone.localRotation : Quaternion.identity;
            }).ToArray();
        }
        finally { UnityEngine.Object.DestroyImmediate(sample); }
        var scorer = root.GetComponent<BanditDuelStrikeDamage>();
        if (!scorer) scorer = root.AddComponent<BanditDuelStrikeDamage>();
        scorer.brain = brain;
        if (grip.staff)
        {
            FitWeapon(grip.staff);
            grip.staff.enabled = true;
            grip.driveVisualStaff = true;
            grip.staff.ownerRoot = brain.transform;
            var wrist = grip.primaryHandIsLeft ? grip.leftGripLocal : grip.rightGripLocal;
            var wristRotation = grip.primaryHandIsLeft ? grip.leftHandFromStaff : grip.rightHandFromStaff;
            grip.staff.grip = Marker(grip.staff.transform, "NPC held grip", wrist);
            grip.staff.grip.localRotation = wristRotation;
            grip.staff.npcGripTarget = Marker(grip.desiredStaff, "Mocap physical grip target", wrist);
            grip.staff.npcGripTarget.localRotation = wristRotation;
            var body = grip.staff.GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = false;
            if (StaffAxisCalibration.TryMeasure(grip.staff.transform, out var positive, out var negative))
            {
                MeasureVisibleTips(grip.staff.transform, ref positive, ref negative);
                grip.positiveTip = Marker(grip.staff.transform, "PositiveStrikingTip", positive);
                grip.negativeTip = Marker(grip.staff.transform, "NegativeStrikingTip", negative);
                brain.positiveStrikingTip = grip.positiveTip;
                brain.negativeStrikingTip = grip.negativeTip;
            }
        }
        var generated = root.GetComponent<NpcGeneratedWeapon>();
        if (generated && generated.weaponPrefab)
        {
            var path = AssetDatabase.GetAssetPath(generated.weaponPrefab);
            var weaponRoot = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var weapon = weaponRoot.GetComponent<PhysicalMeleeWeapon>();
                ReorientGeneratedWeapon(root.name, grip, weapon.transform);
                FitWeapon(weapon);
                var wrist = grip.primaryHandIsLeft ? grip.leftGripLocal : grip.rightGripLocal;
                var wristRotation = grip.primaryHandIsLeft ? grip.leftHandFromStaff : grip.rightHandFromStaff;
                if (root.name is "StarvingBandit_ShortSaber" or "StarvingBandit_CurvedCleaver" or
                    "StarvingBandit_HookSword" or "StarvingBandit_BroadCleaver" or "StarvingBandit_LongBlade")
                {
                    var positive = weapon.transform.Find("PositiveStrikingTip");
                    var negative = weapon.transform.Find("NegativeStrikingTip");
                    if (!positive || !negative) throw new Exception("Missing blade tips: " + root.name);
                    bool useNegativeBladeTip =
                        (negative.localPosition - wrist).sqrMagnitude >
                        (positive.localPosition - wrist).sqrMagnitude;
                    for (int attack = 0; attack < brain.attacks.Length; attack++)
                        brain.attacks[attack].useNegativeTip = useNegativeBladeTip;
                }
                weapon.grip = Marker(weapon.transform, "NPC held grip", wrist);
                weapon.grip.localRotation = wristRotation;
                PrefabUtility.SaveAsPrefabAsset(weaponRoot, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(weaponRoot); }
            var wristLocal = grip.primaryHandIsLeft ? grip.leftGripLocal : grip.rightGripLocal;
            var wristLocalRotation = grip.primaryHandIsLeft ? grip.leftHandFromStaff : grip.rightHandFromStaff;
            generated.gripTarget = Marker(grip.desiredStaff, "Mocap physical grip target", wristLocal);
            generated.gripTarget.localRotation = wristLocalRotation;
            generated.weaponPoseTarget = grip.desiredStaff;
            grip.driveVisualStaff = true;
        }
        if (grip.secondaryVisualWeapon)
        {
            var saber = grip.secondaryVisualWeapon.GetComponent<PhysicalMeleeWeapon>();
            if (!saber) saber = grip.secondaryVisualWeapon.gameObject.AddComponent<PhysicalMeleeWeapon>();
            saber.grip = Marker(saber.transform, "Saber wrist grip", Vector3.zero);
            saber.ownerRoot = brain.transform;
            saber.strikeType = PhysicalMeleeWeapon.StrikeType.Cutting;
            saber.npcGripTarget = Marker(grip.transform, "Saber physical target", Vector3.zero);
            saber.npcGripSlack = .015f;
            saber.npcAngularSlack = 4f;
            saber.GetComponent<Rigidbody>().useGravity = false;
            grip.secondaryPhysicalWeapon = saber;
            grip.secondaryWeaponTarget = saber.npcGripTarget;
            FitWeapon(saber);
        }
    }
    static void ReorientGeneratedWeapon(string variantName, MocapStaffPreview grip, Transform weapon)
    {
        var model = weapon.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Generated "));
        if (variantName == "StarvingBandit_BroadCleaver")
        {
            if (!model) throw new Exception("Generated model missing for " + variantName);
            AlignGeneratedHilt("BroadCleaver", model, weapon, grip);
            return;
        }
        Vector3 axis = variantName switch {
            "StarvingBandit_ShortSaber" or "StarvingBandit_CurvedCleaver" or
            "StarvingBandit_HookSword" => Vector3.right,
            "StarvingBandit_LongBlade" => Vector3.up,
            _ => Vector3.zero
        };
        if (axis == Vector3.zero) return;
        if (!model) throw new Exception("Generated model missing for " + variantName);
        var bounds = new Bounds();
        bool hasPoint = false;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh) continue;
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                var local = model.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                if (!hasPoint) { bounds = new Bounds(local, Vector3.zero); hasPoint = true; }
                else bounds.Encapsulate(local);
            }
        }
        if (!hasPoint) throw new Exception("Generated mesh missing for " + variantName);
        float span = Mathf.Abs(axis.x) > .5f ? bounds.size.x : bounds.size.y;
        float fraction = grip.secondaryHandGrips ? .26f : .15f;
        var sourceGrip = bounds.center;
        bool hiltAtMaximum = variantName is "StarvingBandit_ShortSaber" or
            "StarvingBandit_CurvedCleaver" or "StarvingBandit_HookSword";
        float along = axis.x < -.5f || hiltAtMaximum ? bounds.max.x - span * fraction :
            axis.x > .5f ? bounds.min.x + span * fraction :
            axis.y < -.5f ? bounds.max.y - span * fraction : bounds.min.y + span * fraction;
        if (Mathf.Abs(axis.x) > .5f) sourceGrip.x = along; else sourceGrip.y = along;
        var primary = grip.primaryHandIsLeft ? grip.leftGripLocal : grip.rightGripLocal;
        var direction = grip.primaryHandIsLeft ? grip.leftGripLocal - grip.rightGripLocal :
            grip.rightGripLocal - grip.leftGripLocal;
        var rotation = Quaternion.FromToRotation(axis, direction.normalized);
        if (variantName is "StarvingBandit_ShortSaber" or "StarvingBandit_HookSword" or
            "StarvingBandit_CurvedCleaver" or "StarvingBandit_LongBlade")
            rotation = Quaternion.AngleAxis(180f, Vector3.up) * rotation;
        model.localRotation = rotation;
        model.localPosition = primary - rotation * Vector3.Scale(sourceGrip, model.localScale);
        // Weapon prefabs have a nonzero root transform. Resolve the grip in
        // world space after rotation so the selected hilt point truly meets the wrist.
        model.position += weapon.TransformPoint(primary) - model.TransformPoint(sourceGrip);
        if (Vector3.Distance(model.TransformPoint(sourceGrip), weapon.TransformPoint(primary)) > .002f)
            throw new Exception("Weapon model grip did not align: " + variantName);
        if (variantName is "StarvingBandit_ShortSaber" or "StarvingBandit_CurvedCleaver" or
            "StarvingBandit_HookSword")
            AlignGeneratedHilt(variantName.Substring("StarvingBandit_".Length), model, weapon, grip);
    }
    public static void FitWeapon(PhysicalMeleeWeapon weapon)
    {
        if (!weapon) return;
        var filters = weapon.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).ToArray();
        if (filters.Length == 0) throw new Exception("No visible weapon mesh: " + weapon.name);
        // Keep legacy collider components for compatibility with builders, but disable their old staff volume.
        foreach (var collider in weapon.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        int meshIndex = 0;
        foreach (var filter in filters)
        {
            foreach (var old in filter.GetComponents<MeshCollider>()) UnityEngine.Object.DestroyImmediate(old);
            var key = weapon.name.Replace(' ', '_').Replace('/', '_') + "_" + meshIndex++;
            foreach (var mesh in ArmedWeaponColliderMesh.Build(filter.sharedMesh, key))
            {
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = true;
                collider.isTrigger = false;
                collider.enabled = true;
            }
        }
        weapon.useMeshContactShape = true;
        if (StaffAxisCalibration.TryMeasure(weapon.transform, out var positive, out var negative))
        {
            MeasureVisibleTips(weapon.transform, ref positive, ref negative);
            Marker(weapon.transform, "PositiveStrikingTip", positive);
            Marker(weapon.transform, "NegativeStrikingTip", negative);
        }
        var grab = weapon.GetComponent<XRGrabInteractable>();
        if (grab) { grab.colliders.Clear(); grab.colliders.AddRange(weapon.GetComponentsInChildren<Collider>().Where(c => c.enabled)); }
    }
    static Transform Marker(Transform parent, string name, Vector3 position)
    {
        var marker = parent.Find(name);
        if (!marker) { marker = new GameObject(name).transform; marker.SetParent(parent, false); }
        marker.localPosition = position;
        marker.localRotation = Quaternion.identity;
        return marker;
    }
    static void MeasureVisibleTips(Transform root, ref Vector3 positive, ref Vector3 negative)
    {
        var axis = (positive - negative).normalized;
        float maximum = float.NegativeInfinity, minimum = float.PositiveInfinity;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh) continue;
            var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                var point = matrix.MultiplyPoint3x4(vertex);
                var distance = Vector3.Dot(point, axis);
                if (distance > maximum) { maximum = distance; positive = point; }
                if (distance < minimum) { minimum = distance; negative = point; }
            }
        }
    }
}
