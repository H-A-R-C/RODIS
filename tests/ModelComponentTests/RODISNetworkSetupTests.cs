// <copyright file="RODISNetworkSetupTests.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODISUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using RODIS.ModelRun;
    using RODIS.ModelSettings;
    using System;
    using System.Collections.Generic;

    [TestClass]
    public class RODISNetworkSetupTests
    {
        // ----------------------------------------------------
        // Helpers
        // ----------------------------------------------------

        /// <summary>Creates an outlet node (zero volume, no downstream, always becomes a confluence).</summary>
        private static LegacySTEDIDamNode MakeOutlet(int id, double totalCatchmentAreaKM2)
        {
            return new LegacySTEDIDamNode
            {
                Identifier = id,
                VolumeML = 0.0,
                SurfaceAreaM2 = 0.0,
                TotalCatchmentAreaKM2 = totalCatchmentAreaKM2,
                DemandGroup = "None",
                ResultsGroup = "Outlet",
                NextDownstreamIdentifier = -99,
            };
        }

        /// <summary>Creates a dam node with specified volume and downstream connection.</summary>
        private static LegacySTEDIDamNode MakeDam(int id, double volumeML, double totalCatchmentAreaKM2, int nextDownstreamId)
        {
            return new LegacySTEDIDamNode
            {
                Identifier = id,
                VolumeML = volumeML,
                SurfaceAreaM2 = volumeML * 2000.0, // arbitrary but consistent
                TotalCatchmentAreaKM2 = totalCatchmentAreaKM2,
                DemandGroup = "Default",
                ResultsGroup = "Default",
                NextDownstreamIdentifier = nextDownstreamId,
            };
        }

        /// <summary>Counts nodes of a given type in the array.</summary>
        private static int CountNodeType(LegacySTEDIDamNode[] nodes, ModelElementType type)
        {
            int count = 0;
            foreach (var n in nodes) { if (n.nodeModelType == type) count++; }
            return count;
        }

        /// <summary>Counts nodes that have a uniform inflow subcatchment assigned.</summary>
        private static int CountSubcatchments(LegacySTEDIDamNode[] nodes)
        {
            int count = 0;
            foreach (var n in nodes) { if (n.SubcatchmentInflowID >= 0) count++; }
            return count;
        }

        // ----------------------------------------------------
        // 1. Single dam
        //
        //    [Dam 2] --? [Outlet 1]
        //     CA=30        CA=100
        //
        // ----------------------------------------------------

        [TestClass]
        public class SingleDam
        {
            private LegacySTEDIDamNode[] nodes;

            [TestInitialize]
            public void Setup()
            {
                this.nodes = new LegacySTEDIDamNode[]
                {
                    MakeOutlet(1, totalCatchmentAreaKM2: 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 30.0, nextDownstreamId: 1),
                };
                RODISNetworkSetup.UpdateLegacySTEDINodeProperties(this.nodes, ModelElementType.RepeatingMonthlyDemand);
            }

            [TestMethod]
            public void NodeTypes_OneWaterBodyOneConfluence()
            {
                Assert.AreEqual(ModelElementType.ConfluenceNode, this.nodes[0].nodeModelType, "Outlet should be confluence");
                Assert.AreEqual(ModelElementType.WaterBodyNode, this.nodes[1].nodeModelType, "Dam should be water body");
            }

            [TestMethod]
            public void IntermediateCatchmentAreas()
            {
                // Dam 2: total=30, upstream=0 ? intermediate=30
                Assert.AreEqual(30.0, this.nodes[1].IntermediateCatchmentAreaKM2, 0.001, "Dam intermediate CA");
                // Outlet 1: total=100, upstream=30 (from Dam 2) ? intermediate=70
                Assert.AreEqual(70.0, this.nodes[0].IntermediateCatchmentAreaKM2, 0.001, "Outlet intermediate CA");
            }

            [TestMethod]
            public void ComponentIDs_AllAssigned()
            {
                // Processed in reverse: i=1 (dam) then i=0 (outlet)
                Assert.AreEqual(0, this.nodes[1].WaterBodyNodeID, "Dam gets WaterBodyNodeID=0");
                Assert.AreEqual(0, this.nodes[0].ConfluenceNodeID, "Outlet gets ConfluenceNodeID=0");
                Assert.AreEqual(0, this.nodes[1].RepeatingMonthlyDemandID, "Dam gets demand ID=0");
            }

            [TestMethod]
            public void AllNodesGetSubcatchments()
            {
                // Both nodes have intermediate CA > 0
                Assert.IsTrue(this.nodes[0].SubcatchmentInflowID >= 0, "Outlet should have subcatchment");
                Assert.IsTrue(this.nodes[1].SubcatchmentInflowID >= 0, "Dam should have subcatchment");
                Assert.AreEqual(2, CountSubcatchments(this.nodes));
            }

            [TestMethod]
            public void AllNodesGetRoutingLinks()
            {
                Assert.IsTrue(this.nodes[0].StraightThroughRoutingLinkID >= 0);
                Assert.IsTrue(this.nodes[1].StraightThroughRoutingLinkID >= 0);
            }

            [TestMethod]
            public void DownstreamLinking_DamLinksToOutletConfluence()
            {
                Assert.AreEqual(this.nodes[0].ConfluenceNodeID, this.nodes[1].NextDownstreamConfluenceID,
                    "Dam should link downstream to outlet confluence");
                Assert.AreEqual(-1, this.nodes[1].NextDownstreamWaterBodyID,
                    "Dam should not link to a downstream water body");
            }
        }

        // ----------------------------------------------------
        // 2. Two dams in parallel
        //
        //    [Dam 2] --+
        //     CA=30     +--? [Outlet 1]
        //    [Dam 3] --+      CA=100
        //     CA=20
        //
        // ----------------------------------------------------

        [TestClass]
        public class TwoDamsParallel
        {
            private LegacySTEDIDamNode[] nodes;

            [TestInitialize]
            public void Setup()
            {
                this.nodes = new LegacySTEDIDamNode[]
                {
                    MakeOutlet(1, totalCatchmentAreaKM2: 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 30.0, nextDownstreamId: 1),
                    MakeDam(3, volumeML: 3.0, totalCatchmentAreaKM2: 20.0, nextDownstreamId: 1),
                };
                RODISNetworkSetup.UpdateLegacySTEDINodeProperties(this.nodes, ModelElementType.RepeatingMonthlyDemand);
            }

            [TestMethod]
            public void NodeTypes_TwoWaterBodiesOneConfluence()
            {
                Assert.AreEqual(2, CountNodeType(this.nodes, ModelElementType.WaterBodyNode));
                Assert.AreEqual(1, CountNodeType(this.nodes, ModelElementType.ConfluenceNode));
            }

            [TestMethod]
            public void IntermediateCatchmentAreas()
            {
                // Dam 3: total=20, upstream=0 ? intermediate=20
                Assert.AreEqual(20.0, this.nodes[2].IntermediateCatchmentAreaKM2, 0.001, "Dam 3");
                // Dam 2: total=30, upstream=0 ? intermediate=30
                Assert.AreEqual(30.0, this.nodes[1].IntermediateCatchmentAreaKM2, 0.001, "Dam 2");
                // Outlet: total=100, upstream=30+20=50 ? intermediate=50
                Assert.AreEqual(50.0, this.nodes[0].IntermediateCatchmentAreaKM2, 0.001, "Outlet");
            }

            [TestMethod]
            public void IntermediateCatchmentAreas_SumToTotal()
            {
                double sum = this.nodes[0].IntermediateCatchmentAreaKM2
                           + this.nodes[1].IntermediateCatchmentAreaKM2
                           + this.nodes[2].IntermediateCatchmentAreaKM2;
                Assert.AreEqual(100.0, sum, 0.001, "Intermediate areas should sum to total catchment area");
            }

            [TestMethod]
            public void BothDamsLinkToOutletConfluence()
            {
                Assert.AreEqual(this.nodes[0].ConfluenceNodeID, this.nodes[1].NextDownstreamConfluenceID, "Dam 2 ? outlet");
                Assert.AreEqual(this.nodes[0].ConfluenceNodeID, this.nodes[2].NextDownstreamConfluenceID, "Dam 3 ? outlet");
            }

            [TestMethod]
            public void EachDamGetsDemandID()
            {
                // Processed reverse: Dam 3 gets ID=0, Dam 2 gets ID=1
                Assert.IsTrue(this.nodes[1].RepeatingMonthlyDemandID >= 0, "Dam 2 should have demand ID");
                Assert.IsTrue(this.nodes[2].RepeatingMonthlyDemandID >= 0, "Dam 3 should have demand ID");
                Assert.AreNotEqual(this.nodes[1].RepeatingMonthlyDemandID, this.nodes[2].RepeatingMonthlyDemandID,
                    "Each dam should get a unique demand ID");
            }
        }

        // ----------------------------------------------------
        // 3. Two dams in series
        //
        //    [Dam 3] --? [Dam 2] --? [Outlet 1]
        //     CA=20        CA=60       CA=100
        //
        // ----------------------------------------------------

        [TestClass]
        public class TwoDamsSeries
        {
            private LegacySTEDIDamNode[] nodes;

            [TestInitialize]
            public void Setup()
            {
                this.nodes = new LegacySTEDIDamNode[]
                {
                    MakeOutlet(1, totalCatchmentAreaKM2: 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 60.0, nextDownstreamId: 1),
                    MakeDam(3, volumeML: 3.0, totalCatchmentAreaKM2: 20.0, nextDownstreamId: 2),
                };
                RODISNetworkSetup.UpdateLegacySTEDINodeProperties(this.nodes, ModelElementType.RepeatingMonthlyDemand);
            }

            [TestMethod]
            public void NodeTypes_TwoWaterBodiesOneConfluence()
            {
                Assert.AreEqual(ModelElementType.WaterBodyNode, this.nodes[2].nodeModelType, "Upstream dam");
                Assert.AreEqual(ModelElementType.WaterBodyNode, this.nodes[1].nodeModelType, "Downstream dam");
                Assert.AreEqual(ModelElementType.ConfluenceNode, this.nodes[0].nodeModelType, "Outlet");
            }

            [TestMethod]
            public void IntermediateCatchmentAreas()
            {
                // Dam 3 (upstream): total=20, upstream=0 ? intermediate=20
                Assert.AreEqual(20.0, this.nodes[2].IntermediateCatchmentAreaKM2, 0.001, "Upstream dam");
                // Dam 2 (downstream): total=60, upstream=20 ? intermediate=40
                Assert.AreEqual(40.0, this.nodes[1].IntermediateCatchmentAreaKM2, 0.001, "Downstream dam");
                // Outlet: total=100, upstream=60 ? intermediate=40
                Assert.AreEqual(40.0, this.nodes[0].IntermediateCatchmentAreaKM2, 0.001, "Outlet");
            }

            [TestMethod]
            public void IntermediateCatchmentAreas_SumToTotal()
            {
                double sum = this.nodes[0].IntermediateCatchmentAreaKM2
                           + this.nodes[1].IntermediateCatchmentAreaKM2
                           + this.nodes[2].IntermediateCatchmentAreaKM2;
                Assert.AreEqual(100.0, sum, 0.001);
            }

            [TestMethod]
            public void DownstreamLinking_UpstreamDamLinksToDownstreamDam()
            {
                // Dam 3 ? Dam 2 (both are water bodies)
                Assert.AreEqual(this.nodes[1].WaterBodyNodeID, this.nodes[2].NextDownstreamWaterBodyID,
                    "Upstream dam should link to downstream dam's WaterBodyNodeID");
                Assert.AreEqual(-1, this.nodes[2].NextDownstreamConfluenceID,
                    "Upstream dam should not link to a confluence");
            }

            [TestMethod]
            public void DownstreamLinking_DownstreamDamLinksToOutlet()
            {
                Assert.AreEqual(this.nodes[0].ConfluenceNodeID, this.nodes[1].NextDownstreamConfluenceID,
                    "Downstream dam should link to outlet confluence");
            }

            [TestMethod]
            public void UpstreamCatchmentAreaAccumulation()
            {
                // Outlet should have received total CA from Dam 2 (which includes Dam 3's contribution)
                Assert.AreEqual(60.0, this.nodes[0].CatchmentAreaFromUpstreamLegacyNodes, 0.001);
                // Dam 2 should have received CA from Dam 3
                Assert.AreEqual(20.0, this.nodes[1].CatchmentAreaFromUpstreamLegacyNodes, 0.001);
                // Dam 3 has no upstream
                Assert.AreEqual(0.0, this.nodes[2].CatchmentAreaFromUpstreamLegacyNodes, 0.001);
            }
        }

        // ----------------------------------------------------
        // 4. Three dams in Y shape
        //
        //    [Dam 3] --+
        //     CA=15     +--? [Dam 2] --? [Outlet 1]
        //    [Dam 4] --+      CA=60       CA=100
        //     CA=10
        //
        // ----------------------------------------------------

        [TestClass]
        public class ThreeDamsYShape
        {
            private LegacySTEDIDamNode[] nodes;

            [TestInitialize]
            public void Setup()
            {
                this.nodes = new LegacySTEDIDamNode[]
                {
                    MakeOutlet(1, totalCatchmentAreaKM2: 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 60.0, nextDownstreamId: 1),  // junction dam
                    MakeDam(3, volumeML: 3.0, totalCatchmentAreaKM2: 15.0, nextDownstreamId: 2),  // left branch
                    MakeDam(4, volumeML: 2.0, totalCatchmentAreaKM2: 10.0, nextDownstreamId: 2),  // right branch
                };
                RODISNetworkSetup.UpdateLegacySTEDINodeProperties(this.nodes, ModelElementType.RepeatingMonthlyDemand);
            }

            [TestMethod]
            public void NodeTypes_ThreeWaterBodiesOneConfluence()
            {
                Assert.AreEqual(3, CountNodeType(this.nodes, ModelElementType.WaterBodyNode));
                Assert.AreEqual(1, CountNodeType(this.nodes, ModelElementType.ConfluenceNode));
            }

            [TestMethod]
            public void IntermediateCatchmentAreas()
            {
                // Dam 4 (right): total=10, upstream=0 ? intermediate=10
                Assert.AreEqual(10.0, this.nodes[3].IntermediateCatchmentAreaKM2, 0.001, "Right branch");
                // Dam 3 (left): total=15, upstream=0 ? intermediate=15
                Assert.AreEqual(15.0, this.nodes[2].IntermediateCatchmentAreaKM2, 0.001, "Left branch");
                // Dam 2 (junction): total=60, upstream=10+15=25 ? intermediate=35
                Assert.AreEqual(35.0, this.nodes[1].IntermediateCatchmentAreaKM2, 0.001, "Junction dam");
                // Outlet: total=100, upstream=60 ? intermediate=40
                Assert.AreEqual(40.0, this.nodes[0].IntermediateCatchmentAreaKM2, 0.001, "Outlet");
            }

            [TestMethod]
            public void IntermediateCatchmentAreas_SumToTotal()
            {
                double sum = 0;
                foreach (var n in this.nodes) sum += n.IntermediateCatchmentAreaKM2;
                Assert.AreEqual(100.0, sum, 0.001);
            }

            [TestMethod]
            public void BothBranchDams_LinkToJunctionDam()
            {
                int junctionWBID = this.nodes[1].WaterBodyNodeID;
                Assert.AreEqual(junctionWBID, this.nodes[2].NextDownstreamWaterBodyID, "Left branch ? junction");
                Assert.AreEqual(junctionWBID, this.nodes[3].NextDownstreamWaterBodyID, "Right branch ? junction");
            }

            [TestMethod]
            public void JunctionDam_LinksToOutlet()
            {
                Assert.AreEqual(this.nodes[0].ConfluenceNodeID, this.nodes[1].NextDownstreamConfluenceID,
                    "Junction dam ? outlet confluence");
            }

            [TestMethod]
            public void UpstreamCatchmentAreaAccumulation()
            {
                // Junction dam receives both branches
                Assert.AreEqual(25.0, this.nodes[1].CatchmentAreaFromUpstreamLegacyNodes, 0.001, "Junction receives 15+10");
                // Outlet receives junction dam's total
                Assert.AreEqual(60.0, this.nodes[0].CatchmentAreaFromUpstreamLegacyNodes, 0.001, "Outlet receives 60");
            }

            [TestMethod]
            public void AllFourNodesGetSubcatchmentsAndRoutingLinks()
            {
                Assert.AreEqual(4, CountSubcatchments(this.nodes), "All 4 nodes have intermediate CA > 0");
                foreach (var n in this.nodes)
                    Assert.IsTrue(n.StraightThroughRoutingLinkID >= 0, $"Node {n.Identifier} should have routing link");
            }

            [TestMethod]
            public void DemandIDs_ThreeUniqueDemandModels()
            {
                var demandIDs = new HashSet<int>();
                for (int i = 1; i <= 3; i++)
                {
                    Assert.IsTrue(this.nodes[i].RepeatingMonthlyDemandID >= 0, $"Node {this.nodes[i].Identifier} should have demand ID");
                    demandIDs.Add(this.nodes[i].RepeatingMonthlyDemandID);
                }
                Assert.AreEqual(3, demandIDs.Count, "All three dams should have unique demand IDs");
            }
        }

        // ----------------------------------------------------
        // 5. Three dams in series
        //
        //    [Dam 4] --? [Dam 3] --? [Dam 2] --? [Outlet 1]
        //     CA=15        CA=40       CA=70       CA=100
        //
        // ----------------------------------------------------

        [TestClass]
        public class ThreeDamsSeries
        {
            private LegacySTEDIDamNode[] nodes;

            [TestInitialize]
            public void Setup()
            {
                this.nodes = new LegacySTEDIDamNode[]
                {
                    MakeOutlet(1, totalCatchmentAreaKM2: 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 70.0, nextDownstreamId: 1),  // bottom
                    MakeDam(3, volumeML: 3.0, totalCatchmentAreaKM2: 40.0, nextDownstreamId: 2),  // middle
                    MakeDam(4, volumeML: 2.0, totalCatchmentAreaKM2: 15.0, nextDownstreamId: 3),  // top
                };
                RODISNetworkSetup.UpdateLegacySTEDINodeProperties(this.nodes, ModelElementType.RepeatingMonthlyDemand);
            }

            [TestMethod]
            public void NodeTypes_ThreeWaterBodiesOneConfluence()
            {
                Assert.AreEqual(3, CountNodeType(this.nodes, ModelElementType.WaterBodyNode));
                Assert.AreEqual(1, CountNodeType(this.nodes, ModelElementType.ConfluenceNode));
            }

            [TestMethod]
            public void IntermediateCatchmentAreas()
            {
                // Dam 4 (top): total=15, upstream=0 ? 15
                Assert.AreEqual(15.0, this.nodes[3].IntermediateCatchmentAreaKM2, 0.001, "Top dam");
                // Dam 3 (middle): total=40, upstream=15 ? 25
                Assert.AreEqual(25.0, this.nodes[2].IntermediateCatchmentAreaKM2, 0.001, "Middle dam");
                // Dam 2 (bottom): total=70, upstream=40 ? 30
                Assert.AreEqual(30.0, this.nodes[1].IntermediateCatchmentAreaKM2, 0.001, "Bottom dam");
                // Outlet: total=100, upstream=70 ? 30
                Assert.AreEqual(30.0, this.nodes[0].IntermediateCatchmentAreaKM2, 0.001, "Outlet");
            }

            [TestMethod]
            public void IntermediateCatchmentAreas_SumToTotal()
            {
                double sum = 0;
                foreach (var n in this.nodes) sum += n.IntermediateCatchmentAreaKM2;
                Assert.AreEqual(100.0, sum, 0.001);
            }

            [TestMethod]
            public void DownstreamLinking_ChainIsCorrect()
            {
                // Top ? Middle (both water bodies)
                Assert.AreEqual(this.nodes[2].WaterBodyNodeID, this.nodes[3].NextDownstreamWaterBodyID, "Top ? Middle");
                // Middle ? Bottom (both water bodies)
                Assert.AreEqual(this.nodes[1].WaterBodyNodeID, this.nodes[2].NextDownstreamWaterBodyID, "Middle ? Bottom");
                // Bottom ? Outlet (confluence)
                Assert.AreEqual(this.nodes[0].ConfluenceNodeID, this.nodes[1].NextDownstreamConfluenceID, "Bottom ? Outlet");
            }

            [TestMethod]
            public void UpstreamCatchmentAreaAccumulation_Cascades()
            {
                // Each node accumulates the total CA of its direct upstream neighbour
                Assert.AreEqual(0.0, this.nodes[3].CatchmentAreaFromUpstreamLegacyNodes, 0.001, "Top: no upstream");
                Assert.AreEqual(15.0, this.nodes[2].CatchmentAreaFromUpstreamLegacyNodes, 0.001, "Middle: receives 15 from top");
                Assert.AreEqual(40.0, this.nodes[1].CatchmentAreaFromUpstreamLegacyNodes, 0.001, "Bottom: receives 40 from middle");
                Assert.AreEqual(70.0, this.nodes[0].CatchmentAreaFromUpstreamLegacyNodes, 0.001, "Outlet: receives 70 from bottom");
            }
        }

        // ----------------------------------------------------
        // 6. SetGeneralBypassCapacities — using Y-shape network
        // ----------------------------------------------------

        [TestClass]
        public class BypassCapacities
        {
            /// <summary>Creates minimal settings for bypass testing.</summary>
            private static RODISSettings MakeBypassSettings(
                double bypassCapacityML_d_km2,
                double volumeThresholdML,
                bool useBypass = true)
            {
                var settings = new RODISSettings
                {
                    UseFixedLowFlowBypassCapacity = useBypass,
                    BypassCapacityML_d_km2 = bypassCapacityML_d_km2,
                    BypassSeasonStartDateIgnoreYear = new DateOnly(2000, 5, 1),
                    BypassSeasonEndDateIgnoreYear = new DateOnly(2000, 10, 31),
                };
                // Set bypass volume threshold via the string property and init
                settings.VolumeThresholdForBypass = volumeThresholdML.ToString() + " ML";
                settings.SetVolumeThresholdForBypass(settings.VolumeThresholdForBypass);
                return settings;
            }

            [TestMethod]
            public void BypassApplied_OnlyToDamsAboveThreshold()
            {
                // Y-shape: Dam 2 = 5 ML, Dam 3 = 3 ML, Dam 4 = 2 ML
                // Threshold = 4 ML ? only Dam 2 should get bypass
                var nodes = new List<LegacySTEDIDamNode>
                {
                    MakeOutlet(1, 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 60.0, nextDownstreamId: 1),
                    MakeDam(3, volumeML: 3.0, totalCatchmentAreaKM2: 15.0, nextDownstreamId: 2),
                    MakeDam(4, volumeML: 2.0, totalCatchmentAreaKM2: 10.0, nextDownstreamId: 2),
                };
                var settings = MakeBypassSettings(bypassCapacityML_d_km2: 0.1, volumeThresholdML: 4.0);

                RODISNetworkSetup.SetGeneralBypassCapacities(nodes, settings);

                Assert.IsTrue(nodes[1].IsBypass, "Dam 2 (5 ML) should have bypass");
                Assert.IsFalse(nodes[2].IsBypass, "Dam 3 (3 ML) should not have bypass");
                Assert.IsFalse(nodes[3].IsBypass, "Dam 4 (2 ML) should not have bypass");
            }

            [TestMethod]
            public void BypassCapacity_ProportionalToCatchmentArea()
            {
                var nodes = new List<LegacySTEDIDamNode>
                {
                    MakeOutlet(1, 100.0),
                    MakeDam(2, volumeML: 10.0, totalCatchmentAreaKM2: 60.0, nextDownstreamId: 1),
                    MakeDam(3, volumeML: 10.0, totalCatchmentAreaKM2: 30.0, nextDownstreamId: 1),
                };
                var settings = MakeBypassSettings(bypassCapacityML_d_km2: 0.05, volumeThresholdML: 1.0);

                RODISNetworkSetup.SetGeneralBypassCapacities(nodes, settings);

                // Dam 2: 60 km² × 0.05 = 3.0 ML/d
                Assert.AreEqual(3.0, nodes[1].BypassCapacity, 0.001, "Dam 2 bypass capacity");
                // Dam 3: 30 km² × 0.05 = 1.5 ML/d
                Assert.AreEqual(1.5, nodes[2].BypassCapacity, 0.001, "Dam 3 bypass capacity");
            }

            [TestMethod]
            public void BypassCapacity_SeasonDatesFromSettings()
            {
                var nodes = new List<LegacySTEDIDamNode>
                {
                    MakeOutlet(1, 100.0),
                    MakeDam(2, volumeML: 10.0, totalCatchmentAreaKM2: 50.0, nextDownstreamId: 1),
                };
                var settings = MakeBypassSettings(bypassCapacityML_d_km2: 0.1, volumeThresholdML: 1.0);

                RODISNetworkSetup.SetGeneralBypassCapacities(nodes, settings);

                Assert.AreEqual(new DateOnly(2000, 5, 1), nodes[1].BypassSeasonStartDateIgnoreYear);
                Assert.AreEqual(new DateOnly(2000, 10, 31), nodes[1].BypassSeasonEndDateIgnoreYear);
            }

            [TestMethod]
            public void BypassDisabled_NoDamsGetBypass()
            {
                var nodes = new List<LegacySTEDIDamNode>
                {
                    MakeOutlet(1, 100.0),
                    MakeDam(2, volumeML: 10.0, totalCatchmentAreaKM2: 50.0, nextDownstreamId: 1),
                };
                var settings = MakeBypassSettings(bypassCapacityML_d_km2: 0.1, volumeThresholdML: 1.0, useBypass: false);

                RODISNetworkSetup.SetGeneralBypassCapacities(nodes, settings);

                Assert.IsFalse(nodes[1].IsBypass, "Bypass disabled ? no bypass on dam");
                Assert.AreEqual(0.0, nodes[1].BypassCapacity, 0.001);
            }

            [TestMethod]
            public void BypassThresholdZero_AllDamsWithVolumeGetBypass()
            {
                var nodes = new List<LegacySTEDIDamNode>
                {
                    MakeOutlet(1, 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 30.0, nextDownstreamId: 1),
                    MakeDam(3, volumeML: 0.5, totalCatchmentAreaKM2: 10.0, nextDownstreamId: 1),
                };
                // Threshold = 0 ? any dam with volume > 0 qualifies
                var settings = MakeBypassSettings(bypassCapacityML_d_km2: 0.1, volumeThresholdML: 0.0);

                RODISNetworkSetup.SetGeneralBypassCapacities(nodes, settings);

                Assert.IsTrue(nodes[1].IsBypass, "Dam 2 (5 ML > 0) should have bypass");
                Assert.IsTrue(nodes[2].IsBypass, "Dam 3 (0.5 ML > 0) should have bypass");
            }

            [TestMethod]
            public void OutletNode_NeverGetsBypass()
            {
                var nodes = new List<LegacySTEDIDamNode>
                {
                    MakeOutlet(1, 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 30.0, nextDownstreamId: 1),
                };
                var settings = MakeBypassSettings(bypassCapacityML_d_km2: 0.1, volumeThresholdML: 0.0);

                RODISNetworkSetup.SetGeneralBypassCapacities(nodes, settings);

                Assert.IsFalse(nodes[0].IsBypass, "Outlet (volume=0) should never get bypass");
            }
        }

        // ----------------------------------------------------
        // 7. Edge cases
        // ----------------------------------------------------

        [TestClass]
        public class EdgeCases
        {
            [TestMethod]
            public void UpstreamCatchmentExceedsTotal_IntermediateClampedToZero()
            {
                // Deliberately set upstream CA > total CA on the downstream dam
                // This can happen with inconsistent input data
                var nodes = new LegacySTEDIDamNode[]
                {
                    MakeOutlet(1, totalCatchmentAreaKM2: 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 10.0, nextDownstreamId: 1), // total=10 but will receive 20 from upstream
                    MakeDam(3, volumeML: 3.0, totalCatchmentAreaKM2: 20.0, nextDownstreamId: 2), // CA=20 > Dam 2's total
                };
                RODISNetworkSetup.UpdateLegacySTEDINodeProperties(nodes, ModelElementType.RepeatingMonthlyDemand);

                // Dam 2: intermediate = max(0, 10 - 20) = 0 (clamped)
                Assert.AreEqual(0.0, nodes[1].IntermediateCatchmentAreaKM2, 0.001,
                    "Intermediate CA should be clamped to zero, not negative");
            }

            [TestMethod]
            public void ZeroIntermediateArea_NoSubcatchmentAssigned()
            {
                // Same setup as above — Dam 2 gets zero intermediate area
                var nodes = new LegacySTEDIDamNode[]
                {
                    MakeOutlet(1, totalCatchmentAreaKM2: 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 10.0, nextDownstreamId: 1),
                    MakeDam(3, volumeML: 3.0, totalCatchmentAreaKM2: 20.0, nextDownstreamId: 2),
                };
                RODISNetworkSetup.UpdateLegacySTEDINodeProperties(nodes, ModelElementType.RepeatingMonthlyDemand);

                Assert.AreEqual(-1, nodes[1].SubcatchmentInflowID,
                    "Zero intermediate CA ? no subcatchment assigned");
            }

            [TestMethod]
            public void TimeSeriesDemand_AssignsTimeSeriesDemandIDs()
            {
                var nodes = new LegacySTEDIDamNode[]
                {
                    MakeOutlet(1, totalCatchmentAreaKM2: 100.0),
                    MakeDam(2, volumeML: 5.0, totalCatchmentAreaKM2: 30.0, nextDownstreamId: 1),
                    MakeDam(3, volumeML: 3.0, totalCatchmentAreaKM2: 20.0, nextDownstreamId: 1),
                };
                RODISNetworkSetup.UpdateLegacySTEDINodeProperties(nodes, ModelElementType.TimeSeriesDemand);

                Assert.IsTrue(nodes[1].TimeSeriesDemandID >= 0, "Dam 2 should have time series demand ID");
                Assert.IsTrue(nodes[2].TimeSeriesDemandID >= 0, "Dam 3 should have time series demand ID");
                // Repeating monthly should NOT be assigned
                Assert.AreEqual(-1, nodes[1].RepeatingMonthlyDemandID);
                Assert.AreEqual(-1, nodes[2].RepeatingMonthlyDemandID);
            }
        }
    }
}