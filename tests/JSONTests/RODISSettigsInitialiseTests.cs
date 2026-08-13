// <copyright file="RODISSettingsInitialiseTests.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>
using RODIS.ModelRun;
using RODIS.ModelSettings;
using UnitsNet;
using UnitsNet.Units;

namespace RODIS.Tests
{
    [TestClass]
    public class RODISSettingsInitialiseTests
    {
        /// <summary>
        /// Ensure custom unit abbreviations are registered before any test runs.
        /// Mirrors the static constructor in RODISSettings but guarantees it for the test assembly.
        /// </summary>
        [ClassInitialize]
        public static void RegisterUnitAbbreviations(TestContext context)
        {
            UnitAbbreviationsCache.Default.MapUnitToAbbreviation(VolumeUnit.Megaliter, "ML");
            UnitAbbreviationsCache.Default.MapUnitToAbbreviation(AreaUnit.SquareKilometer, "km2");
        }

        // ----------------------------------------------
        //  Helper
        // ----------------------------------------------

        private static RODISSettings CreateSettingsForInit(
            string catchmentArea = "",
            string volumeThresholdForDemandGroups = "",
            bool useVolumeThresholdForDemandGroups = false,
            string volumeThresholdForBypass = "",
            Dictionary<string, double> maxVolumesAndProbs = null)
        {
            var s = new RODISSettings
            {
                CatchmentArea = catchmentArea,
                VolumeThresholdForDemandGroups = volumeThresholdForDemandGroups,
                UseVolumeThresholdForDemandGroups = useVolumeThresholdForDemandGroups,
                VolumeThresholdForBypass = volumeThresholdForBypass,
                MaxVolumesAndIntervalProbabilities = maxVolumesAndProbs,
                TimeSeriesDemandModelsJSONPath = string.Empty,
                RepeatingMonthlyDemandModelsJSONPath = string.Empty,
                VolumeFromInputPropertiesJSON = string.Empty,
                CatchmentAreaFromInputPropertiesJSON = string.Empty,
            };
            return s;
        }

        // -----------------------------------------------
        //  1. SetCatchmentArea  (string ? km²)
        // -----------------------------------------------

        [TestMethod]
        [DataRow("100 km²", 100.0)]
        [DataRow("100 km2", 100.0)]
        [DataRow("50.5 km²", 50.5)]
        public void SetCatchmentArea_ValidKm2String_ParsesCorrectly(string input, double expectedKm2)
        {
            var settings = CreateSettingsForInit(catchmentArea: input);
            settings.InitialiseFromJSON();
            Assert.AreEqual(expectedKm2, settings.GetCatchmentAreakm2(), 0.01,
                $"CatchmentArea '{input}' should parse to {expectedKm2} km²");
        }

        [TestMethod]
        public void SetCatchmentArea_EmptyString_LeavesDefault()
        {
            var settings = CreateSettingsForInit(catchmentArea: "");
            settings.InitialiseFromJSON();
            Assert.AreEqual(-1.0, settings.GetCatchmentAreakm2(), 0.001,
                "Empty CatchmentArea string should leave default (-1)");
        }

        [TestMethod]
        public void SetCatchmentArea_NegativeValue_ClampsToZero()
        {
            var settings = CreateSettingsForInit(catchmentArea: "-50 km²");
            settings.InitialiseFromJSON();
            Assert.IsTrue(settings.GetCatchmentAreakm2() >= 0,
                "Negative catchment area should be clamped to >= 0");
        }

        [TestMethod]
        public void SetCatchmentArea_ViaDoubleOverload_SetsDirectly()
        {
            var settings = new RODISSettings();
            settings.SetCatchmentArea(123.45);
            Assert.AreEqual(123.45, settings.GetCatchmentAreakm2(), 0.001);
        }

        // -----------------------------------------------
        //  2. SetVolumeThresholdForDemandGroups
        // -----------------------------------------------

