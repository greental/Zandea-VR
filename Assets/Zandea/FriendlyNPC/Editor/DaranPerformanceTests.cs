#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace Zandea.FriendlyNPC.Editor
{
    public sealed class DaranPerformanceTests
    {
        [UnityTest, Ignore("Requires tracked PCVR; headless XR scene reload fails before sampling.")]
        public IEnumerator ProfileApproachConversationAndChoices()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/FriendlyNPC/DaranMerchantVoiceTest.unity", OpenSceneMode.Single);
            yield return new EnterPlayMode();
            var npc = FindInScene<FriendlyNpcInteraction>();
            var camera = FindInScene<Camera>();
            var playerRoot = camera ? camera.transform.root : null;
            Assert.IsNotNull(npc);
            Assert.IsNotNull(camera);
            Assert.IsNotNull(playerRoot);
            var choices = FindInScene<PalmDialogueChoices>();
            Assert.IsNotNull(choices);

            playerRoot.position = npc.transform.position + new Vector3(15f, 0f, 0f);
            yield return Measure("far", 90);
            Assert.IsFalse(npc.IsConversing);
            playerRoot.position = npc.transform.position + new Vector3(0f, 0f, -4.5f);
            yield return Measure("invite", 90);
            Assert.IsFalse(npc.IsTrading, "The invitation ring must not lock the player into a trade.");
            bool greetingClip = false;
            foreach (string id in new[] { "greeting_001", "greeting_002" })
            {
                var voiceEvent = npc.voiceBank.Find(id);
                if (voiceEvent == null) continue;
                foreach (var take in voiceEvent.takes)
                    if (take != null && take.clip == npc.mouthAudio.clip) greetingClip = true;
            }
            Assert.IsTrue(greetingClip, "Daran should invite the player at 3–6 metres.");
            playerRoot.position = npc.transform.position + new Vector3(0f, 0f, -.6f);
            yield return Measure("near", 90);
            Assert.IsFalse(npc.IsConversing, "Approaching alone must not open dialogue.");
            Assert.IsTrue(FriendlyNpcInteraction.TryBeginBestWave(camera.transform),
                "A deliberate wave should choose the NPC the player is viewing.");
            Assert.IsTrue(npc.IsConversing);
            yield return Measure("speaking", 90);
            npc.mouthAudio.Stop();
            yield return new WaitForSecondsRealtime(1f);
            var responses = npc.dialogue.Find(npc.dialogue.entryNodeId).responses;
            choices.Show(responses, _ => { });
            // Emulate a free tracked controller deliberately raised near the chest.
            var providers = playerRoot.GetComponentsInChildren<LocomotionProvider>(true);
            var enabledBefore = new bool[providers.Length];
            for (int i = 0; i < providers.Length; i++) enabledBefore[i] = providers[i].enabled;
            choices.rightHand.transform.position = camera.transform.position +
                camera.transform.forward * .3f - Vector3.up * .28f;
            yield return null;
            Assert.IsFalse(choices.panel.activeSelf,
                "The right hand must stay free and never carry dialogue choices.");
            void RaiseHand() => choices.leftHand.transform.position = camera.transform.position +
                camera.transform.forward * .3f - Vector3.up * .28f;
            RaiseHand();
            yield return Measure("choices", 90, RaiseHand);
            Debug.Log("Daran profile palm panel visible=" + choices.panel.activeSelf);
            Assert.IsTrue(choices.panel.activeSelf, "A raised free hand must show dialogue responses.");
            Assert.IsNotNull(choices.panel.transform.Find("Notebook pages"));
            Assert.Less(Mathf.Abs(Vector3.Dot(camera.transform.right,
                choices.panel.transform.position - camera.transform.position)), .5f,
                "Notebook should remain near the player's central view.");
            for (int i = 0; i < providers.Length; i++)
                Assert.AreEqual(enabledBefore[i], providers[i].enabled,
                    "Dialogue must not disable locomotion or turn providers.");
            npc.EndConversation();
            yield return null;
            yield return new ExitPlayMode();
        }

        static T FindInScene<T>() where T : Component
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!root) continue;
                var found = root.GetComponentInChildren<T>(true);
                if (found) return found;
            }
            return null;
        }

        static IEnumerator Measure(string phase, int frames, Action afterFrame = null)
        {
            var times = new List<float>(frames);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                afterFrame?.Invoke();
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
