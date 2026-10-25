using System;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    public enum ExpressionType { Neutral, Happy, Angry, Sad, Surprised, Afraid, Pain }

    [ExecuteAlways]
    public sealed class NPCFacialExpressionController : MonoBehaviour
    {
        public Transform visualRoot;
        [Header("Edit Mode preview")]
        public ExpressionType previewExpression;
        [Range(0f, 1f)] public float previewIntensity = 1f;
        [Min(0f)] public float defaultTransition = .2f;

        static readonly string[] Names = { "", "ExprHappy", "ExprAngry", "ExprSad", "ExprSurprised", "ExprAfraid", "ExprPain" };
        SkinnedMeshRenderer[] renderers;
        int[][] shapeIndices;
        bool hasAnyTargets;
        Transform jaw;
        Quaternion restJawRotation;
        static readonly float[] JawDegrees = { 0f, 2f, 3f, 2f, 18f, 13f, 6f };
        readonly float[] current = new float[Names.Length];
        readonly float[] from = new float[Names.Length];
        readonly float[] target = new float[Names.Length];
        readonly float[] temporaryReturn = new float[Names.Length];
        float transitionStart, transitionDuration, temporaryEnd;
        bool temporary;
        float speechOpen;

        // Speech composes with emotion in this controller; no other component writes the jaw.
        public void SetSpeechOpen(float amount) => speechOpen = Mathf.Clamp01(amount);

        void OnEnable()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(gameObject)) return;
#endif
            if (!visualRoot) visualRoot = transform;
            var foundJaw = FindBone(visualRoot, "CC_Base_JawRoot_040");
            if (foundJaw != jaw)
            {
                jaw = foundJaw;
                if (jaw) restJawRotation = jaw.localRotation;
            }
            renderers = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            shapeIndices = new int[renderers.Length][];
            hasAnyTargets = false;
            for (int r = 0; r < renderers.Length; r++)
            {
                shapeIndices[r] = new int[Names.Length];
                for (int i = 1; i < Names.Length; i++)
                {
                    shapeIndices[r][i] = renderers[r].sharedMesh ? renderers[r].sharedMesh.GetBlendShapeIndex(Names[i]) : -1;
                    hasAnyTargets |= shapeIndices[r][i] >= 0;
                }
            }
            if (!Application.isPlaying) ApplyPreview();
        }
        void OnValidate()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(gameObject)) return;
#endif
            if (!Application.isPlaying && isActiveAndEnabled)
            { renderers = null; shapeIndices = null; ApplyPreview(); }
        }
        void OnDisable()
        {
            speechOpen = 0f;
            if (jaw) jaw.localRotation = restJawRotation;
        }
        static Transform FindBone(Transform root, string boneName)
        {
            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                if (bone.name == boneName) return bone;
            return null;
        }
        public bool HasMorph(ExpressionType type)
        {
            if (type == ExpressionType.Neutral) return true;
            if (shapeIndices == null) return false;
            for (int r = 0; r < shapeIndices.Length; r++)
                if (shapeIndices[r][(int)type] >= 0) return true;
            return false;
        }
        public void SetExpression(ExpressionType type, float intensity = 1f, float transitionDuration = .2f)
        {
            temporary = false;
            Array.Clear(target, 0, target.Length);
            if (type != ExpressionType.Neutral) target[(int)type] = Mathf.Clamp01(intensity);
            Begin(transitionDuration);
        }
        public void PlayTemporaryExpression(ExpressionType type, float intensity, float duration)
        {
            if (!temporary) Array.Copy(target, temporaryReturn, target.Length);
            Array.Clear(target, 0, target.Length);
            if (type != ExpressionType.Neutral) target[(int)type] = Mathf.Clamp01(intensity);
            temporaryEnd = Time.time + Mathf.Max(0f, duration);
            temporary = true;
            Begin(defaultTransition);
        }
        public void ResetExpression(float transitionDuration = .2f) => SetExpression(ExpressionType.Neutral, 0f, transitionDuration);
        void Begin(float duration)
        {
            Array.Copy(current, from, current.Length);
            transitionStart = Time.time;
            transitionDuration = Mathf.Max(0f, duration);
            if (transitionDuration <= 0f) { Array.Copy(target, current, target.Length); ApplyCurrent(); }
        }
        void Update()
        {
            if (!Application.isPlaying) return;
            if (temporary && Time.time >= temporaryEnd)
            {
                temporary = false;
                Array.Copy(temporaryReturn, target, target.Length);
                Begin(defaultTransition);
            }
            float t = transitionDuration <= 0f ? 1f : Mathf.Clamp01((Time.time - transitionStart) / transitionDuration);
            for (int i = 1; i < current.Length; i++) current[i] = Mathf.Lerp(from[i], target[i], t);
            ApplyCurrent();
        }
        void LateUpdate()
        {
            // Animator evaluates before LateUpdate, so the expression keeps the
            // lower jaw open while a humanoid animation or VR pose is playing.
            if (Application.isPlaying) ApplyJaw();
        }
        void ApplyPreview()
        {
            if (renderers == null) OnEnable();
            Array.Clear(current, 0, current.Length);
            if (previewExpression != ExpressionType.Neutral) current[(int)previewExpression] = Mathf.Clamp01(previewIntensity);
            ApplyCurrent();
        }
        public void PreviewInEditor() { if (!Application.isPlaying) ApplyPreview(); }
        void ApplyCurrent()
        {
            ApplyJaw();
            if (!hasAnyTargets || renderers == null) return;
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (!renderer || !renderer.sharedMesh) continue;
                for (int i = 1; i < Names.Length; i++)
                {
                    int index = shapeIndices[r][i];
                    if (index >= 0) renderer.SetBlendShapeWeight(index, current[i]);
                }
            }
        }
        void ApplyJaw()
        {
            if (!jaw) return;
            float degrees = 0f;
            for (int i = 1; i < current.Length; i++) degrees += JawDegrees[i] * current[i];
            jaw.localRotation = restJawRotation * Quaternion.Euler(0f, 0f, -Mathf.Clamp(degrees + speechOpen * 9f, 0f, 20f));
        }
    }
}