        [TestMethod]
        [DataRow("5 ML", 5.0)]
        [DataRow("10 ML", 10.0)]
        [DataRow("0.5 ML", 0.5)]
        public void SetVolumeThresholdForDemandGroups_PositiveML_ParsesCorrectly(string input, double expectedML)
        {
            var settings = CreateSettingsForInit(
                volumeThresholdForDemandGroups: input,
                useVolumeThresholdForDemandGroups: true);
            settings.InitialiseFromJSON();
            Assert.AreEqual(expectedML, settings.GetVolumeThresholdMLForDemandGroups(), 0.001,
                $"VolumeThresholdForDemandGroups '{input}' should parse to {expectedML} ML");
            Assert.IsTrue(settings.UseVolumeThresholdForDemandGroups,
                "UseVolumeThresholdForDemandGroups should remain true for positive threshold");
        }

        [TestMethod]
        public void SetVolumeThresholdForDemandGroups_ZeroOrNegative_DisablesFeature()
        {
            var settings = CreateSettingsForInit(
                volumeThresholdForDemandGroups: "0 ML",
                useVolumeThresholdForDemandGroups: true);
            settings.InitialiseFromJSON();
            Assert.IsFalse(settings.UseVolumeThresholdForDemandGroups,
                "UseVolumeThresholdForDemandGroups should be set to false for zero/negative threshold");
            Assert.IsTrue(settings.GetVolumeThresholdMLForDemandGroups() < 0,
                "volumeThresholdMLForDemandGroups should be reset to sentinel value (-9999)");
        }

        [TestMethod]
        public void SetVolumeThresholdForDemandGroups_EmptyString_LeavesDefault()
        {
            var settings = CreateSettingsForInit(
                volumeThresholdForDemandGroups: "",
                useVolumeThresholdForDemandGroups: false);
            settings.InitialiseFromJSON();
            Assert.AreEqual(-9999.0, settings.GetVolumeThresholdMLForDemandGroups(), 0.001,
                "Empty threshold string should leave default sentinel (-9999)");
        }

        // -----------------------------------------------
        //  3. SetVolumeThresholdForBypass
        // -----------------------------------------------

        [TestMethod]
        [DataRow("0 ML", 0.0)]
        [DataRow("5 ML", 5.0)]
        [DataRow("100 ML", 100.0)]
        public void SetVolumeThresholdForBypass_ValidML_ParsesCorrectly(string input, double expectedML)
        {
            var settings = CreateSettingsForInit(volumeThresholdForBypass: input);
            settings.InitialiseFromJSON();
            Assert.AreEqual(expectedML, settings.GetVolumeThresholdMLForBypass(), 0.001,
                $"VolumeThresholdForBypass '{input}' should parse to {expectedML} ML");
        }

        [TestMethod]
        public void SetVolumeThresholdForBypass_EmptyString_LeavesDefault()
        {
            var settings = CreateSettingsForInit(volumeThresholdForBypass: "");
            settings.InitialiseFromJSON();
            Assert.AreEqual(-9999.0, settings.GetVolumeThresholdMLForBypass(), 0.001,
                "Empty bypass threshold string should leave default sentinel (-9999)");
        }

        // -----------------------------------------------
        //  4. SetMaxVolumesAndIntervalProbabilities
        // -----------------------------------------------

        [TestMethod]
        public void SetMaxVolumesAndIntervalProbabilities_ValidTable_ParsesAllEntries()
        {
            var table = new Dictionary<string, double>
            {
                { "0 ML",  0.0 },
                { "1 ML",  0.3 },
                { "5 ML",  0.5 },
                { "10 ML", 0.2 },
            };
            var settings = CreateSettingsForInit(maxVolumesAndProbs: table);
            settings.InitialiseFromJSON();
            var parsed = settings.GetMaxVolumesAndIntervalProbabilities();
            Assert.AreEqual(4, parsed.Count, "Should have 4 entries after parsing");
            Assert.IsTrue(parsed.ContainsKey(0.0), "Should contain key 0.0 ML");
            Assert.IsTrue(parsed.ContainsKey(1.0), "Should contain key 1.0 ML");
            Assert.IsTrue(parsed.ContainsKey(5.0), "Should contain key 5.0 ML");
            Assert.IsTrue(parsed.ContainsKey(10.0), "Should contain key 10.0 ML");
        }

