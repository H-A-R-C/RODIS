
namespace STEDIUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using STEDI.ModelRun;
    using System;

    [TestClass]
    public class BypassAndPumpingDates
    {
        /// <summary>Creates a minimal CatchmentModel with the specified water body nodes.</summary>
        private static CatchmentModel CreateModelWithNodes(params WaterBodyModelNode[] nodes)
        {
            return new CatchmentModel
            {
                WaterBodyNodes = nodes,
                // Minimal setup — other arrays not needed for this method
                ConfluenceNodes = Array.Empty<ConfluenceModelNode>(),
                SubcatchmentsInflowModels = Array.Empty<SubcatchmentInflowModel>(),
                StraightThroughRoutingLinks = Array.Empty<StraightThroughRoutingLink>(),
                ElementModelCalculationOrder = Array.Empty<ModelElementTypeIndex>(),
            };
        }

        /// <summary>Creates a water body node with specified existence and bypass/pumping date windows.</summary>
        private static WaterBodyModelNode MakeNode(
            DateTime startDate, DateTime endDate,
            DateTime startBypass, DateTime endBypass,
            DateTime startPump, DateTime endPump)
        {
            WaterBodyModelNode result = new WaterBodyModelNode()
            {
                StartBypassDate = startBypass,
                EndBypassDate = endBypass,
                StartPumpedInflowDate = startPump,
                EndPumpedInflowDate = endPump,
            };

            result.SetStartDate(startDate);
            result.SetEndDate(endDate);

            return result;
        }

        // ────────────────────────────────────────────────────
        // 1. Already within range — no change expected
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void DatesAlreadyWithinExistence_NoChange()
        {
            var node = MakeNode(
                new DateTime(1990, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(1995, 1, 1), new DateTime(2015, 12, 31),
                new DateTime(2000, 6, 1), new DateTime(2018, 6, 1));

            var model = CreateModelWithNodes(node);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            Assert.AreEqual(new DateTime(1995, 1, 1), node.StartBypassDate);
            Assert.AreEqual(new DateTime(2015, 12, 31), node.EndBypassDate);
            Assert.AreEqual(new DateTime(2000, 6, 1), node.StartPumpedInflowDate);
            Assert.AreEqual(new DateTime(2018, 6, 1), node.EndPumpedInflowDate);
        }

        // ────────────────────────────────────────────────────
        // 2. Bypass start too early — clamped to node start
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void BypassStartBeforeExistence_ClampedToStart()
        {
            var node = MakeNode(
                new DateTime(2000, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(1990, 1, 1), new DateTime(2020, 12, 31),  // bypass starts before existence
                new DateTime(2000, 1, 1), new DateTime(2020, 12, 31));

            var model = CreateModelWithNodes(node);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            Assert.AreEqual(new DateTime(2000, 1, 1), node.StartBypassDate, "Bypass start should clamp to node start");
            Assert.AreEqual(new DateTime(2020, 12, 31), node.EndBypassDate, "Bypass end unchanged");
        }

        // ────────────────────────────────────────────────────
        // 3. Bypass end too late — clamped to node end
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void BypassEndAfterExistence_ClampedToEnd()
        {
            var node = MakeNode(
                new DateTime(2000, 1, 1), new DateTime(2010, 12, 31),
                new DateTime(2000, 1, 1), new DateTime(2025, 12, 31),  // bypass ends after existence
                new DateTime(2000, 1, 1), new DateTime(2010, 12, 31));

            var model = CreateModelWithNodes(node);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            Assert.AreEqual(new DateTime(2000, 1, 1), node.StartBypassDate, "Bypass start unchanged");
            Assert.AreEqual(new DateTime(2010, 12, 31), node.EndBypassDate, "Bypass end should clamp to node end");
        }

        // ────────────────────────────────────────────────────
        // 4. Both bypass dates outside existence — clamped, still valid
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void BypassBothOutside_ClampedToExistenceWindow()
        {
            var node = MakeNode(
                new DateTime(2000, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(1990, 1, 1), new DateTime(2030, 12, 31),
                new DateTime(2000, 1, 1), new DateTime(2020, 12, 31));

            var model = CreateModelWithNodes(node);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            Assert.AreEqual(new DateTime(2000, 1, 1), node.StartBypassDate);
            Assert.AreEqual(new DateTime(2020, 12, 31), node.EndBypassDate);
        }

        // ────────────────────────────────────────────────────
        // 5. Node removed (MaxValue) — bypass disabled
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void NodeRemovedByScenario_BypassAndPumpingDisabled()
        {
            var node = MakeNode(
                DateTime.MaxValue, DateTime.MaxValue,
                new DateTime(1995, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(2000, 6, 1), new DateTime(2018, 6, 1));

            var model = CreateModelWithNodes(node);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            Assert.AreEqual(DateTime.MaxValue, node.StartBypassDate, "Bypass should be disabled");
            Assert.AreEqual(DateTime.MaxValue, node.EndBypassDate, "Bypass should be disabled");
            Assert.AreEqual(DateTime.MaxValue, node.StartPumpedInflowDate, "Pumping should be disabled");
            Assert.AreEqual(DateTime.MaxValue, node.EndPumpedInflowDate, "Pumping should be disabled");
        }

        // ────────────────────────────────────────────────────
        // 6. Pumping start too early — clamped to node start
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void PumpingStartBeforeExistence_ClampedToStart()
        {
            var node = MakeNode(
                new DateTime(2005, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(2005, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(1998, 1, 1), new DateTime(2020, 12, 31));  // pumping starts before existence

            var model = CreateModelWithNodes(node);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            Assert.AreEqual(new DateTime(2005, 1, 1), node.StartPumpedInflowDate, "Pumping start should clamp to node start");
            Assert.AreEqual(new DateTime(2020, 12, 31), node.EndPumpedInflowDate, "Pumping end unchanged");
        }

        // ────────────────────────────────────────────────────
        // 7. Existence window shrinks so bypass range inverts — disabled
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void ExistenceShrinksToExcludeBypass_BypassDisabled()
        {
            // Node now exists only 2015–2020, but bypass was 1990–2010 (entirely before new window)
            var node = MakeNode(
                new DateTime(2015, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(1990, 1, 1), new DateTime(2010, 12, 31),
                new DateTime(2015, 1, 1), new DateTime(2020, 12, 31));

            var model = CreateModelWithNodes(node);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            // Start clamped to 2015, end clamped to 2010 → inverted → disabled
            Assert.AreEqual(DateTime.MaxValue, node.StartBypassDate, "Inverted bypass should be disabled");
            Assert.AreEqual(DateTime.MaxValue, node.EndBypassDate, "Inverted bypass should be disabled");
        }

        // ────────────────────────────────────────────────────
        // 8. Same for pumping — window entirely excluded
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void ExistenceShrinksToExcludePumping_PumpingDisabled()
        {
            // Node now exists 2015–2020, pumping was 1990–2005 (entirely before new window)
            var node = MakeNode(
                new DateTime(2015, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(2015, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(1990, 1, 1), new DateTime(2005, 12, 31));

            var model = CreateModelWithNodes(node);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            Assert.AreEqual(DateTime.MaxValue, node.StartPumpedInflowDate, "Inverted pumping should be disabled");
            Assert.AreEqual(DateTime.MaxValue, node.EndPumpedInflowDate, "Inverted pumping should be disabled");
        }

        // ────────────────────────────────────────────────────
        // 9. Multiple nodes — each clamped independently
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void MultipleNodes_EachClampedIndependently()
        {
            var nodeA = MakeNode(
                new DateTime(2000, 1, 1), new DateTime(2020, 12, 31),
                new DateTime(1990, 1, 1), new DateTime(2020, 12, 31),  // bypass early
                new DateTime(2000, 1, 1), new DateTime(2020, 12, 31));

            var nodeB = MakeNode(
                new DateTime(2010, 1, 1), new DateTime(2015, 12, 31),
                new DateTime(2010, 1, 1), new DateTime(2015, 12, 31),  // fine
                new DateTime(2005, 1, 1), new DateTime(2025, 12, 31)); // pumping both outside

            var model = CreateModelWithNodes(nodeA, nodeB);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            // Node A
            Assert.AreEqual(new DateTime(2000, 1, 1), nodeA.StartBypassDate);
            Assert.AreEqual(new DateTime(2020, 12, 31), nodeA.EndBypassDate);

            // Node B — pumping clamped both sides
            Assert.AreEqual(new DateTime(2010, 1, 1), nodeB.StartPumpedInflowDate);
            Assert.AreEqual(new DateTime(2015, 12, 31), nodeB.EndPumpedInflowDate);
        }

        // ────────────────────────────────────────────────────
        // 10. Null nodes array — no crash
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void NullWaterBodyNodes_NoCrash()
        {
            var model = new CatchmentModel { WaterBodyNodes = null };
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();
            // No exception = pass
        }

        // ────────────────────────────────────────────────────
        // 11. Empty nodes array — no crash
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void EmptyWaterBodyNodes_NoCrash()
        {
            var model = CreateModelWithNodes();
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();
            // No exception = pass
        }

        // ────────────────────────────────────────────────────
        // 12. Bypass start == end (zero-length window) — disabled
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void BypassStartEqualsEnd_Disabled()
        {
            var sameDate = new DateTime(2010, 6, 15);
            var node = MakeNode(
                new DateTime(2000, 1, 1), new DateTime(2020, 12, 31),
                sameDate, sameDate,
                new DateTime(2000, 1, 1), new DateTime(2020, 12, 31));

            var model = CreateModelWithNodes(node);
            model.ClampBypassAndPumpingDatesToWaterBodyExistence();

            Assert.AreEqual(DateTime.MaxValue, node.StartBypassDate, "Zero-length bypass window should be disabled");
            Assert.AreEqual(DateTime.MaxValue, node.EndBypassDate, "Zero-length bypass window should be disabled");
        }

    }
}
