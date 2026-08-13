// <copyright file="RODISEngine.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    using RODIS.InputOutput;
    using RODIS.JSON;
    using RODIS.ModelSettings;
    using RODIS.MonteCarlo;
    using RODIS.Series;
    using RODIS.Static;
    using RODIS.Statistics;
    using System.Xml.Linq;

    /// <summary>
    /// Executes RODIS runs (single, multi-scenario, Monte Carlo).
    /// Owns model state including the catchment model runner and optional generated dam network.
    /// </summary>
    public class RODISEngine
    {
        /// <summary>Name of the calling program assembly, written to output file headers.</summary>
        private readonly string programName;

        /// <summary>Version string of the calling program assembly, written to output file headers.</summary>
        private readonly string programVersion;

        /// <summary>Legacy STEDI dam nodes read from input, or null if using GIS spatial initialisation.</summary>
        public LegacySTEDIDamNode[] LegacySTEDIDamNodes { get; set; } = null;

        /// <summary>Gets the Catchment Model runner object.</summary>
        public CatchmentModelRunner CatchmentModelRunner { get; private set; } = null;

        /// <summary>Subcatchment areas in km² for base scenario before Monte Carlo or scenario implementation.</summary>
        private double[] baseSubcatchmentAreas;

        /// <summary>Demand proportions by month for each water body for base scenario, before Monte Carlo or scenario implementation.</summary>
        private double[][] baseMonthlyDemandProportions;

        /// <summary>Performance timer for diagnosing bottlenecks. Null = no timing.</summary>
        public PerformanceTimer Timer { get; set; } = null;

        /// <summary>Initialises a new RODISEngine with program identity for output file headers.</summary>
        public RODISEngine()
        {
            this.programName = string.Empty;
            this.programVersion = string.Empty;
        }

        /// <summary>Initialises a new RODISEngine with program identity for output file headers.</summary>
        /// <param name="programName">Name of the calling program assembly.</param>
        /// <param name="programVersion">Version string of the calling program assembly.</param>
        public RODISEngine(string programName, string programVersion)

        {
            this.programName = programName ?? string.Empty;
            this.programVersion = programVersion ?? string.Empty;
        }

        /// <summary>Loads base RODIS settings from JSON and generates dam network if required.</summary>
        /// <param name="settingsJsonPath">Path to the RODIS JSON settings file.</param>
        /// <returns>Initialised RODISSettings with dam network stored on this engine instance.</returns>
        public RODISSettings LoadBaseSettings(string settingsJsonPath)
        {
            try
            {
                LegacySTEDIDamNode[] damNodes;
                RODISSettings settings = RODISSettingsHelper.LoadAndValidateSettings(settingsJsonPath, out damNodes);
                this.LegacySTEDIDamNodes = damNodes;
                return settings;
            }
            catch (Exception ex) when (ex is not FileNotFoundException and not ArgumentException and not InvalidDataException)
            {
                throw new InvalidDataException($"Failed to load RODIS settings from '{settingsJsonPath}'. {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads scenarios from external JSON if specified in settings, parses volume revision dictionaries, and ensures at least one scenario ("Base case") exists.
        /// </summary>
        /// <param name="settings">RODIS settings to populate with scenarios.</param>
        /// <returns>True if multiple named scenarios were loaded; false if only the default base case.</returns>
        public bool LoadScenariosIntoSettings(RODISSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!string.IsNullOrWhiteSpace(settings.ScenarioControlFileJSONPath))
            {
                if (!File.Exists(settings.ScenarioControlFileJSONPath))
                {
                    throw new FileNotFoundException($"Scenario control file does not exist: '{settings.ScenarioControlFileJSONPath}'.", settings.ScenarioControlFileJSONPath);
                }

                Console.WriteLine("Reading scenarios from " + settings.ScenarioControlFileJSONPath);

                try
                {
                    settings.ScenariosToRun = JSONSerialisation.DeserialiseFileThrowOnError<Dictionary<string, Scenario>>(settings.ScenarioControlFileJSONPath);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Failed to deserialise scenario control file '{settings.ScenarioControlFileJSONPath}'. {ex.Message}", ex);
                }

                if (settings.ScenariosToRun == null || settings.ScenariosToRun.Count == 0)
                    throw new InvalidDataException($"Scenario control file '{settings.ScenarioControlFileJSONPath}' contains no scenarios.");

                // Post-deserialisation: parse volume revision dictionaries for each scenario
                foreach (var kvp in settings.ScenariosToRun)
                {
                    var scen = kvp.Value;
                    if (scen.RevisionYears_TotalStorageVolume != null && scen.RevisionYears_TotalStorageVolume.Count > 0)
                    {
                        try
                        {
                            scen.ParseVolumeRevisionDictionary();
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidDataException(
                                $"Failed to parse volume revision data for scenario '{kvp.Key}' "
                                + $"in '{settings.ScenarioControlFileJSONPath}'. {ex.Message}", ex);
                        }
                    }
                }
            }

            bool isMultipleScenarios = settings.ScenariosToRun != null && settings.ScenariosToRun.Count > 0;
            if (!isMultipleScenarios)
            {
                settings.ScenariosToRun = new Dictionary<string, Scenario>
                {
                    { "Base case", new Scenario() }
                };
            }

            return isMultipleScenarios;
        }

        /// <summary>
        /// Captures base model state for properties that are modified multiplicatively by ApplyUncertaintyRealisation.
        /// Must be called once after Initialise and before any MC iterations. RestoreBaseModelState() resets these before each uncertainty application.
        /// Currently captures only subcatchment areas (needed by U4); monthly demand proportions are restored via the per-scenario LOD pipeline.
        /// </summary>
        public void CaptureBaseModelState()
        {
            var catchment = this.CatchmentModelRunner.catchmentModel;

            // U4 requires base subcatchment areas to prevent multiplicative accumulation of CatchmentAreaMultipliers across iterations.
            if (catchment.SubcatchmentsInflowModels != null)
            {
                this.baseSubcatchmentAreas = new double[catchment.SubcatchmentsInflowModels.Length];
                for (int i = 0; i < catchment.SubcatchmentsInflowModels.Length; i++)
                {
                    this.baseSubcatchmentAreas[i] = catchment.SubcatchmentsInflowModels[i].AreaKM2;
                }
            }

            // U7 requires base monthly demand proportions to prevent multiplicative accumulation of MonthlyDemandProportions across iterations.
            if (catchment.RepeatingMonthlyDemandModels != null)
            {
                this.baseMonthlyDemandProportions = new double[catchment.RepeatingMonthlyDemandModels.Length][];
                for (int i = 0; i < catchment.RepeatingMonthlyDemandModels.Length; i++)
                {
                    var src = catchment.RepeatingMonthlyDemandModels[i].MonthlyDemandProportions;
                    if (src == null || src.Length != 12)
                    {
                        this.baseMonthlyDemandProportions[i] = null;
                        continue;
                    }
                    this.baseMonthlyDemandProportions[i] = new double[12];
                    Array.Copy(src, this.baseMonthlyDemandProportions[i], 12);
                }
            }
        }

        /// <summary>
        /// Restores subcatchment areas, demand proportions, and observed flow time series to their base (pre-uncertainty) values.
        /// Called at the start of each <see cref="ApplyUncertaintyRealisation"/> invocation to prevent multiplicative accumulation across scenarios and iterations.
        /// </summary>
        // NOTE: This reset covers Investigation 6 transient state only. Global multipliers used by ApplyMonteCarloSample
        // (catchment.RainfallMultiplier, PETMultiplier, SubcatchmentsInflowModels[].InflowMultiplier) are NOT reset here;
        // if both MC paths are ever mixed in one engine instance, that scope will need extending.
        public void RestoreBaseModelState()
        {
            var catchment = this.CatchmentModelRunner.catchmentModel;

            // Restore subcatchment areas
            if (this.baseSubcatchmentAreas != null)
            {
                for (int i = 0; i < catchment.SubcatchmentsInflowModels.Length
                                 && i < this.baseSubcatchmentAreas.Length; i++)
                {
                    catchment.SubcatchmentsInflowModels[i].AreaKM2 = this.baseSubcatchmentAreas[i];
                }
            }

            // Restore monthly demand proportions
            if (this.baseMonthlyDemandProportions != null && catchment.RepeatingMonthlyDemandModels != null)
            {
                for (int i = 0; i < catchment.RepeatingMonthlyDemandModels.Length
                                 && i < this.baseMonthlyDemandProportions.Length; i++)
                {
                    Array.Copy(
                        this.baseMonthlyDemandProportions[i],
                        catchment.RepeatingMonthlyDemandModels[i].MonthlyDemandProportions, 12);
                }
            }

            // At the end of RestoreBaseModelState — reset transient per-dam multipliers/flags to defaults.
            // Without this, a U5/U9/U10 value from a previous iteration persists if the block is disabled in the current iteration.
            if (catchment.WaterBodyNodes != null)
            {
                for (int i = 0; i < catchment.WaterBodyNodes.Length; i++)
                {
                    catchment.WaterBodyNodes[i].LocalRainfallMultiplier = 1.0;
                    catchment.WaterBodyNodes[i].LocalEvaporationMultiplier = 1.0;
                    catchment.WaterBodyNodes[i].IgnoreUpstreamDamFlows = false;
                }
            }

            // Restore original observed flow (overwritten by ReloadInputFlowTimeSeries during LOD scenarios)
            this.CatchmentModelRunner.RestoreOriginalFlowTimeSeries();
        }

        /// <summary>
        /// Writes diagnostic information about the mapping between water body nodes and demand model arrays.
        /// Call after model initialisation to verify that per-dam demand factor indexing will work correctly.
        /// </summary>
        public void DiagnoseDemandModelMapping()
        {
            var catchment = this.CatchmentModelRunner.catchmentModel;
            int nWB = catchment.WaterBodyNodes?.Length ?? 0;
            int nRM = catchment.RepeatingMonthlyDemandModels?.Length ?? 0;
            int nTS = catchment.TimeSeriesDemandModels?.Length ?? 0;

            Console.WriteLine();
            Console.WriteLine("-- Demand Model Mapping Diagnostic --");
            Console.WriteLine($"  Water body nodes:              {nWB}");
            Console.WriteLine($"  RepeatingMonthlyDemandModels:  {nRM}");
            Console.WriteLine($"  TimeSeriesDemandModels:        {nTS}");

            // -- Check 1: Array length alignment --
            bool rmAligned = nRM == nWB || nRM == 0;
            bool tsAligned = nTS == nWB || nTS == 0;

            if (rmAligned && tsAligned)
            {
                Console.WriteLine("  ? Demand model arrays are 1:1 with water body nodes (or absent).");
            }
            else
            {
                Console.WriteLine("  ? MISMATCH — per-dam demand factor indexing may not align correctly.");
                if (nRM > 0 && nRM != nWB)
                    Console.WriteLine($"    RepeatingMonthly: {nRM} models vs {nWB} water bodies (delta = {nRM - nWB})");
                if (nTS > 0 && nTS != nWB)
                    Console.WriteLine($"    TimeSeries: {nTS} models vs {nWB} water bodies (delta = {nTS - nWB})");
            }

            // -- Check 2: Per-node detail dump (first 10 + last 2, to keep output manageable) --
            if (nWB == 0)
                return;

            Console.WriteLine();
            Console.WriteLine("  idx | WB_Volume_ML | WB_SA_m2     | RM_DemandFactor | TS_DemandFactor | RM_GroupName          | TS_GroupName");
            Console.WriteLine("  ----|--------------|--------------|-----------------|-----------------|----------------------|---------------------");

            int[] indicesToShow = GetDiagnosticIndices(nWB, maxHead: 10, maxTail: 2);
            int prevIdx = -1;

            for (int k = 0; k < indicesToShow.Length; k++)
            {
                int i = indicesToShow[k];

                // Show ellipsis if there's a gap
                if (prevIdx >= 0 && i > prevIdx + 1)
                    Console.WriteLine("  ... |              |              |                 |                 |                      |");

                var node = catchment.WaterBodyNodes[i];
                double vol = node.MaxStorageCapacityVolumeAtSpill;
                double sa = node.SurfaceAreaAtSpill;

                string rmFactor = "n/a";
                string rmGroup = "n/a";
                if (catchment.RepeatingMonthlyDemandModels != null && i < nRM)
                {
                    rmFactor = catchment.RepeatingMonthlyDemandModels[i].AnnualDemandFactor.ToString("F4");
                    rmGroup = catchment.RepeatingMonthlyDemandModels[i].GetType().Name;
                }

                string tsFactor = "n/a";
                string tsGroup = "n/a";
                if (catchment.TimeSeriesDemandModels != null && i < nTS)
                {
                    tsFactor = catchment.TimeSeriesDemandModels[i].AnnualDemandFactor.ToString("F4");
                    tsGroup = catchment.TimeSeriesDemandModels[i].GetType().Name;
                }

                Console.WriteLine($"  {i,3} | {vol,12:F2} | {sa,12:F1} | {rmFactor,15} | {tsFactor,15} | {rmGroup,-20} | {tsGroup,-20}");
                prevIdx = i;
            }

            // -- Check 3: Demand group distribution --
            if (nRM > 0)
            {
                Console.WriteLine();
                double minFac = double.MaxValue, maxFac = double.MinValue, sumFac = 0.0;
                for (int i = 0; i < nRM; i++)
                {
                    double f = catchment.RepeatingMonthlyDemandModels[i].AnnualDemandFactor;
                    minFac = Math.Min(minFac, f);
                    maxFac = Math.Max(maxFac, f);
                    sumFac += f;
                }

                Console.WriteLine($"  RepeatingMonthly AnnualDemandFactor: min={minFac:F4}, mean={sumFac / nRM:F4}, max={maxFac:F4}");
            }

            if (nTS > 0)
            {
                Console.WriteLine();
                double minFac = double.MaxValue, maxFac = double.MinValue, sumFac = 0.0;
                for (int i = 0; i < nTS; i++)
                {
                    double f = catchment.TimeSeriesDemandModels[i].AnnualDemandFactor;
                    minFac = Math.Min(minFac, f);
                    maxFac = Math.Max(maxFac, f);
                    sumFac += f;
                }

                Console.WriteLine($"  TimeSeries AnnualDemandFactor: min={minFac:F4}, mean={sumFac / nTS:F4}, max={maxFac:F4}");
            }

            // -- Check 4: Volume threshold grouping --
            Console.WriteLine();
            int countAbove = 0;
            int countBelow = 0;
            double volumeThreshold = 5000.0; // from settings; hardcoded here for diagnostic only
            for (int i = 0; i < nWB; i++)
            {
                if (catchment.WaterBodyNodes[i].MaxStorageCapacityVolumeAtSpill >= volumeThreshold)
                    countAbove++;
                else
                    countBelow++;
            }

            Console.WriteLine($"  Volume threshold grouping (threshold = {volumeThreshold:F0} ML):");
            Console.WriteLine($"    >= threshold: {countAbove} dams");
            Console.WriteLine($"    <  threshold: {countBelow} dams");
            Console.WriteLine("--------------------------------------");
            Console.WriteLine();
        }

        /// <summary>Returns an array of indices to display: first maxHead, last maxTail, avoiding duplicates.</summary>
        private static int[] GetDiagnosticIndices(int total, int maxHead, int maxTail)
        {
            if (total <= maxHead + maxTail)
            {
                int[] all = new int[total];
                for (int i = 0; i < total; i++)
                    all[i] = i;
                return all;
            }

            var indices = new List<int>();
            for (int i = 0; i < maxHead; i++)
                indices.Add(i);
            for (int i = total - maxTail; i < total; i++)
            {
                if (!indices.Contains(i))
                    indices.Add(i);
            }

            return indices.ToArray();
        }

        /// <summary>
        /// Validates input files, reads time series data, initialises the catchment model runner
        /// and prepares the model for execution.
        /// </summary>
        /// <param name="rodisSettings">Settings for this RODIS run.</param>
        /// <returns>True if setup completed successfully; false if a validation or load error occurred.</returns>
        public bool SetUpFirstRun(RODISSettings rodisSettings)
        {
            try
            {
                // --- Validate input files exist ---
                RODISSettingsHelper.ValidateFilesExist(rodisSettings.RainfallInputPath, rodisSettings.PETInputPath, rodisSettings.FlowInputPath);

                Console.WriteLine("Reading input file for {0}: {1}", "rainfall", rodisSettings.RainfallInputPath);
                Console.WriteLine("Reading input file for {0}: {1}", "evaporation", rodisSettings.PETInputPath);
                if (rodisSettings.CalculateUnimpactedGivenObserved)
                    Console.WriteLine("Reading input file for {0}: {1}", "observed flow", rodisSettings.FlowInputPath);
                else
                    Console.WriteLine("Reading input file for {0}: {1}", "unimpacted flow", rodisSettings.FlowInputPath);

                if (rodisSettings.TimeSeriesDemandGroups.Count > 0)
                {
                    HashSet<string> demandTimeSeriesPaths = new HashSet<string>();
                    foreach (var group in rodisSettings.TimeSeriesDemandGroups)
                    {
                        if (!demandTimeSeriesPaths.Contains(group.Value.InputFilePath))
                        {
                            demandTimeSeriesPaths.Add(group.Value.InputFilePath);
                            Console.WriteLine("Reading input file for {0}: {1}", "demand pattern", demandTimeSeriesPaths.Last());
                        }
                    }
                    if (demandTimeSeriesPaths.Count > 0)
                        RODISSettingsHelper.ValidateFilesExist(demandTimeSeriesPaths.ToArray());
                }

                // --- Read input time series ---
                this.CatchmentModelRunner = new CatchmentModelRunner();

                string rainfallInputUnits = string.Empty;
                string petInputUnits = string.Empty;
                string flowInputUnits = string.Empty;

                TimeSeriesValue[] rainfallTimeSeries =
                    ReadTimeSeries.ReadTimeSeriesFromFile(rodisSettings.RainfallInputPath, ref rainfallInputUnits, rodisSettings.InputRainfallColumn, rodisSettings.InputDateTimeColForRainfall);
                TimeSeriesValue[] petTimeSeries =
                    ReadTimeSeries.ReadTimeSeriesFromFile(rodisSettings.PETInputPath, ref petInputUnits, rodisSettings.InputEvaporationColumn, rodisSettings.InputDateTimeColForEvaporation);
                TimeSeriesValue[] flowTimeSeries =
                    ReadTimeSeries.ReadTimeSeriesFromFile(rodisSettings.FlowInputPath, ref flowInputUnits, rodisSettings.InputFlowColumn, rodisSettings.InputDateTimeColForFlow);

                // --- Load and validate time series ---
                bool isLoadOK = this.CatchmentModelRunner.LoadInputTimeSeries(rainfallTimeSeries, petTimeSeries, flowTimeSeries, rodisSettings);

                // --- Initialise catchment model ---
                DateTime startRunDate = this.CatchmentModelRunner.GetStartRun();
                DateTime endRunDate = this.CatchmentModelRunner.GetEndRun();
                this.CatchmentModelRunner.StartDateForStatistics = startRunDate;
                this.CatchmentModelRunner.EndDateForStatistics = endRunDate;

                if (this.LegacySTEDIDamNodes == null)
                    this.CatchmentModelRunner.catchmentModel.Initialise(rodisSettings, startRunDate, endRunDate);
                else
                    this.CatchmentModelRunner.catchmentModel.Initialise(this.LegacySTEDIDamNodes, rodisSettings);

                this.CatchmentModelRunner.SetOutputTimeSeriesDetails(rodisSettings);
                return isLoadOK;
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                return false;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"ERROR: Invalid input — {ex.Message}");
                return false;
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"ERROR: Model initialisation failed — {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Unexpected failure during model setup.");
                Console.WriteLine($"  Type: {ex.GetType().Name}");
                Console.WriteLine($"  Message: {ex.Message}");
                Console.WriteLine($"  Location: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                return false;
            }
        }

        /// <summary>
        /// Runs all scenarios defined in settings, writing time series and node metadata outputs for each.
        /// Accumulates mean annual values for optional Monte Carlo post-processing.
        /// </summary>
        /// <param name="settings">Settings for the base scenario run.</param>
        /// <param name="waterYearsStartIgnoreYear">First date of each water year (year component ignored).</param>
        /// <param name="overallMeanAnnualValues">Accumulator for overall mean annual outputs by replicate and scenario.</param>
        /// <param name="groupMeanAnnualValues">Accumulator for reporting-group mean annual outputs.</param>
        /// <param name="mcRunIdentifierString">Optional label for Monte Carlo replicate identification in console output.</param>
        /// <param name="writeFullOutputForAllReplicates">If true, writes full time series output for every replicate.</param>
        /// <param name="volumeScaleFactorNoSurfaceAreaChange">Scale factor for storage volume without surface area change.</param>
        /// <param name="postScenarioSetupAction">Optional callback invoked after each scenario's parameter setup but before model execution. Used by the VOI MC loop to apply per-dam uncertainty after scenario-level volume/date adjustments.</param>
        /// <param name="postScenarioRunAction">Optional callback invoked after each scenario's model run and mean annual value calculation. Used by the VOI MC loop to capture water year time series data.</param>
        /// <param name="presetBaseVolumes">Optional array of pristine base dam volumes (ML), captured once from the base case. Used by RescaleWaterBodiesAndDemands to reset volumes before each scenario's scaling, preventing multiplicative accumulation across MC iterations.</param>
        /// <param name="presetBaseSurfaceAreas">Optional array of pristine base dam surface areas (m²), captured alongside presetBaseVolumes. Used for analytical surface area recalculation during scenario volume scaling.</param>
        /// <param name="presetBaseTSDemandCapacities">Optional array of pristine base time-series demand-model capacities (ML), captured once from the base case. Used by RescaleWaterBodiesAndDemands to reset TS demand-model capacities before each scenario's scaling, preventing multiplicative accumulation across MC iterations.</param>
        /// <param name="presetBaseRMDemandCapacities">Optional array of pristine base repeating-monthly demand-model capacities (ML), captured alongside presetBaseTSDemandCapacities. Used for the equivalent reset on RM demand models.</param>
        public void RunScenarios(
            RODISSettings settings,
            DateTime waterYearsStartIgnoreYear,
            List<List<double[]>> overallMeanAnnualValues,
            List<List<double[][]>> groupMeanAnnualValues,
            string mcRunIdentifierString = "",
            bool writeFullOutputForAllReplicates = true,
            double volumeScaleFactorNoSurfaceAreaChange = 1.0,
            Action postScenarioSetupAction = null,
            Action postScenarioRunAction = null,
            double[] presetBaseVolumes = null,
            double[] presetBaseSurfaceAreas = null,
            double[] presetBaseTSDemandCapacities = null,
            double[] presetBaseRMDemandCapacities = null
)
        {
            // Use preset base arrays if provided (MC loop), otherwise capture from current model state (single run)
            double[] baseScenarioMaxWaterBodyVolumes = presetBaseVolumes ?? this.CatchmentModelRunner.GetMaxWaterBodyStorageVolumes();
            double[] baseScenarioSurfaceAreasAtSpill = presetBaseSurfaceAreas ?? this.CatchmentModelRunner.GetSurfaceAreasAtSpill();

            // Mirror for demand-model capacities — captured inline because there are no helper getters on CatchmentModelRunner.
            double[] baseScenarioTSDemandCapacities = presetBaseTSDemandCapacities
                ?? this.CatchmentModelRunner.catchmentModel.TimeSeriesDemandModels?.Select(m => m.DamStorageCapacityVolumeAtSpill).ToArray();
            double[] baseScenarioRMDemandCapacities = presetBaseRMDemandCapacities
                ?? this.CatchmentModelRunner.catchmentModel.RepeatingMonthlyDemandModels?.Select(m => m.DamStorageCapacityVolumeAtSpill).ToArray();

            RODISSettings baseRunSettings = this.DeepCopySettings(settings);

            string baseOutputFileNoExt = ReadWriteResCSV.GetFileNameWithoutExtension(baseRunSettings.ResCSVOutputPath);
            string baseOutputPath = Path.GetDirectoryName(baseRunSettings.ResCSVOutputPath);

            Dictionary<string, Dictionary<string, string>> scenarioParamsAsString = null;
            if (!string.IsNullOrEmpty(settings.ScenarioControlFileJSONPath))
            {
                if (File.Exists(settings.ScenarioControlFileJSONPath))
                {
                    scenarioParamsAsString = JsonUtils.LoadScenarioDictionaryAsStrings(settings.ScenarioControlFileJSONPath);
                }
            }

            TimeSeriesValue[] baseScenarioImpactedFlowTimeSeries = null;
            TimeSeriesValue[] baseScenarioUnimpactedFlowTimeSeries = null;

            List<double[]> overallMeanValuesForScen = new List<double[]>();
            List<double[][]> groupMeanValuesForScen = new List<double[][]>();

            string[] scenarioNames = settings.ScenariosToRun.Keys.ToArray();
            for (int i = 0; i < settings.ScenariosToRun.Count && !string.IsNullOrEmpty(baseOutputPath); ++i)
            {
                try
                {
                    this.Timer?.Start("DeepCopySettings");
                    RODISSettings thisRunSettings = this.DeepCopySettings(baseRunSettings);
                    this.Timer?.Stop("DeepCopySettings");

                    settings.ScenarioName = scenarioNames[i];
                    Console.WriteLine("RUNNING " + mcRunIdentifierString + "Scenario " + (i + 1).ToString() + " of " + settings.ScenariosToRun.Count + ": " + scenarioNames[i]);

                    Scenario thisScenario = settings.ScenariosToRun[scenarioNames[i]];

                    thisRunSettings.ScenarioName = scenarioNames[i];
                    if (settings.ScenariosToRun.Count > 1)
                    {
                        thisRunSettings.ResCSVOutputPath = Path.Combine(baseOutputPath, baseOutputFileNoExt + "_" + scenarioNames[i] + ".res.csv");
                    }

                    if (scenarioParamsAsString != null)
                    {
                        // Check that scenarioParamsAsString item has same name as the scenario name
                        if (scenarioNames[i] != scenarioParamsAsString.Keys.ToArray()[i])
                        {
                            throw new ArgumentException("ERROR: Inconsistent scenario naming in scenario control file " + settings.ScenarioControlFileJSONPath);
                        }
                        else
                        {
                            if (scenarioParamsAsString[scenarioNames[i]] != null)
                            {
                                if (scenarioParamsAsString[scenarioNames[i]].Keys.Contains("AllStoragesProportionFullAtStartOfRun"))
                                {
                                    thisRunSettings.AllStoragesProportionFullAtStartOfRun = thisScenario.AllStoragesProportionFullAtStartOfRun;
                                }

                                if (scenarioParamsAsString[scenarioNames[i]].Keys.Contains("CalculateUnimpactedGivenObserved"))
                                {
                                    thisRunSettings.CalculateUnimpactedGivenObserved = thisScenario.CalculateUnimpactedGivenObserved;
                                }

                                if (scenarioParamsAsString[scenarioNames[i]].Keys.Contains("UseLegacySTEDICalculationMethods"))
                                {
                                    thisRunSettings.UseLegacySTEDICalculationMethods = thisScenario.UseLegacySTEDICalculationMethods;
                                }

                                if (scenarioParamsAsString[scenarioNames[i]].Keys.Contains("UseFixedLowFlowBypassCapacity"))
                                {
                                    thisRunSettings.UseFixedLowFlowBypassCapacity = thisScenario.UseFixedLowFlowBypassCapacity;
                                }

                                if (thisRunSettings.UseFixedLowFlowBypassCapacity && scenarioParamsAsString[scenarioNames[i]].Keys.Contains("VolumeThresholdForBypass"))
                                {
                                    if (string.IsNullOrWhiteSpace(thisScenario.VolumeThresholdForBypass))
                                        throw new ArgumentException($"Scenario '{scenarioNames[i]}': VolumeThresholdForBypass is specified but empty. Provide a value with units, e.g. \"5 ML\".");
                                    thisRunSettings.SetVolumeThresholdForBypass(thisScenario.VolumeThresholdForBypass);
                                }

                                if (thisRunSettings.UseFixedLowFlowBypassCapacity && scenarioParamsAsString[scenarioNames[i]].Keys.Contains("BypassCapacityML_d_km2"))
                                {
                                    // No unit conversion — always ML/d/km² (compound unit not supported by UnitsNet)
                                    thisRunSettings.BypassCapacityML_d_km2 = thisScenario.BypassCapacityML_d_km2;
                                }

                                DateOnly earliestValidDate = new DateOnly(1700, 1, 1);

                                if (scenarioParamsAsString[scenarioNames[i]].Keys.Contains("DamsRevisionDateIgnoreYear"))
                                {
                                    thisRunSettings.DamsRevisionDateIgnoreYear = thisScenario.DamsRevisionDateIgnoreYear;
                                }

                                if (scenarioParamsAsString[scenarioNames[i]].Keys.Contains("BypassSeasonStartDateIgnoreYear") && scenarioParamsAsString[scenarioNames[i]].Keys.Contains("BypassSeasonEndDateIgnoreYear"))
                                {
                                    if (thisScenario.BypassSeasonStartDateIgnoreYear > earliestValidDate && thisScenario.BypassSeasonEndDateIgnoreYear > earliestValidDate)
                                    {
                                        thisRunSettings.BypassSeasonStartDateIgnoreYear = thisScenario.BypassSeasonStartDateIgnoreYear;
                                        thisRunSettings.BypassSeasonEndDateIgnoreYear = thisScenario.BypassSeasonEndDateIgnoreYear;
                                    }
                                }

                                if (scenarioParamsAsString[scenarioNames[i]].Keys.Contains("PumpingSeasonStartDateIgnoreYear") && scenarioParamsAsString[scenarioNames[i]].Keys.Contains("PumpingSeasonEndDateIgnoreYear"))
                                {
                                    if (thisScenario.PumpingSeasonStartDateIgnoreYear > earliestValidDate && thisScenario.PumpingSeasonEndDateIgnoreYear > earliestValidDate)
                                    {
                                        thisRunSettings.PumpingSeasonStartDateIgnoreYear = thisScenario.PumpingSeasonStartDateIgnoreYear;
                                        thisRunSettings.PumpingSeasonEndDateIgnoreYear = thisScenario.PumpingSeasonEndDateIgnoreYear;
                                    }
                                }

                                if (scenarioParamsAsString[scenarioNames[i]].Keys.Contains("RevisionYears_TotalStorageVolume"))
                                {
                                    thisScenario.ParseVolumeRevisionDictionary();
                                }

                                this.Timer?.Start("ModifyWaterBodies");

                                bool isScenarioOK = this.ModifyWaterBodiesForScenario(
                                    thisScenario,
                                    baseScenarioMaxWaterBodyVolumes,
                                    baseScenarioSurfaceAreasAtSpill,
                                    baseScenarioTSDemandCapacities,
                                    baseScenarioRMDemandCapacities,
                                    thisRunSettings,
                                    volumeScaleFactorNoSurfaceAreaChange);

                                this.Timer?.Stop("ModifyWaterBodies");
                            }
                        }
                    }

                    // -- Investigation 6 hook: apply per-dam uncertainty AFTER scenario date/volume setup --
                    // This ensures scenario-level adjustments (which dams are active, nominal volume scaling) are established first, then uncertainty perturbs individual dam properties on top.
                    // For standard MC runs (RunRODISMonteCarlo), this is null and has no effect.
                    this.Timer?.Start("PostScenarioSetup");
                    postScenarioSetupAction?.Invoke();
                    this.Timer?.Stop("PostScenarioSetup");

                    // Propagate any output folder redirect from the callback to per-scenario settings
                    // (supports base case folder redirect to 1_BaseCase/<Scenario>/ subdirectories)
                    if (postScenarioSetupAction != null)
                    {
                        thisRunSettings.OutFolder = settings.OutFolder;
                    }

                    if (i == 0)
                    {
                        // Run the model for the first (base case) scenario
                        this.Timer?.Start("RunAll");
                        settings.CalculateUnimpactedGivenObserved = thisRunSettings.CalculateUnimpactedGivenObserved;
                        this.CatchmentModelRunner.catchmentModel.CalculateUnimpactedGivenObserved = thisRunSettings.CalculateUnimpactedGivenObserved;
                        this.CatchmentModelRunner.RunAll();
                        this.Timer?.Stop("RunAll");

                        // Store the time series of impacted and unimpacted CatchmentModelRunner from the first scenario and use these to update subsequent model runs
                        baseScenarioImpactedFlowTimeSeries = this.CatchmentModelRunner.GetImpactedOutletFlow();
                        baseScenarioUnimpactedFlowTimeSeries = this.CatchmentModelRunner.GetUnimpactedOutletFlow();
                    }
                    else
                    {
                        this.Timer?.Start("ReloadFlowTS");

                        // Take the impacted or unimpacted flow time series from the first scenario run and load the correct time series in for this run
                        if (thisRunSettings.CalculateUnimpactedGivenObserved)
                        {
                            // Set the impacted flows from the base scenario as the input time series
                            if (baseScenarioImpactedFlowTimeSeries != null)
                            {
                                this.CatchmentModelRunner.ReloadInputFlowTimeSeries(baseScenarioImpactedFlowTimeSeries);
                            }
                        }
                        else
                        {
                            // Set the unimpacted flows from the base scenario as the input time series
                            if (baseScenarioUnimpactedFlowTimeSeries != null)
                            {
                                this.CatchmentModelRunner.ReloadInputFlowTimeSeries(baseScenarioUnimpactedFlowTimeSeries);
                            }
                        }

                        this.Timer?.Stop("ReloadFlowTS");

                        // Run the scenario with modified flow inputs
                        this.Timer?.Start("RunAll");
                        settings.CalculateUnimpactedGivenObserved = thisRunSettings.CalculateUnimpactedGivenObserved;
                        this.CatchmentModelRunner.catchmentModel.CalculateUnimpactedGivenObserved = thisRunSettings.CalculateUnimpactedGivenObserved;
                        this.CatchmentModelRunner.RunAll();
                        this.Timer?.Stop("RunAll");
                    }

                    this.Timer?.Start("CalcMeanAnnual");
                    this.CatchmentModelRunner.CalculateMeanAndMeanAnnualValues();
                    this.Timer?.Stop("CalcMeanAnnual");

                    // Water year capture callback (used by VOI MC for per-scenario water year data)
                    postScenarioRunAction?.Invoke();

                    // Write node data — only for base case or when full output requested
                    string defaultOutputFileName = ReadWriteResCSV.GetFileNameWithoutExtension(thisRunSettings.ResCSVOutputPath);

                    overallMeanValuesForScen.Add(this.CatchmentModelRunner.OverallOutputMeanAnnualValues);
                    groupMeanValuesForScen.Add(this.CatchmentModelRunner.GroupOutputMeanAnnualValues);

                    if (writeFullOutputForAllReplicates)
                    {
                        this.Timer?.Start("WriteNodeData");
                        string nodeMetadataFileName = Path.Combine(thisRunSettings.OutFolder, defaultOutputFileName) + "_NodeData.csv";
                        this.CatchmentModelRunner.catchmentModel.WriteAllNodesToCSV(nodeMetadataFileName);
                        this.Timer?.Stop("WriteNodeData");

                        this.Timer?.Start("WriteWaterYear");
                        Console.WriteLine("Writing water year simulation outputs to directory " + thisRunSettings.OutFolder);
                        this.WriteSingleRunWaterYearOutputsToFiles(waterYearsStartIgnoreYear, thisRunSettings, mcRunIdentifierString);
                        this.Timer?.Stop("WriteWaterYear");
                        Console.WriteLine();
                    }

                    if (writeFullOutputForAllReplicates)
                    {
                        this.Timer?.Start("WriteFullOutput");
                        Console.WriteLine("Writing simulation outputs to directory " + settings.OutFolder);
                        this.WriteSingleRunOutputsToFiles(thisRunSettings);
                        Console.WriteLine();
                        this.Timer?.Stop("WriteFullOutput");
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nERROR: Scenario '{scenarioNames[i]}' failed and will be skipped.");
                    Console.WriteLine($"  Type: {ex.GetType().Name}");
                    Console.WriteLine($"  Message: {ex.Message}");
                    Console.WriteLine($"  Location: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");

                    // Add NaN placeholders so accumulator arrays stay aligned
                    int outputCount = this.CatchmentModelRunner.GetOutputNames().Length;
                    overallMeanValuesForScen.Add(Enumerable.Repeat(double.NaN, outputCount).ToArray());
                    string[] reportingGroups = this.CatchmentModelRunner.catchmentModel.GetReportingGroups();
                    double[][] nanGroupValues = new double[reportingGroups.Length][];
                    for (int g = 0; g < reportingGroups.Length; g++)
                        nanGroupValues[g] = Enumerable.Repeat(double.NaN, outputCount).ToArray();
                    groupMeanValuesForScen.Add(nanGroupValues);

                    continue;
                }
            }

            overallMeanAnnualValues.Add(overallMeanValuesForScen);
            groupMeanAnnualValues.Add(groupMeanValuesForScen);


        }

        /// <summary>Writes overall and per-reporting-group .res.csv time series outputs for a single run.</summary>
        /// <param name="rodisSettings">Settings for this RODIS run (provides output paths).</param>
        /// <param name="jsonProjectFile">Optional project file identifier written to the CSV header.</param>
        public void WriteSingleRunOutputsToFiles(RODISSettings rodisSettings, string jsonProjectFile = "")
        {
            try
            {
                if (!Directory.Exists(rodisSettings.OutFolder))
                {
                    Directory.CreateDirectory(rodisSettings.OutFolder);
                }

                string defaultOutputFileName = ReadWriteResCSV.GetFileNameWithoutExtension(rodisSettings.ResCSVOutputPath);
                string[] reportingGroups = this.CatchmentModelRunner.catchmentModel.GetReportingGroups();

                Console.Write(".");
                string outFileName = Path.Combine(rodisSettings.OutFolder, defaultOutputFileName) + RODISSettingsHelper.DefaultFileExtension;
                ReadWriteResCSV.WriteResCSV(this.CatchmentModelRunner.OverallOutputTimeSeries, outFileName, this.programName + " " + this.programVersion, jsonProjectFile);

                for (int iRG = 0; iRG < reportingGroups.Length; iRG++)
                {
                    Console.Write(".");
                    outFileName = Path.Combine(rodisSettings.OutFolder, defaultOutputFileName + "_" + reportingGroups[iRG]) + RODISSettingsHelper.DefaultFileExtension;
                    ReadWriteResCSV.WriteResCSV(this.CatchmentModelRunner.GroupOutputTimeSeries[iRG], outFileName, this.programName + " " + this.programVersion, jsonProjectFile);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"Cannot write output files — access denied. Check folder permissions for '{rodisSettings.OutFolder}'. {ex.Message}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new IOException($"Output directory does not exist and could not be created: '{rodisSettings.OutFolder}'. {ex.Message}", ex);
            }
        }

        /// <summary>Writes water-year aggregated .res.csv outputs (overall and per reporting group) for a single run.</summary>
        /// <param name="waterYearsStartIgnoreYear">First date of each water year (year component ignored).</param>
        /// <param name="rodisSettings">Settings for this RODIS run (provides output paths).</param>
        /// <param name="jsonProjectFile">Optional project file identifier written to the CSV header.</param>
        public void WriteSingleRunWaterYearOutputsToFiles(DateTime waterYearsStartIgnoreYear, RODISSettings rodisSettings, string jsonProjectFile = "")
        {
            try
            {
                if (!Directory.Exists(rodisSettings.OutFolder))
                {
                    Directory.CreateDirectory(rodisSettings.OutFolder);
                }

                string defaultOutputFileName = ReadWriteResCSV.GetFileNameWithoutExtension(rodisSettings.ResCSVOutputPath) + "_WaterYear";
                string outFileName = Path.Combine(rodisSettings.OutFolder, defaultOutputFileName) + RODISSettingsHelper.DefaultFileExtension;
                List<TimeSeriesWithMetadata> annualOutputTimeSeries = this.CatchmentModelRunner.GetWaterYearOutputTimeSeries(this.CatchmentModelRunner.OverallOutputTimeSeries, waterYearsStartIgnoreYear);
                ReadWriteResCSV.WriteResCSV(annualOutputTimeSeries, outFileName, this.programName + " " + this.programVersion, jsonProjectFile);

                string[] reportingGroups = this.CatchmentModelRunner.catchmentModel.GetReportingGroups();
                for (int iRG = 0; iRG < reportingGroups.Length; iRG++)
                {
                    Console.Write(".");
                    outFileName = Path.Combine(rodisSettings.OutFolder, defaultOutputFileName + "_" + reportingGroups[iRG]) + RODISSettingsHelper.DefaultFileExtension;
                    annualOutputTimeSeries = this.CatchmentModelRunner.GetWaterYearOutputTimeSeries(this.CatchmentModelRunner.GroupOutputTimeSeries[iRG], waterYearsStartIgnoreYear);
                    ReadWriteResCSV.WriteResCSV(annualOutputTimeSeries, outFileName, this.programName + " " + this.programVersion, jsonProjectFile);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"Cannot write water-year output files — access denied. Check folder permissions for '{rodisSettings.OutFolder}'. {ex.Message}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new IOException($"Output directory does not exist and could not be created: '{rodisSettings.OutFolder}'. {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Modifies water body start/end dates and volumes to match scenario-specified annual target volumes.
        /// Applies volume scaling if date adjustments alone cannot achieve the target.
        /// </summary>
        /// <param name="thisScenario">Scenario parameters specifying target volumes and revision years.</param>
        /// <param name="baseScenarioMaxWaterBodyVolumes">Base scenario full-supply volumes (ML), in model order.</param>
        /// <param name="baseScenarioSurfaceAreasAtSpill">Base scenario surface areas at full (m²), in model order.</param>
        /// <param name="baseScenarioTSDemandCapacities">Pristine base time-series demand-model capacities (ML), used to reset DamStorageCapacityVolumeAtSpill on TS demand models before LOD scaling.</param>
        /// <param name="baseScenarioRMDemandCapacities">Pristine base repeating-monthly demand-model capacities (ML), used to reset DamStorageCapacityVolumeAtSpill on RM demand models before LOD scaling.</param>
        /// <param name="thisRunSettings">Settings for this scenario run.</param>
        /// <param name="volumeScaleFactorNoSurfaceAreaChange">Additional volume scale factor without surface area change.</param>
        /// <returns>True if the scenario definition is valid; false if revision years/volumes are inconsistent.</returns>
        private bool ModifyWaterBodiesForScenario(
            Scenario thisScenario,
            double[] baseScenarioMaxWaterBodyVolumes,
            double[] baseScenarioSurfaceAreasAtSpill,
            double[] baseScenarioTSDemandCapacities,
            double[] baseScenarioRMDemandCapacities,
            RODISSettings thisRunSettings,
            double volumeScaleFactorNoSurfaceAreaChange = 1.0)
        {
            bool isScenarioDefinitionOK = true;
            DateTime[] validInputDates = this.CatchmentModelRunner.GetValidInputDateTimes();

            if (validInputDates == null || validInputDates.Length == 0)
            {
                Console.WriteLine("WARNING: No valid input dates available. Cannot modify water bodies for scenario.");
                return false;
            }

            double volumeScaleFactorWithSurfaceAreaChange = 1.0;

            double[] revisionYears = thisScenario.GetRevisionYears();
            double[] totalStorageVolumeML = thisScenario.GetTotalStorageVolumeML();

            if (revisionYears != null && thisScenario.GetTotalStorageVolumeML() != null)
            {
                if (revisionYears.Length == totalStorageVolumeML.Length)
                {
                    // Step 1: Set the water body start and end dates back to the start and end dates for the base scenario
                    this.Timer?.Start("  MWB.ResetDates");
                    this.CatchmentModelRunner.ResetToBaseStartEndDates();
                    this.Timer?.Stop("  MWB.ResetDates");

                    this.Timer?.Start("  MWB.InterpolateTargets");

                    // Step 2: Work out target storage volumes on the anniversary date of each year
                    double[] yearsToRun = this.CatchmentModelRunner.GetYearsToRun();
                    double[] targetTotalVolumeByYear = new double[yearsToRun.Length];
                    bool isInterpolationError = false;
                    int okCount = 0;
                    for (int iYear = 0; iYear < yearsToRun.Length; iYear++)
                    {
                        targetTotalVolumeByYear[iYear] = Interpolation.InterpLinLin(yearsToRun[iYear], revisionYears, totalStorageVolumeML, ref isInterpolationError);
                        if (!isInterpolationError)
                        {
                            okCount++;
                        }
                    }

                    List<TimeSeriesValue> targetVolumeAtAnniversaryDates = new List<TimeSeriesValue>();
                    for (int iYear = 0; iYear < yearsToRun.Length; iYear++)
                    {
                        TimeSeriesValue anniversaryVolume = new TimeSeriesValue()
                        {
                            Time = new DateTime((int)(yearsToRun[iYear] + 0.001), thisScenario.DamsRevisionDateIgnoreYear.Month, thisScenario.DamsRevisionDateIgnoreYear.Day),
                            Value = targetTotalVolumeByYear[iYear],
                            IsValid = true,
                        };

                        targetVolumeAtAnniversaryDates.Add(anniversaryVolume);
                    }

                    // Step 3: change water body start and end dates, to try to get to the target total storage volume in each year
                    DateTime[] baseScenarioStartDates = this.CatchmentModelRunner.GetWaterBodyStartDates();
                    DateTime[] baseScenarioEndDates = this.CatchmentModelRunner.GetWaterBodyEndDates();

                    int[] indexByStartDate = new int[baseScenarioStartDates.Length];
                    int[] indexByEndDate = new int[baseScenarioEndDates.Length];

                    this.Timer?.Stop("  MWB.InterpolateTargets");

                    if (indexByStartDate.Length != baseScenarioEndDates.Length)
                    {
                        throw new InvalidOperationException($"Inconsistent water body date arrays: {indexByStartDate.Length} start dates vs {baseScenarioEndDates.Length} end dates.");
                    }
                    else
                    {
                        this.Timer?.Start("  MWB.SortIndices");

                        for (int iStorage = 0; iStorage < baseScenarioStartDates.Length; ++iStorage)
                        {
                            indexByStartDate[iStorage] = iStorage;
                            indexByEndDate[iStorage] = iStorage;
                        }

                        Array.Sort(baseScenarioStartDates, indexByStartDate);
                        Array.Sort(baseScenarioEndDates, indexByEndDate);

                        for (int iStorage = 0; iStorage < this.CatchmentModelRunner.catchmentModel.WaterBodyNodes.Length; ++iStorage)
                        {
                            this.CatchmentModelRunner.catchmentModel.WaterBodyNodes[iStorage].SetStartDate(DateTime.MaxValue);
                            this.CatchmentModelRunner.catchmentModel.WaterBodyNodes[iStorage].SetEndDate(DateTime.MaxValue);
                        }

                        this.Timer?.Stop("  MWB.SortIndices");

                        this.Timer?.Start("  MWB.DateAdjustLoop");

                        double maxTargetVolumeAtAnyTimeStep = 0.0;
                        double maxVolumeIncludedAtAnyTimeStep = 0.0;

                        int iYear = 0;
                        for (int iTS = 0; iTS < validInputDates.Length && iYear < targetVolumeAtAnniversaryDates.Count(); ++iTS)
                        {
                            while (iYear < targetVolumeAtAnniversaryDates.Count()
                                && validInputDates[iTS].Year > targetVolumeAtAnniversaryDates[iYear].Time.Year)
                            {
                                ++iYear;
                            }

                            double targetVolumeML = targetVolumeAtAnniversaryDates[Math.Max(0, iYear - 1)].Value;

                            double totalVolumeSoFar = 0.0;

                            // Set start dates until we reach or exceed the target volume in ML
                            bool isTargetReached = totalVolumeSoFar >= targetVolumeML;
                            for (int iStorage = 0; iStorage < this.CatchmentModelRunner.catchmentModel.WaterBodyNodes.Length && !isTargetReached; ++iStorage)
                            {
                                int waterBodyIndex = indexByStartDate[iStorage];
                                if (this.CatchmentModelRunner.catchmentModel.WaterBodyNodes[waterBodyIndex].StartDate > validInputDates[iTS])
                                {
                                    this.CatchmentModelRunner.catchmentModel.WaterBodyNodes[waterBodyIndex].SetStartDate(validInputDates[iTS]);
                                    int validInputLength = validInputDates.Length;
                                    TimeSpan lastTimeStep = validInputDates[validInputLength - 1] - validInputDates[validInputLength - 2];
                                    DateTime endOfLast = validInputDates.Last().Add(lastTimeStep);
                                    this.CatchmentModelRunner.catchmentModel.WaterBodyNodes[waterBodyIndex].SetEndDate(endOfLast);
                                }

                                totalVolumeSoFar += baseScenarioMaxWaterBodyVolumes[waterBodyIndex];
                                isTargetReached = totalVolumeSoFar >= targetVolumeML;
                            }

                            // Capture the volume of all dams activated by the start loop (used for scale factor)
                            double volumeActivatedByStartLoop = totalVolumeSoFar;

                            // Set end dates until we reach or undershoot the target volume in ML
                            isTargetReached = totalVolumeSoFar <= targetVolumeML;
                            for (int iStorage = 0; iStorage < this.CatchmentModelRunner.catchmentModel.WaterBodyNodes.Length && !isTargetReached; ++iStorage)
                            {
                                int waterBodyIndex = indexByEndDate[iStorage];
                                if (this.CatchmentModelRunner.catchmentModel.WaterBodyNodes[waterBodyIndex].EndDate > validInputDates[iTS]
                                    && validInputDates[iTS] >= this.CatchmentModelRunner.catchmentModel.WaterBodyNodes[waterBodyIndex].StartDate)
                                {
                                    this.CatchmentModelRunner.catchmentModel.WaterBodyNodes[waterBodyIndex].SetStartDate(DateTime.MaxValue);
                                    this.CatchmentModelRunner.catchmentModel.WaterBodyNodes[waterBodyIndex].SetEndDate(DateTime.MaxValue);

                                    totalVolumeSoFar -= baseScenarioMaxWaterBodyVolumes[waterBodyIndex];
                                }

                                isTargetReached = totalVolumeSoFar <= targetVolumeML;
                            }

                            maxTargetVolumeAtAnyTimeStep = Math.Max(maxTargetVolumeAtAnyTimeStep, targetVolumeML);
                            maxVolumeIncludedAtAnyTimeStep = Math.Max(maxVolumeIncludedAtAnyTimeStep, volumeActivatedByStartLoop);
                        }

                        // Step 4: if we can't achieve the target volume just with start and end dates, scale all water body volumes
                        if (maxVolumeIncludedAtAnyTimeStep > 0.0)
                        {
                            volumeScaleFactorWithSurfaceAreaChange = maxTargetVolumeAtAnyTimeStep / maxVolumeIncludedAtAnyTimeStep;
                        }
                        else
                        {
                            Console.WriteLine($"WARNING: No water bodies were included at any timestep for this scenario. Volume scaling set to 1.0.");
                            volumeScaleFactorWithSurfaceAreaChange = 1.0;
                        }

                        this.Timer?.Stop("  MWB.DateAdjustLoop");

                        // Step 5: clamp bypass and pumping existence dates to the (possibly shifted) water body existence window
                        this.Timer?.Start("  MWB.ClampBypass");
                        this.CatchmentModelRunner.catchmentModel.ClampBypassAndPumpingDatesToWaterBodyExistence();
                        this.Timer?.Stop("  MWB.ClampBypass");
                    }
                }
                else
                {
                    isScenarioDefinitionOK = false;
                }
            }
            else
            {
                isScenarioDefinitionOK = false;
            }

            try
            {
                this.Timer?.Start("  MWB.RescaleVolDemands");
                this.CatchmentModelRunner.catchmentModel.RescaleWaterBodiesAndDemands(
                    volumeScaleFactorWithSurfaceAreaChange,
                    thisRunSettings,
                    validInputDates[0],
                    baseScenarioMaxWaterBodyVolumes,
                    volumeScaleFactorNoSurfaceAreaChange,
                    baseScenarioSurfaceAreasAtSpill: baseScenarioSurfaceAreasAtSpill,
                    baseScenarioTSDemandCapacities: baseScenarioTSDemandCapacities,
                    baseScenarioRMDemandCapacities: baseScenarioRMDemandCapacities);
                this.Timer?.Stop("  MWB.RescaleVolDemands");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to rescale water bodies and demands for scenario '{thisRunSettings.ScenarioName}'. {ex.Message}", ex);
            }

            double sumAreaAfterScenarioSetup = 0.0;
            for (int i = 0; i < this.CatchmentModelRunner.catchmentModel.SubcatchmentsInflowModels.Length; i++)
            {
                sumAreaAfterScenarioSetup += this.CatchmentModelRunner.catchmentModel.SubcatchmentsInflowModels[i].AreaKM2;
            }

            //Console.WriteLine($"  SCENARIO SETUP: SumArea after ModifyWaterBodies = {sumAreaAfterScenarioSetup:F6}");

            return isScenarioDefinitionOK;
        }

        /// <summary>Creates a deep copy of RODIS settings, preserving compiled equation state and scenario references.</summary>
        /// <param name="settings">Input RODIS run settings to copy.</param>
        /// <returns>Independent deep copy of the settings.</returns>
        public RODISSettings DeepCopySettings(RODISSettings settings)
        {
            RODISSettings copy = settings.DeepCopyViaNewtonsoft();
            copy.InitialiseFromJSON();

            // Only EquationParser needs special handling (has compiled state)
            if (settings.VolumeCatchmentAreaEquation != null)
                copy.VolumeCatchmentAreaEquation = settings.VolumeCatchmentAreaEquation.DeepCopy();
            if (settings.VolumeSurfaceAreaEquation != null)
                copy.VolumeSurfaceAreaEquation = settings.VolumeSurfaceAreaEquation.DeepCopy();

            // Shallow-copy scenario references (they're not modified per-replicate)
            copy.ScenariosToRun.Clear();
            foreach (var kvp in settings.ScenariosToRun)
                copy.ScenariosToRun[kvp.Key] = kvp.Value;

            return copy;
        }

        /// <summary>
        /// Applies a sampled Monte Carlo replicate to the current model state.
        /// Call after DeepCopySettings and before RunScenarios.
        /// </summary>
        /// <param name="sample">Immutable sample drawn by MonteCarloSampler.</param>
        /// <param name="settings">RODIS settings for this replicate (used by demand application in future phases).</param>
        public void ApplyMonteCarloSample(MonteCarloReplicateSample sample, RODISSettings settings)
        {
            this.ApplyIndividualCapacitySample(sample, settings);
            this.ApplySeepageSample(sample);

            this.ApplyDemandSample(sample, settings);
            this.ApplyClimateSample(sample);

            this.ApplySpatialRunoffSample(sample);

            this.ApplyDetectionSample(sample);
        }

        // ----------------------------------------------------------------------------------------------
        //  Investigation 6 — Uncertainty Realisation Application
        //  These methods apply per-dam uncertainty draws from MonteCarloUncertaintySampler.
        //  Called INSTEAD OF ApplyMonteCarloSample in the Investigation 6 MC loop.
        // ----------------------------------------------------------------------------------------------

        /// <summary>
        /// Applies an <see cref="UncertaintyRealisation"/> to the current model state for Investigation 6 value-of-information MC experiments.
        /// Call after DeepCopySettings and before RunScenarios. The same realisation must be shared across all scenarios within an iteration
        /// to ensure the calibration, 2026 LOD, and 2009 LOD scenarios use identical uncertainty draws.
        /// </summary>
        /// <param name="realisation">Sampled uncertainty parameters for this MC iteration, drawn by <see cref="MonteCarloUncertaintySampler"/>.</param>
        /// <param name="settings">RODIS settings for this replicate (provides demand group templates that may be reinitialised by RescaleWaterBodiesAndDemands).</param>
        public void ApplyUncertaintyRealisation(UncertaintyRealisation realisation, RODISSettings settings)
        {
            if (realisation == null)
                throw new ArgumentNullException(nameof(realisation));

            // Reset accumulated state from previous scenario/iteration
            this.RestoreBaseModelState();

            // Phase A
            this.ApplyVolumeUncertainty(realisation);              // U3
            this.ApplyDemandUncertainty(realisation);              // U6
            this.ApplyRunoffSpatialUncertainty(realisation);       // U11

            // Phase B
            this.ApplySeepageUncertainty(realisation);             // U8
            this.ApplyDemandPatternUncertainty(realisation);       // U7
            this.ApplyDetectionDelayUncertainty(realisation);      // U2

            // Phase C
            this.ApplyCatchmentAreaUncertainty(realisation);       // U4
            this.ApplyRainfallUncertainty(realisation);            // U9
            this.ApplyEvaporationUncertainty(realisation);         // U10
            this.ApplyTopologyUncertainty(realisation);            // U5

            // Must be LAST — removes misclassified dams from simulation
            this.ApplyClassificationUncertainty(realisation);      // U1
        }

        /// <summary>
        /// U3: Applies per-dam log-normal volume multipliers to storage capacity on the WaterBodyNode AND to the matching demand-model DamStorageCapacityVolumeAtSpill,
        /// so demand abstraction scales physically with the sampled dam size. 
        /// Demand-model arrays are 1:1-indexed with WaterBodyNodes (same indexing convention as ApplyDemandUncertainty); a single dam may have entries in both
        /// the TS and RM demand-model arrays, so both are checked and updated independently. 
        /// Initialise() is called on each touched demand model to refresh annualDemandVolume = AnnualDemandFactor × DamStorageCapacityVolumeAtSpill from the new capacity.
        /// SurfaceAreaAtSpill is deliberately NOT modified — it is a measured quantity from satellite/aerial imagery, known with complete accuracy per the Investigation 6 baseline assumption;
        /// the storage–area curve adjusts implicitly via the depth.
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing the DamVolumeMultipliers array (length = number of dams).</param>
        private void ApplyVolumeUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.DamVolumeMultipliers == null)
                return;

            var catchment = this.CatchmentModelRunner.catchmentModel;
            var nodes = catchment.WaterBodyNodes;
            var tsModels = catchment.TimeSeriesDemandModels;
            var rmModels = catchment.RepeatingMonthlyDemandModels;

            int dams = Math.Min(nodes.Length, realisation.DamVolumeMultipliers.Length);
            for (int i = 0; i < dams; i++)
            {
                double factor = realisation.DamVolumeMultipliers[i];
                if (Math.Abs(factor - 1.0) < 1e-12)
                    continue;

                // 1. WaterBodyNode storage capacity — existing behaviour, preserved exactly.
                nodes[i].MaxStorageCapacityVolumeAtSpill *= factor;

                // 2. Paired TS demand model — new in Option A; propagates U3 so demand scales with sampled dam size.
                if (tsModels != null && i < tsModels.Length && tsModels[i] != null)
                {
                    tsModels[i].DamStorageCapacityVolumeAtSpill *= factor;
                    tsModels[i].Initialise();
                }

                // 3. Paired RM demand model — new in Option A; same rationale as TS.
                if (rmModels != null && i < rmModels.Length && rmModels[i] != null)
                {
                    rmModels[i].DamStorageCapacityVolumeAtSpill *= factor;
                    rmModels[i].Initialise();
                }
            }
        }

        /// <summary>
        /// U6: Applies per-dam PERT-distributed demand factors to AnnualDemandFactor on the matching TS and RM demand models,
        /// and re-initialises each touched model so the cached annualDemandVolume (= AnnualDemandFactor × DamStorageCapacityVolumeAtSpill)
        /// reflects the new factor. Demand-model arrays are 1:1-indexed with WaterBodyNodes (same indexing convention as
        /// ApplyVolumeUncertainty); a single dam may have entries in both the TS and RM arrays, so both are checked and updated
        /// independently. Initialise() must be called here because no other Phase A/B/C block guarantees a re-initialise after
        /// AnnualDemandFactor changes — without it, U6 has zero effect on simulated demand.
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing the DamDemandFactors array (length = number of dams).</param>
        private void ApplyDemandUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.DamDemandFactors == null)
                return;

            var catchment = this.CatchmentModelRunner.catchmentModel;
            var nodes = catchment.WaterBodyNodes;
            var tsModels = catchment.TimeSeriesDemandModels;
            var rmModels = catchment.RepeatingMonthlyDemandModels;

            int dams = Math.Min(nodes.Length, realisation.DamDemandFactors.Length);
            for (int i = 0; i < dams; i++)
            {
                double newFactor = realisation.DamDemandFactors[i];

                // Paired TS demand model — write the new factor and refresh annualDemandVolume.
                if (tsModels != null && i < tsModels.Length && tsModels[i] != null)
                {
                    tsModels[i].AnnualDemandFactor = newFactor;
                    tsModels[i].Initialise();
                }

                // Paired RM demand model — same rationale.
                if (rmModels != null && i < rmModels.Length && rmModels[i] != null)
                {
                    rmModels[i].AnnualDemandFactor = newFactor;
                    rmModels[i].Initialise();
                }
            }
        }

        /// <summary>
        /// U11: Applies spatial runoff variation (elevation tilt + per-dam perturbation) to subcatchment inflow multipliers.
        /// The tilt coefficient controls the systematic elevation gradient; perturbations add per-dam random variation.
        /// Flow conservation is handled downstream by the catchment model (multipliers adjust the relative distribution, and the engine normalises to match total unimpacted flow).
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing TiltCoefficient and DamRunoffPerturbations.</param>
        private void ApplyRunoffSpatialUncertainty(UncertaintyRealisation realisation)
        {
            var catchment = this.CatchmentModelRunner.catchmentModel;

            // Early exit: if no spatial uncertainty is active, reset all multipliers to 1.0
            bool hasTilt = Math.Abs(realisation.TiltCoefficient) > 1e-12;
            bool hasPerturbations = realisation.DamRunoffPerturbations != null;
            if (!hasTilt && !hasPerturbations)
            {
                this.ResetInflowMultipliers(catchment);
                return;
            }

            // -- Extract elevations from water body and confluence nodes --
            int nWB = catchment.WaterBodyNodes?.Length ?? 0;
            int nCN = catchment.ConfluenceNodes?.Length ?? 0;
            int nTotal = nWB + nCN;

            if (nTotal == 0)
                return;

            double[] wbElevations = new double[nWB];
            for (int i = 0; i < nWB; i++)
                wbElevations[i] = catchment.WaterBodyNodes[i].Elevation;

            double[] cnElevations = new double[nCN];
            for (int i = 0; i < nCN; i++)
                cnElevations[i] = catchment.ConfluenceNodes[i].Elevation;

            // -- Calculate mean and range of elevation across all nodes --
            double sumElev = 0.0;
            double minElev = double.MaxValue;
            double maxElev = double.MinValue;
            for (int i = 0; i < nWB; i++)
            {
                sumElev += wbElevations[i];
                minElev = Math.Min(minElev, wbElevations[i]);
                maxElev = Math.Max(maxElev, wbElevations[i]);
            }

            for (int i = 0; i < nCN; i++)
            {
                sumElev += cnElevations[i];
                minElev = Math.Min(minElev, cnElevations[i]);
                maxElev = Math.Max(maxElev, cnElevations[i]);
            }

            double meanElev = sumElev / nTotal;
            double elevRange = maxElev - minElev;
            bool flatCatchment = elevRange < 1e-6;

            // -- Calculate per-node multipliers --
            double[] wbMultipliers = new double[nWB];
            for (int i = 0; i < nWB; i++)
            {
                double tiltEffect = 0.0;
                if (hasTilt && !flatCatchment)
                    tiltEffect = realisation.TiltCoefficient * (wbElevations[i] - meanElev) / elevRange;

                double perturbation = 0.0;
                if (hasPerturbations && i < realisation.DamRunoffPerturbations.Length)
                    perturbation = realisation.DamRunoffPerturbations[i];

                wbMultipliers[i] = Math.Max(0.01, 1.0 + tiltEffect + perturbation);
            }

            double[] cnMultipliers = new double[nCN];
            for (int i = 0; i < nCN; i++)
            {
                double tiltEffect = 0.0;
                if (hasTilt && !flatCatchment)
                    tiltEffect = realisation.TiltCoefficient * (cnElevations[i] - meanElev) / elevRange;

                cnMultipliers[i] = Math.Max(0.01, 1.0 + tiltEffect);
            }

            // -- Map to subcatchments via shared helper --
            this.ApplyMultipliersToSubcatchments(wbMultipliers, cnMultipliers);
        }

        /// <summary>
        /// U8: Applies per-dam seepage loss rates. Converts mm/d to ML/d using each dam's surface area at spill, then sets the SeepageLossRateAtFull property on each water body node.
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing DamSeepageRates in mm/d.</param>
        private void ApplySeepageUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.DamSeepageRates == null)
                return;

            var nodes = this.CatchmentModelRunner.catchmentModel.WaterBodyNodes;
            for (int i = 0; i < nodes.Length && i < realisation.DamSeepageRates.Length; i++)
            {
                // Convert mm/d to ML/d: mm/d × m² × 1e-6 = ML/d
                // NOTE: assumes daily timestep — ML/d equals ML/timestep. If non-daily timesteps are ever supported, multiply by timeStep.TotalDays.
                nodes[i].SeepageLossRateAtFull = Math.Max(0.0, realisation.DamSeepageRates[i] * nodes[i].SurfaceAreaAtSpill * 1.0E-6);
            }
        }

        /// <summary>
        /// U7: Applies per-dam demand pattern perturbation to both repeating monthly and time series demand models.
        /// For repeating monthly models, multiplicative factors are applied to MonthlyDemandProportions and re-normalised.
        /// For time series models, the same factors are stored as MonthlyScaleFactors and applied during daily demand calculation.
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing DamMonthlyDemandProportions (multiplicative factors summing to 12).</param>
        private void ApplyDemandPatternUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.DamMonthlyDemandProportions == null)
                return;

            var catchment = this.CatchmentModelRunner.catchmentModel;

            // -- Repeating monthly demand models: perturb proportions directly --
            if (catchment.RepeatingMonthlyDemandModels != null)
            {
                for (int i = 0; i < catchment.RepeatingMonthlyDemandModels.Length
                               && i < realisation.DamMonthlyDemandProportions.Length; i++)
                {
                    double[] factors = realisation.DamMonthlyDemandProportions[i];
                    if (factors == null || factors.Length != 12)
                        continue;

                    var model = catchment.RepeatingMonthlyDemandModels[i];
                    double sum = 0.0;
                    for (int m = 0; m < 12; m++)
                    {
                        model.MonthlyDemandProportions[m] = Math.Max(0.001, model.MonthlyDemandProportions[m] * factors[m]);
                        sum += model.MonthlyDemandProportions[m];
                    }

                    if (sum > 0.0)
                    {
                        for (int m = 0; m < 12; m++)
                            model.MonthlyDemandProportions[m] /= sum;
                    }

                    model.Initialise();
                }
            }

            // -- Time series demand models: store monthly scale factors for daily application --
            if (catchment.TimeSeriesDemandModels != null)
            {
                for (int i = 0; i < catchment.TimeSeriesDemandModels.Length
                               && i < realisation.DamMonthlyDemandProportions.Length; i++)
                {
                    double[] factors = realisation.DamMonthlyDemandProportions[i];
                    if (factors == null || factors.Length != 12)
                        continue;

                    Array.Copy(factors, catchment.TimeSeriesDemandModels[i].MonthlyScaleFactors, 12);
                }
            }
        }

        /// <summary>
        /// U2: Applies per-dam detection delay by shifting water body start dates backward.
        /// Dams with a delay are assumed to have existed earlier than first detected. Re-clamps bypass and pumping dates to the shifted existence window.
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing DetectionDelayYears.</param>
        private void ApplyDetectionDelayUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.DetectionDelayYears == null)
                return;

            var nodes = this.CatchmentModelRunner.catchmentModel.WaterBodyNodes;
            bool anyShifted = false;

            for (int i = 0; i < nodes.Length && i < realisation.DetectionDelayYears.Length; i++)
            {
                int delay = realisation.DetectionDelayYears[i];
                if (delay <= 0)
                    continue;

                // Shift start date BACKWARD — dam existed earlier than detected
                try
                {
                    DateTime earlierStart = nodes[i].StartDate.AddYears(-delay);
                    nodes[i].SetStartDate(earlierStart);
                    anyShifted = true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Underflow — leave as-is
                }
            }

            if (anyShifted)
                this.CatchmentModelRunner.catchmentModel.ClampBypassAndPumpingDatesToWaterBodyExistence();
        }

        /// <summary>
        /// U4: Applies per-dam catchment area multipliers to subcatchment inflow model areas.
        /// Uses the element calculation order to map water body node index ? subcatchment index.
        /// Only applies to subcatchments draining to water body nodes (confluences have no dam-level uncertainty).
        /// Reads from baseSubcatchmentAreas (captured by CaptureBaseModelState) to prevent multiplicative accumulation across iterations;
        /// falls back to in-place *= only when no base array is available (single-run, non-MC callers).
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing CatchmentAreaMultipliers.</param>
        private void ApplyCatchmentAreaUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.CatchmentAreaMultipliers == null)
                return;

            var catchment = this.CatchmentModelRunner.catchmentModel;
            if (catchment.ElementModelCalculationOrder == null || catchment.SubcatchmentsInflowModels == null)
                return;

            bool useBase = this.baseSubcatchmentAreas != null
                           && this.baseSubcatchmentAreas.Length == catchment.SubcatchmentsInflowModels.Length;

            int subcatchmentIndex = 0;
            for (int i = 0; i < catchment.ElementModelCalculationOrder.Length; i++)
            {
                if (catchment.ElementModelCalculationOrder[i].ElementType != ModelElementType.SubcatchmentInflow)
                    continue;

                int dsIndex = catchment.ElementModelCalculationOrder[i].IndexForNextDownstreamElementType;
                ModelElementType dsType = catchment.ElementModelCalculationOrder[i].NextDownstreamElementType;

                if (dsType == ModelElementType.WaterBodyNode
                    && dsIndex >= 0 && dsIndex < realisation.CatchmentAreaMultipliers.Length
                    && subcatchmentIndex < catchment.SubcatchmentsInflowModels.Length)
                {
                    double factor = realisation.CatchmentAreaMultipliers[dsIndex];
                    if (Math.Abs(factor - 1.0) > 1e-12)
                    {
                        catchment.SubcatchmentsInflowModels[subcatchmentIndex].AreaKM2 = useBase
                            ? factor * this.baseSubcatchmentAreas[subcatchmentIndex]
                            : factor * catchment.SubcatchmentsInflowModels[subcatchmentIndex].AreaKM2;
                    }
                }
                ++subcatchmentIndex;
            }
        }

        /// <summary>
        /// U9: Applies per-dam rainfall multipliers to water body nodes.
        /// These multiply with the global RainfallMultiplier during CalculateFlowsAtTimeStep:
        /// node.Rainfall = catchment.Rainfall × RainfallMultiplier × node.LocalRainfallMultiplier.
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing RainfallMultipliers.</param>
        private void ApplyRainfallUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.RainfallMultipliers == null)
                return;

            var nodes = this.CatchmentModelRunner.catchmentModel.WaterBodyNodes;
            for (int i = 0; i < nodes.Length && i < realisation.RainfallMultipliers.Length; i++)
            {
                nodes[i].LocalRainfallMultiplier = realisation.RainfallMultipliers[i];
            }
        }

        /// <summary>
        /// U10: Applies per-dam evaporation multipliers to water body nodes.
        /// These multiply with the global PETMultiplier during CalculateFlowsAtTimeStep:
        /// node.Evaporation = catchment.Evaporation × PETMultiplier × node.LocalEvaporationMultiplier.
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing EvaporationMultipliers.</param>
        private void ApplyEvaporationUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.EvaporationMultipliers == null)
                return;

            var nodes = this.CatchmentModelRunner.catchmentModel.WaterBodyNodes;
            for (int i = 0; i < nodes.Length && i < realisation.EvaporationMultipliers.Length; i++)
            {
                nodes[i].LocalEvaporationMultiplier = realisation.EvaporationMultipliers[i];
            }
        }

        /// <summary>
        /// U5: Applies simplified topology uncertainty. When a dam is flagged as independent,
        /// it ignores upstream dam spill/bypass inflows — only local subcatchment runoff enters storage.
        /// This is the simplified binary/probabilistic approach: each dam either uses its GIS-derived
        /// cascade connection or acts independently, controlled by a per-dam Bernoulli draw.
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing UseIndependentTopology flags.</param>
        private void ApplyTopologyUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.UseIndependentTopology == null)
                return;

            var nodes = this.CatchmentModelRunner.catchmentModel.WaterBodyNodes;
            for (int i = 0; i < nodes.Length && i < realisation.UseIndependentTopology.Length; i++)
            {
                nodes[i].IgnoreUpstreamDamFlows = realisation.UseIndependentTopology[i];
            }
        }

        /// <summary>
        /// U1: Removes misclassified water bodies from the simulation by setting their existence dates to DateTime.MaxValue.
        /// Must be called LAST in ApplyUncertaintyRealisation since it permanently disables dams for this iteration.
        /// </summary>
        /// <param name="realisation">Uncertainty realisation containing IsNaturalWaterBody flags.</param>
        private void ApplyClassificationUncertainty(UncertaintyRealisation realisation)
        {
            if (realisation.IsNaturalWaterBody == null)
                return;

            var nodes = this.CatchmentModelRunner.catchmentModel.WaterBodyNodes;
            for (int i = 0; i < nodes.Length && i < realisation.IsNaturalWaterBody.Length; i++)
            {
                if (realisation.IsNaturalWaterBody[i])
                {
                    // Exclude this dam — set dates to MaxValue so it's never active
                    nodes[i].SetStartDate(DateTime.MaxValue);
                    nodes[i].SetEndDate(DateTime.MaxValue);
                }
            }
        }

        /// <summary>
        /// Applies per-node storage capacity factors. Recalculates surface area at spill
        /// to stay consistent with the volume-surface area equation.
        /// </summary>
        /// <param name="sample">Monte Carlo replicate sample.</param>
        /// <param name="settings">RODIS settings (provides the volume-surface area equation).</param>
        private void ApplyIndividualCapacitySample(MonteCarloReplicateSample sample, RODISSettings settings)
        {
            if (sample.IndividualCapacityFactors == null)
                return;

            var nodes = this.CatchmentModelRunner.catchmentModel.WaterBodyNodes;
            for (int i = 0; i < nodes.Length && i < sample.IndividualCapacityFactors.Length; i++)
            {
                double factor = sample.IndividualCapacityFactors[i];
                if (Math.Abs(factor - 1.0) < 1e-12)
                    continue; // no change needed

                nodes[i].MaxStorageCapacityVolumeAtSpill *= factor;

                // Recalculate surface area to stay consistent with the volume-SA relationship
                double newSA = settings.SolveForSurfaceAreaFromVolume(nodes[i].MaxStorageCapacityVolumeAtSpill);
                if (!double.IsNaN(newSA) && newSA > 0.0)
                {
                    nodes[i].SurfaceAreaAtSpill = newSA;
                }
            }
        }

        /// <summary>
        /// Applies seepage loss sampling: sets base rate from mm/d on all nodes,
        /// then applies per-node individual factors.
        /// </summary>
        /// <param name="sample">Monte Carlo replicate sample.</param>
        private void ApplySeepageSample(MonteCarloReplicateSample sample)
        {
            var nodes = this.CatchmentModelRunner.catchmentModel.WaterBodyNodes;

            for (int i = 0; i < nodes.Length; i++)
            {
                // Determine base seepage rate in ML/d at full supply
                double baseRate_MLPerDay;
                if (!double.IsNaN(sample.MeanSeepageLossRate_mmPerDay))
                {
                    // Convert mm/d to ML/d using this node's surface area at spill:
                    // mm/d × m² × 1e-6 = ML/d
                    baseRate_MLPerDay = Math.Max(0.0,
                        sample.MeanSeepageLossRate_mmPerDay * nodes[i].SurfaceAreaAtSpill * 1.0E-6);
                }
                else
                {
                    // No MC override — keep whatever the base settings provided
                    baseRate_MLPerDay = nodes[i].SeepageLossRateAtFull;
                }

                // Apply per-node factor
                double factor = 1.0;
                if (sample.IndividualSeepageFactors != null && i < sample.IndividualSeepageFactors.Length)
                {
                    factor = sample.IndividualSeepageFactors[i];
                }

                nodes[i].SeepageLossRateAtFull = Math.Max(0.0, baseRate_MLPerDay * factor);
            }
        }

        /// <summary>
        /// Applies the sampled demand ratio to all demand model groups in the settings.
        /// The actual recalculation of daily demand volumes happens later when
        /// <see cref="CatchmentModel.RescaleWaterBodiesAndDemands"/> calls Initialise()
        /// on each demand model with the updated AnnualDemandFactor.
        /// </summary>
        /// <param name="sample">Monte Carlo replicate sample.</param>
        /// <param name="settings">RODIS settings containing the demand model groups.</param>
        private void ApplyDemandSample(MonteCarloReplicateSample sample, RODISSettings settings)
        {
            if (double.IsNaN(sample.MeanAnnualDemandRatio))
                return;

            // Apply to repeating monthly demand groups (settings level — these are the templates)
            if (settings.RepeatingMonthlyDemandGroups != null)
            {
                foreach (var group in settings.RepeatingMonthlyDemandGroups.Values)
                {
                    group.AnnualDemandFactor = sample.MeanAnnualDemandRatio;
                }
            }

            // Apply to time series demand groups (settings level — these are the templates)
            if (settings.TimeSeriesDemandGroups != null)
            {
                foreach (var group in settings.TimeSeriesDemandGroups.Values)
                {
                    group.AnnualDemandFactor = sample.MeanAnnualDemandRatio;
                }
            }

            // Also apply directly to the per-node demand model instances on the catchment model,
            // since these were already created from the templates during Initialise/SetUpFirstRun.
            // RescaleWaterBodiesAndDemands will call Initialise() on each to recalculate daily volumes.
            var catchment = this.CatchmentModelRunner.catchmentModel;

            if (catchment.RepeatingMonthlyDemandModels != null)
            {
                for (int i = 0; i < catchment.RepeatingMonthlyDemandModels.Length; i++)
                {
                    catchment.RepeatingMonthlyDemandModels[i].AnnualDemandFactor = sample.MeanAnnualDemandRatio;
                }
            }

            if (catchment.TimeSeriesDemandModels != null)
            {
                for (int i = 0; i < catchment.TimeSeriesDemandModels.Length; i++)
                {
                    catchment.TimeSeriesDemandModels[i].AnnualDemandFactor = sample.MeanAnnualDemandRatio;
                }
            }
        }

        /// <summary>
        /// Applies historical detection delay sampling to water body start dates.
        /// For each node, shifts the StartDate forward by the sampled number of years, clamped to not exceed the node's EndDate. Then re-clamps bypass and pumping dates to the (possibly shifted) existence window.
        /// Must be called after all other sample applications, since shifted dates affect bypass/pumping clamping.
        /// </summary>
        /// <param name="sample">Monte Carlo replicate sample.</param>
        private void ApplyDetectionSample(MonteCarloReplicateSample sample)
        {
            if (sample.NodeStartDateDelayYears == null)
                return;

            var catchment = this.CatchmentModelRunner.catchmentModel;
            if (catchment.WaterBodyNodes == null)
                return;

            bool anyDateShifted = false;

            for (int i = 0; i < catchment.WaterBodyNodes.Length && i < sample.NodeStartDateDelayYears.Length; i++)
            {
                int delayYears = sample.NodeStartDateDelayYears[i];
                if (delayYears <= 0)
                    continue;

                var node = catchment.WaterBodyNodes[i];

                // Don't shift nodes that are already disabled (MaxValue dates)
                if (node.StartDate == DateTime.MaxValue)
                    continue;

                // Shift start date forward, but don't exceed end date
                DateTime shiftedStart;
                try
                {
                    shiftedStart = node.StartDate.AddYears(delayYears);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // AddYears overflowed — treat as removing the node
                    shiftedStart = DateTime.MaxValue;
                }

                if (shiftedStart >= node.EndDate)
                {
                    // Delay pushes start past end — node effectively doesn't exist in this replicate
                    node.SetStartDate(DateTime.MaxValue);
                    node.SetEndDate(DateTime.MaxValue);
                }
                else
                {
                    node.SetStartDate(shiftedStart);
                }

                anyDateShifted = true;
            }

            // Re-clamp bypass and pumping dates to the shifted existence windows
            if (anyDateShifted)
            {
                catchment.ClampBypassAndPumpingDatesToWaterBodyExistence();
            }
        }

        /// <summary>
        /// Applies rainfall and PET multipliers to the catchment model.
        /// These are applied per time step inside CatchmentModelRunner.Run.
        /// </summary>
        /// <param name="sample">Monte Carlo replicate sample.</param>
        private void ApplyClimateSample(MonteCarloReplicateSample sample)
        {
            this.CatchmentModelRunner.catchmentModel.RainfallMultiplier = sample.RainfallFactor;
            this.CatchmentModelRunner.catchmentModel.PETMultiplier = sample.PETFactor;
        }

        /// <summary>
        /// Applies spatial runoff variation to subcatchment inflow multipliers.
        /// Calculates per-node multipliers from coordinates, spatial gradients, and random factors,
        /// then maps them to subcatchments via the element calculation order.
        /// </summary>
        /// <param name="sample">Monte Carlo replicate sample.</param>
        private void ApplySpatialRunoffSample(MonteCarloReplicateSample sample)
        {
            var catchment = this.CatchmentModelRunner.catchmentModel;

            // Early exit: if no spatial parameters are active, reset all multipliers to 1.0 and return
            bool hasSpatialGradient = Math.Abs(sample.SlopeRunoffWithLocation) > 1e-12
                                   || Math.Abs(sample.SlopeRunoffWithElevation) > 1e-12;
            bool hasIndividualFactors = sample.IndividualRunoffFactors != null;

            if (!hasSpatialGradient && !hasIndividualFactors)
            {
                this.ResetInflowMultipliers(catchment);
                return;
            }

            // -- Extract coordinates from water body and confluence nodes --
            int nWB = catchment.WaterBodyNodes?.Length ?? 0;
            int nCN = catchment.ConfluenceNodes?.Length ?? 0;

            double[] wbEastings = new double[nWB];
            double[] wbNorthings = new double[nWB];
            double[] wbElevations = new double[nWB];
            for (int i = 0; i < nWB; i++)
            {
                wbEastings[i] = catchment.WaterBodyNodes[i].Easting;
                wbNorthings[i] = catchment.WaterBodyNodes[i].Northing;
                wbElevations[i] = catchment.WaterBodyNodes[i].Elevation;
            }

            double[] cnEastings = new double[nCN];
            double[] cnNorthings = new double[nCN];
            double[] cnElevations = new double[nCN];
            for (int i = 0; i < nCN; i++)
            {
                cnEastings[i] = catchment.ConfluenceNodes[i].Easting;
                cnNorthings[i] = catchment.ConfluenceNodes[i].Northing;
                cnElevations[i] = catchment.ConfluenceNodes[i].Elevation;
            }

            // -- Calculate per-node multipliers --
            SpatialRunoffCalculator.CalculateMultipliersByNodeType(
                wbEastings, wbNorthings, wbElevations,
                cnEastings, cnNorthings, cnElevations,
                sample.SlopeRunoffWithLocation,
                sample.SlopeRunoffWithElevation,
                sample.OrientationDegrees,
                sample.IndividualRunoffFactors,
                out double[] wbMultipliers,
                out double[] cnMultipliers);

            // -- Map to subcatchments via shared helper --
            this.ApplyMultipliersToSubcatchments(wbMultipliers, cnMultipliers);
        }

        /// <summary>Resets all subcatchment inflow multipliers to 1.0 (neutral).</summary>
        /// <param name="catchment">Catchment model to reset.</param>
        private void ResetInflowMultipliers(CatchmentModel catchment)
        {
            if (catchment.SubcatchmentsInflowModels == null)
                return;

            for (int i = 0; i < catchment.SubcatchmentsInflowModels.Length; i++)
            {
                catchment.SubcatchmentsInflowModels[i].InflowMultiplier = 1.0;
            }
        }

        /// <summary>
        /// Maps per-node inflow multipliers to subcatchment inflow models via the element calculation order.
        /// Each SubcatchmentInflow entry in the calculation order references a downstream node (WaterBody or Confluence);
        /// the corresponding multiplier is looked up from the appropriate array and applied to the subcatchment's InflowMultiplier.
        /// </summary>
        /// <param name="wbMultipliers">Multipliers indexed by water body node index. Null treated as all 1.0.</param>
        /// <param name="cnMultipliers">Multipliers indexed by confluence node index. Null treated as all 1.0.</param>
        private void ApplyMultipliersToSubcatchments(double[] wbMultipliers, double[] cnMultipliers)
        {
            var catchment = this.CatchmentModelRunner.catchmentModel;
            if (catchment.ElementModelCalculationOrder == null || catchment.SubcatchmentsInflowModels == null)
                return;

            int subcatchmentIndex = 0;
            for (int i = 0; i < catchment.ElementModelCalculationOrder.Length; i++)
            {
                if (catchment.ElementModelCalculationOrder[i].ElementType != ModelElementType.SubcatchmentInflow)
                    continue;

                int dsIndex = catchment.ElementModelCalculationOrder[i].IndexForNextDownstreamElementType;
                ModelElementType dsType = catchment.ElementModelCalculationOrder[i].NextDownstreamElementType;

                double multiplier = 1.0;
                switch (dsType)
                {
                    case ModelElementType.WaterBodyNode:
                        if (wbMultipliers != null && dsIndex >= 0 && dsIndex < wbMultipliers.Length)
                            multiplier = wbMultipliers[dsIndex];
                        break;

                    case ModelElementType.ConfluenceNode:
                        if (cnMultipliers != null && dsIndex >= 0 && dsIndex < cnMultipliers.Length)
                            multiplier = cnMultipliers[dsIndex];
                        break;
                }

                if (subcatchmentIndex < catchment.SubcatchmentsInflowModels.Length)
                    catchment.SubcatchmentsInflowModels[subcatchmentIndex].InflowMultiplier = multiplier;

                ++subcatchmentIndex;
            }
        }
    }
}
