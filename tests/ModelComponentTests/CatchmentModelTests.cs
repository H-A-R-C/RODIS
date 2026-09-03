// <copyright file="CatchmentModelTests.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>
namespace RODISUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using RODIS.ModelRun;
    using RODIS.ModelSettings;
    using RODIS.Series;
    using System;
    using System.Collections.Generic;

    // ------------------------------------------------------------------------
    //  Helper: builds small hand-wired test catchments without GIS files
    // ------------------------------------------------------------------------

    /// <summary>
    /// Factory methods for building small test catchments that can be initialised
    /// and run without GIS files or external JSON. Uses CatchmentModelRunner to
    /// match the production API exactly.
    /// </summary>
    internal static class TestCatchmentBuilder
    {
        // -- Time series helpers ------------------------------------------

        /// <summary>Creates a daily TimeSeriesValue array with a constant value.</summary>
        private static TimeSeriesValue[] MakeConstantTimeSeries(DateTime start, DateTime end, double value)
        {
            var list = new List<TimeSeriesValue>();
            for (DateTime d = start; d <= end; d = d.AddDays(1))
            {
                list.Add(new TimeSeriesValue { Time = d, Value = value, IsValid = true });
            }
            return list.ToArray();
        }

        // -- Settings helper ---------------------------------------------

        /// <summary>Builds minimal RODISSettings for a test run.</summary>
        private static RODISSettings BuildSettings(double startFraction = 0.0, double annualDemandFactor = 0.0)
        {
            return new RODISSettings
            {
                CalculateUnimpactedGivenObserved = true,
                UseLegacySTEDICalculationMethods = false,
                MaximumProportionOfCatchmentImpounded = 0.999,
                AllStoragesProportionFullAtStartOfRun = startFraction,
                UseSpecificDamNetworkDetails = true,
                UseVolumeThresholdForDemandGroups = false,
                UseFixedLowFlowBypassCapacity = false,
                VolumeSurfaceAreaEquation = new EquationParser
                {
                    VariablesWithDescriptions = new Dictionary<string, string> { { "SA", "Surface area in m " } },
                    Equation = "0.0001449275*SA^1.314",
                },
                OutletStreamName = "Test Creek",
                OutletNodeName = "Test Outlet",
                OutletNodeNumber = "999999",
                RepeatingMonthlyDemandGroups = new Dictionary<string, FarmDamRepeatingMonthlyDemandModel>
                {
                    ["RunoffDams"] = new FarmDamRepeatingMonthlyDemandModel
                    {
                        DemandGroup = "RunoffDams",
                        AnnualDemandFactor = annualDemandFactor,
                        MonthlyDemandProportions = new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
                    },
                },
            };
        }

        // ----------------------------------------------------------------
        //  Network builders — return initialised CatchmentModelRunner
        // ----------------------------------------------------------------

        /// <summary>
        /// Y-shaped: D1 (0.5 km²) ? Outlet(3), D2 (0.4 km²) ? Outlet(3).
        /// Outlet = 0.3 km² local. Total = 1.2 km².
        /// </summary>
        public static CatchmentModelRunner BuildYNetwork(double startFraction = 0.0)
        {
            var nodes = new[]
            {
                MakeDam(1, 3, 500, 1.0, 0.5),
                MakeDam(2, 3, 400, 0.8, 0.4),
                MakeOutlet(3, 0.3),
            };
            return BuildRunner(nodes, startFraction);
        }

        /// <summary>
        /// Chain: D1 (0.5 km²) ? D2 (0.3 km²) ? Outlet(3, 0.2 km²). Total = 1.0 km².
        /// </summary>
        public static CatchmentModelRunner BuildChainNetwork(double startFraction = 0.0, double annualDemandFactor = 0.0)
        {
            var nodes = new[]
            {
                MakeDam(1, 2, 500, 1.0, 0.5),
                MakeDam(2, 3, 400, 0.8, 0.3),
                MakeOutlet(3, 0.2),
            };

            return BuildRunner(nodes, startFraction, annualDemandFactor);
        }

        /// <summary>
        /// Mixed: D1 (0.5) ? D3, D2 (0.4) ? D3, D3 (0.3) ? Outlet(5),
        /// D4 (0.6) ? Outlet(5). Outlet = 0.2 km². Total = 2.0 km².
        /// </summary>
        public static CatchmentModelRunner BuildMixedNetwork(double startFraction = 0.0)
        {
            var nodes = new[]
            {
                MakeDam(1, 3, 500, 1.0, 0.5),
                MakeDam(2, 3, 400, 0.8, 0.4),
                MakeDam(3, 5, 300, 0.6, 0.3),
                MakeDam(4, 5, 600, 1.2, 0.6),
                MakeOutlet(5, 0.2),
            };
            return BuildRunner(nodes, startFraction);
        }

        // ----------------------------------------------------------------
        //  Core builder: initialises model + loads time series
        // ----------------------------------------------------------------

        /// <summary>
        /// Creates a LegacySTEDIDamNode representing a dam.
        /// Note: legacy nodes don't support per-node start/end existence dates.
        /// All dams exist for the entire simulation period.
        /// </summary>
        private static LegacySTEDIDamNode MakeDam(
            int id, int downstreamId,
            double surfaceAream2, double volumeML, double catchmentAreakm2)
        {
            return new LegacySTEDIDamNode
            {
                Identifier = id,
                NextDownstreamIdentifier = downstreamId,
                SurfaceAreaM2 = surfaceAream2,
                VolumeML = volumeML,
                IntermediateCatchmentAreaKM2 = catchmentAreakm2,
                DemandGroup = "RunoffDams",
                ResultsGroup = "Rural",
            };
        }

        /// <summary>
        /// Creates a LegacySTEDIDamNode representing a confluence (outlet).
        /// Volume and surface area = 0 ? model treats it as a ConfluenceNode.
        /// </summary>
        private static LegacySTEDIDamNode MakeOutlet(
            int id, double catchmentAreakm2)
        {
            return new LegacySTEDIDamNode
            {
                Identifier = id,
                NextDownstreamIdentifier = 0,
                SurfaceAreaM2 = 0.0,
                VolumeML = 0.0,
                IntermediateCatchmentAreaKM2 = catchmentAreakm2,
                DemandGroup = string.Empty,
                ResultsGroup = "Outlet",
            };
        }

        private static CatchmentModelRunner BuildRunner(
            LegacySTEDIDamNode[] nodes,
            double startFraction,
            double annualDemandFactor = 0.0)
        {
            RODISSettings settings = BuildSettings(startFraction, annualDemandFactor);
            CatchmentModelRunner runner = new CatchmentModelRunner();

            runner.catchmentModel.Initialise(nodes, settings);

            return runner;
        }

        /// <summary>
        /// Loads constant-value daily time series into the runner, sets up output
        /// time series, runs the simulation, and returns the runner for assertions.
        /// </summary>
        /// <param name="runner">Initialised CatchmentModelRunner (from a Build method).</param>
        /// <param name="start">Simulation start date.</param>
        /// <param name="end">Simulation end date.</param>
        /// <param name="rainfall">Constant daily rainfall in mm. Default 3.0.</param>
        /// <param name="pet">Constant daily PET in mm. Default 4.0.</param>
        /// <param name="observedFlow">Constant daily observed flow in ML. Default 1.0.</param>
        public static CatchmentModelRunner RunAndReturn(
            CatchmentModelRunner runner,
            DateTime start, DateTime end,
            double rainfall = 3.0, double pet = 4.0, double observedFlow = 1.0)
        {
            var settings = BuildSettings();

            var rain = MakeConstantTimeSeries(start, end, rainfall);
            var evap = MakeConstantTimeSeries(start, end, pet);
            var flow = MakeConstantTimeSeries(start, end, observedFlow);

            runner.LoadInputTimeSeries(rain, evap, flow, settings);
            runner.SetOutputTimeSeriesDetails(settings);
            runner.RunAll();

            return runner;
        }
    }

    // ------------------------------------------------------------------------
    //  Group 1: Topological sort tests
    // ------------------------------------------------------------------------

    [TestClass]
    public class TopologicalSortTests
    {
        [TestMethod]
        public void SingleNode_RemainsUnchanged()
        {
            var nodes = new WaterBodyWithCatchment[]
            {
                new() { Label = "Outlet", NextDownstreamArrayPosition = -1 }
            };

            CatchmentModel.TopologicalSortWaterBodies(nodes);

            Assert.AreEqual("Outlet", nodes[0].Label);
        }

        [TestMethod]
        public void LinearChain_OutletFirst_SortsToLeavesFirst()
        {
            // Input: C (outlet), B, A — wrong order
            var nodes = new WaterBodyWithCatchment[]
            {
                new() { Label = "C", NextDownstreamArrayPosition = -1 },
                new() { Label = "B", NextDownstreamArrayPosition = 0 },
                new() { Label = "A", NextDownstreamArrayPosition = 1 },
            };

            CatchmentModel.TopologicalSortWaterBodies(nodes);

            int posA = Array.FindIndex(nodes, n => n.Label == "A");
            int posB = Array.FindIndex(nodes, n => n.Label == "B");
            int posC = Array.FindIndex(nodes, n => n.Label == "C");

            Assert.IsTrue(posA < posB, "Leaf A must precede B");
            Assert.IsTrue(posB < posC, "B must precede outlet C");
        }

        [TestMethod]
        public void YNetwork_BothLeavesBeforeOutlet()
        {
            var nodes = new WaterBodyWithCatchment[]
            {
                new() { Label = "Outlet", NextDownstreamArrayPosition = -1 },
                new() { Label = "L1", NextDownstreamArrayPosition = 0 },
                new() { Label = "L2", NextDownstreamArrayPosition = 0 },
            };

            CatchmentModel.TopologicalSortWaterBodies(nodes);

            int posL1 = Array.FindIndex(nodes, n => n.Label == "L1");
            int posL2 = Array.FindIndex(nodes, n => n.Label == "L2");
            int posOut = Array.FindIndex(nodes, n => n.Label == "Outlet");

            Assert.IsTrue(posL1 < posOut, "L1 must precede Outlet");
            Assert.IsTrue(posL2 < posOut, "L2 must precede Outlet");
        }

        [TestMethod]
        public void AlreadySorted_PreservesOrder()
        {
            var nodes = new WaterBodyWithCatchment[]
            {
                new() { Label = "A", NextDownstreamArrayPosition = 1 },
                new() { Label = "B", NextDownstreamArrayPosition = 2 },
                new() { Label = "C", NextDownstreamArrayPosition = -1 },
            };

            CatchmentModel.TopologicalSortWaterBodies(nodes);

            int posA = Array.FindIndex(nodes, n => n.Label == "A");
            int posB = Array.FindIndex(nodes, n => n.Label == "B");
            int posC = Array.FindIndex(nodes, n => n.Label == "C");

            Assert.IsTrue(posA < posB && posB < posC);
        }

        [TestMethod]
        public void MixedNetwork_AllUpstreamBeforeDownstream()
        {
            // D0 ? D2, D1 ? D2, D2 ? Out, D3 ? Out
            // Input order deliberately scrambled: Out, D2, D3, D0, D1
            var nodes = new WaterBodyWithCatchment[]
            {
                new() { Label = "Out", NextDownstreamArrayPosition = -1 },
                new() { Label = "D2",  NextDownstreamArrayPosition = 0 },
                new() { Label = "D3",  NextDownstreamArrayPosition = 0 },
                new() { Label = "D0",  NextDownstreamArrayPosition = 1 },
                new() { Label = "D1",  NextDownstreamArrayPosition = 1 },
            };

            CatchmentModel.TopologicalSortWaterBodies(nodes);

            int posD0 = Array.FindIndex(nodes, n => n.Label == "D0");
            int posD1 = Array.FindIndex(nodes, n => n.Label == "D1");
            int posD2 = Array.FindIndex(nodes, n => n.Label == "D2");
            int posD3 = Array.FindIndex(nodes, n => n.Label == "D3");
            int posOut = Array.FindIndex(nodes, n => n.Label == "Out");

            Assert.IsTrue(posD0 < posD2, "D0 must precede D2");
            Assert.IsTrue(posD1 < posD2, "D1 must precede D2");
            Assert.IsTrue(posD2 < posOut, "D2 must precede Out");
            Assert.IsTrue(posD3 < posOut, "D3 must precede Out");
        }
    }

    // ------------------------------------------------------------------------
    //  Group 2: Network setup and area traversal tests
    // ------------------------------------------------------------------------

    [TestClass]
    public class CatchmentModelNetworkTests
    {
        [TestMethod]
        public void YNetwork_TraversalAreaMatchesDirectSum()
        {
            var runner = TestCatchmentBuilder.BuildYNetwork();
            AssertTraversalMatchesDirectSum(runner.catchmentModel, expectedArea: 1.2);
        }

        [TestMethod]
        public void ChainNetwork_TraversalAreaMatchesDirectSum()
        {
            var runner = TestCatchmentBuilder.BuildChainNetwork();
            AssertTraversalMatchesDirectSum(runner.catchmentModel, expectedArea: 1.0);
        }

        [TestMethod]
        public void MixedNetwork_TraversalAreaMatchesDirectSum()
        {
            var runner = TestCatchmentBuilder.BuildMixedNetwork();
            AssertTraversalMatchesDirectSum(runner.catchmentModel, expectedArea: 2.0);
        }

        [TestMethod]
        public void RoutingLinks_ConfluenceTargets_HaveValidIndex()
        {
            var runner = TestCatchmentBuilder.BuildMixedNetwork();
            var model = runner.catchmentModel;

            for (int i = 0; i < model.ElementModelCalculationOrder.Length; i++)
            {
                var entry = model.ElementModelCalculationOrder[i];
                if (entry.ElementType == ModelElementType.StraightThroughRoutingLink
                    && entry.NextDownstreamElementType == ModelElementType.ConfluenceNode)
                {
                    Assert.IsTrue(
                        entry.IndexForNextDownstreamElementType >= 0
                        && entry.IndexForNextDownstreamElementType < model.ConfluenceNodes.Length,
                        $"RL[{entry.IndexForElementType}] targets ConfluenceNode" +
                        $"[{entry.IndexForNextDownstreamElementType}] — out of range.");
                }
            }
        }

        private static void AssertTraversalMatchesDirectSum(CatchmentModel model, double expectedArea)
        {
            double directSum = 0.0;
            for (int i = 0; i < model.SubcatchmentsInflowModels.Length; i++)
            {
                directSum += model.SubcatchmentsInflowModels[i].AreaKM2;
            }

            Assert.AreEqual(expectedArea, directSum, 1.0E-4,
                $"Direct sum should be {expectedArea} km².");
            Assert.AreEqual(directSum, model.TotalCatchmentAreaKM2, 1.0E-6,
                "Traversal area must match direct sum.");
        }
    }

    // ------------------------------------------------------------------------
    //  Group 3: Mass balance closure tests (integration)
    // ------------------------------------------------------------------------


    [TestClass]
    public class MassBalanceClosureTests
    {
        private static readonly DateTime RunStart = new(2005, 1, 1);
        private static readonly DateTime RunEnd = new(2005, 12, 31);

        [TestMethod]
        public void YNetwork_BothBalancesClose()
        {
            var runner = TestCatchmentBuilder.BuildYNetwork(startFraction: 0.7);
            TestCatchmentBuilder.RunAndReturn(runner, RunStart, RunEnd);

            Assert.AreEqual(0.0, runner.catchmentModel.CumulativeTopDownMisclosure, 1.0E-4);
            Assert.AreEqual(0.0, runner.catchmentModel.CumulativeBottomUpMisclosure, 1.0E-10);
        }

        [TestMethod]
        public void ChainNetwork_BothBalancesClose()
        {
            var runner = TestCatchmentBuilder.BuildChainNetwork(startFraction: 0.7);
            TestCatchmentBuilder.RunAndReturn(runner, RunStart, RunEnd);

            Assert.AreEqual(0.0, runner.catchmentModel.CumulativeTopDownMisclosure, 1.0E-4);
            Assert.AreEqual(0.0, runner.catchmentModel.CumulativeBottomUpMisclosure, 1.0E-10);
        }

        [TestMethod]
        public void MixedNetwork_BothBalancesClose()
        {
            var runner = TestCatchmentBuilder.BuildMixedNetwork(startFraction: 0.7);
            TestCatchmentBuilder.RunAndReturn(runner, RunStart, RunEnd);

            Assert.AreEqual(0.0, runner.catchmentModel.CumulativeTopDownMisclosure, 1.0E-4);
            Assert.AreEqual(0.0, runner.catchmentModel.CumulativeBottomUpMisclosure, 1.0E-10);
        }

        [TestMethod]
        public void ZeroInflow_AllBalancesZero()
        {
            var runner = TestCatchmentBuilder.BuildYNetwork();
            TestCatchmentBuilder.RunAndReturn(runner, RunStart, RunEnd,
                rainfall: 0.0, pet: 0.0, observedFlow: 0.0);

            Assert.AreEqual(0.0, runner.catchmentModel.CumulativeTopDownMisclosure, 1.0E-10);
            Assert.AreEqual(0.0, runner.catchmentModel.CumulativeBottomUpMisclosure, 1.0E-10);
        }

        [TestMethod]
        public void MixedNetwork_ZeroWarnings()
        {
            var runner = TestCatchmentBuilder.BuildMixedNetwork(startFraction: 0.5);
            TestCatchmentBuilder.RunAndReturn(runner, RunStart, RunEnd);

            Assert.AreEqual(0, runner.catchmentModel.MassBalanceWarningCount,
                "No mass balance warnings should fire when balance closes.");
        }

        // ------------------------------------------------------------------------
        // Group 4: Reverse-solve calendar and physical-state tests
        // ------------------------------------------------------------------------

        [TestClass]
        public class ReverseSolveLeapDayTests
        {
            private static readonly DateTime RunStart = new DateTime(1960, 2, 27);
            private static readonly DateTime RunEnd = new DateTime(1960, 3, 2);

            /// <summary>Verifies the reverse solver remains finite and physically bounded over a window containing 29 February 1960.</summary>
            [TestMethod]
            public void ReverseSolve_LeapDayWindow_AllStatesRemainFiniteAndPhysical()
            {
                CatchmentModelRunner runner = TestCatchmentBuilder.BuildChainNetwork(
                    startFraction: 0.7,
                    annualDemandFactor: 0.5);

                TestCatchmentBuilder.RunAndReturn(
                    runner,
                    RunStart,
                    RunEnd,
                    rainfall: 3.0,
                    pet: 4.0,
                    observedFlow: 1.0);

                CatchmentModel model = runner.catchmentModel;

                foreach (WaterBodyModelNode node in model.WaterBodyNodes)
                {
                    AssertWaterBodyStateIsFiniteAndPhysical(node);
                }

                foreach (ConfluenceModelNode node in model.ConfluenceNodes)
                {
                    AssertConfluenceStateIsFiniteAndPhysical(node);
                }

                Assert.AreEqual(
                    0.0,
                    model.CumulativeTopDownMisclosure,
                    1.0E-4,
                    "The top-down mass balance should close across the leap-day window.");

                Assert.AreEqual(
                    0.0,
                    model.CumulativeBottomUpMisclosure,
                    1.0E-10,
                    "The bottom-up mass balance should close across the leap-day window.");

                Assert.AreEqual(
                    0,
                    model.MassBalanceWarningCount,
                    "The leap-day reverse solve should not produce mass-balance warnings.");
            }

            /// <summary>Verifies a reverse solve ending on 29 February produces finite, non-negative demand and flow values.</summary>
            [TestMethod]
            public void ReverseSolve_EndingOnLeapDay_ProducesFiniteNonNegativeFluxes()
            {
                CatchmentModelRunner runner = TestCatchmentBuilder.BuildChainNetwork(
                    startFraction: 0.7,
                    annualDemandFactor: 0.5);

                TestCatchmentBuilder.RunAndReturn(
                    runner,
                    new DateTime(1960, 2, 27),
                    new DateTime(1960, 2, 29),
                    rainfall: 3.0,
                    pet: 4.0,
                    observedFlow: 1.0);

                foreach (WaterBodyModelNode node in runner.catchmentModel.WaterBodyNodes)
                {
                    Assert.IsTrue(
                        double.IsFinite(node.UnrestrictedDemand),
                        $"Unrestricted demand for node '{node.Label}' should be finite on 29 February 1960.");

                    Assert.IsTrue(
                        node.UnrestrictedDemand >= 0.0,
                        $"Unrestricted demand for node '{node.Label}' should not be negative on 29 February 1960.");

                    Assert.IsTrue(
                        double.IsFinite(node.DemandVolumeExtracted),
                        $"Extracted demand for node '{node.Label}' should be finite on 29 February 1960.");

                    Assert.IsTrue(
                        node.DemandVolumeExtracted >= 0.0,
                        $"Extracted demand for node '{node.Label}' should not be negative on 29 February 1960.");

                    Assert.IsTrue(
                        double.IsFinite(node.DownstreamFlow),
                        $"Downstream flow for node '{node.Label}' should be finite on 29 February 1960.");

                    Assert.IsTrue(
                        node.DownstreamFlow >= 0.0,
                        $"Downstream flow for node '{node.Label}' should not be negative on 29 February 1960.");

                    AssertWaterBodyStateIsFiniteAndPhysical(node);
                }
            }

            /// <summary>Verifies extending a reverse run through leap day does not introduce an extreme discontinuity into the final physical state.</summary>
            [TestMethod]
            public void ReverseSolve_ExtendingThroughLeapDay_DoesNotCreateExtremeState()
            {
                DateTime runStart = new DateTime(1960, 2, 24);

                CatchmentModelRunner beforeLeapDay = TestCatchmentBuilder.BuildChainNetwork(
                    startFraction: 0.7,
                    annualDemandFactor: 0.5);

                TestCatchmentBuilder.RunAndReturn(
                    beforeLeapDay,
                    runStart,
                    new DateTime(1960, 2, 28),
                    rainfall: 3.0,
                    pet: 4.0,
                    observedFlow: 1.0);

                CatchmentModelRunner throughLeapDay = TestCatchmentBuilder.BuildChainNetwork(
                    startFraction: 0.7,
                    annualDemandFactor: 0.5);

                TestCatchmentBuilder.RunAndReturn(
                    throughLeapDay,
                    runStart,
                    new DateTime(1960, 2, 29),
                    rainfall: 3.0,
                    pet: 4.0,
                    observedFlow: 1.0);

                Assert.AreEqual(
                    beforeLeapDay.catchmentModel.WaterBodyNodes.Length,
                    throughLeapDay.catchmentModel.WaterBodyNodes.Length,
                    "Both runs should contain the same water bodies.");

                for (int i = 0; i < throughLeapDay.catchmentModel.WaterBodyNodes.Length; i++)
                {
                    WaterBodyModelNode before = beforeLeapDay.catchmentModel.WaterBodyNodes[i];
                    WaterBodyModelNode after = throughLeapDay.catchmentModel.WaterBodyNodes[i];

                    AssertWaterBodyStateIsFiniteAndPhysical(after);

                    Assert.IsTrue(
                        Math.Abs(after.VolumeInStorage - before.VolumeInStorage) <= after.StorageCapacityVolumeAtSpill + 1.0E-10,
                        $"Adding 29 February should not change storage for node '{after.Label}' by more than the dam's storage capacity.");

                    Assert.IsTrue(
                        after.UnrestrictedDemand <= after.MaxStorageCapacityVolumeAtSpill + 1.0E-10,
                        $"One day of unrestricted demand for node '{after.Label}' should not exceed its storage capacity in this test configuration.");

                    double physicallyAvailableWater =
                        before.VolumeInStorage
                        + after.UpstreamFlow
                        + after.PumpedInflow
                        + after.RainfallVolume;

                    Assert.IsTrue(
                        after.DownstreamFlow <= physicallyAvailableWater + 1.0E-8,
                        $"Downstream flow for node '{after.Label}' should not exceed physically available water on 29 February 1960.");
                }
            }

            /// <summary>Asserts that a water-body node contains only finite, physically admissible state and flux values.</summary>
            /// <param name="node">Water-body node to check.</param>
            private static void AssertWaterBodyStateIsFiniteAndPhysical(WaterBodyModelNode node)
            {
                AssertFinite(node.StartTimeStepVolumeInStorage, node.Label, nameof(node.StartTimeStepVolumeInStorage));
                AssertFinite(node.VolumeInStorage, node.Label, nameof(node.VolumeInStorage));
                AssertFinite(node.ChangeInVolumeInStorageForTimeStep, node.Label, nameof(node.ChangeInVolumeInStorageForTimeStep));
                AssertFinite(node.UnrestrictedDemand, node.Label, nameof(node.UnrestrictedDemand));
                AssertFinite(node.DemandVolumeExtracted, node.Label, nameof(node.DemandVolumeExtracted));
                AssertFinite(node.RainfallVolume, node.Label, nameof(node.RainfallVolume));
                AssertFinite(node.EvaporationVolume, node.Label, nameof(node.EvaporationVolume));
                AssertFinite(node.NetRainfallVolume, node.Label, nameof(node.NetRainfallVolume));
                AssertFinite(node.SeepageLossVolume, node.Label, nameof(node.SeepageLossVolume));
                AssertFinite(node.PumpedInflow, node.Label, nameof(node.PumpedInflow));
                AssertFinite(node.DownstreamFlow, node.Label, nameof(node.DownstreamFlow));
                AssertFinite(node.DownstreamFlowFromBypass, node.Label, nameof(node.DownstreamFlowFromBypass));
                AssertFinite(node.DownstreamFlowFromSpill, node.Label, nameof(node.DownstreamFlowFromSpill));
                AssertFinite(node.VolumeBalanceMisclosure, node.Label, nameof(node.VolumeBalanceMisclosure));

                Assert.IsTrue(
                    node.VolumeInStorage >= -1.0E-10,
                    $"Storage for node '{node.Label}' should not be negative.");

                Assert.IsTrue(
                    node.VolumeInStorage <= node.StorageCapacityVolumeAtSpill + 1.0E-10,
                    $"Storage for node '{node.Label}' should not exceed capacity.");

                Assert.IsTrue(
                    node.UnrestrictedDemand >= 0.0,
                    $"Unrestricted demand for node '{node.Label}' should not be negative.");

                Assert.IsTrue(
                    node.DemandVolumeExtracted >= 0.0,
                    $"Extracted demand for node '{node.Label}' should not be negative.");

                Assert.IsTrue(
                    node.EvaporationVolume >= 0.0,
                    $"Evaporation volume for node '{node.Label}' should not be negative.");

                Assert.IsTrue(
                    node.SeepageLossVolume >= 0.0,
                    $"Seepage loss for node '{node.Label}' should not be negative.");

                Assert.IsTrue(
                    node.PumpedInflow >= 0.0,
                    $"Pumped inflow for node '{node.Label}' should not be negative.");

                Assert.IsTrue(
                    node.DownstreamFlow >= 0.0,
                    $"Downstream flow for node '{node.Label}' should not be negative.");

                Assert.IsTrue(
                    node.DownstreamFlowFromBypass >= 0.0,
                    $"Bypass flow for node '{node.Label}' should not be negative.");

                Assert.IsTrue(
                    node.DownstreamFlowFromSpill >= 0.0,
                    $"Spill flow for node '{node.Label}' should not be negative.");

                Assert.AreEqual(
                    0.0,
                    node.VolumeBalanceMisclosure,
                    1.0E-8,
                    $"Water-balance misclosure for node '{node.Label}' should be near zero.");
            }

            /// <summary>Asserts that a confluence node contains only finite, non-negative flow values.</summary>
            /// <param name="node">Confluence node to check.</param>
            private static void AssertConfluenceStateIsFiniteAndPhysical(ConfluenceModelNode node)
            {
                AssertFinite(node.UpstreamFlow, node.Label, nameof(node.UpstreamFlow));
                AssertFinite(node.DownstreamFlow, node.Label, nameof(node.DownstreamFlow));
                AssertFinite(node.DownstreamFlowFromBypass, node.Label, nameof(node.DownstreamFlowFromBypass));
                AssertFinite(node.DownstreamFlowFromSpill, node.Label, nameof(node.DownstreamFlowFromSpill));
                AssertFinite(node.DownstreamFlowFromCatchment, node.Label, nameof(node.DownstreamFlowFromCatchment));
                AssertFinite(node.VolumeBalanceMisclosure, node.Label, nameof(node.VolumeBalanceMisclosure));

                Assert.IsTrue(node.UpstreamFlow >= 0.0, $"Upstream flow for confluence '{node.Label}' should not be negative.");
                Assert.IsTrue(node.DownstreamFlow >= 0.0, $"Downstream flow for confluence '{node.Label}' should not be negative.");

                Assert.AreEqual(
                    0.0,
                    node.VolumeBalanceMisclosure,
                    1.0E-10,
                    $"Water-balance misclosure for confluence '{node.Label}' should be near zero.");
            }

            /// <summary>Asserts that a model value is finite.</summary>
            /// <param name="value">Value to check.</param>
            /// <param name="nodeLabel">Node label used in the failure message.</param>
            /// <param name="propertyName">Property name used in the failure message.</param>
            private static void AssertFinite(double value, string nodeLabel, string propertyName)
            {
                Assert.IsTrue(
                    double.IsFinite(value),
                    $"{propertyName} for node '{nodeLabel}' should be finite but was {value}.");
            }
        }
    }
}