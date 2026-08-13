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
        private static RODISSettings BuildSettings(double startFraction = 0.0)
        {
            return new RODISSettings
            {
                CalculateUnimpactedGivenObserved = true,
                UseLegacyRODIS1CalculationMethods = false,
                MaximumProportionOfCatchmentImpounded = 0.999,
                AllStoragesProportionFullAtStartOfRun = startFraction,
                UseSpecificDamNetworkDetails = true,
                UseVolumeThresholdForDemandGroups = false,
                UseFixedLowFlowBypassCapacity = false,
                VolumeSurfaceAreaEquation = new EquationParser
                {
                    VariablesWithDescriptions = new Dictionary<string, string> { { "SA", "Surface area in m²" } },
                    Equation = "0.0001449275*SA^1.314",
                },
                OutletStreamName = "Test Creek",
                OutletNodeName = "Test Outlet",
                OutletNodeNumber = "999999",
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
        public static CatchmentModelRunner BuildChainNetwork(double startFraction = 0.0)
        {
            var nodes = new[]
            {
                MakeDam(1, 2, 500, 1.0, 0.5),
                MakeDam(2, 3, 400, 0.8, 0.3),
                MakeOutlet(3, 0.2),
            };
            return BuildRunner(nodes, startFraction);
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
        /// Creates a LegacyRODISDamNode representing a dam.
        /// Note: legacy nodes don't support per-node start/end existence dates.
        /// All dams exist for the entire simulation period.
        /// </summary>
        private static LegacyRODISDamNode MakeDam(
            int id, int downstreamId,
            double surfaceAream2, double volumeML, double catchmentAreakm2)
        {
            return new LegacyRODISDamNode
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
        /// Creates a LegacyRODISDamNode representing a confluence (outlet).
        /// Volume and surface area = 0 ? model treats it as a ConfluenceNode.
        /// </summary>
        private static LegacyRODISDamNode MakeOutlet(
            int id, double catchmentAreakm2)
        {
            return new LegacyRODISDamNode
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
            LegacyRODISDamNode[] nodes, double startFraction)
        {
            var settings = BuildSettings(startFraction);
            var runner = new CatchmentModelRunner();
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
    }
}