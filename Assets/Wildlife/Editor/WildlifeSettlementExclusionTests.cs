#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
namespace Zandea.Wildlife.Editor
{
    public sealed class WildlifeSettlementExclusionTests
    {
        [Test]
        public void WalledSettlementExcludesInteriorAndTwentyMeterMarginAfterOriginShift()
        {
            var root = new GameObject("Wildlife boundary test Settlement");
            root.transform.position = new Vector3(100000f, 0f, 100000f);
            try
            {
                Wall("Wall north", new Vector3(0f, 0f, 10f));
                Wall("Wall south", new Vector3(0f, 0f, -10f));
                Wall("Wall east", new Vector3(10f, 0f, 0f));
                Wall("Wall west", new Vector3(-10f, 0f, 0f));
                Assert.IsTrue(WildlifeSettlementExclusion.IsExcluded(root.transform.position));
                Assert.IsTrue(WildlifeSettlementExclusion.IsExcluded(root.transform.position + Vector3.right * 29f));
                Assert.IsFalse(WildlifeSettlementExclusion.IsExcluded(root.transform.position + Vector3.right * 31f));
                root.transform.position += new Vector3(-500f, 0f, 700f);
                Assert.IsTrue(WildlifeSettlementExclusion.IsExcluded(root.transform.position));
                Assert.IsFalse(WildlifeSettlementExclusion.IsExcluded(root.transform.position + Vector3.right * 31f));
            }
            finally { Object.DestroyImmediate(root); }
            void Wall(string name, Vector3 local)
            {
                var wall = new GameObject(name);
                wall.transform.SetParent(root.transform, false);
                wall.transform.localPosition = local;
            }
        }
    }
}
#endif
