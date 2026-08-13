namespace RODISUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using RODIS.ModelRun;

    [TestClass]
    public class UniformInflowSubcatchmentModelTests
    {
        // ----------------------------------------------------
        // BeforeRunTimeStep — legacy vs non-legacy area calculation
        // ----------------------------------------------------

        [TestMethod]
        public void BeforeRunTimeStep_Legacy_NonWaterBodyAreaEqualsTotal()
        {
            var model = new SubcatchmentInflowModel
            {
                AreaKM2 = 100.0,
                WaterBodyAreaKM2 = 20.0,
            };

            model.BeforeRunTimeStep(isLegacySTEDICalculationMethods: true);

            Assert.AreEqual(100.0, model.NonWaterBodyAreaKM2, 0.001,
                "Legacy mode: NonWaterBodyArea should equal total AreaKM2");
        }

        [TestMethod]
        public void BeforeRunTimeStep_NonLegacy_SubtractsWaterBodyArea()
        {
            var model = new SubcatchmentInflowModel
            {
                AreaKM2 = 100.0,
                WaterBodyAreaKM2 = 20.0,
            };

            model.BeforeRunTimeStep(isLegacySTEDICalculationMethods: false);

            Assert.AreEqual(80.0, model.NonWaterBodyAreaKM2, 0.001,
                "Non-legacy: should subtract water body area");
        }

        [TestMethod]
        public void BeforeRunTimeStep_NonLegacy_WaterBodyExceedsTotal_ClampedToZero()
        {
            var model = new SubcatchmentInflowModel
            {
                AreaKM2 = 10.0,
                WaterBodyAreaKM2 = 15.0,
            };

            model.BeforeRunTimeStep(isLegacySTEDICalculationMethods: false);

            Assert.AreEqual(0.0, model.NonWaterBodyAreaKM2, 0.001,
                "Should clamp to zero, not go negative");
        }

        // ----------------------------------------------------
        // RunTimeStep — downstream flow = area × rate
        // ----------------------------------------------------

        [TestMethod]
        public void RunTimeStep_CalculatesDownstreamFlow()
        {
            var model = new SubcatchmentInflowModel
            {
                AreaKM2 = 100.0,
                WaterBodyAreaKM2 = 0.0,
                NonWaterBodyAreaKM2 = 100.0,
                InflowRateMLPerKM2 = 0.5,
            };

            model.RunTimeStep();

            Assert.AreEqual(50.0, model.DownstreamFlow, 0.001, "100 km² × 0.5 ML/km² = 50 ML");
            Assert.AreEqual(0.0, model.VolumeBalanceMisclosure, 0.001);
        }

        [TestMethod]
        public void RunTimeStep_ZeroArea_ZeroFlow()
        {
            var model = new SubcatchmentInflowModel
            {
                NonWaterBodyAreaKM2 = 0.0,
                InflowRateMLPerKM2 = 10.0,
            };

            model.RunTimeStep();

            Assert.AreEqual(0.0, model.DownstreamFlow, 0.001);
        }

        [TestMethod]
        public void RunTimeStep_ZeroRate_ZeroFlow()
        {
            var model = new SubcatchmentInflowModel
            {
                NonWaterBodyAreaKM2 = 100.0,
                InflowRateMLPerKM2 = 0.0,
            };

            model.RunTimeStep();

            Assert.AreEqual(0.0, model.DownstreamFlow, 0.001);
        }
    }
}