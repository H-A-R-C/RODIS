// <copyright file="FarmDamRepeatingMonthlyDemandModelTests.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>
namespace RODISUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using RODIS.ModelRun;
    using System;

    [TestClass]
    public class FarmDamRepeatingMonthlyDemandModelTests
    {
        /// <summary>Creates a demand model with uniform monthly proportions (1/12 each).</summary>
        private static FarmDamRepeatingMonthlyDemandModel MakeUniformModel(double annualFactor = 0.5, double volumeML = 10.0)
        {
            return new FarmDamRepeatingMonthlyDemandModel
            {
                AnnualDemandFactor = annualFactor,
                DamStorageCapacityVolumeAtSpill = volumeML,
                MonthlyDemandProportions = new double[]
                    { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 },
            };
        }

        // ----------------------------------------------------
        // Initialise — annual demand volume
        // ----------------------------------------------------

        [TestMethod]
        public void Initialise_AnnualDemandVolume_CorrectlyCalculated()
        {
            var model = MakeUniformModel(annualFactor: 0.5, volumeML: 10.0);
            model.Initialise();

            // Indirectly verify: run Jan 1 for 1 day, uniform demand = 5 ML/yr ÷ 365.25 days/yr ˜ constant
            model.RunTimeStep(new DateTime(2010, 1, 1), TimeSpan.FromDays(1));
            double janDailyDemand = model.UnrestrictedDemand;
            Assert.IsTrue(janDailyDemand > 0.0, "Should have positive demand");
        }

        // ----------------------------------------------------
        // Initialise — zero demand factor
        // ----------------------------------------------------

        [TestMethod]
        public void Initialise_ZeroDemandFactor_ZeroDemand()
        {
            var model = MakeUniformModel(annualFactor: 0.0, volumeML: 10.0);
            model.Initialise();

            model.RunTimeStep(new DateTime(2010, 6, 15), TimeSpan.FromDays(1));
            Assert.AreEqual(0.0, model.UnrestrictedDemand, 0.001);
        }

        // ----------------------------------------------------
        // RunTimeStep — seasonal pattern
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_SeasonalPattern_SummerHigherThanWinter()
        {
            var model = new FarmDamRepeatingMonthlyDemandModel
            {
                AnnualDemandFactor = 1.0,
                DamStorageCapacityVolumeAtSpill = 10.0,
                // High in summer (Dec-Feb), low in winter (Jun-Aug)
                MonthlyDemandProportions = new double[]
                    { 2.0, 2.0, 1.0, 0.5, 0.2, 0.1, 0.1, 0.1, 0.2, 0.5, 1.0, 2.0 },
            };
            model.Initialise();

            model.RunTimeStep(new DateTime(2010, 1, 15), TimeSpan.FromDays(1));
            double janDemand = model.UnrestrictedDemand;

            model.RunTimeStep(new DateTime(2010, 7, 15), TimeSpan.FromDays(1));
            double julDemand = model.UnrestrictedDemand;

            Assert.IsTrue(janDemand > julDemand, $"Jan ({janDemand}) should exceed Jul ({julDemand})");
        }

        // ----------------------------------------------------
        // RunTimeStep — weekly time step spanning two months
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_WeeklyStepSpanningMonths_BlendsDemand()
        {
            var model = new FarmDamRepeatingMonthlyDemandModel
            {
                AnnualDemandFactor = 1.0,
                DamStorageCapacityVolumeAtSpill = 10.0,
                MonthlyDemandProportions = new double[]
                    { 1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 },
                // All demand in January
            };
            model.Initialise();

            // 7-day step starting 28 Jan ? spans into Feb
            model.RunTimeStep(new DateTime(2010, 1, 28), TimeSpan.FromDays(7));
            double demand = model.UnrestrictedDemand;

            // Should have 3 days of Jan demand + 4 days of Feb demand (= 0)
            Assert.IsTrue(demand > 0.0, "Should have some demand from January days");
        }

        // ----------------------------------------------------
        // Annual total demand approximately correct
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_FullYearDaily_SumsToAnnualDemand()
        {
            var model = MakeUniformModel(annualFactor: 0.5, volumeML: 10.0);
            model.Initialise();

            double totalDemand = 0.0;
            DateTime start = new DateTime(2010, 1, 1);
            for (int d = 0; d < 365; d++)
            {
                model.RunTimeStep(start.AddDays(d), TimeSpan.FromDays(1));
                totalDemand += model.UnrestrictedDemand;
            }

            // Expected: 0.5 × 10 = 5.0 ML/year
            Assert.AreEqual(5.0, totalDemand, 0.1, "Annual total should be ~5.0 ML");
        }

        /// <summary>Creates the Scenario 11 repeating-monthly demand model, with annual demand of 25 ML and a February proportion of 0.077.</summary>
        private static FarmDamRepeatingMonthlyDemandModel MakeScenario11DemandModel()
        {
            return new FarmDamRepeatingMonthlyDemandModel
            {
                AnnualDemandFactor = 0.5,
                DamStorageCapacityVolumeAtSpill = 50.0,
                MonthlyDemandProportions = new double[]
                {
            0.085,
            0.077,
            0.085,
            0.082,
            0.085,
            0.082,
            0.085,
            0.085,
            0.082,
            0.085,
            0.082,
            0.085,
                },
            };
        }

        /// <summary>Verifies February demand in a common year is divided by the actual 28 days rather than the nominal 28.25 days previously used.</summary>
        [TestMethod]
        public void RunTimeStep_FebruaryInCommonYear_Uses28Days()
        {
            FarmDamRepeatingMonthlyDemandModel model = MakeScenario11DemandModel();
            model.Initialise();

            model.RunTimeStep(new DateTime(1951, 2, 15), TimeSpan.FromDays(1));

            const double annualDemandVolume = 0.5 * 50.0;
            const double februaryProportion = 0.077;
            const int daysInFebruary = 28;

            double expectedDailyDemand = annualDemandVolume * februaryProportion / daysInFebruary;

            Assert.AreEqual(
                expectedDailyDemand,
                model.UnrestrictedDemand,
                1.0E-12,
                "February 1951 demand should equal the February volume divided by 28 days.");
        }

        /// <summary>Verifies February demand in a leap year is divided by the actual 29 days rather than the nominal 28.25 days previously used.</summary>
        [TestMethod]
        public void RunTimeStep_FebruaryInLeapYear_Uses29Days()
        {
            FarmDamRepeatingMonthlyDemandModel model = MakeScenario11DemandModel();
            model.Initialise();

            model.RunTimeStep(new DateTime(1952, 2, 15), TimeSpan.FromDays(1));

            const double annualDemandVolume = 0.5 * 50.0;
            const double februaryProportion = 0.077;
            const int daysInFebruary = 29;

            double expectedDailyDemand = annualDemandVolume * februaryProportion / daysInFebruary;

            Assert.AreEqual(
                expectedDailyDemand,
                model.UnrestrictedDemand,
                1.0E-12,
                "February 1952 demand should equal the February volume divided by 29 days.");
        }

        /// <summary>Verifies daily February demand sums to the specified monthly volume in a common year.</summary>
        [TestMethod]
        public void RunTimeStep_FebruaryInCommonYear_SumsToSpecifiedMonthlyVolume()
        {
            FarmDamRepeatingMonthlyDemandModel model = MakeScenario11DemandModel();
            model.Initialise();

            const double annualDemandVolume = 0.5 * 50.0;
            const double februaryProportion = 0.077;
            double expectedFebruaryDemand = annualDemandVolume * februaryProportion;

            double actualFebruaryDemand = 0.0;
            DateTime start = new DateTime(1951, 2, 1);
            int daysInFebruary = DateTime.DaysInMonth(start.Year, start.Month);

            for (int day = 0; day < daysInFebruary; day++)
            {
                model.RunTimeStep(start.AddDays(day), TimeSpan.FromDays(1));
                actualFebruaryDemand += model.UnrestrictedDemand;
            }

            Assert.AreEqual(
                28,
                daysInFebruary,
                "The test year must be a common year.");

            Assert.AreEqual(
                expectedFebruaryDemand,
                actualFebruaryDemand,
                1.0E-10,
                "February 1951 should deliver exactly its specified share of annual demand.");
        }

        /// <summary>Verifies daily February demand sums to the specified monthly volume in a leap year.</summary>
        [TestMethod]
        public void RunTimeStep_FebruaryInLeapYear_SumsToSpecifiedMonthlyVolume()
        {
            FarmDamRepeatingMonthlyDemandModel model = MakeScenario11DemandModel();
            model.Initialise();

            const double annualDemandVolume = 0.5 * 50.0;
            const double februaryProportion = 0.077;
            double expectedFebruaryDemand = annualDemandVolume * februaryProportion;

            double actualFebruaryDemand = 0.0;
            DateTime start = new DateTime(1952, 2, 1);
            int daysInFebruary = DateTime.DaysInMonth(start.Year, start.Month);

            for (int day = 0; day < daysInFebruary; day++)
            {
                model.RunTimeStep(start.AddDays(day), TimeSpan.FromDays(1));
                actualFebruaryDemand += model.UnrestrictedDemand;
            }

            Assert.AreEqual(
                29,
                daysInFebruary,
                "The test year must be a leap year.");

            Assert.AreEqual(
                expectedFebruaryDemand,
                actualFebruaryDemand,
                1.0E-10,
                "February 1952 should deliver exactly its specified share of annual demand.");
        }

        /// <summary>Runs the repeating-monthly demand model daily for a complete calendar year and returns the total unrestricted demand.</summary>
        /// <param name="model">Initialised repeating-monthly demand model.</param>
        /// <param name="year">Calendar year to simulate.</param>
        /// <returns>Total unrestricted demand for the year, in ML.</returns>
        private static double SumDailyDemandForYear(FarmDamRepeatingMonthlyDemandModel model, int year)
        {
            DateTime startDate = new DateTime(year, 1, 1);
            DateTime endDate = new DateTime(year, 12, 31);
            double totalDemand = 0.0;

            for (DateTime date = startDate; date <= endDate; date = date.AddDays(1))
            {
                model.RunTimeStep(date, TimeSpan.FromDays(1));
                totalDemand += model.UnrestrictedDemand;
            }

            return totalDemand;
        }

        /// <summary>Verifies daily demand over a complete common year sums exactly to the configured annual demand volume.</summary>
        [TestMethod]
        public void RunTimeStep_CommonYearDaily_SumsToAnnualDemand()
        {
            FarmDamRepeatingMonthlyDemandModel model = MakeScenario11DemandModel();
            model.Initialise();

            const int year = 1951;
            const double annualDemandFactor = 0.5;
            const double storageCapacityML = 50.0;
            const double expectedAnnualDemand = annualDemandFactor * storageCapacityML;

            Assert.IsFalse(
                DateTime.IsLeapYear(year),
                "The test year must be a common year.");

            double actualAnnualDemand = SumDailyDemandForYear(model, year);

            Assert.AreEqual(
                expectedAnnualDemand,
                actualAnnualDemand,
                1.0E-10,
                "Daily demand over the 1951 common year should sum exactly to the configured annual demand volume.");
        }

        /// <summary>Verifies daily demand over a complete leap year sums exactly to the configured annual demand volume.</summary>
        [TestMethod]
        public void RunTimeStep_LeapYearDaily_SumsToAnnualDemand()
        {
            FarmDamRepeatingMonthlyDemandModel model = MakeScenario11DemandModel();
            model.Initialise();

            const int year = 1952;
            const double annualDemandFactor = 0.5;
            const double storageCapacityML = 50.0;
            const double expectedAnnualDemand = annualDemandFactor * storageCapacityML;

            Assert.IsTrue(
                DateTime.IsLeapYear(year),
                "The test year must be a leap year.");

            double actualAnnualDemand = SumDailyDemandForYear(model, year);

            Assert.AreEqual(
                expectedAnnualDemand,
                actualAnnualDemand,
                1.0E-10,
                "Daily demand over the 1952 leap year should sum exactly to the configured annual demand volume.");
        }

        /// <summary>Verifies repeating-monthly demand rejects a negative monthly scale factor.</summary>
        [TestMethod]
        public void Initialise_NegativeMonthlyScaleFactor_ThrowsInvalidDataException()
        {
            FarmDamRepeatingMonthlyDemandModel model = MakeScenario11DemandModel();
            model.MonthlyScaleFactors[1] = -0.5;

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(
                model.Initialise,
                "Repeating-monthly demand should reject a negative scale factor.");

            StringAssert.Contains(exception.Message, "February");
            StringAssert.Contains(exception.Message, "-0.5");
        }

        /// <summary>Verifies a zero monthly scale factor is valid and suppresses repeating-monthly demand for that month.</summary>
        [TestMethod]
        public void Initialise_ZeroMonthlyScaleFactor_IsAcceptedAndProducesZeroDemand()
        {
            FarmDamRepeatingMonthlyDemandModel model = MakeScenario11DemandModel();
            model.MonthlyScaleFactors[1] = 0.0;

            model.Initialise();
            model.RunTimeStep(new DateTime(1952, 2, 15), TimeSpan.FromDays(1));

            Assert.AreEqual(
                0.0,
                model.UnrestrictedDemand,
                1.0E-12,
                "A zero February scale factor should suppress February demand.");
        }
    }
}