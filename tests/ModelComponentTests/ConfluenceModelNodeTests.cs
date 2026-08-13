namespace RODISUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using RODIS.ModelRun;
    using System;

    [TestClass]
    public class ConfluenceModelNodeTests
    {
        private static ConfluenceModelNode MakeNode(double upstreamFlow, double upstreamBypass = 0, double upstreamSpill = 0, double upstreamCatchment = 0)
        {
            return new ConfluenceModelNode
            {
                UpstreamFlow = upstreamFlow,
                UpstreamFlowFromBypass = upstreamBypass,
                UpstreamFlowFromSpill = upstreamSpill,
                UpstreamFlowFromCatchment = upstreamCatchment,
                SumUpstreamAndPumpedInflows = 0.0,
                SumDaysOfUpstreamAndPumpedInflows = 0.0,
            };
        }

        [TestMethod]
        public void RunTimeStep_PassesThroughAllFlowComponents()
        {
            var node = MakeNode(10.0, 3.0, 5.0, 2.0);
            node.RunTimeStep(new DateTime(2010, 6, 15), TimeSpan.FromDays(1), false);

            Assert.AreEqual(10.0, node.DownstreamFlow, 0.001);
            Assert.AreEqual(3.0, node.DownstreamFlowFromBypass, 0.001);
            Assert.AreEqual(5.0, node.DownstreamFlowFromSpill, 0.001);
            Assert.AreEqual(2.0, node.DownstreamFlowFromCatchment, 0.001);
        }

        [TestMethod]
        public void RunTimeStep_AccumulatesInflowAndDays()
        {
            var node = MakeNode(10.0);
            var ts = TimeSpan.FromDays(7);

            node.RunTimeStep(new DateTime(2010, 1, 1), ts, false);
            node.RunTimeStep(new DateTime(2010, 1, 8), ts, false);

            Assert.AreEqual(20.0, node.SumUpstreamAndPumpedInflows, 0.001, "Two steps × 10 ML");
            Assert.AreEqual(14.0, node.SumDaysOfUpstreamAndPumpedInflows, 0.001, "Two steps × 7 days");
        }

        [TestMethod]
        public void RunTimeStep_PumpedInflowAlwaysZero()
        {
            var node = MakeNode(10.0);
            node.RunTimeStep(new DateTime(2010, 6, 15), TimeSpan.FromDays(1), false);

            Assert.AreEqual(0.0, node.PumpedInflow, 0.001);
            Assert.AreEqual(0.0, node.PumpedInflowCapacity, 0.001);
            Assert.AreEqual(0.0, node.PumpedInflowCapacityAtTimeStep, 0.001);
        }

        [TestMethod]
        public void RunTimeStep_MisclosureZero()
        {
            var node = MakeNode(10.0);
            node.RunTimeStep(new DateTime(2010, 6, 15), TimeSpan.FromDays(1), false);
            Assert.AreEqual(0.0, node.VolumeBalanceMisclosure, 0.001);
        }
    }
}