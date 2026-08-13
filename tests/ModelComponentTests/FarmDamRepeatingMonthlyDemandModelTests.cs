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
    }
}