// <copyright file="WaterBodyModelNodeTests.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>
namespace RODISUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using RODIS.ModelRun;
    using System;

    [TestClass]
    public class WaterBodyModelNodeTests
    {
        private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

        /// <summary>Creates a water body node with sensible defaults for a 1 ML dam.</summary>
        private static WaterBodyModelNode MakeNode(
            double maxVolumeML = 1.0,
            double surfaceAreaM2 = 10000.0,
            double startVolumeML = 0.5,
            double exponent = 1.314)
        {
            WaterBodyModelNode result = new WaterBodyModelNode()
            {
                MaxStorageCapacityVolumeAtSpill = maxVolumeML,
                StorageCapacityVolumeAtSpill = maxVolumeML,
                SurfaceAreaAtSpill = surfaceAreaM2,
                StartTimeStepVolumeInStorage = startVolumeML,
                VolumeInStorage = startVolumeML,
                VolumeSurfaceAreaRelationshipExponent = exponent,
                StartBypassDate = DateTime.MinValue,
                EndBypassDate = DateTime.MinValue,
                StartPumpedInflowDate = DateTime.MinValue,
                EndPumpedInflowDate = DateTime.MinValue,
                BypassFlowCapacity = 0.0,
                PumpedInflowCapacity = 0.0,
                SeepageLossRateAtFull = 0.0,
                SeepageLossVolumeRelationshipExponent = 0.0,
                UpstreamFlow = 0.0,
                UpstreamFlowFromBypass = 0.0,
                UpstreamFlowFromSpill = 0.0,
                UpstreamFlowFromCatchment = 0.0,
                Rainfall = 0.0,
                Evaporation = 0.0,
                UnrestrictedDemand = 0.0,
            };

            result.SetStartDate(new DateTime(2000, 1, 1));
            result.SetEndDate(new DateTime(2030, 12, 31));

            return result;
        }

        // ----------------------------------------------------
        // CalculateSurfaceArea
        // ----------------------------------------------------

        [TestMethod]
        public void CalculateSurfaceArea_FullStorage_ReturnsSurfaceAreaAtSpill()
        {
            var node = MakeNode(maxVolumeML: 10.0, surfaceAreaM2: 5000.0, startVolumeML: 10.0);
            double sa = node.CalculateSurfaceArea();
            Assert.AreEqual(5000.0, sa, 0.01);
        }

        [TestMethod]
        public void CalculateSurfaceArea_EmptyStorage_ReturnsZero()
        {
            var node = MakeNode(maxVolumeML: 10.0, surfaceAreaM2: 5000.0, startVolumeML: 0.0);
            node.VolumeInStorage = 0.0;
            double sa = node.CalculateSurfaceArea();
            Assert.AreEqual(0.0, sa, 0.001);
        }

        [TestMethod]
        public void CalculateSurfaceArea_HalfVolume_LessThanFull()
        {
            var node = MakeNode(maxVolumeML: 10.0, surfaceAreaM2: 5000.0, startVolumeML: 5.0);
            double sa = node.CalculateSurfaceArea();
            Assert.IsTrue(sa > 0.0 && sa < 5000.0, $"Expected between 0 and 5000, got {sa}");
        }

        [TestMethod]
        public void CalculateSurfaceArea_ZeroCapacity_ReturnsZero()
        {
            var node = MakeNode(maxVolumeML: 0.0, surfaceAreaM2: 5000.0, startVolumeML: 0.0);
            node.VolumeInStorage = 0.0;
            double sa = node.CalculateSurfaceArea();
            Assert.AreEqual(0.0, sa, 0.001);
        }

        // ----------------------------------------------------
        // RunTimeStep — before dam exists (pass-through)
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_BeforeStartDate_PassThroughFlow()
        {
            var node = MakeNode();
            node.SetStartDate(new DateTime(2010, 1, 1));
            node.UpstreamFlow = 5.0;
            node.UpstreamFlowFromBypass = 1.0;
            node.UpstreamFlowFromSpill = 2.0;
            node.UpstreamFlowFromCatchment = 2.0;

            node.RunTimeStep(new DateTime(2005, 6, 15), OneDay, false);

            Assert.AreEqual(5.0, node.DownstreamFlow, 0.001, "Should pass through upstream flow");
            Assert.AreEqual(0.0, node.VolumeInStorage, 0.001, "No storage before existence");
            Assert.AreEqual(0.0, node.StorageCapacityVolumeAtSpill, 0.001);
        }

        [TestMethod]
        public void RunTimeStep_AfterEndDate_PassThroughFlow()
        {
            var node = MakeNode();
            node.SetEndDate(new DateTime(2010, 1, 1));
            node.UpstreamFlow = 3.0;

            node.RunTimeStep(new DateTime(2015, 6, 15), OneDay, false);

            Assert.AreEqual(3.0, node.DownstreamFlow, 0.001, "Should pass through upstream flow");
            Assert.AreEqual(0.0, node.VolumeInStorage, 0.001);
        }

        // ----------------------------------------------------
        // RunTimeStep — dam exists, zero inflow, no demand
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_ZeroInflow_ZeroDemand_VolumeUnchanged()
        {
            var node = MakeNode(maxVolumeML: 10.0, startVolumeML: 5.0);
            node.UpstreamFlow = 0.0;
            node.Rainfall = 0.0;
            node.Evaporation = 0.0;
            node.UnrestrictedDemand = 0.0;

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, false);

            Assert.AreEqual(5.0, node.VolumeInStorage, 0.001, "Volume should be unchanged");
            Assert.AreEqual(0.0, node.DownstreamFlow, 0.001, "No spill expected");
        }

        // ----------------------------------------------------
        // RunTimeStep — spill when inflow exceeds capacity
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_InflowExceedsCapacity_Spills()
        {
            var node = MakeNode(maxVolumeML: 10.0, startVolumeML: 8.0);
            node.UpstreamFlow = 5.0; // 8 + 5 = 13 > 10

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, false);

            Assert.AreEqual(10.0, node.VolumeInStorage, 0.001, "Should be full");
            Assert.AreEqual(3.0, node.DownstreamFlowFromSpill, 0.001, "3 ML should spill");
        }

        // ----------------------------------------------------
        // RunTimeStep — demand extraction limited to available volume
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_DemandExceedsStorage_LimitedToAvailable()
        {
            var node = MakeNode(maxVolumeML: 10.0, startVolumeML: 2.0);
            node.UpstreamFlow = 0.0;
            node.UnrestrictedDemand = 5.0; // more than the 2 ML available

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, false);

            // After net rainfall (0) and seepage (0), volume = 2.0
            // Demand extracted = min(5.0, 2.0) = 2.0
            Assert.AreEqual(2.0, node.DemandVolumeExtracted, 0.001);
        }

        // ----------------------------------------------------
        // RunTimeStep — seepage loss
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_SeepageLoss_ReducesVolume()
        {
            var node = MakeNode(maxVolumeML: 10.0, startVolumeML: 10.0);
            node.SeepageLossRateAtFull = 0.5; // 0.5 ML/timestep at full
            node.SeepageLossVolumeRelationshipExponent = 1.0; // linear
            node.UpstreamFlow = 0.0;

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, false);

            Assert.AreEqual(0.5, node.SeepageLossVolume, 0.001);
            Assert.IsTrue(node.VolumeInStorage < 10.0, "Seepage should reduce volume");
        }

        // ----------------------------------------------------
        // RunTimeStep — bypass active within date and season
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_BypassActive_ReducesStorageInflow()
        {
            var node = MakeNode(maxVolumeML: 100.0, startVolumeML: 50.0);
            node.UpstreamFlow = 10.0;
            node.StartBypassDate = new DateTime(2000, 1, 1);
            node.EndBypassDate = new DateTime(2030, 12, 31);
            node.BypassFlowCapacity = 3.0; // ML/d
            node.BypassSeasonStartDateIgnoreYear = new DateOnly(2000, 1, 1);
            node.BypassSeasonEndDateIgnoreYear = new DateOnly(2000, 12, 31);

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, false);

            Assert.AreEqual(3.0, node.DownstreamFlowFromBypass, 0.001, "Bypass should pass 3 ML/d");
            // Storage gets inflow minus bypass = 10 - 3 = 7
            Assert.IsTrue(node.VolumeInStorage > 50.0, "Storage should increase by net inflow after bypass");
        }

        // ----------------------------------------------------
        // RunTimeStep — bypass outside date window, no bypass
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_BypassOutsideDateWindow_NoBypassed()
        {
            var node = MakeNode(maxVolumeML: 100.0, startVolumeML: 50.0);
            node.UpstreamFlow = 10.0;
            node.StartBypassDate = new DateTime(2020, 1, 1);
            node.EndBypassDate = new DateTime(2030, 12, 31);
            node.BypassFlowCapacity = 3.0;

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, false); // before bypass start

            Assert.AreEqual(0.0, node.DownstreamFlowFromBypass, 0.001, "Bypass not active");
        }

        // ----------------------------------------------------
        // RunTimeStep — pumped inflow fills storage
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_PumpedInflow_LimitedToSpareCapacity()
        {
            var node = MakeNode(maxVolumeML: 10.0, startVolumeML: 8.0);
            node.UpstreamFlow = 0.0;
            node.StartPumpedInflowDate = new DateTime(2000, 1, 1);
            node.EndPumpedInflowDate = new DateTime(2030, 12, 31);
            node.PumpedInflowCapacity = 5.0; // ML/d
            node.PumpedInflowSeasonStartDateIgnoreYear = new DateOnly(2000, 1, 1);
            node.PumpedInflowSeasonEndDateIgnoreYear = new DateOnly(2000, 12, 31);

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, false);

            // Spare capacity = 10 - 8 = 2 ML, pump capacity = 5, so pumped = 2
            Assert.AreEqual(2.0, node.PumpedInflow, 0.001, "Pumped inflow limited to spare capacity");
            Assert.AreEqual(10.0, node.VolumeInStorage, 0.001, "Should be full after pumping");
        }

        // ----------------------------------------------------
        // RunTimeStep — mass balance check
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_MassBalance_MisclosureNearZero()
        {
            var node = MakeNode(maxVolumeML: 100.0, startVolumeML: 50.0);
            node.UpstreamFlow = 10.0;
            node.Rainfall = 5.0;    // mm
            node.Evaporation = 3.0; // mm
            node.UnrestrictedDemand = 2.0;
            node.SeepageLossRateAtFull = 0.1;
            node.SeepageLossVolumeRelationshipExponent = 1.0;

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, false);

            Assert.AreEqual(0.0, node.VolumeBalanceMisclosure, 0.01, "Mass balance misclosure should be near zero");
        }

        // ----------------------------------------------------
        // RunTimeStep — legacy vs non-legacy surface area for rainfall
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_LegacyMode_UsesSurfaceAreaAtSpill()
        {
            var node = MakeNode(maxVolumeML: 10.0, surfaceAreaM2: 10000.0, startVolumeML: 5.0);
            node.Rainfall = 10.0; // mm
            node.Evaporation = 0.0;
            node.UpstreamFlow = 0.0;

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, isLegacySTEDICalculationMethods: true);

            // Legacy: rainfall vol = 10mm * 10000m² * 1e-6 = 0.1 ML (uses SA at spill, not stored)
            Assert.AreEqual(0.1, node.RainfallVolume, 0.001);
        }

        [TestMethod]
        public void RunTimeStep_NonLegacyMode_UsesStoredSurfaceArea()
        {
            var node = MakeNode(maxVolumeML: 10.0, surfaceAreaM2: 10000.0, startVolumeML: 5.0);
            node.Rainfall = 10.0; // mm
            node.Evaporation = 0.0;
            node.UpstreamFlow = 0.0;

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, isLegacySTEDICalculationMethods: false);

            // Non-legacy: SA based on stored volume (< SA at spill), so rainfall volume < 0.1 ML
            Assert.IsTrue(node.RainfallVolume < 0.1, $"Expected < 0.1, got {node.RainfallVolume}");
            Assert.IsTrue(node.RainfallVolume > 0.0, "Should still have some rainfall volume");
        }

        // ----------------------------------------------------
        // RunTimeStep — evaporation capped at available volume
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_EvaporationExceedsVolume_Capped()
        {
            var node = MakeNode(maxVolumeML: 10.0, surfaceAreaM2: 100000.0, startVolumeML: 0.01);
            node.Rainfall = 0.0;
            node.Evaporation = 100.0; // very high
            node.UpstreamFlow = 0.0;

            node.RunTimeStep(new DateTime(2010, 6, 15), OneDay, false);

            Assert.IsTrue(node.EvaporationVolume <= 0.01 + 0.001, "Evaporation should not exceed starting volume + rainfall");
        }
    }
}