        [TestMethod]
        public void SetMaxVolumesAndIntervalProbabilities_ProbabilitiesPreserved()
        {
            var table = new Dictionary<string, double>
            {
                { "0 ML",  0.0 },
                { "5 ML",  0.6 },
                { "20 ML", 0.4 },
            };
            var settings = CreateSettingsForInit(maxVolumesAndProbs: table);
            settings.InitialiseFromJSON();
            var parsed = settings.GetMaxVolumesAndIntervalProbabilities();
            Assert.AreEqual(0.0, parsed[0.0], 0.001);
            Assert.AreEqual(0.6, parsed[5.0], 0.001);
            Assert.AreEqual(0.4, parsed[20.0], 0.001);
        }

        [TestMethod]
        public void SetMaxVolumesAndIntervalProbabilities_NullTable_ClearsExisting()
        {
            // Step 1: set up with a table via InitialiseFromJSON
            var table = new Dictionary<string, double>
            {
                { "1 ML", 0.5 },
                { "2 ML", 0.5 },
            };
            var settings = CreateSettingsForInit(maxVolumesAndProbs: table);
            settings.InitialiseFromJSON();
            Assert.AreEqual(2, settings.GetMaxVolumesAndIntervalProbabilities().Count,
                "Setup: should have 2 entries");

            // Step 2: re-initialise with null table — should clear
            settings.MaxVolumesAndIntervalProbabilities = null;
            settings.SetMaxVolumesAndIntervalProbabilities(null);
            Assert.AreEqual(0, settings.GetMaxVolumesAndIntervalProbabilities().Count,
                "Null table should clear existing entries");
        }

        // -----------------------------------------------
        //  5. ReadDemandModelsFromJSON — graceful no-op
        // -----------------------------------------------

        [TestMethod]
        public void ReadDemandModelsFromJSON_EmptyPaths_DoesNotThrow()
        {
            var settings = CreateSettingsForInit();
            settings.InitialiseFromJSON();
            Assert.AreEqual(0, settings.RepeatingMonthlyDemandGroups.Count,
                "No demand groups should be loaded when paths are empty");
            Assert.AreEqual(0, settings.TimeSeriesDemandGroups.Count,
                "No time series demand groups should be loaded when paths are empty");
        }

        [TestMethod]
        public void ReadDemandModelsFromJSON_NonExistentPaths_DoesNotThrow()
        {
            var settings = CreateSettingsForInit();
            settings.TimeSeriesDemandModelsJSONPath = @"C:\nonexistent\path\does_not_exist.json";
            settings.InitialiseFromJSON();
            Assert.AreEqual(0, settings.TimeSeriesDemandGroups.Count);
        }

        // -----------------------------------------------
        //  6. Equation loaders — graceful no-op
        // -----------------------------------------------

        [TestMethod]
        public void SetVolumeEquationFromJSON_EmptyPath_KeepsDefaultEquation()
        {
            var settings = CreateSettingsForInit();
            settings.InitialiseFromJSON();
            Assert.IsNotNull(settings.VolumeSurfaceAreaEquation,
                "Default SA-Volume equation should not be null");
            Assert.IsFalse(string.IsNullOrEmpty(settings.VolumeSurfaceAreaEquation.Equation),
                "Default SA-Volume equation string should not be empty");
        }

        [TestMethod]
        public void SetCatchmentAreaEquationFromJSON_EmptyPath_LeavesNull()
        {
            var settings = CreateSettingsForInit();
            settings.InitialiseFromJSON();
            Assert.IsNull(settings.VolumeCatchmentAreaEquation,
                "VolumeCatchmentAreaEquation should remain null when no JSON path is specified");
        }

        // -----------------------------------------------
        //  7. Full integration
        // -----------------------------------------------

