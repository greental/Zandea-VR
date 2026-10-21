using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using ScavengerNPC;
using KenshiVR.CharacterCreator;

namespace KenshiVR.MeleeCombat
{
    public enum SeverPoint { Head, LeftShoulder, RightShoulder, LeftElbow, RightElbow,
        LeftWrist, RightWrist, LeftHip, RightHip, LeftKnee, RightKnee }

    [Serializable]
    public sealed class SeverResistance
    {
        public SeverPoint point;
        [Min(0f)] public float damage = 25f;
        [Min(0f)] public float speed = 4f;
    }

    [Serializable]
    public sealed class SeverJoint
    {
        public SeverPoint point;
        public Transform bone;
    }

    public sealed class SeveredBodyPartIdentity : MonoBehaviour
    {
        public string npcId;
        public string characterName;
        public string factionId;
        public string bountyId;
        public int bountyValue;
        public int appearanceSeed;
        public string appearancePresetId;
        public string appearanceArchetypeId;
        public SeverPoint bodyPart;
        public bool protectedFromCleanup;
    }

    public sealed class SeveredBodyPart : MonoBehaviour
    {
        [Min(0f)] public float cleanupAfterSeconds = 240f;
        SeveredBodyPartIdentity identity;
        XRGrabInteractable grab;
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        float expiry, nextCheck;

        public void OwnMesh(Mesh mesh)
        {
            if (mesh) ownedMeshes.Add(mesh);
        }

        void Awake()
        {
            identity = GetComponent<SeveredBodyPartIdentity>();
            grab = GetComponent<XRGrabInteractable>();
            expiry = Time.time + cleanupAfterSeconds;
        }

