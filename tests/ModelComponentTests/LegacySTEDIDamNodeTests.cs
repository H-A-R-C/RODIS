namespace STEDIUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using STEDI.ModelRun;
    using System;

    [TestClass]
    public class LegacySTEDIDamNodeTests
    {
        private static LegacySTEDIDamNode MakeDamNode(double volumeML = 5.0, double saM2 = 10000.0, double caKM2 = 50.0)
        {
            return new LegacySTEDIDamNode
            {
                Identifier = 42,
                VolumeML = volumeML,
                SurfaceAreaM2 = saM2,
                TotalCatchmentAreaKM2 = caKM2,
                DemandGroup = "Stock",
                ResultsGroup = "Default",
                IsBypass = false,
                BypassCapacity = 0.0,
                IsWinterfill = false,
                WinterfillRate = 0.0,
                NextDownstreamIdentifier = 1,
            };
        }

        // ────────────────────────────────────────────────────
        // GetWaterBodyModelNode — basic property mapping
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void GetWaterBodyModelNode_MapsProperties()
        {
            var legacy = MakeDamNode(volumeML: 5.0, saM2: 12000.0, caKM2: 50.0);
            var node = legacy.GetWaterBodyModelNode();

            Assert.AreEqual("42", node.Label);
            Assert.AreEqual(5.0, node.MaxStorageCapacityVolumeAtSpill, 0.001);
            Assert.AreEqual(12000.0, node.SurfaceAreaAtSpill, 0.001);
            Assert.AreEqual("Stock", node.DemandGroup);
            Assert.AreEqual("Default", node.ReportingGroup);
        }

        // ────────────────────────────────────────────────────
        // GetWaterBodyModelNode — bypass enabled
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void GetWaterBodyModelNode_BypassEnabled_SetsCapacityAndDates()
        {
            var legacy = MakeDamNode();
            legacy.IsBypass = true;
            legacy.BypassCapacity = 2.5;
            legacy.BypassSeasonStartDateIgnoreYear = new DateOnly(2000, 5, 1);
            legacy.BypassSeasonEndDateIgnoreYear = new DateOnly(2000, 10, 31);

            var node = legacy.GetWaterBodyModelNode();

            Assert.AreEqual(2.5, node.BypassFlowCapacity, 0.001);
            Assert.AreEqual(node.StartDate, node.StartBypassDate, "Bypass start should match node start");
            Assert.AreEqual(new DateOnly(2000, 5, 1), node.BypassSeasonStartDateIgnoreYear);
            Assert.AreEqual(new DateOnly(2000, 10, 31), node.BypassSeasonEndDateIgnoreYear);
        }

        // ────────────────────────────────────────────────────
        // GetWaterBodyModelNode — bypass disabled
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void GetWaterBodyModelNode_BypassDisabled_ZeroCapacity()
        {
            var legacy = MakeDamNode();
            legacy.IsBypass = false;

            var node = legacy.GetWaterBodyModelNode();

            Assert.AreEqual(0.0, node.BypassFlowCapacity, 0.001);
        }

        // ────────────────────────────────────────────────────
        // GetWaterBodyModelNode — winterfill (pumped inflow)
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void GetWaterBodyModelNode_WinterfillEnabled_SetsPumpedInflow()
        {
            var legacy = MakeDamNode();
            legacy.IsWinterfill = true;
            legacy.WinterfillRate = 1.5;
            legacy.WinterfillSeasonStartDateIgnoreYear = new DateOnly(2000, 6, 1);
            legacy.WinterfillSeasonEndDateIgnoreYear = new DateOnly(2000, 9, 30);

            var node = legacy.GetWaterBodyModelNode();

            Assert.AreEqual(1.5, node.PumpedInflowCapacity, 0.001);
            Assert.AreEqual(new DateOnly(2000, 6, 1), node.PumpedInflowSeasonStartDateIgnoreYear);
        }

        // ────────────────────────────────────────────────────
        // GetConfluenceModelNode — zero-volume node
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void GetConfluenceModelNode_CorrectType()
        {
            var legacy = MakeDamNode(volumeML: 0.0);
            var node = legacy.GetConfluenceModelNode();

            Assert.AreEqual(ModelElementType.ConfluenceNode, node.ModelElementType);
            Assert.AreEqual("42", node.Label);
        }

        // ────────────────────────────────────────────────────
        // GetSubcatchmentInflowModel
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void GetUniformInflowSubcatchmentModel_SetsArea()
        {
            var legacy = MakeDamNode();
            legacy.IntermediateCatchmentAreaKM2 = 25.0;

            var subcatch = legacy.GetSubcatchmentInflowModel(IsLegacySTEDICatchmentInflows: true);

            Assert.AreEqual(25.0, subcatch.AreaKM2, 0.001);
        }
    }
}