        [TestMethod]
        public void InitialiseFromJSON_FullIntegration_SetsAllDerivedFields()
        {
            var table = new Dictionary<string, double>
            {
                { "0 ML",  0.0 },
                { "2 ML",  0.4 },
                { "10 ML", 0.6 },
            };
            var settings = CreateSettingsForInit(
                catchmentArea: "250 km²",
                volumeThresholdForDemandGroups: "3 ML",
                useVolumeThresholdForDemandGroups: true,
                volumeThresholdForBypass: "1 ML",
                maxVolumesAndProbs: table);
            settings.InitialiseFromJSON();

            Assert.AreEqual(250.0, settings.GetCatchmentAreakm2(), 0.01, "Catchment area");
            Assert.AreEqual(3.0, settings.GetVolumeThresholdMLForDemandGroups(), 0.01, "Demand threshold");
            Assert.IsTrue(settings.UseVolumeThresholdForDemandGroups, "UseVolumeThreshold flag");
            Assert.AreEqual(1.0, settings.GetVolumeThresholdMLForBypass(), 0.01, "Bypass threshold");
            var probs = settings.GetMaxVolumesAndIntervalProbabilities();
            Assert.AreEqual(3, probs.Count, "Probability distribution entry count");
            Assert.IsNotNull(settings.VolumeSurfaceAreaEquation, "SA-Volume equation");
        }

        // -----------------------------------------------
        //  8. GetDemandModelType
        // -----------------------------------------------

        [TestMethod]
        public void GetDemandModelType_NoGroups_ReturnsMissing()
        {
            var settings = new RODISSettings();
            Assert.AreEqual(ModelElementType.Missing, settings.GetDemandModelType());
        }

        [TestMethod]
        public void GetDemandModelType_OneRepeatingGroup_ReturnsRepeatingMonthly()
        {
            var settings = new RODISSettings();
            settings.RepeatingMonthlyDemandGroups.Add("Stock", new FarmDamRepeatingMonthlyDemandModel
            {
                DemandGroup = "Stock",
                AnnualDemandFactor = 0.5,
                MonthlyDemandProportions = new double[12],
            });
            Assert.AreEqual(ModelElementType.RepeatingMonthlyDemand, settings.GetDemandModelType(minGroupCount: 1));
        }

        [TestMethod]
        public void GetDemandModelType_MinGroupCount2_NeedsAtLeast2Groups()
        {
            var settings = new RODISSettings();
            settings.RepeatingMonthlyDemandGroups.Add("Stock", new FarmDamRepeatingMonthlyDemandModel
            {
                DemandGroup = "Stock",
                AnnualDemandFactor = 0.5,
                MonthlyDemandProportions = new double[12],
            });
            Assert.AreEqual(ModelElementType.Missing, settings.GetDemandModelType(minGroupCount: 2),
                "Should return Missing when group count < minGroupCount");
        }

        [TestMethod]
        public void GetDemandModelType_RepeatingTakesPrecedenceOverTimeSeries()
        {
            var settings = new RODISSettings();
            settings.RepeatingMonthlyDemandGroups.Add("Stock", new FarmDamRepeatingMonthlyDemandModel
            {
                DemandGroup = "Stock",
                AnnualDemandFactor = 0.5,
                MonthlyDemandProportions = new double[12],
            });
            settings.TimeSeriesDemandGroups.Add("Irrigation", new FarmDamTimeSeriesDemandModel
            {
                DemandGroup = "Irrigation",
                AnnualDemandFactor = 1.0,
            });
            Assert.AreEqual(ModelElementType.RepeatingMonthlyDemand, settings.GetDemandModelType(minGroupCount: 1),
                "RepeatingMonthly should take precedence when both are present");
        }

        [TestMethod]
        public void GetDemandModelType_FallsBackToTimeSeries_WhenNoRepeating()
        {
            var settings = new RODISSettings();
            settings.TimeSeriesDemandGroups.Add("Irrigation", new FarmDamTimeSeriesDemandModel
            {
                DemandGroup = "Irrigation",
                AnnualDemandFactor = 1.0,
            });
            Assert.AreEqual(ModelElementType.TimeSeriesDemand, settings.GetDemandModelType(minGroupCount: 1),
                "Should fall back to TimeSeriesDemand when no repeating groups exist");
        }

        // -----------------------------------------------
        //  9. GetGroupDemandModelIndexByVolume
        //     KEY FIX: use InitialiseFromJSON() to parse
        //     the threshold, then add groups AFTER
        //     (ReadDemandModelsFromJSON clears them)
        // -----------------------------------------------

