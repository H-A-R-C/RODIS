namespace STEDIUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using STEDI.ModelRun;

    [TestClass]
    public class StraightThroughRoutingLinkTests
    {
        [TestMethod]
        public void RunTimeStep_DownstreamEqualsUpstream()
        {
            var link = new StraightThroughRoutingLink
            {
                UpstreamFlow = 12.5,
                UpstreamFlowFromBypass = 3.0,
                UpstreamFlowFromSpill = 5.0,
                UpstreamFlowFromCatchment = 4.5,
            };

            link.RunTimeStep();

            Assert.AreEqual(12.5, link.DownstreamFlow, 0.001);
            Assert.AreEqual(3.0, link.DownstreamFlowFromBypass, 0.001);
            Assert.AreEqual(5.0, link.DownstreamFlowFromSpill, 0.001);
            Assert.AreEqual(4.5, link.DownstreamFlowFromCatchment, 0.001);
        }

        [TestMethod]
        public void RunTimeStep_MisclosureIsZero()
        {
            var link = new StraightThroughRoutingLink { UpstreamFlow = 7.77 };
            link.RunTimeStep();
            Assert.AreEqual(0.0, link.VolumeBalanceMisclosure, 0.001);
        }

        [TestMethod]
        public void RunTimeStep_ZeroFlow()
        {
            var link = new StraightThroughRoutingLink { UpstreamFlow = 0.0 };
            link.RunTimeStep();
            Assert.AreEqual(0.0, link.DownstreamFlow, 0.001);
            Assert.AreEqual(0.0, link.VolumeBalanceMisclosure, 0.001);
        }
    }
}