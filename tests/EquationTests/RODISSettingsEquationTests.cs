// <copyright file="RODISSettingsEquationTests.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>
using RODIS.ModelRun;
using RODIS.ModelSettings;

namespace RODIS.Tests
{
    [TestClass]
    public class RODISSettingsEquationTests
    {
        /// <summary>Default settings with the Lowe et al. (2005) SA–Volume equation already wired in.</summary>
        private static RODISSettings CreateDefaultSettings()
        {
            var settings = new RODISSettings();
            // VolumeSurfaceAreaEquation is initialised with the Lowe default in RODISSettings,
            // so no extra setup is needed for SA ? Volume tests.
            return settings;
        }

        /// <summary>Settings that also have a Volume ? CatchmentArea equation (simple power law for test).</summary>
        private static RODISSettings CreateSettingsWithVolumeCatchmentAreaEquation()
        {
            var settings = CreateDefaultSettings();
            // Example: CatchmentArea(km²) = 0.05 * Volume(ML) ^ 0.6
            settings.VolumeCatchmentAreaEquation = new EquationParser()
            {
                VariablesWithDescriptions = new Dictionary<string, string> { { "Volume", "Storage volume when full in ML" } },
                Equation = "0.05*Volume^0.6",
            };
            return settings;
        }

        // ----------------------------------------------
        //  Surface Area ? Volume equation tests
        // ----------------------------------------------

        [TestMethod]
        [DataRow(1000)]
        [DataRow(5000)]
        [DataRow(10000)]
        [DataRow(20000)]
        public void EvaluateSurfaceAreaVolumeEquation_PositiveArea_ReturnsFinitePositive(double surfaceAreaM2)
        {
            var settings = CreateDefaultSettings();

            double volumeML = settings.EvaluateSurfaceAreaVolumeEquation(surfaceAreaM2);

            Assert.IsFalse(double.IsNaN(volumeML), $"Volume should not be NaN for SA = {surfaceAreaM2} m²");
            Assert.IsTrue(volumeML > 0, $"Volume should be positive for SA = {surfaceAreaM2} m², got {volumeML}");
        }

        [TestMethod]
        public void EvaluateSurfaceAreaVolumeEquation_ZeroArea_ReturnsZeroOrNaN()
        {
            var settings = CreateDefaultSettings();

            double volumeML = settings.EvaluateSurfaceAreaVolumeEquation(0.0);

            // Equation 0.0001449275 * 0^1.314 = 0
            Assert.IsTrue(volumeML == 0.0 || double.IsNaN(volumeML), $"Volume for zero surface area should be 0 or NaN, got {volumeML}");
        }

        [TestMethod]
        public void EvaluateSurfaceAreaVolumeEquation_VolumeIncreasesWithArea()
        {
            var settings = CreateDefaultSettings();

            double volSmall = settings.EvaluateSurfaceAreaVolumeEquation(1000);
            double volLarge = settings.EvaluateSurfaceAreaVolumeEquation(20000);

            Assert.IsTrue(volLarge > volSmall, $"Volume at 20 000 m² ({volLarge:F4} ML) should exceed volume at 1 000 m² ({volSmall:F4} ML)");
        }

        // ----------------------------------------------
        //  Volume ? Catchment Area equation tests
        // ----------------------------------------------

        [TestMethod]
        [DataRow(1)]
        [DataRow(5)]
        [DataRow(10)]
        [DataRow(20)]
        public void EvaluateVolumeCatchmentAreaEquation_PositiveVolume_ReturnsFinitePositive(double volumeML)
        {
            var settings = CreateSettingsWithVolumeCatchmentAreaEquation();

            double areaKM2 = settings.EvaluateVolumeCatchmentAreaEquation(volumeML);

            Assert.IsFalse(double.IsNaN(areaKM2), $"Catchment area should not be NaN for Volume = {volumeML} ML");
            Assert.IsTrue(areaKM2 > 0, $"Catchment area should be positive for Volume = {volumeML} ML, got {areaKM2}");
        }

        [TestMethod]
        public void EvaluateVolumeCatchmentAreaEquation_AreaIncreasesWithVolume()
        {
            var settings = CreateSettingsWithVolumeCatchmentAreaEquation();

            double areaSmall = settings.EvaluateVolumeCatchmentAreaEquation(1);
            double areaLarge = settings.EvaluateVolumeCatchmentAreaEquation(20);

            Assert.IsTrue(areaLarge > areaSmall,
                $"Catchment area at 20 ML ({areaLarge:F4} km²) should exceed area at 1 ML ({areaSmall:F4} km²)");
        }

        // ----------------------------------------------
        //  Solve for Surface Area from Volume (inverse)
        // ----------------------------------------------

        [TestMethod]
        [DataRow(1.0)]
        [DataRow(5.0)]
        [DataRow(10.0)]
        [DataRow(20.0)]
        public void SolveForSurfaceAreaFromVolume_PositiveVolume_ReturnsFinitePositive(double volumeML)
        {
            var settings = CreateDefaultSettings();

            double surfaceAreaM2 = settings.SolveForSurfaceAreaFromVolume(volumeML);

            Assert.IsFalse(double.IsNaN(surfaceAreaM2), $"Surface area should not be NaN for Volume = {volumeML} ML");
            Assert.IsTrue(surfaceAreaM2 > 0, $"Surface area should be positive for Volume = {volumeML} ML, got {surfaceAreaM2}");
        }

        [TestMethod]
        public void SolveForSurfaceAreaFromVolume_ZeroVolume_ReturnsZero()
        {
            var settings = CreateDefaultSettings();

            double surfaceAreaM2 = settings.SolveForSurfaceAreaFromVolume(0.0);

            Assert.AreEqual(0.0, surfaceAreaM2, 1e-9, "Surface area for zero volume should be 0");
        }

        [TestMethod]
        [DataRow(1.0)]
        [DataRow(5.0)]
        [DataRow(10.0)]
        [DataRow(20.0)]
        public void SolveForSurfaceAreaFromVolume_RoundTripsWithEvaluate(double volumeML)
        {
            // Solve SA from V, then evaluate V from SA — should get back to the original volume
            var settings = CreateDefaultSettings();
            const double toleranceML = 0.01;

            double solvedSA = settings.SolveForSurfaceAreaFromVolume(volumeML);
            double roundTrippedVolume = settings.EvaluateSurfaceAreaVolumeEquation(solvedSA);

            Assert.AreEqual(volumeML, roundTrippedVolume, toleranceML, $"Round-trip failed: V={volumeML} ? SA={solvedSA:F1} ? V={roundTrippedVolume:F4}");
        }

        [TestMethod]
        public void SolveForSurfaceAreaFromVolume_AreaIncreasesWithVolume()
        {
            var settings = CreateDefaultSettings();

            double saSmall = settings.SolveForSurfaceAreaFromVolume(1);
            double saLarge = settings.SolveForSurfaceAreaFromVolume(20);

            Assert.IsTrue(saLarge > saSmall, $"Surface area at 20 ML ({saLarge:F1} m²) should exceed area at 1 ML ({saSmall:F1} m²)");
        }
    }
}