        void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 1f;
            if (identity && identity.protectedFromCleanup || cleanupAfterSeconds <= 0f) return;
            if (grab && grab.isSelected) { expiry = Time.time + cleanupAfterSeconds; return; }
            if (Time.time >= expiry) Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var mesh in ownedMeshes)
                if (mesh) Destroy(mesh);
        }
    }

    // Uses bone-defined mesh masks. The source's shared skinned mesh is never disabled or edited.
    public sealed class DismemberableCharacter : MonoBehaviour
    {
        public bool enableDismemberment = true;
        public bool forceSeverOnValidJointStrike;
        public bool disableSevering;
        [Min(0f)] public float minimumSlashSpeed = 5f;
        [Min(0f)] public float minimumSeveringDamage = 6f;
        [Header("Living combat resistance")]
        [Min(1f)] public float livingSeverSpeedMultiplier = 1.5f;
        [Min(1f)] public float livingSeverDamageMultiplier = 2f;
        [Header("Postmortem cuts")]
        [Range(.1f, 1f)] public float corpseSeverSpeedMultiplier = .6f;
        [Range(.1f, 1f)] public float corpseSeverDamageMultiplier = .35f;
        [Range(0f, 1f)] public float minimumEdgeAlignment = .3f;
        [Min(0f)] public float jointHitRadius = .18f;
        [Min(0f)] public float physicsImpulseMultiplier = 1f;
        [Min(1)] public int maximumSeveredBodyParts = 11;
        [Min(0f)] public float collisionGraceSeconds = .5f;
        [Min(0f)] public float amputationBleedPerSecond = 1.1f;
        [Min(0f)] public float amputationBleedSeconds = 45f;
        public Material woundMaterial;
        [Tooltip("Optional bone overrides for Generic rigs. Unassigned joints use Humanoid bones or known imported bone names.")]
        public SeverJoint[] joints;
        public SeverResistance[] resistances = {
            new SeverResistance { point = SeverPoint.Head, damage = 10f, speed = 10f },
            new SeverResistance { point = SeverPoint.LeftShoulder, damage = 9f, speed = 9f },
            new SeverResistance { point = SeverPoint.RightShoulder, damage = 9f, speed = 9f },
            new SeverResistance { point = SeverPoint.LeftElbow, damage = 7f, speed = 8f },
            new SeverResistance { point = SeverPoint.RightElbow, damage = 7f, speed = 8f },
            new SeverResistance { point = SeverPoint.LeftWrist, damage = 6f, speed = 7f },
            new SeverResistance { point = SeverPoint.RightWrist, damage = 6f, speed = 7f },
            new SeverResistance { point = SeverPoint.LeftHip, damage = 10f, speed = 10f },
            new SeverResistance { point = SeverPoint.RightHip, damage = 10f, speed = 10f },
            new SeverResistance { point = SeverPoint.LeftKnee, damage = 7f, speed = 8f },
            new SeverResistance { point = SeverPoint.RightKnee, damage = 7f, speed = 8f }
        };
        [Header("Identity")]
        public string npcId;
        public string characterName;
        public string factionId;
        public string bountyId;
        public int bountyValue;

        readonly HashSet<SeverPoint> severed = new HashSet<SeverPoint>();
        readonly List<SeveredBodyPartIdentity> parts = new List<SeveredBodyPartIdentity>();
        readonly List<Mesh> instanceMeshes = new List<Mesh>();
        readonly Dictionary<string, Transform> namedBones = new Dictionary<string, Transform>();
        static Material defaultWoundMaterial;
        Animator animator;
        ScavengerHealth health;
        ScavengerRagdoll ragdoll;
        GoreImpactSystem blood;
        NPCCharacterAppearance appearance;
        float lastSeverTime = float.NegativeInfinity;

        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            health = GetComponent<ScavengerHealth>();
            ragdoll = GetComponent<ScavengerRagdoll>();
            blood = GetComponent<GoreImpactSystem>();
            appearance = GetComponent<NPCCharacterAppearance>();
            if (string.IsNullOrEmpty(npcId)) npcId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(characterName)) characterName = gameObject.name;
            if (string.IsNullOrEmpty(factionId) && GetComponent<StarvingBandit>()) factionId = "starving_bandits";
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (!namedBones.ContainsKey(child.name)) namedBones.Add(child.name, child);
        }

        public bool TrySever(Vector3 hit, Vector3 slashVelocity, Vector3 bladeEdge, float damage,
            bool cuttingWeapon, Vector3 inheritedVelocity)
        {
            if (!enableDismemberment || disableSevering || !cuttingWeapon ||
                parts.Count >= maximumSeveredBodyParts || slashVelocity.sqrMagnitude < .01f ||
                Time.time - lastSeverTime < .35f) return false;
            float speed = slashVelocity.magnitude;
            if (Mathf.Abs(Vector3.Dot(slashVelocity.normalized, bladeEdge.normalized)) < minimumEdgeAlignment) return false;
            SeverPoint best = SeverPoint.Head;
            Transform bestBone = null;
            float nearest = jointHitRadius * jointHitRadius;
            foreach (SeverPoint point in Enum.GetValues(typeof(SeverPoint)))
            {
                if (severed.Contains(point) || HasSeveredAncestor(point)) continue;
                var bone = GetJointBone(point);
                if (!bone) continue;
                float distance = (bone.position - hit).sqrMagnitude;
                if (distance < nearest) { nearest = distance; best = point; bestBone = bone; }
            }
            if (!bestBone) return false;
            if (!forceSeverOnValidJointStrike)
            {
                float speedMultiplier = health && health.IsDead ? corpseSeverSpeedMultiplier :
                    health ? Mathf.Max(1.5f, livingSeverSpeedMultiplier) : 1f;
                float damageMultiplier = health && health.IsDead ? corpseSeverDamageMultiplier :
                    health ? Mathf.Max(2f, livingSeverDamageMultiplier) : 1f;
                if (speed < minimumSlashSpeed * speedMultiplier ||
                    damage < minimumSeveringDamage * damageMultiplier) return false;
                foreach (var resistance in resistances)
                    if (resistance != null && resistance.point == best &&
                        (speed < resistance.speed * speedMultiplier ||
                         damage < resistance.damage * damageMultiplier)) return false;
            }
            bool completed = Sever(best, bestBone, hit, slashVelocity, inheritedVelocity);
            if (completed) lastSeverTime = Time.time;
            return completed;
        }

        bool Sever(SeverPoint point, Transform bone, Vector3 hit, Vector3 slashVelocity, Vector3 inheritedVelocity)
        {
            var detached = new GameObject(characterName + " " + point);
            detached.transform.SetPositionAndRotation(bone.position, bone.rotation);
            var snapshot = new Mesh();
            var detachedMeshes = new List<Mesh>();
            int meshCount = 0;
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skin.sharedMesh || skin.bones == null || skin.bones.Length == 0) continue;
                if (!skin.sharedMesh.isReadable)
                {
                    Debug.LogWarning($"Gore skipped unreadable mesh {skin.sharedMesh.name} on {name}; " +
                        "enable Read/Write for complete severed clothing geometry.", skin);
                    continue;
                }
                var source = skin.sharedMesh;
                var weights = source.boneWeights;
                if (weights == null || weights.Length != source.vertexCount) continue;
                var affected = new bool[skin.bones.Length];
                bool any = false;
                for (int i = 0; i < affected.Length; i++)
                    if (skin.bones[i] && (skin.bones[i] == bone || skin.bones[i].IsChildOf(bone)))
                        any = affected[i] = true;
                if (!any && point != SeverPoint.Head) continue;
                var selected = new bool[weights.Length];
                Vector3[] bakedVertices = null;
                bool allowHeadVolume = false;
                if (point == SeverPoint.Head)
                {
                    snapshot.Clear();
                    skin.BakeMesh(snapshot, true);
                    bakedVertices = snapshot.vertices;
                    // The fallback includes rigid facial meshes with no head
                    // weights, but must not cut broad torso or arm surfaces.
                    allowHeadVolume = Vector3.Scale(snapshot.bounds.size,
                        skin.transform.lossyScale).magnitude < .45f;
                }
                for (int i = 0; i < weights.Length; i++)
                {
                    var w = weights[i];
                    float limbWeight = (Affected(affected, w.boneIndex0) ? w.weight0 : 0f) +
                        (Affected(affected, w.boneIndex1) ? w.weight1 : 0f) +
                        (Affected(affected, w.boneIndex2) ? w.weight2 : 0f) +
                        (Affected(affected, w.boneIndex3) ? w.weight3 : 0f);
                    // Wrist blends are spread across the palm and cuff. A strict
                    // majority clips those blended triangles and shrinks the hand.
                    selected[i] = limbWeight >= (point == SeverPoint.LeftWrist || point == SeverPoint.RightWrist ? .3f : .5f);
                    if (!selected[i] && allowHeadVolume && bakedVertices != null && i < bakedVertices.Length)
                    {
                        Vector3 worldVertex = skin.transform.TransformPoint(bakedVertices[i]);
                        Vector3 fromHead = worldVertex - bone.position;
                        selected[i] = InHeadVolume(fromHead);
                    }
                }
                var retained = Instantiate(source);
                if (point != SeverPoint.Head) snapshot.Clear();
                // Bake in the source mesh's unscaled coordinates. The detached
                // renderer then applies the skin's world scale exactly once.
                skin.BakeMesh(snapshot, true);
                var baked = Instantiate(snapshot);
                baked.name = source.name + "_severed";
                int cutTriangles = 0;
                var cutBounds = new Bounds();
                bool hasCutBounds = false;
                var detachedVertices = baked.vertices;
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    var triangles = source.GetTriangles(sub);
                    var keep = new List<int>(triangles.Length);
                    var cut = new List<int>();
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int count = (selected[triangles[t]] ? 1 : 0) +
                            (selected[triangles[t + 1]] ? 1 : 0) + (selected[triangles[t + 2]] ? 1 : 0);
                        bool inHead = false;
                        if (allowHeadVolume && bakedVertices != null)
                        {
                            Vector3 center = (bakedVertices[triangles[t]] + bakedVertices[triangles[t + 1]] +
                                bakedVertices[triangles[t + 2]]) / 3f;
                            inHead = InHeadVolume(skin.transform.TransformPoint(center) - bone.position);
                        }
                        var destination = count >= 2 || inHead ? cut : keep;
                        destination.Add(triangles[t]); destination.Add(triangles[t + 1]); destination.Add(triangles[t + 2]);
                    }
                    cutTriangles += cut.Count;
                    retained.SetTriangles(keep, sub, false);
                    baked.SetTriangles(cut, sub, false);
                    foreach (int index in cut)
                    {
                        if (index < 0 || index >= detachedVertices.Length) continue;
                        if (!hasCutBounds) { cutBounds = new Bounds(detachedVertices[index], Vector3.zero); hasCutBounds = true; }
                        else cutBounds.Encapsulate(detachedVertices[index]);
                    }
                }
                if (cutTriangles == 0) { Destroy(retained); Destroy(baked); continue; }
                skin.sharedMesh = retained;
                instanceMeshes.Add(retained);
                retained.RecalculateBounds();
                // SetTriangles does not remove unused source vertices. RecalculateBounds
                // would include the other arm/leg and inflate the detached collider.
                if (hasCutBounds) baked.bounds = cutBounds;
                var visual = new GameObject(skin.name);
                visual.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                visual.transform.localScale = skin.transform.lossyScale;
                visual.transform.SetParent(detached.transform, true);
                visual.AddComponent<MeshFilter>().sharedMesh = baked;
                var detachedRenderer = visual.AddComponent<MeshRenderer>();
                detachedRenderer.sharedMaterials = skin.sharedMaterials;
                detachedRenderer.enabled = skin.enabled;
                var sharedProperties = new MaterialPropertyBlock();
                skin.GetPropertyBlock(sharedProperties);
                detachedRenderer.SetPropertyBlock(sharedProperties);
                for (int materialIndex = 0; materialIndex < skin.sharedMaterials.Length; materialIndex++)
                {
                    var properties = new MaterialPropertyBlock();
                    skin.GetPropertyBlock(properties, materialIndex);
                    if (!properties.isEmpty) detachedRenderer.SetPropertyBlock(properties, materialIndex);
                }
                detachedMeshes.Add(baked);
                meshCount++;
            }
            Destroy(snapshot);
            if (meshCount == 0) { Destroy(detached); return false; }
            foreach (var item in GetComponentsInChildren<AppearanceSpawnedItem>(true))
            {
                if (!item || !item.definition || item.definition.skinned ||
                    !(item.transform == bone || item.transform.IsChildOf(bone))) continue;
                var accessory = Instantiate(item.gameObject, item.transform.position, item.transform.rotation);
                accessory.transform.localScale = item.transform.lossyScale;
                accessory.transform.SetParent(detached.transform, true);
                var sourceRenderers = item.GetComponentsInChildren<Renderer>(true);
                var copiedRenderers = accessory.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < Mathf.Min(sourceRenderers.Length, copiedRenderers.Length); rendererIndex++)
                {
                    var sourceRenderer = sourceRenderers[rendererIndex];
                    var copiedRenderer = copiedRenderers[rendererIndex];
                    var colorProperties = new MaterialPropertyBlock();
                    sourceRenderer.GetPropertyBlock(colorProperties);
                    copiedRenderer.SetPropertyBlock(colorProperties);
                    for (int materialIndex = 0; materialIndex < sourceRenderer.sharedMaterials.Length; materialIndex++)
                    {
                        sourceRenderer.GetPropertyBlock(colorProperties, materialIndex);
                        if (!colorProperties.isEmpty) copiedRenderer.SetPropertyBlock(colorProperties, materialIndex);
                    }
                }
                foreach (var shape in accessory.GetComponentsInChildren<Collider>(true))
                { shape.enabled = false; Destroy(shape); }
                foreach (var rigidbody in accessory.GetComponentsInChildren<Rigidbody>(true))
                { rigidbody.isKinematic = true; Destroy(rigidbody); }
                item.gameObject.SetActive(false);
            }
            severed.Add(point);
            DisableDetachedRagdollBones(bone);
            AddWoundCap(bone.parent ? bone.parent : transform, bone.position, .07f);
            AddWoundCap(detached.transform, bone.position, .07f);
            var bounds = new Bounds(Vector3.zero, Vector3.one * .08f);
            foreach (var renderer in detached.GetComponentsInChildren<Renderer>())
            {
                var b = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                    bounds.Encapsulate(detached.transform.InverseTransformPoint(new Vector3(
                        (corner & 1) == 0 ? b.min.x : b.max.x,
                        (corner & 2) == 0 ? b.min.y : b.max.y,
                        (corner & 4) == 0 ? b.min.z : b.max.z)));
            }
            var box = detached.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * .06f);
            var body = detached.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(bounds.size.magnitude * 2f, .3f, 5f);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxDepenetrationVelocity = 2f;
            body.linearDamping = .1f;
            body.angularDamping = .2f;
            body.linearVelocity = Vector3.ClampMagnitude(inheritedVelocity + slashVelocity * .15f * physicsImpulseMultiplier, 7f);
            body.angularVelocity = UnityEngine.Random.onUnitSphere * 3f;
            var grab = detached.AddComponent<XRGrabInteractable>();
            grab.colliders.Add(box);
            detached.AddComponent<HeldLootCollisionGuard>().Bind(grab);
            var identity = detached.AddComponent<SeveredBodyPartIdentity>();
            identity.npcId = npcId; identity.characterName = characterName; identity.factionId = factionId;
            identity.bountyId = bountyId; identity.bountyValue = bountyValue;
            identity.appearanceSeed = appearance ? appearance.seed : 0;
            identity.appearancePresetId = appearance && appearance.preset ? appearance.preset.name : string.Empty;
            identity.appearanceArchetypeId = appearance && appearance.archetype ? appearance.archetype.name : string.Empty;
            identity.bodyPart = point;
            identity.protectedFromCleanup = point == SeverPoint.Head;
            var severedPart = detached.AddComponent<SeveredBodyPart>();
            foreach (var mesh in detachedMeshes) severedPart.OwnMesh(mesh);
            parts.Add(identity);
            if (point == SeverPoint.Head)
            {
                GetComponent<CharacterCosmeticsController>()?.PrepareDetachedHair(detached.transform,bone);
                GetComponent<NpcClothingLoot>()?.TransferHeadwearToDetachedHead(detached.transform);
                GetComponent<CharacterCosmeticsController>()?.SetHeadPresent(false);
            }
            StartCoroutine(AllowFormerBodyCollision(box));
            blood?.RegisterAmputation(amputationBleedPerSecond, amputationBleedSeconds,
                bone.position, slashVelocity, point == SeverPoint.Head, bone.parent);
            ReactToLoss(point, bone, hit, slashVelocity);
            return true;
        }

        IEnumerator AllowFormerBodyCollision(Collider detached)
        {
            var former = GetComponentsInChildren<Collider>();
            foreach (var collider in former) if (collider && detached) Physics.IgnoreCollision(collider, detached, true);
            yield return new WaitForSeconds(collisionGraceSeconds);
            foreach (var collider in former) if (collider && detached) Physics.IgnoreCollision(collider, detached, false);
        }

        void DisableDetachedRagdollBones(Transform bone)
        {
            if (!ragdoll) return;
            var keptBodies = new List<Rigidbody>();
            foreach (var body in ragdoll.bones)
            {
                if (!body) continue;
                if (body.transform == bone || body.transform.IsChildOf(bone))
                { body.isKinematic = true; foreach (var joint in body.GetComponents<Joint>()) Destroy(joint); }
                else keptBodies.Add(body);
            }
            ragdoll.bones = keptBodies.ToArray();
            var keptColliders = new List<Collider>();
            foreach (var collider in ragdoll.boneColliders)
            {
                if (!collider) continue;
                if (collider.transform == bone || collider.transform.IsChildOf(bone)) collider.enabled = false;
                else keptColliders.Add(collider);
            }
            ragdoll.boneColliders = keptColliders.ToArray();
            ragdoll.RefreshCorpseGrabColliders();
        }

        void ReactToLoss(SeverPoint point, Transform bone, Vector3 hit, Vector3 direction)
        {
            if (!health) return;
            if (point == SeverPoint.Head)
            { health.Die(hit, Vector3.ClampMagnitude(direction, 6f)); return; }
            bool leg = point == SeverPoint.LeftHip || point == SeverPoint.RightHip ||
                point == SeverPoint.LeftKnee || point == SeverPoint.RightKnee;
            if (leg) health.KnockOutForAmputation(hit, direction);
            else
            {
                var generated = GetComponent<NpcGeneratedWeapon>();
                bool rightArm = point == SeverPoint.RightShoulder || point == SeverPoint.RightElbow ||
                    point == SeverPoint.RightWrist;
                if (generated && (rightArm || generated.gripTarget &&
                    (generated.gripTarget == bone || generated.gripTarget.IsChildOf(bone))))
                    generated.DropForKnockout();
                var bandit = GetComponent<StarvingBandit>();
                if (bandit && bandit.staff && (generated == null ||
                    bandit.staff.proficiencyClass == PhysicalMeleeWeapon.WeaponProficiencyClass.Staff))
                {
                    bandit.staff.DropFromNpc();
                    bandit.SetEquippedStaff(null, bandit.weaponGoal);
                }
                health.ReactToAmputation(hit, direction);
            }
        }

        void AddWoundCap(Transform parent, Vector3 position, float radius)
        {
            var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cap.name = "Wound cap";
            var collider = cap.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            cap.transform.SetPositionAndRotation(position, parent.rotation);
            cap.transform.SetParent(parent, true);
            cap.transform.localScale = Vector3.one * radius * 2f;
            cap.GetComponent<Renderer>().sharedMaterial = woundMaterial ? woundMaterial : DefaultWoundMaterial();
        }

        static bool Affected(bool[] map, int index) => index >= 0 && index < map.Length && map[index];
        static bool InHeadVolume(Vector3 fromHead) =>
            fromHead.y > -.17f && fromHead.y < .42f &&
            new Vector2(fromHead.x, fromHead.z).sqrMagnitude < .28f * .28f;

        static Material DefaultWoundMaterial()
        {
            if (defaultWoundMaterial) return defaultWoundMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            defaultWoundMaterial = new Material(shader) { name = "Runtime wound cap" };
            defaultWoundMaterial.SetColor("_BaseColor", new Color(.16f, .012f, .01f));
            return defaultWoundMaterial;
        }

        void OnDestroy()
        {
            foreach (var mesh in instanceMeshes) if (mesh) Destroy(mesh);
        }

        bool HasSeveredAncestor(SeverPoint point)
        {
            if (point == SeverPoint.LeftElbow || point == SeverPoint.LeftWrist)
                if (severed.Contains(SeverPoint.LeftShoulder)) return true;
            if (point == SeverPoint.RightElbow || point == SeverPoint.RightWrist)
                if (severed.Contains(SeverPoint.RightShoulder)) return true;
            if (point == SeverPoint.LeftWrist && severed.Contains(SeverPoint.LeftElbow) ||
                point == SeverPoint.RightWrist && severed.Contains(SeverPoint.RightElbow) ||
                point == SeverPoint.LeftKnee && severed.Contains(SeverPoint.LeftHip) ||
                point == SeverPoint.RightKnee && severed.Contains(SeverPoint.RightHip)) return true;
            return false;
        }

        static HumanBodyBones BoneFor(SeverPoint point) => point switch
        {
            SeverPoint.Head => HumanBodyBones.Head,
            SeverPoint.LeftShoulder => HumanBodyBones.LeftUpperArm,
            SeverPoint.RightShoulder => HumanBodyBones.RightUpperArm,
            SeverPoint.LeftElbow => HumanBodyBones.LeftLowerArm,
            SeverPoint.RightElbow => HumanBodyBones.RightLowerArm,
            SeverPoint.LeftWrist => HumanBodyBones.LeftHand,
            SeverPoint.RightWrist => HumanBodyBones.RightHand,
            SeverPoint.LeftHip => HumanBodyBones.LeftUpperLeg,
            SeverPoint.RightHip => HumanBodyBones.RightUpperLeg,
            SeverPoint.LeftKnee => HumanBodyBones.LeftLowerLeg,
            _ => HumanBodyBones.RightLowerLeg
        };

        public Transform GetJointBone(SeverPoint point)
        {
            if (joints != null)
                foreach (var joint in joints)
                    if (joint != null && joint.point == point && joint.bone) return joint.bone;
            if (animator && animator.isHuman)
            {
                var humanoid = animator.GetBoneTransform(BoneFor(point));
                if (humanoid) return humanoid;
            }
            string name = point switch
            {
                SeverPoint.Head => "CC_Base_Head_038",
                SeverPoint.LeftShoulder => "CC_Base_L_Upperarm_050",
                SeverPoint.RightShoulder => "CC_Base_R_Upperarm_074",
                SeverPoint.LeftElbow => "CC_Base_L_Forearm_051",
                SeverPoint.RightElbow => "CC_Base_R_Forearm_077",
                SeverPoint.LeftWrist => "CC_Base_L_Hand_055",
                SeverPoint.RightWrist => "CC_Base_R_Hand_081",
                SeverPoint.LeftHip => "CC_Base_L_Thigh_04",
                SeverPoint.RightHip => "CC_Base_R_Thigh_00",
                SeverPoint.LeftKnee => "CC_Base_L_Calf_05",
                SeverPoint.RightKnee => "CC_Base_R_Calf_021",
                _ => string.Empty
            };
            if (namedBones.TryGetValue(name, out var found)) return found;
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
