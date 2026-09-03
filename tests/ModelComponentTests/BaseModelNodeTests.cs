// <copyright file="BaseModelNodeTests.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>
namespace RODISUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using RODIS.ModelRun;
    using System;

    [TestClass]
    public class BaseModelNodeTests
    {
        /// <summary>Uses ConfluenceModelNode as a concrete subclass of BaseModelNode.</summary>
        private static ConfluenceModelNode MakeNode() => new ConfluenceModelNode();

        // ----------------------------------------------------
        // SetBaseStartEndDates — normal
        // ----------------------------------------------------

        [TestMethod]
        public void SetBaseStartEndDates_ValidRange_SetsCorrectly()
        {
            var node = MakeNode();
            var start = new DateTime(2000, 1, 1);
            var end = new DateTime(2020, 12, 31);

            node.SetBaseStartEndDates(start, end);

            Assert.AreEqual(start, node.StartDate);
            Assert.AreEqual(end, node.EndDate);
        }

        // ----------------------------------------------------
        // SetBaseStartEndDates — end before start
        // ----------------------------------------------------

        [TestMethod]
        public void SetBaseStartEndDates_EndBeforeStart_SetsEndToStart()
        {
            var node = MakeNode();
            var start = new DateTime(2020, 1, 1);
            var end = new DateTime(2010, 1, 1); // before start

            node.SetBaseStartEndDates(start, end);

            // Per code: sets both to startDate when end < start
            Assert.AreEqual(start, node.StartDate);
            Assert.AreEqual(start, node.EndDate, "EndDate should be clamped to StartDate");
        }

        // ----------------------------------------------------
        // ResetToBaseStartEndDates — restores original
        // ----------------------------------------------------

        [TestMethod]
        public void ResetToBaseStartEndDates_RestoresOriginal()
        {
            var node = MakeNode();
            var origStart = new DateTime(2000, 1, 1);
            var origEnd = new DateTime(2020, 12, 31);
            node.SetBaseStartEndDates(origStart, origEnd);

            // Modify dates (as scenario date-shifting does)
            node.SetStartDate(new DateTime(2010, 1, 1));
            node.SetEndDate(DateTime.MaxValue);

            node.ResetToBaseStartEndDates();

            Assert.AreEqual(origStart, node.StartDate, "Should restore original start");
            Assert.AreEqual(origEnd, node.EndDate, "Should restore original end");
        }

        // ----------------------------------------------------
        // CalculateMeanAnnualInflow
        // ----------------------------------------------------

        [TestMethod]
        public void CalculateMeanAnnualInflow_OneYearOfData()
        {
            var node = MakeNode();
            node.SumUpstreamAndPumpedInflows = 365.25; // 365.25 ML total
            node.SumDaysOfUpstreamAndPumpedInflows = 365.25;

            double result = node.CalculateMeanAnnualInflow();

            Assert.AreEqual(365.25, result, 0.01, "365.25 ML over 1 year = 365.25 ML/yr");
        }

        [TestMethod]
        public void CalculateMeanAnnualInflow_ZeroDays_ReturnsZero()
        {
            var node = MakeNode();
            node.SumUpstreamAndPumpedInflows = 100.0;
            node.SumDaysOfUpstreamAndPumpedInflows = 0.0;

            double result = node.CalculateMeanAnnualInflow();

            Assert.AreEqual(0.0, result, 0.001, "Zero days should return zero");
        }
    }
}