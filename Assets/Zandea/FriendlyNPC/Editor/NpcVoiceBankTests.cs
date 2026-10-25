using NUnit.Framework;

namespace Zandea.FriendlyNPC.Editor
{
    public sealed class NpcVoiceBankTests
    {
        [Test]
        public void TakeSelectionNeverRepeatsPreviousWhenAlternatesExist()
        {
            for (int count = 2; count <= 4; count++)
                for (int previous = 0; previous < count; previous++)
                    for (int random = 0; random < 12; random++)
                    {
                        int chosen = NpcVoiceBank.SelectNextIndex(count, previous, random);
                        Assert.That(chosen, Is.InRange(0, count - 1));
                        Assert.That(chosen, Is.Not.EqualTo(previous));
                    }
        }

        [Test]
        public void FirstSelectionCanReachEveryTake()
        {
            for (int index = 0; index < 4; index++)
                Assert.That(NpcVoiceBank.SelectNextIndex(4, -1, index), Is.EqualTo(index));
        }
    }
}
