// <copyright file="RunRODISLegacyVersion.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.CommandLineOptions
{
    using RODIS.InputOutput;
    using RODIS.ModelRun;
    using RODIS.ModelSettings;
    using RODIS.TimeSeries;

    /// <summary>Command-line option that runs the legacy RODIS model from a text-format scenario file.</summary>
    public class RunRODISLegacyVersion : BaseCommandLineOption
    {
        /// <inheritdoc/>
        public override string CommandLineFlag => nameof(RunRODISLegacyVersion);

        /// <inheritdoc/>
        public override string OptionDescription => "Run Legacy RODIS model from scenario file";

        /// <inheritdoc/>
        public override Type SettingsType => typeof(LegacyRODISSettings);

        /// <summary>Reads legacy scenario file, builds dam network, and delegates execution to RODISEngine.</summary>
        /// <param name="argumentPath">Path to the legacy RODIS text scenario file.</param>
        public override void Run(string argumentPath)
        {
            this.DisplayProgramDetailsOnConsole();

            if (string.IsNullOrWhiteSpace(argumentPath))
                throw new ArgumentException("ERROR: Legacy RODIS scenario input file path is null or empty.");

            if (!File.Exists(argumentPath))
                throw new ArgumentException("ERROR: Legacy RODIS scenario input file does not exist or has incorrect file path.\n File specified was " + argumentPath);

            try
            {
                Console.WriteLine("Reading legacy RODIS scenario input file " + argumentPath);

                double catchmentAreaKM2;
                LegacyRODISSettings rodisSettings = this.ReadRODISSettings(argumentPath, out catchmentAreaKM2);
                ModelElementType demandModelType = RODISSettingsHelper.GetDemandModelType(rodisSettings.Settings);

                LegacyRODISDamNode[] legacyRODISDamNodes = null;

                if (rodisSettings.Settings.UseSpecificDamNetworkDetails)
                {
                    legacyRODISDamNodes = this.ReadExplicitRODISNetwork(argumentPath, catchmentAreaKM2, demandModelType);
                }
                else
                {
                    rodisSettings.Settings.SetCatchmentArea(catchmentAreaKM2);

                    if (catchmentAreaKM2 <= 0)
                        throw new ArgumentException("ERROR: Catchment area must be specified to randomly generate water bodies. File: " + argumentPath);

                    if (!(demandModelType == ModelElementType.RepeatingMonthlyDemand || demandModelType == ModelElementType.TimeSeriesDemand))
                        throw new ArgumentException("ERROR: Invalid demand model type specification. File: " + argumentPath);

                    legacyRODISDamNodes = RODISNetworkSetup.RandomlyGenerateRODISNetwork(rodisSettings.Settings, demandModelType);
                }

                this.RunModelAfterRead(legacyRODISDamNodes, rodisSettings);
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine($"\nERROR: Invalid or inconsistent input data.\n  {ex.Message}");
                Console.WriteLine("Please check that all input time series files have the same timestep and overlapping date ranges.");
            }
            catch (FormatException ex)
            {
                Console.WriteLine($"\nERROR: Could not parse a value in the scenario file.\n  {ex.Message}");
                Console.WriteLine("Please check the scenario file format matches the expected legacy RODIS format.");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"\nERROR: {ex.Message}");
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"\nERROR: A required input file was not found.");
                Console.WriteLine($"  File: {ex.FileName}");
                Console.WriteLine($"  {ex.Message}");
                Console.WriteLine("Please check that all file paths in the scenario file are correct and that the files exist.");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"\nERROR: File I/O failure.\n  {ex.Message}");
                Console.WriteLine("Please check that output directories exist and files are not locked by another program.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: An unexpected error occurred during the RODIS run.");
                Console.WriteLine($"  Type: {ex.GetType().Name}");
                Console.WriteLine($"  Message: {ex.Message}");
                Console.WriteLine($"  Location: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                Console.WriteLine("Please report this error to the development team.");
            }
        }

        private const int LegacyFlowOutputIndex = 12;

        /// <summary>
        /// Delegates model setup and execution to RODISEngine, then writes legacy-specific .getdat output followed by standard .res.csv and node metadata outputs.
        /// </summary>
        /// <param name="legacyRODISDamNodes">Farm dam nodes read from the legacy RODIS v1.0 file.</param>
        /// <param name="rodisSettings">Legacy RODIS settings including output file paths.</param>
        private void RunModelAfterRead(LegacyRODISDamNode[] legacyRODISDamNodes, LegacyRODISSettings rodisSettings)
        {
            // --- Delegate setup to engine ---
            var engine = new RODISEngine(this.ProgramName, this.ProgramVersion);
            engine.LegacyRODISDamNodes = legacyRODISDamNodes;

            if (!engine.SetUpFirstRun(rodisSettings.Settings))
                return;

            // Legacy-specific: force legacy calculation methods
            engine.CatchmentModelRunner.catchmentModel.IsLegacyRODISCalculationMethods = true;

            Console.WriteLine("Running simulation ...");
            engine.CatchmentModelRunner.RunAll();

            // --- Outputs ---
            try
            {
                Console.WriteLine("Writing simulation outputs to directory " + rodisSettings.Settings.OutFolder);

                // Legacy-specific: write .getdat flow output file
                Console.Write(".");
                ReadWriteGetDatFiles.WriteGetDatFile(
                    engine.CatchmentModelRunner.OverallOutputTimeSeries[LegacyFlowOutputIndex].Data.ToArray(),
                    rodisSettings.FlowOutputFilePath,
                    this.ProgramName + " " + this.ProgramVersion,
                    rodisSettings.Settings.OutletNodeName,
                    "ML/d");

                // Set default output path based on legacy scenario file name (not JSON path)
                if (string.IsNullOrEmpty(rodisSettings.Settings.ResCSVOutputPath))
                {
                    rodisSettings.Settings.ResCSVOutputPath = Path.Combine(
                        rodisSettings.Settings.OutFolder,
                        Path.GetFileNameWithoutExtension(rodisSettings.ScenarioFilePath))
                        + RODISSettingsHelper.DefaultFileExtension;
                }

                // Write .res.csv outputs via engine (overall + per reporting group)
                engine.WriteSingleRunOutputsToFiles(rodisSettings.Settings);

                // Write node metadata
                string defaultOutputFileName = ReadWriteResCSV.GetFileNameWithoutExtension(rodisSettings.Settings.ResCSVOutputPath);
                string nodeMetadataFileName = Path.Combine(rodisSettings.Settings.OutFolder, defaultOutputFileName) + "_NodeData.csv";
                engine.CatchmentModelRunner.catchmentModel.WriteAllNodesToCSV(nodeMetadataFileName);

                Console.WriteLine("Completed");
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"Cannot write output files — access denied. Check folder permissions for '{rodisSettings.Settings.OutFolder}'. {ex.Message}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new IOException($"Output directory does not exist: '{rodisSettings.Settings.OutFolder}'. {ex.Message}", ex);
            }
        }

        /// <summary>Reads general settings, file paths, demand groups, and equations from a legacy RODIS scenario file.</summary>
        /// <param name="scenarioFileName">Path to the legacy RODIS text scenario file.</param>
        /// <param name="catchmentAreaKM2">Output: total catchment area in km² parsed from the file.</param>
        /// <returns>Populated LegacyRODISSettings with an inner RODISSettings object.</returns>
        private LegacyRODISSettings ReadRODISSettings(string scenarioFileName, out double catchmentAreaKM2)
        {
            LegacyRODISSettings settings = new LegacyRODISSettings()
            {
                Settings = new RODISSettings(),
            };

            settings.ScenarioFilePath = scenarioFileName;

            string timeStepString = string.Empty;
            catchmentAreaKM2 = -1;
            bool boolResult = false;
            double doubleResult = -9999;
            settings.Settings.DemandTimeSeriesInputPath = string.Empty;

            using (StreamReader sr = new StreamReader(scenarioFileName))
            {
                string line = sr.ReadLine();

                while (!sr.EndOfStream)
                {
                    line = sr.ReadLine().Trim();

                    if (!string.IsNullOrEmpty(line))
                    {
                        switch (line)
                        {
                            case "Stream name:":
                                settings.Settings.OutletStreamName = sr.ReadLine().Trim();
                                break;

                            case "Gauging station name:":
                                settings.Settings.OutletNodeName = sr.ReadLine().Trim();
                                break;

                            case "Gauge number:":
                                settings.Settings.OutletNodeNumber = sr.ReadLine().Trim();
                                break;

                            case "Scenario title:":
                                settings.Settings.ScenarioName = sr.ReadLine().Trim();
                                break;

                            case "Calculation timestep:":
                                timeStepString = sr.ReadLine().Trim();
                                break;

                            case "Calculation method - Calculate natural given observed?:":
                                bool isMethodOK = bool.TryParse(sr.ReadLine().Trim(), out boolResult);
                                if (isMethodOK) settings.Settings.CalculateUnimpactedGivenObserved = boolResult;
                                break;

                            case "Catchment area (km²):":
                                bool isAreaOK = double.TryParse(sr.ReadLine().Trim(), out catchmentAreaKM2);
                                if (isAreaOK)
                                {
                                    settings.Settings.SetCatchmentArea(catchmentAreaKM2);
                                }
                                break;

                            case "Output file (full):":
                                settings.FullOutputFilePath = sr.ReadLine().Trim();
                                settings.Settings.OutFolder = Path.GetDirectoryName(settings.FullOutputFilePath) + "\\";
                                break;

                            case "Output of flow time series:":
                                settings.FlowOutputFilePath = sr.ReadLine().Trim();
                                settings.Settings.OutFolder = Path.GetDirectoryName(settings.FlowOutputFilePath) + "\\";
                                break;

                            case "Input flow file:":
                                settings.Settings.FlowInputPath = sr.ReadLine().Trim();
                                string flowColString = sr.ReadLine().Trim();
                                if (!int.TryParse(flowColString, out int flowCol))
                                    throw new FormatException($"Cannot parse flow column number '{flowColString}' in scenario file. Expected an integer after 'Input flow file:'.");
                                settings.Settings.InputFlowColumn = flowCol;
                                break;

                            case "Rainfall file:":
                                settings.Settings.RainfallInputPath = sr.ReadLine().Trim();
                                string rainColString = sr.ReadLine().Trim();
                                if (!int.TryParse(rainColString, out int rainCol))
                                    throw new FormatException($"Cannot parse rainfall column number '{rainColString}' in scenario file. Expected an integer after 'Rainfall file:'.");
                                settings.Settings.InputRainfallColumn = rainCol;
                                break;

                            case "Evaporation file:":
                                settings.Settings.PETInputPath = sr.ReadLine().Trim();
                                string evapColString = sr.ReadLine().Trim();
                                if (!int.TryParse(evapColString, out int evapCol))
                                    throw new FormatException($"Cannot parse evaporation column number '{evapColString}' in scenario file. Expected an integer after 'Evaporation file:'.");
                                settings.Settings.InputEvaporationColumn = evapCol;
                                break;

                            case "Enter details of each dam individually?:":
                                bool areDamDetailsOK = bool.TryParse(sr.ReadLine().Trim(), out boolResult);
                                if (areDamDetailsOK)
                                {
                                    settings.Settings.UseSpecificDamNetworkDetails = boolResult;
                                    if (!settings.Settings.UseSpecificDamNetworkDetails)
                                    {
                                        // Use probability distribution, so read details from file
                                        this.ReadDamVolumeAndDistribution(sr, settings);
                                    }
                                }
                                break;

                            case "Monthly demand details:":
                                settings.Settings.RepeatingMonthlyDemandGroups = this.ReadMonthlyDemandModels(sr);
                                break;

                            case "Dam Volume (ML) = A *  SurfaceArea(m²) ^ B:":
                                settings.Settings.VolumeSurfaceAreaEquation.Equation = this.ReadSurfaceAreaVolumeEquation(sr, settings);
                                break;

                            case "Employ volume threshold between demand groups:":
                                bool isDemandThresholdOK = bool.TryParse(sr.ReadLine().Trim(), out boolResult);
                                if (isDemandThresholdOK)
                                {
                                    settings.Settings.UseVolumeThresholdForDemandGroups = boolResult;
                                }
                                break;

                            case "Threshold volume (ML):":
                                bool isVolumeThresholdOK = double.TryParse(sr.ReadLine().Trim(), out doubleResult);
                                if (isVolumeThresholdOK && settings.Settings.UseVolumeThresholdForDemandGroups)
                                {
                                    settings.Settings.VolumeThresholdForDemandGroups = doubleResult.ToString() + " ML";
                                    settings.Settings.SetVolumeThresholdForDemandGroups(settings.Settings.VolumeThresholdForDemandGroups);
                                }
                                else
                                {
                                    settings.Settings.UseVolumeThresholdForDemandGroups = false;
                                }
                                break;

                            case "Demand Timeseries Input File:":
                                settings.Settings.DemandTimeSeriesInputPath = sr.ReadLine().Trim();
                                break;

                            case "Demand Timeseries Details:":
                                this.ReadDemandTimeSeriesGroups(sr, settings);
                                break;

                            case "Volume Catchment Area Relationship:":
                                this.ReadVolumeCatchmentAreaRelationship(sr, settings);
                                break;

                            case "Rate for low flow bypass capacity in ml/d/km2:":
                                this.ReadLowFlowBypassOverride(sr, settings);
                                break;

                            default:
                                if (line.Contains("Catchment area (km") && !line.Contains("Dam Volume"))
                                {
                                    double.TryParse(sr.ReadLine().Trim(), out catchmentAreaKM2);
                                }

                                if (line.Contains("Dam Volume (ML) = A *  SurfaceArea(m"))
                                {
                                    settings.Settings.VolumeSurfaceAreaEquation.Equation = this.ReadSurfaceAreaVolumeEquation(sr, settings);
                                }
                                break;
                        }
                    }
                }
            }

            // Apply modelling time span from legacy timestep string
            StandardModellingTimeSpan parsedTimeSpan = RODISSettingsHelper.ParseModellingTimeSpan(timeStepString);
            if (parsedTimeSpan != null)
            {
                settings.Settings.ModellingTimeSpan = parsedTimeSpan;
            }
            else if (!string.IsNullOrWhiteSpace(timeStepString))
            {
                Console.WriteLine("WARNING: Unrecognised calculation timestep '" + timeStepString + "'. Defaulting to daily.");
            }

            // Validate essential file paths were populated
            if (string.IsNullOrEmpty(settings.Settings.RainfallInputPath))
                throw new InvalidDataException("Scenario file does not specify a rainfall input file. Check for 'Rainfall file:' entry.");

            if (string.IsNullOrEmpty(settings.Settings.PETInputPath))
                throw new InvalidDataException("Scenario file does not specify an evaporation input file. Check for 'Evaporation file:' entry.");

            if (string.IsNullOrEmpty(settings.Settings.FlowInputPath))
                throw new InvalidDataException("Scenario file does not specify a flow input file. Check for 'Input flow file:' entry.");

            // Validate files exist
            RODISSettingsHelper.ValidateFilesExist(settings.Settings.RainfallInputPath, settings.Settings.PETInputPath, settings.Settings.FlowInputPath);

            return settings;
        }

        /// <summary>Reads demand time series group definitions from the legacy scenario file into settings.</summary>
        /// <param name="sr">StreamReader positioned at the demand time series section.</param>
        /// <param name="settings">Legacy settings to populate with time series demand groups.</param>
        private void ReadDemandTimeSeriesGroups(StreamReader sr, LegacyRODISSettings settings)
        {
            Dictionary<string, FarmDamTimeSeriesDemandModel> modelsList = new Dictionary<string, FarmDamTimeSeriesDemandModel>();

            // Read header line
            string line = sr.ReadLine().Trim();

            string label = string.Empty;

            bool isParseOK = !string.IsNullOrEmpty(line);

            // Read out remaining lines till we get to an empty line
            while (!string.IsNullOrEmpty(line.Trim()) && !sr.EndOfStream)
            {
                line = sr.ReadLine().Trim();

                if (!string.IsNullOrEmpty(settings.Settings.DemandTimeSeriesInputPath))
                {
                    if (!File.Exists(settings.Settings.DemandTimeSeriesInputPath))
                    {
                        throw new FileNotFoundException(
                            $"Demand time series input file does not exist: '{settings.Settings.DemandTimeSeriesInputPath}'.",
                            settings.Settings.DemandTimeSeriesInputPath);
                    }
                    else
                    {
                        if (line.Length > 19)
                        {
                            label = line.Substring(0, 19).Trim();
                            isParseOK = !string.IsNullOrEmpty(label);

                            if (line.Length > 31)
                            {
                                int column = -1;
                                isParseOK &= int.TryParse(line.Substring(20, 11).Trim(), out column);

                                if (isParseOK && line.Length >= 49)
                                {
                                    double factor = 0.0;
                                    isParseOK &= double.TryParse(line.Substring(31, 18).Trim(), out factor);

                                    if (isParseOK)
                                    {
                                        string demandInputUnits = string.Empty;

                                        FarmDamTimeSeriesDemandModel groupModel = new FarmDamTimeSeriesDemandModel()
                                        {
                                            DemandGroup = label,
                                            AnnualDemandFactor = factor,
                                            InputFileColumnNumber = column,
                                            InputFilePath = settings.Settings.DemandTimeSeriesInputPath,
                                            InputPattern = ReadTimeSeries.ReadTimeSeriesFromFile(settings.Settings.DemandTimeSeriesInputPath, ref demandInputUnits, column),
                                        };

                                        modelsList.Add(label, groupModel);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            settings.Settings.TimeSeriesDemandGroups = modelsList;
        }

        /// <summary>Reads surface-area/volume power-law equation coefficients (A, B) from the legacy file.</summary>
        /// <param name="sr">StreamReader positioned at the equation section.</param>
        /// <param name="settings">Legacy settings containing the equation variable definitions.</param>
        /// <returns>Equation string in the form "A*SA^B".</returns>
        private string ReadSurfaceAreaVolumeEquation(StreamReader sr, LegacyRODISSettings settings)
        {
            string result = string.Empty;

            bool isSAVolOK = true;
            string line = sr.ReadLine().Trim();
            if (!sr.EndOfStream && isSAVolOK && line == "A:")
            {
                line = sr.ReadLine().Trim();
                double saVolConst = 0;
                isSAVolOK &= !sr.EndOfStream && double.TryParse(line, out saVolConst);

                line = sr.ReadLine().Trim();
                if (!sr.EndOfStream && isSAVolOK && line == "B:")
                {
                    line = sr.ReadLine().Trim();
                    double saVolExponent = 0;
                    isSAVolOK &= !sr.EndOfStream && double.TryParse(line, out saVolExponent);

                    if (isSAVolOK)
                    {
                        result = saVolConst.ToString() + "*" + settings.Settings.VolumeSurfaceAreaEquation.VariablesWithDescriptions.First().Key + "^" + saVolExponent.ToString();
                    }
                }
            }

            return result;
        }

        /// <summary>Reads a piecewise-linear volume/catchment-area lookup table and builds an equation string.</summary>
        /// <param name="sr">StreamReader positioned at the volume-catchment area section.</param>
        /// <param name="settings">Legacy settings to populate with the parsed EquationParser.</param>
        private void ReadVolumeCatchmentAreaRelationship(StreamReader sr, LegacyRODISSettings settings)
        {
            const string variableName = "Volume";
            string equation = string.Empty;

            string line = sr.ReadLine().Trim();

            int numRows = 0;
            bool isReadOK = int.TryParse(line, out numRows);

            // Read column headers line
            line = sr.ReadLine().Trim();

            List<double> damVolumes = new List<double>();
            List<double> catchmentAreas = new List<double>();

            while (!string.IsNullOrEmpty(line))
            {
                line = sr.ReadLine().Trim();

                string[] partsOfLine = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                isReadOK = partsOfLine.Length >= 2;
                if (isReadOK)
                {
                    double volume = 0, area = 0;
                    isReadOK &= double.TryParse(partsOfLine[0].Trim(), out volume);
                    isReadOK &= double.TryParse(partsOfLine[1].Trim(), out area);

                    if (isReadOK)
                    {
                        damVolumes.Add(volume);
                        catchmentAreas.Add(area);
                    }
                }
            }

            if (damVolumes.Count() >= 2 && catchmentAreas.Count() >= 2)
            {
                equation = string.Empty;

                for (int i = 1; i < damVolumes.Count() && i < catchmentAreas.Count(); i++)
                {
                    double intercept = catchmentAreas[i - 1];
                    double slope = (catchmentAreas[i] - catchmentAreas[i - 1]) / (damVolumes[i] - damVolumes[i - 1]);

                    if (i < damVolumes.Count() - 1)
                    {
                        equation += "if(" + variableName + " < ";
                        equation += damVolumes[i].ToString() + ", ";
                    }
                    equation += intercept.ToString("0.0") + " + ";
                    equation += slope.ToString() + " * (" + variableName;
                    equation += " - " + damVolumes[i - 1].ToString() + ")";
                    if (i < damVolumes.Count() - 1)
                    {
                        equation += ", ";
                    }
                }

                for (int i = 1; i < damVolumes.Count() - 1; i++)
                {
                    equation += ")";
                }

                settings.Settings.VolumeCatchmentAreaEquation = new EquationParser()
                {
                    VariablesWithDescriptions = new Dictionary<string, string> { { variableName, "Storage volume when full in ML" } },
                    Equation = equation,
                };
            }
        }

        /// <summary>Reads low-flow bypass override settings including capacity, volume threshold, and season dates.</summary>
        /// <param name="sr">StreamReader positioned at the bypass override section.</param>
        /// <param name="settings">Legacy settings to populate with bypass parameters.</param>
        private void ReadLowFlowBypassOverride(StreamReader sr, LegacyRODISSettings settings)
        {
            int blankLineCount = 0;

            string line = sr.ReadLine().Trim();
            if (string.IsNullOrEmpty(line))
            {
                ++blankLineCount;
            }

            double bypassCapacity = 0;
            bool isReadOK = double.TryParse(line, out bypassCapacity);

            if (isReadOK && bypassCapacity >= 0)
            {
                settings.Settings.UseFixedLowFlowBypassCapacity = true;
                settings.Settings.BypassCapacityML_d_km2 = bypassCapacity;
                settings.Settings.SetVolumeThresholdForBypass("0.0 ML");

                while (!sr.EndOfStream && blankLineCount < 2)
                {
                    line = sr.ReadLine().Trim();

                    if (string.IsNullOrEmpty(line))
                    {
                        ++blankLineCount;
                    }
                    else
                    {
                        blankLineCount = 0;
                    }

                    switch (line)
                    {
                        case "Bypass override - turn low flow bypasses on for all dams bigger than capacity in ml:":
                            line = sr.ReadLine().Trim();
                            double volumeThreshold = 0;
                            isReadOK = double.TryParse(line, out volumeThreshold);
                            if (isReadOK && volumeThreshold >= 0)
                            {
                                settings.Settings.SetVolumeThresholdForBypass(volumeThreshold.ToString() + " ML");
                            }
                            break;

                        case "Period for which bypasses are active (start/end mmdd mmdd):":
                            line = sr.ReadLine().Trim();
                            string[] partsOfLine = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (partsOfLine.Length >= 2)
                            {
                                DateOnly startOutputDate = new DateOnly();
                                isReadOK = TryParseDateIgnoreYear(partsOfLine[0], out startOutputDate);

                                DateOnly endOutputDate = new DateOnly();
                                isReadOK &= TryParseDateIgnoreYear(partsOfLine[1], out endOutputDate);
                                if (isReadOK)
                                {
                                    settings.Settings.BypassSeasonStartDateIgnoreYear = startOutputDate;
                                    settings.Settings.BypassSeasonEndDateIgnoreYear = endOutputDate;
                                }
                            }
                            break;
                    }
                }
            }
        }

        /// <summary>Parses a 4-character MMDD string into a DateOnly, using a fixed year (default 2000).</summary>
        /// <param name="input">Input string in MMDD format.</param>
        /// <param name="dateOnlyIgnoreYear">Output date with the specified fixed year.</param>
        /// <param name="year">Fixed year to use (default 2000).</param>
        /// <returns>True if parsing succeeded.</returns>
        public bool TryParseDateIgnoreYear(string input, out DateOnly dateOnlyIgnoreYear, int year = 2000)
        {
            int month = -1, day = -1;
            bool isReadOK = true;
            dateOnlyIgnoreYear = DateOnly.MinValue;

            isReadOK = int.TryParse(input.Trim().Substring(0, 2), out month);
            isReadOK &= int.TryParse(input.Trim().Substring(2, 2), out day);
            if (isReadOK)
            {
                try
                {
                    dateOnlyIgnoreYear = new DateOnly(year, month, day);
                }
                catch
                {
                    isReadOK = false;
                }
            }

            return isReadOK;
        }

        /// <summary>Reads total dam volume and probability distribution of dam size classes from the legacy file.</summary>
        /// <param name="sr">StreamReader positioned at the dam volume/distribution section.</param>
        /// <param name="settings">Legacy settings to populate with volume distribution data.</param>
        private void ReadDamVolumeAndDistribution(StreamReader sr, LegacyRODISSettings settings)
        {
            int blankLineCount = 0;

            // Read (and discard) the number-of-rows line
            string line = sr.ReadLine().Trim();
            if (string.IsNullOrEmpty(line))
            {
                ++blankLineCount;
            }

            bool isReadOK = true;

            while (!sr.EndOfStream && blankLineCount < 2)
            {
                line = sr.ReadLine().Trim();

                if (string.IsNullOrEmpty(line))
                {
                    ++blankLineCount;
                }
                else
                {
                    blankLineCount = 0;
                }

                switch (line)
                {
                    case "Total volume of farm dams in catchment (ML):":
                        line = sr.ReadLine().Trim();
                        double totalVolume = 0;
                        isReadOK = double.TryParse(line, out totalVolume);
                        if (isReadOK && totalVolume >= 0)
                        {
                            settings.Settings.ProbabilityDistributionTotalDamVolume = totalVolume;
                        }
                        break;

                    case "Distribution of farm dam volumes:":
                        // Read number of rows line
                        line = sr.ReadLine().Trim();
                        // Read column headers line
                        line = sr.ReadLine().Trim();

                        List<double> maxDamVolume = new List<double>();
                        List<double> incrementalProbability = new List<double>();
                        double volume = 0, probability = 0, totalProbability = 0;

                        while (!string.IsNullOrEmpty(line))
                        {
                            line = sr.ReadLine().Trim();

                            if (string.IsNullOrEmpty(line))
                            {
                                ++blankLineCount;
                            }
                            else
                            {
                                blankLineCount = 0;
                            }

                            string[] partsOfLine = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                            isReadOK = partsOfLine.Length >= 4;
                            if (isReadOK)
                            {
                                if (maxDamVolume.Count == 0 && incrementalProbability.Count == 0)
                                {
                                    isReadOK &= double.TryParse(partsOfLine[1].Trim(), out volume);
                                    if (isReadOK)
                                    {
                                        maxDamVolume.Add(volume);
                                        incrementalProbability.Add(0);
                                    }
                                }

                                isReadOK &= double.TryParse(partsOfLine[2].Trim(), out volume);
                                isReadOK &= double.TryParse(partsOfLine[3].Trim(), out probability);

                                if (isReadOK)
                                {
                                    maxDamVolume.Add(volume);
                                    incrementalProbability.Add(probability);
                                    totalProbability += probability;
                                }
                            }
                        }

                        Dictionary<string, double> maxVolumesMLAndIntervalProbabilities = new Dictionary<string, double>();
                        for (int i = 0; i < maxDamVolume.Count && i < incrementalProbability.Count; ++i)
                        {
                            maxVolumesMLAndIntervalProbabilities.Add(maxDamVolume[i].ToString() + " ML", incrementalProbability[i] / totalProbability);
                        }
                        settings.Settings.SetMaxVolumesAndIntervalProbabilities(maxVolumesMLAndIntervalProbabilities);

                        blankLineCount = 10;

                        const double maxAllowableError = 0.001;
                        if (Math.Abs(totalProbability - 1.0) > maxAllowableError)
                        {
                            Console.WriteLine("WARNING: Total probabilities in distribution of farm dam volumes do not sum to 1.000. Total probability = " + totalProbability);
                            Console.WriteLine("Class probabilities have been rescaled to sum to 1 exactly");
                        }

                        break;
                }
            }
        }

        /// <summary>Reads an explicit network of individually specified dam nodes from the legacy scenario file.</summary>
        /// <param name="scenarioFileName">Path to the legacy RODIS text scenario file.</param>
        /// <param name="catchmentAreaKM2">Total catchment area in km² (assigned to first node).</param>
        /// <param name="demandModelType">Demand model type for node property updates.</param>
        /// <returns>Array of legacy RODIS dam nodes comprising the network.</returns>
        private LegacyRODISDamNode[] ReadExplicitRODISNetwork(string scenarioFileName, double catchmentAreaKM2, ModelElementType demandModelType)
        {
            List<LegacyRODISDamNode> legacyNetwork = new List<LegacyRODISDamNode>();
            int numNodesInNetwork = -1;

            bool isBypassForSome = false;
            DateOnly startBypassDate = new DateOnly();
            DateOnly endBypassDate = new DateOnly();

            bool isWinterfillForSome = false;
            DateOnly startWinterfillDate = new DateOnly();
            DateOnly endWinterfillDate = new DateOnly();

            if (!string.IsNullOrEmpty(scenarioFileName))
            {
                if (File.Exists(scenarioFileName))
                {
                    using (StreamReader sr = new StreamReader(scenarioFileName))
                    {
                        string line = sr.ReadLine();

                        while (!sr.EndOfStream)
                        {
                            line = sr.ReadLine();

                            if (!string.IsNullOrEmpty(line))
                            {
                                if (numNodesInNetwork > 0 && legacyNetwork.Count < numNodesInNetwork)
                                {
                                    string[] partsOfLine = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                                    if (partsOfLine.Length < 9)
                                    {
                                        Console.WriteLine($"WARNING: Skipping dam node line (expected 9+ columns, got {partsOfLine.Length}): '{line.Trim()}'");
                                        continue;
                                    }

                                    int id, nextDSid;
                                    bool isParseOK = int.TryParse(partsOfLine[0], out id);

                                    double surfaceArea, volume, catchmentArea, winterfillRate, bypassRate;
                                    isParseOK &= double.TryParse(partsOfLine[1], out surfaceArea);
                                    isParseOK &= double.TryParse(partsOfLine[2], out volume);
                                    isParseOK &= double.TryParse(partsOfLine[3], out catchmentArea);
                                    isParseOK &= double.TryParse(partsOfLine[6], out winterfillRate);
                                    isParseOK &= double.TryParse(partsOfLine[7], out bypassRate);
                                    isParseOK = int.TryParse(partsOfLine[8], out nextDSid);

                                    if (!isParseOK)
                                    {
                                        Console.WriteLine($"WARNING: Could not parse dam node data on line: '{line.Trim()}'. This node will be skipped.");
                                        continue;
                                    }

                                    LegacyRODISDamNode newNode = new LegacyRODISDamNode()
                                    {
                                        Identifier = id,
                                        SurfaceAreaM2 = surfaceArea,
                                        VolumeML = volume,
                                        TotalCatchmentAreaKM2 = catchmentArea,
                                        DemandGroup = partsOfLine[4],
                                        ResultsGroup = partsOfLine[5],
                                        //WinterfillRate = winterfillRate,
                                        //BypassCapacity = bypassRate,
                                        NextDownstreamIdentifier = nextDSid,
                                    };

                                    // Set bypass properties
                                    if (isBypassForSome && bypassRate > 0)
                                    {
                                        newNode.IsBypass = true;
                                        newNode.BypassCapacity = bypassRate;
                                        newNode.BypassSeasonStartDateIgnoreYear = startBypassDate;
                                        newNode.BypassSeasonEndDateIgnoreYear = endBypassDate;
                                    }
                                    else
                                    {
                                        newNode.IsBypass = false;
                                        newNode.BypassCapacity = 0.0;
                                    }

                                    // Set winterfill properties
                                    if (isWinterfillForSome && winterfillRate > 0)
                                    {
                                        newNode.IsWinterfill = true;
                                        newNode.WinterfillRate = winterfillRate;
                                        newNode.WinterfillSeasonStartDateIgnoreYear = startWinterfillDate;
                                        newNode.WinterfillSeasonEndDateIgnoreYear = endWinterfillDate;
                                    }
                                    else
                                    {
                                        newNode.IsWinterfill = false;
                                        newNode.WinterfillRate = 0.0;
                                    }

                                    // First node of the legacy RODIS input network is the catchment outlet, so set area to total catchment area
                                    if (legacyNetwork.Count == 0 && catchmentAreaKM2 > 0)
                                    {
                                        newNode.TotalCatchmentAreaKM2 = catchmentAreaKM2;
                                    }

                                    legacyNetwork.Add(newNode);
                                }
                                else
                                {
                                    string trimmedLineStart = line.Substring(0, Math.Min(24, line.Length)).Trim();

                                    switch (trimmedLineStart)
                                    {
                                        case "Dam Details:":
                                            line = sr.ReadLine();
                                            bool isParseOK = int.TryParse(line.Trim(), out numNodesInNetwork);

                                            if (!isParseOK)
                                            {
                                                numNodesInNetwork = -1;
                                            }
                                            else
                                            {
                                                // Number of dams read OK
                                                // Now read 2 header lines
                                                line = sr.ReadLine();
                                                line = sr.ReadLine();
                                            }
                                            break;

                                        case "Low flow bypasses?":
                                            string[] partsOfLine = line.Substring(24).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                            if (partsOfLine.Length >= 3)
                                            {
                                                bool isReadOK = bool.TryParse(partsOfLine[0].ToLower(), out isBypassForSome);
                                                isReadOK &= TryParseDateIgnoreYear(partsOfLine[1], out startBypassDate);
                                                isReadOK &= TryParseDateIgnoreYear(partsOfLine[2], out endBypassDate);

                                                if (!isReadOK)
                                                {
                                                    isBypassForSome = false;
                                                    startBypassDate = DateOnly.MinValue;
                                                    endBypassDate = DateOnly.MinValue;
                                                }
                                            }
                                            else
                                            {
                                                isBypassForSome = false;
                                            }
                                            break;

                                        case "Winterfill pumping?":
                                            partsOfLine = line.Substring(24).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                            if (partsOfLine.Length >= 3)
                                            {
                                                bool isReadOK = bool.TryParse(partsOfLine[0].ToLower(), out isWinterfillForSome);
                                                isReadOK &= TryParseDateIgnoreYear(partsOfLine[1], out startWinterfillDate);
                                                isReadOK &= TryParseDateIgnoreYear(partsOfLine[2], out endWinterfillDate);

                                                if (!isReadOK)
                                                {
                                                    isWinterfillForSome = false;
                                                    startWinterfillDate = DateOnly.MinValue;
                                                    endWinterfillDate = DateOnly.MinValue;
                                                }
                                            }
                                            else
                                            {
                                                isWinterfillForSome = false;
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (numNodesInNetwork < 0)
            {
                throw new InvalidDataException(
                    $"Could not find 'Dam Details:' section in '{scenarioFileName}'. "
                    + "Check that the scenario file contains an explicit dam network definition.");
            }

            LegacyRODISDamNode[] legacyRODISDamNodes = legacyNetwork.ToArray();

            if (legacyNetwork.Count == numNodesInNetwork)
            {
                RODISNetworkSetup.UpdateLegacyRODISNodeProperties(legacyRODISDamNodes, demandModelType);
            } 
            else
            {
                throw new InvalidDataException(
                        $"Expected {numNodesInNetwork} dam nodes in network but successfully parsed {legacyNetwork.Count}. "
                        + $"Check the 'Dam Details:' section in '{scenarioFileName}'.");
            }

            return legacyRODISDamNodes;
        }

        /// <summary>Reads repeating monthly demand group definitions from the legacy scenario file.</summary>
        /// <param name="sr">StreamReader positioned at the monthly demand section.</param>
        /// <returns>Dictionary of demand group name to repeating monthly demand model.</returns>
        private Dictionary<string, FarmDamRepeatingMonthlyDemandModel> ReadMonthlyDemandModels(StreamReader sr)
        {
            Dictionary<string, FarmDamRepeatingMonthlyDemandModel> modelsList = new Dictionary<string, FarmDamRepeatingMonthlyDemandModel>();

            // Read header line
            string line = sr.ReadLine().Trim();

            string label = string.Empty;

            bool isParseOK = !string.IsNullOrEmpty(line);
            if (isParseOK)
            {
                isParseOK = (line == "Demand Group       Factor   Jan   Feb   Mar   Apr   May   Jun   Jul   Aug   Sep   Oct   Nov   Dec");

                while (isParseOK && !sr.EndOfStream)
                {
                    line = sr.ReadLine().Trim();

                    if (line.Length > 20)
                    {
                        label = line.Substring(0, 20).Trim();
                        isParseOK = !string.IsNullOrEmpty(label);

                        FarmDamRepeatingMonthlyDemandModel newDemandModel = new FarmDamRepeatingMonthlyDemandModel()
                        {
                            DemandGroup = label,
                        };

                        string[] partsOfLine = line.Substring(20).Split(' ', StringSplitOptions.RemoveEmptyEntries);

                        if (partsOfLine.Length >= 13)
                        {
                            double value = 0.0;
                            isParseOK &= double.TryParse(partsOfLine[0], out value);
                            if (isParseOK)
                            {
                                newDemandModel.AnnualDemandFactor = value;
                            }

                            newDemandModel.MonthlyDemandProportions = new double[12];
                            for (int i = 1; i <= 12 && isParseOK; i++)
                            {
                                isParseOK &= double.TryParse(partsOfLine[i], out value);
                                if (isParseOK)
                                {
                                    newDemandModel.MonthlyDemandProportions[i - 1] = value;
                                }
                            }
                        }
                        else
                        {
                            isParseOK = false;
                        }

                        if (isParseOK)
                        {
                            modelsList.Add(label, newDemandModel);
                        }
                    }
                    else
                    {
                        isParseOK = false;
                    }
                }
            }

            return modelsList;
        }
    }
}