        [TestMethod]
        public void GetGroupDemandModelIndexByVolume_BelowThreshold_ReturnsFirstKey()
        {
            var settings = CreateSettingsForInit(
                volumeThresholdForDemandGroups: "5 ML",
                useVolumeThresholdForDemandGroups: true);
            settings.InitialiseFromJSON();

            // Add demand groups AFTER InitialiseFromJSON (which clears them)
            settings.RepeatingMonthlyDemandGroups.Add("Small", new FarmDamRepeatingMonthlyDemandModel
            {
                DemandGroup = "Small",
                AnnualDemandFactor = 0.3,
                MonthlyDemandProportions = new double[12],
            });
            settings.RepeatingMonthlyDemandGroups.Add("Large", new FarmDamRepeatingMonthlyDemandModel
            {
                DemandGroup = "Large",
                AnnualDemandFactor = 0.8,
                MonthlyDemandProportions = new double[12],
            });

            string result = settings.GetGroupDemandModelIndexByVolume(3.0, ModelElementType.RepeatingMonthlyDemand);
            Assert.AreEqual("Small", result,
                "Volume 3 ML (below 5 ML threshold) should map to first demand group");
        }

        [TestMethod]
        public void GetGroupDemandModelIndexByVolume_AboveThreshold_ReturnsLastKey()
        {
            var settings = CreateSettingsForInit(
                volumeThresholdForDemandGroups: "5 ML",
                useVolumeThresholdForDemandGroups: true);
            settings.InitialiseFromJSON();

            // Add demand groups AFTER InitialiseFromJSON (which clears them)
            settings.RepeatingMonthlyDemandGroups.Add("Small", new FarmDamRepeatingMonthlyDemandModel
            {
                DemandGroup = "Small",
                AnnualDemandFactor = 0.3,
                MonthlyDemandProportions = new double[12],
            });
            settings.RepeatingMonthlyDemandGroups.Add("Large", new FarmDamRepeatingMonthlyDemandModel
            {
                DemandGroup = "Large",
                AnnualDemandFactor = 0.8,
                MonthlyDemandProportions = new double[12],
            });

            string result = settings.GetGroupDemandModelIndexByVolume(10.0, ModelElementType.RepeatingMonthlyDemand);
            Assert.AreEqual("Large", result,
                "Volume 10 ML (above 5 ML threshold) should map to last demand group");
        }

        [TestMethod]
        public void GetGroupDemandModelIndexByVolume_ThresholdDisabled_ReturnsEmpty()
        {
            var settings = new RODISSettings
            {
                UseVolumeThresholdForDemandGroups = false,
            };
            string result = settings.GetGroupDemandModelIndexByVolume(3.0, ModelElementType.RepeatingMonthlyDemand);
            Assert.AreEqual(string.Empty, result,
                "Should return empty when volume threshold is disabled");
        }

        // -----------------------------------------------
        //  10. Idempotency
        //      KEY FIX: call SetMaxVolumesAndIntervalProbabilities
        //      directly for the second pass instead of
        //      full InitialiseFromJSON (avoids re-clearing demands)
        // -----------------------------------------------

        [TestMethod]
        public void InitialiseFromJSON_CalledTwice_ProducesSameResult()
        {
            var table = new Dictionary<string, double>
            {
                { "0 ML", 0.0 },
                { "5 ML", 1.0 },
            };
            var settings = CreateSettingsForInit(
                catchmentArea: "100 km²",
                volumeThresholdForBypass: "2 ML",
                maxVolumesAndProbs: table);

            settings.InitialiseFromJSON();
            double area1 = settings.GetCatchmentAreakm2();
            double bypass1 = settings.GetVolumeThresholdMLForBypass();
            int probCount1 = settings.GetMaxVolumesAndIntervalProbabilities().Count;

            settings.InitialiseFromJSON();
            double area2 = settings.GetCatchmentAreakm2();
            double bypass2 = settings.GetVolumeThresholdMLForBypass();
            int probCount2 = settings.GetMaxVolumesAndIntervalProbabilities().Count;

            Assert.AreEqual(area1, area2, 0.001, "Catchment area should be identical on second call");
            Assert.AreEqual(bypass1, bypass2, 0.001, "Bypass threshold should be identical on second call");
            Assert.AreEqual(probCount1, probCount2, "Probability table count should be identical on second call");
        }
    }
}
