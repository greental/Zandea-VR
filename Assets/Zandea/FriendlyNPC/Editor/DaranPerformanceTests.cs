#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Unity.XR.CoreUtils;

namespace Zandea.FriendlyNPC.Editor
{
    public sealed class DaranPerformanceTests
    {
        [UnityTest]
        public IEnumerator ProfileApproachConversationAndChoices()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/FriendlyNPC/DaranMerchantVoiceTest.unity", OpenSceneMode.Single);
            yield return new EnterPlayMode();
            var npc = UnityEngine.Object.FindFirstObjectByType<FriendlyNpcInteraction>();
            var origin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            Assert.IsNotNull(npc);
            Assert.IsNotNull(origin);
            var choices = UnityEngine.Object.FindFirstObjectByType<PalmDialogueChoices>();
            Assert.IsNotNull(choices);

            origin.transform.position = new Vector3(15f, 0f, 0f);
            yield return Measure("far", 90);
            origin.transform.position = new Vector3(0f, 0f, 0f);
            yield return Measure("near", 90);
            Assert.IsTrue(npc.TryBegin(origin.Camera.transform));
            yield return Measure("speaking", 90);
            npc.mouthAudio.Stop();
            yield return new WaitForSecondsRealtime(1f);
            var responses = npc.dialogue.Find(npc.dialogue.entryNodeId).responses;
            choices.Show(responses, _ => { });
            // Emulate a free controller hand being deliberately raised near the chest.
            var raisedPalm = new GameObject("Profile raised palm");
            choices.leftPalm = raisedPalm.transform;
            raisedPalm.transform.position = origin.Camera.transform.position +
                origin.Camera.transform.forward * .3f - Vector3.up * .28f;
            yield return Measure("choices", 90);
            Debug.Log("Daran profile palm panel visible=" + choices.panel.activeSelf);
            Assert.IsTrue(choices.panel.activeSelf, "A raised free hand must show dialogue responses.");
            npc.EndConversation();
            UnityEngine.Object.Destroy(raisedPalm);
            yield return null;
            yield return new ExitPlayMode();
        }

        static IEnumerator Measure(string phase, int frames)
        {
            var times = new List<float>(frames);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                times.Add(Time.unscaledDeltaTime * 1000f);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            times.Sort();
            float sum = 0f;
            foreach (var time in times) sum += time;
            Debug.Log($"Daran profile {phase}: frames={frames}, mean={sum / frames:F2}ms, " +
                $"p95={times[Mathf.Clamp(Mathf.CeilToInt(frames * .95f) - 1, 0, frames - 1)]:F2}ms, " +
                $"max={times[frames - 1]:F2}ms, main-thread-allocated={allocated} bytes");
        }
    }
}
#endif
