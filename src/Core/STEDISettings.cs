namespace STEDI.ModelSettings
{
    using Newtonsoft.Json;
    using STEDI.InputOutput;
    using STEDI.JSON;
    using STEDI.ModelRun;
    using STEDI.Static;
    using STEDI.TimeSeries;
    using UnitsNet;
    using UnitsNet.Units;

    /// <summary>Complete STEDI model settings: input paths, catchment parameters, demand groups, equations, scenario overrides, and output configuration.</summary>
    public class STEDISettings
    {
        /// <summary>
        /// Initializes static members of the <see cref="STEDISettings"/> class. Containing custom unit abbreviations used in STEDI JSON files.
        /// </summary>
        static STEDISettings()
        {
            // "ML" is standard in Australian hydrology but not a default UnitsNet abbreviation
            UnitsNet.UnitAbbreviationsCache.Default.MapUnitToAbbreviation(VolumeUnit.Megaliter, "ML");

            // ASCII alternatives for commonly typed unit strings (without Unicode superscripts)
            UnitsNet.UnitAbbreviationsCache.Default.MapUnitToAbbreviation(AreaUnit.SquareKilometer, "km2");
            UnitsNet.UnitAbbreviationsCache.Default.MapUnitToAbbreviation(AreaUnit.SquareMeter, "m2");
            UnitsNet.UnitAbbreviationsCache.Default.MapUnitToAbbreviation(VolumeUnit.CubicMeter, "m3");
        }

        /// <summary>Gets or sets the name of the stream, e.g. Quininup Brook.</summary>
        public string OutletStreamName { get; set; } = string.Empty;

        /// <summary>Gets or sets the name of the location at the outlet of the catchment, e.g. Quininup Falls.</summary>
        public string OutletNodeName { get; set; } = string.Empty;

        /// <summary>Gets or sets an identifier code or gauge number for the outlet of the catchment, e.g. 610005A.</summary>
        public string OutletNodeNumber { get; set; } = string.Empty;

        /// <summary>Gets or sets a name for the scenario.</summary>
        public string ScenarioName { get; set; } = string.Empty;

        /// <summary>Gets or sets a name for the run.</summary>
        public string RunName { get; set; } = string.Empty;

        /// <summary>Gets or sets a description for the run.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets or sets rainfall input file path.</summary>
        public string RainfallInputPath { get; set; } = string.Empty;

        /// <summary>Gets or sets PET input file path.</summary>
        public string PETInputPath { get; set; } = string.Empty;

        /// <summary>Gets or sets flow input file path.</summary>
        public string FlowInputPath { get; set; } = string.Empty;

        /// <summary>Gets or sets the path to a JSON scenario control file.</summary>
        public string ScenarioControlFileJSONPath { get; set; } = string.Empty;

        /// <summary>Gets or sets the column number of the input file for observed impacted or unimpacted flow.</summary>
        public int InputFlowColumn { get; set; } = 1;

        /// <summary>Gets or sets the column number of the dates and times from the input file for observed impacted or unimpacted flow.</summary>
        public int InputDateTimeColForFlow { get; set; } = 0;

        /// <summary>Gets or sets the column number of the input file for rainfall.</summary>
        public int InputRainfallColumn { get; set; } = 1;

        /// <summary>Gets or sets the column number of the dates and times from the input file for rainfall.</summary>
        public int InputDateTimeColForRainfall { get; set; } = 0;

        /// <summary>Gets or sets the column number of the input file for potential evapotranspiration.</summary>
        public int InputEvaporationColumn { get; set; } = 2;

        /// <summary>Gets or sets the column number of the dates and times from the input file for evaporation.</summary>
        public int InputDateTimeColForEvaporation { get; set; } = 0;

        /// <summary>Gets or sets the demand time series input file path.</summary>
        public string DemandTimeSeriesInputPath { get; set; } = string.Empty;

        /// <summary>Gets or sets output folder.</summary>
        public string OutFolder { get; set; } = string.Empty;

        /// <summary>Gets or sets the name of the main summary res.csv output file.</summary>
        public string ResCSVOutputPath { get; set; } = string.Empty;

        /// <summary>Catchment area in km².</summary>
        private double catchmentAreakm2 = -1;

        /// <summary>Gets or sets Catchment area as a string with units (e.g. "150 km2"). Bare numbers default to km².</summary>
        [JsonConverter(typeof(UnitStringJsonConverter), "km2")]
        public string CatchmentArea { get; set; } = string.Empty;

        /// <summary>Gets the catchment area in km².</summary>
        /// <returns>Catchment area in km².</returns>
        public double GetCatchmentAreakm2() { return catchmentAreakm2; }

        /// <summary>Sets the catchment area in km² from a numeric value.</summary>
        /// <param name="catchmentAreaWithUnits">Catchment area as a string with units (or assumes unit is km² if no units specified).</param>
        public void SetCatchmentArea(string catchmentAreaWithUnits)
        {
            double catchmentArea = UnitConversions.ValueFromAreaString(catchmentAreaWithUnits, AreaUnit.SquareKilometer);

            if (!double.IsNaN(catchmentArea))
            {
                this.catchmentAreakm2 = Math.Max(0.0, catchmentArea);
            }
        }

        /// <summary>Sets the catchment area in km² from a numeric value.</summary>
        /// <param name="catchmentAreaInkm2">Catchment area in km².</param>
        public void SetCatchmentArea(double catchmentAreaInkm2)
        {
            this.catchmentAreakm2 = catchmentAreaInkm2;
        }

        /// <summary>Gets or sets the seed for the random number generator, when randomly generating the dam network.</summary>
        public int RNGSeed { get; set; } = 2025;

        /// <summary>Gets or sets the modelling time span for the simulation (daily, weekly, or monthly).</summary>
        public StandardModellingTimeSpan ModellingTimeSpan { get; set; } = new StandardModellingTimeSpan(BaseModellingTimeSpan.Daily, 1.0);

        /// <summary>
        /// Gets or sets the initial volume in each storage at the start of the run, as a proportion of the full storage at the start of the run.
        /// Uses the applicable maximum storage volume for the water body at the time step for the start of the run.
        /// </summary>
        public double AllStoragesProportionFullAtStartOfRun { get; set; } = 0.0;

        /// <summary>Gets or sets a value indicating whether unimpacted flow output is to be calculated for providing observed (gauged) flow input (true) or false if observed flow output is to be calculated from unimpacted flow input.</summary>
        public bool CalculateUnimpactedGivenObserved { get; set; } = false;

        /// <summary>Gets or sets a value indicating whether calculation methods are the same as legacy STEDI version 1 (true) or false if new calculation methods to be adopted.</summary>
        public bool UseLegacySTEDI1CalculationMethods { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether specific details are to be provided for a network with details of every dam and upstream catchments (true)
        /// or false if only a probability distribution of input dams and total volume is to be provided.
        /// </summary>
        public bool UseSpecificDamNetworkDetails { get; set; } = false;

        /// <summary>Gets or sets the total volume in ML of farm dams in the catchment, which are to be randomly assigned from a probability distribution.</summary>
        public double ProbabilityDistributionTotalDamVolume { get; set; } = 0;

        /// <summary>
        /// Gets or sets a dictionary to define a histogram probability distribution of dam volumes in the catchment.
        /// Keys represent the storage volume of a dam in ML, with first value usually set to 0 ML.
        /// Values represent the probability of dams occuring in the class below the defined value. First class normally has 0 probability. Probabilities should sum to 1.
        /// </summary>
        private Dictionary<double, double> MaxVolumesMLAndIntervalProbabilities { get; set; } = new Dictionary<double, double>();

        /// <summary>
        /// Gets or sets a dictionary to define a histogram probability distribution of dam volumes in the catchment.
        /// Keys represent the storage volume of a dam in ML, with first value usually set to 0 ML.
        /// Values represent the probability of dams occuring in the class below the defined value. First class normally has 0 probability. Probabilities should sum to 1.
        /// </summary>
        public Dictionary<string, double> MaxVolumesAndIntervalProbabilities { get; set; }

        /// <summary>Gets the dam volume probability distribution as volume (ML) to probability pairs.</summary>
        /// <returns>Dictionary of volume thresholds (ML) to interval probabilities.</returns>
        public Dictionary<double, double> GetMaxVolumesAndIntervalProbabilities()
        {
            return this.MaxVolumesMLAndIntervalProbabilities;
        }

        /// <summary>Parses volume strings with units into the internal probability distribution dictionary.</summary>
        /// <param name="table">Dictionary of volume strings (with units) to interval probabilities.</param>
        public void SetMaxVolumesAndIntervalProbabilities(Dictionary<string, double> table)
        {
            this.MaxVolumesMLAndIntervalProbabilities.Clear();

            if (table != null)
            {
                foreach (KeyValuePair<string, double> row in table)
                {
                    double volumeML = UnitConversions.ValueFromVolumeString(row.Key, VolumeUnit.Megaliter);
                    this.MaxVolumesMLAndIntervalProbabilities.Add(volumeML, row.Value);
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum proportion of catchment that will be upstream of water bodies in the catchment.
        /// Must be between 0.001 and 0.999: code will check for this and won't use a value outside of this range.
        /// Default value of 99.9% impounded is conservatively high.
        /// </summary>
        public readonly double LowerLimitMaximumProportionOfCatchmentImpounded = 0.001;
        public readonly double UpperLimitMaximumProportionOfCatchmentImpounded = 0.999;
        public double MaximumProportionOfCatchmentImpounded { get; set; } = 0.999;

        /// <summary>Gets or sets a value indicating whether demand groups are to be set according to a single, specific threshold of volume.</summary>
        public bool UseVolumeThresholdForDemandGroups { get; set; } = false;

        /// <summary>
        /// Gets or sets volume in ML that determines a fixed threshold between only 2 demand groups.
        /// If the specified volume is 0 or negative, this feature is ignored.
        /// If it is a positive value, all dams with volumes less or equal to the threshold get demand group 1 and all dams with volumes greater than the threshold get demand group 2.
        /// </summary>
        private double volumeThresholdMLForDemandGroups { get; set; } = -9999.0;

        /// <summary>
        /// Gets or sets a volume that determines a fixed threshold between only 2 demand groups (e.g. "5 ML").
        /// Bare numbers default to ML. If empty or ≤ 0, this feature is ignored.
        /// Positive values assign demand group 1 to dams at or below the threshold and group 2 to those above.
        /// </summary>
        [JsonConverter(typeof(UnitStringJsonConverter), "ML")]
        public string VolumeThresholdForDemandGroups { get; set; } = string.Empty;

        /// <summary>
        /// Gets the volume threshold between demand groups in ML.
        /// </summary>
        /// <returns>Volume threshold between demand groups in ML.</returns>
        public double GetVolumeThresholdMLForDemandGroups()
        {
            return this.volumeThresholdMLForDemandGroups;
        }

        /// <summary>
        /// Parses the supplied string, which can have units, into the volume threshold for demand groups in ML.
        /// </summary>
        /// <param name="volumeThresholdWithUnits">Volume threshold for demand groups as a string, with units — assumes ML if no units supplied.</param>
        public void SetVolumeThresholdForDemandGroups(string volumeThresholdWithUnits)
        {
            double threshold = UnitConversions.ValueFromVolumeString(volumeThresholdWithUnits, VolumeUnit.Megaliter);

            if (!double.IsNaN(threshold))
            {
                if (threshold > 0)
                {
                    this.volumeThresholdMLForDemandGroups = Math.Max(0.0, threshold);
                }
                else
                {
                    this.volumeThresholdMLForDemandGroups = -9999.0;
                    this.UseVolumeThresholdForDemandGroups = false;
                }
            }
        }

        /// <summary>Gets or sets a value indicating whether low flow bypasses are to be applied to all dams bigger than a single, specific threshold of volume.</summary>
        public bool UseFixedLowFlowBypassCapacity { get; set; } = false;

        /// <summary>Minimum dam volume in ML, for which low flow bypass is applied to all larger dams. All smaller dams get 0 bypass capacity.</summary>
        private double volumeThresholdMLForBypass { get; set; } = -9999.0;

        /// <summary>Minimum dam volume (e.g. "5 ML") for which low flow bypass is applied. Bare numbers default to ML.</summary>
        [JsonConverter(typeof(UnitStringJsonConverter), "ML")]
        public string VolumeThresholdForBypass { get; set; } = string.Empty;

        /// <summary>
        /// Gets the volume threshold for applying bypass to all larger dams, in ML.
        /// </summary>
        /// <returns>Volume threshold for applying bypass to all larger dams, in ML.</returns>
        public double GetVolumeThresholdMLForBypass()
        {
            return this.volumeThresholdMLForBypass;
        }

        /// <summary>Parses the supplied string, which can have units, into the volume threshold for bypass in ML.</summary>
        /// <param name="volumeThresholdWithUnits">Volume threshold for bypass as a string, with units — assumes ML if no units supplied.</param>
        public void SetVolumeThresholdForBypass(string volumeThresholdWithUnits)
        {
            double threshold = UnitConversions.ValueFromVolumeString(volumeThresholdWithUnits, VolumeUnit.Megaliter);

            if (!double.IsNaN(threshold))
            {
                this.volumeThresholdMLForBypass = Math.Max(0.0, threshold);
            }
        }

        /// <summary>Gets or sets the bypass capacity flow rate in ML/d for each 1 km² of total upstream catchment area.</summary>
        public double BypassCapacityML_d_km2 { get; set; } = 0.0;

        /// <summary>Gets or sets the start date (year doesn't matter) of the season for which low flow bypasses are active.</summary>
        public DateOnly BypassSeasonStartDateIgnoreYear { get; set; } = new DateOnly(2000, 1, 1);

        /// <summary>Gets or sets the last date (year doesn't matter) of the season for which low flow bypasses are active.</summary>
        public DateOnly BypassSeasonEndDateIgnoreYear { get; set; } = new DateOnly(2000, 12, 31);

        /// <summary>Gets or sets the start date (year doesn't matter) of the season for which pumping is permitted.</summary>
        public DateOnly PumpingSeasonStartDateIgnoreYear { get; set; } = new DateOnly(2000, 1, 1);

        /// <summary>Gets or sets the last date (year doesn't matter) of the season for which pumping is permitted.</summary>
        public DateOnly PumpingSeasonEndDateIgnoreYear { get; set; } = new DateOnly(2000, 12, 31);

        /// <summary>Gets or sets a JSON file that specifies the repeating monthly demands for farm dams by type.</summary>
        public string RepeatingMonthlyDemandModelsJSONPath { get; set; } = string.Empty;

        /// <summary>Gets or sets an array of specifications of groups for repeating monthly demands.</summary>
        public Dictionary<string, FarmDamRepeatingMonthlyDemandModel> RepeatingMonthlyDemandGroups { get; set; } = new Dictionary<string, FarmDamRepeatingMonthlyDemandModel>();

        /// <summary>Gets or sets a JSON file that specifies the demands for farm dams specified by time series patterns.</summary>
        public string TimeSeriesDemandModelsJSONPath { get; set; } = string.Empty;

        /// <summary>Gets or sets an array of specifications of groups for time series demands.</summary>
        public Dictionary<string, FarmDamTimeSeriesDemandModel> TimeSeriesDemandGroups { get; set; } = new Dictionary<string, FarmDamTimeSeriesDemandModel>();

        /// <summary>Gets or sets a value indicating whether dam volumes are to be recalculated from surface areas using the equation specified in the scenario file.</summary>
        public bool RecalculateDamVolumesFromSurfaceAreas { get; set; } = false;

        /// <summary>Gets or sets Path to JSON file defining how to derive storage volume from input spatial properties.</summary>
        public string VolumeFromInputPropertiesJSON { get; set; } = string.Empty;

        /// <summary>Gets or sets Path to JSON file defining how to derive catchment area from input spatial properties.</summary>
        public string CatchmentAreaFromInputPropertiesJSON { get; set; } = string.Empty;

        /// <summary>Gets or sets Path to JSON file specifying which water bodies to include in the model (null = include all).</summary>
        public string WaterBodiesToIncludeJSONPath { get; set; } = string.Empty;

        /// <summary>Gets or sets Path to the GIS file (shapefile or geopackage) containing water body polygons.</summary>
        public string WaterBodyPolygonsGISFilePath { get; set; } = string.Empty;

        /// <summary>Gets or sets Path to the GIS file (shapefile or geopackage) containing catchment polygons.</summary>
        public string CatchmentPolygonsGISFilePath { get; set; } = string.Empty;

        /// <summary>Gets or sets a path to a JSON file that specifies the dam type groups to use.</summary>
        public string DamTypeGroupsJSONPath { get; set; } = string.Empty;

        /// <summary>Gets or sets a JSON file that specifies the fields of the input spatial data file that maps to the inputs required for each water body in the STEDI model.</summary>
        public string WaterBodyFieldsToReadJSONPath { get; set; } = string.Empty;

        /// <summary>Gets or sets a JSON file that specifies the fields of the input spatial data file that map to the inputs required for each local catchment in the STEDI model.</summary>
        public string CatchmentFieldsToReadJSONPath { get; set; } = string.Empty;

        /// <summary>Gets or sets the date in each year when dams are revised. Year component is ignored.</summary>
        public DateOnly DamsRevisionDateIgnoreYear { get; set; } = new DateOnly(1900, 1, 1);

        /// <summary>Gets or sets the dictionary of named scenarios to run.</summary>
        public Dictionary<string, Scenario> ScenariosToRun { get; set; } = new Dictionary<string, Scenario>();

        /// <summary>Gets or sets equation for catchment area in km² upstream of a dam as a function of storage volume in ML.</summary>
        public EquationParser VolumeCatchmentAreaEquation { get; set; } = null;

        /// <summary>Read specified JSON file, if provided, to specify equation for storage volume of each water body.</summary>
        public void SetVolumeEquationFromJSON()
        {
            if (this.VolumeFromInputPropertiesJSON != string.Empty)
            {
                this.VolumeSurfaceAreaEquation = JSONSerialisation.DeserialiseFileThrowOnError<EquationParser>(this.VolumeFromInputPropertiesJSON);
            }
        }

        /// <summary>Read specified JSON file, if provided, to specify equation for local catchment area of each water body.</summary>
        public void SetCatchmentAreaEquationFromJSON()
        {
            if (this.CatchmentAreaFromInputPropertiesJSON != string.Empty)
            {
                this.VolumeCatchmentAreaEquation = JSONSerialisation.DeserialiseFileThrowOnError<EquationParser>(this.CatchmentAreaFromInputPropertiesJSON);
            }
        }

        /// <summary>Gets or sets equation for Volume in ML as a function of surface area in m².</summary>
        public EquationParser VolumeSurfaceAreaEquation { get; set; } = new EquationParser()
        {
            // Default equation is default from legacy STEDI manual
            // Lowe et al. (2005) equation, V = 1/6900 * SA ^ 1.314
            VariablesWithDescriptions = new Dictionary<string, string>() { { "SA", "Surface area in m²" } },
            Equation = "0.0001449275*SA^1.314",
        };

        /// <summary>Gets a merged string of the outlet node identifier: stream name @ location on stream.</summary>
        /// <returns>Merged string output.</returns>
        public string GetOutletNumberStreamName()
        {
            string result = this.OutletNodeNumber;

            if (!string.IsNullOrEmpty(this.OutletNodeNumber))
                result += ": ";

            result += this.OutletStreamName;

            if (!string.IsNullOrEmpty(this.OutletStreamName) && !string.IsNullOrEmpty(this.OutletNodeName))
                result += " @ ";

            result += this.OutletNodeName;

            return result;
        }

        /// <summary>
        /// Returns the catchment area in km² for the specified dam volume in ML.
        /// </summary>
        /// <param name="volume">Dam storage volume at full capacity in ML.</param>
        /// <returns>Catchment area in km².</returns>
        public double EvaluateVolumeCatchmentAreaEquation(double volume)
        {
            double result = double.NaN;
            EquationParser volCAEquation = this.VolumeCatchmentAreaEquation;

            if (volCAEquation?.VariablesWithDescriptions != null && volCAEquation.VariablesWithDescriptions.Count > 0)
            {
                volCAEquation.IsValidResult = false;

                string variableName = volCAEquation.VariablesWithDescriptions.First().Key;

                if (volCAEquation.VariableValues.ContainsKey(variableName))
                    volCAEquation.VariableValues[variableName] = volume;
                else
                    volCAEquation.VariableValues.Add(variableName, volume);

                volCAEquation.Evaluate();
                if (volCAEquation.IsValidResult)
                {
                    result = volCAEquation.EquationResult;
                }
                else if (!string.IsNullOrEmpty(volCAEquation.ErrorMessage))
                {
                    Console.WriteLine($"WARNING: Volume–catchment area equation failed for volume = {volume} ML. {volCAEquation.ErrorMessage}");
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the dam volume in ML for the specified dam surface area at full capacity in m².
        /// </summary>
        /// <param name="surfaceArea">Dam surface area at full capacity in m².</param>
        /// <returns>Dam storage volume at full capacity in ML.</returns>
        public double EvaluateSurfaceAreaVolumeEquation(double surfaceArea)
        {
            double result = double.NaN;

            EquationParser saVolEquation = this.VolumeSurfaceAreaEquation;

            if (saVolEquation?.VariablesWithDescriptions != null && saVolEquation.VariablesWithDescriptions.Count > 0)
            {
                saVolEquation.IsValidResult = false;
                string variableName = saVolEquation.VariablesWithDescriptions.First().Key;

                if (saVolEquation.VariableValues.ContainsKey(variableName))
                    saVolEquation.VariableValues[variableName] = surfaceArea;
                else
                    saVolEquation.VariableValues.Add(variableName, surfaceArea);

                saVolEquation.Evaluate();
                if (saVolEquation.IsValidResult)
                {
                    result = saVolEquation.EquationResult;
                }
                else if (!string.IsNullOrEmpty(saVolEquation.ErrorMessage))
                {
                    Console.WriteLine($"WARNING: Surface area–volume equation failed for surface area = {surfaceArea} m². {saVolEquation.ErrorMessage}");
                }
            }

            return result;
        }

        /// <summary>
        /// Solves for the surface area of the dam in m² for the specified dam volume in ML.
        /// </summary>
        /// <param name="volume">Dam storage volume at full capacity in ML.</param>
        /// <returns>Dam surface area at full capacity in m².</returns>
        public double SolveForSurfaceAreaFromVolume(double volume)
        {
            double result = double.NaN;

            if (volume <= 0.0)
            {
                result = 0.0;
            }
            else
            {
                Func<double, double> f = x => this.EvaluateSurfaceAreaVolumeEquation(x) - volume;
                result = NewtonRaphsonSolver.Solve(f, 5000);
            }

            return result;
        }

        /// <summary>Runs all post-deserialisation init steps. Call once after deserialising from JSON.</summary>
        public void InitialiseFromJSON()
        {
            this.SetCatchmentArea(this.CatchmentArea);
            this.SetVolumeThresholdForDemandGroups(this.VolumeThresholdForDemandGroups);
            this.SetVolumeThresholdForBypass(this.VolumeThresholdForBypass);
            this.SetMaxVolumesAndIntervalProbabilities(this.MaxVolumesAndIntervalProbabilities);
            this.ReadDemandModelsFromJSON();
            this.SetCatchmentAreaEquationFromJSON();
            this.SetVolumeEquationFromJSON();
        }

        /// <summary>Returns demand model type. Requires at least <paramref name="minGroupCount"/> groups.</summary>
        /// <param name="minGroupCount">Minimum number of allowable valid demand groups to set.</param>
        public ModelElementType GetDemandModelType(int minGroupCount = 1)
        {
            if (this.RepeatingMonthlyDemandGroups?.Count >= minGroupCount)
                return ModelElementType.RepeatingMonthlyDemand;

            if (this.TimeSeriesDemandGroups?.Count >= minGroupCount)
                return ModelElementType.TimeSeriesDemand;

            return ModelElementType.Missing;
        }

        /// <summary>
        /// Gets the index of the demand model by volume, if a volume threshold is specified for groups by demand type.
        /// </summary>
        /// <param name="volume">Storage volume in ML.</param>
        /// <param name="demandNodeType">Type of demand node groups defined in model.</param>
        /// <returns>Group index for demand.</returns>
        public string GetGroupDemandModelIndexByVolume(double volume, ModelElementType demandNodeType)
        {
            string demandGroupIndex = string.Empty;

            if (this.UseVolumeThresholdForDemandGroups && this.volumeThresholdMLForDemandGroups > 0)
            {
                // Work out demand groups by volume of dam above or below threshold
                if (volume <= this.volumeThresholdMLForDemandGroups)
                {
                    switch (demandNodeType)
                    {
                        case ModelElementType.RepeatingMonthlyDemand:
                            demandGroupIndex = this.RepeatingMonthlyDemandGroups.First().Key;
                            break;
                        case ModelElementType.TimeSeriesDemand:
                            demandGroupIndex = this.TimeSeriesDemandGroups.First().Key;
                            break;
                    }
                }
                else
                {
                    switch (demandNodeType)
                    {
                        case ModelElementType.RepeatingMonthlyDemand:
                            demandGroupIndex = this.RepeatingMonthlyDemandGroups.Last().Key;
                            break;
                        case ModelElementType.TimeSeriesDemand:
                            demandGroupIndex = this.TimeSeriesDemandGroups.Last().Key;
                            break;
                    }
                }
            }

            return demandGroupIndex;
        }

        /// <summary>
        /// Gets the index of the repeating monthly demand model for the legacy STEDI node.
        /// </summary>
        /// <param name="legacySTEDIDamNode">Legacy STEDI farm dam node.</param>
        /// <returns>Group index for repeating monthly demand.</returns>
        public string GetRepeatingMonthlyDemandModelIndex(LegacySTEDIDamNode legacySTEDIDamNode)
        {
            string result = string.Empty;

            Dictionary<string, FarmDamRepeatingMonthlyDemandModel> groupModels = this.RepeatingMonthlyDemandGroups;

            if (groupModels != null)
            {
                if (groupModels.ContainsKey(legacySTEDIDamNode.DemandGroup.Trim()))
                {
                    result = legacySTEDIDamNode.DemandGroup.Trim();
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the index of the time series demand model for the legacy STEDI node.
        /// </summary>
        /// <param name="legacySTEDIDamNode">Legacy STEDI farm dam node.</param>
        /// <returns>Group index for time series demand.</returns>
        public string GetTimeSeriesDemandModelIndex(LegacySTEDIDamNode legacySTEDIDamNode)
        {
            string result = string.Empty;

            Dictionary<string, FarmDamTimeSeriesDemandModel> groupModels = this.TimeSeriesDemandGroups;

            if (groupModels != null)
            {
                if (groupModels.ContainsKey(legacySTEDIDamNode.DemandGroup.Trim()))
                {
                    result = legacySTEDIDamNode.DemandGroup.Trim();
                }
            }

            return result;
        }

        /// <summary>
        /// Reads demand model groups from specified JSON file path.
        /// </summary>
        public void ReadDemandModelsFromJSON()
        {
            this.TimeSeriesDemandGroups.Clear();
            this.RepeatingMonthlyDemandGroups.Clear();

            if (!string.IsNullOrEmpty(this.TimeSeriesDemandModelsJSONPath))
            {
                if (!File.Exists(this.TimeSeriesDemandModelsJSONPath))
                {
                    throw new FileNotFoundException($"Time series demand models JSON file does not exist: '{this.TimeSeriesDemandModelsJSONPath}'.", this.TimeSeriesDemandModelsJSONPath);
                }

                try
                {
                    this.TimeSeriesDemandGroups = JSONSerialisation.DeserialiseFileThrowOnError<Dictionary<string, FarmDamTimeSeriesDemandModel>>(TimeSeriesDemandModelsJSONPath);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Failed to deserialise time series demand models from '{this.TimeSeriesDemandModelsJSONPath}'. {ex.Message}", ex);
                }

                if (this.TimeSeriesDemandGroups == null || this.TimeSeriesDemandGroups.Count == 0)
                {
                    throw new InvalidDataException($"Time series demand models file '{this.TimeSeriesDemandModelsJSONPath}' contains no demand groups.");
                }

                string[] demandGroupNames = this.TimeSeriesDemandGroups.Keys.ToArray();
                for (int i = 0; i < demandGroupNames.Length; i++)
                {
                    string groupName = demandGroupNames[i];
                    int column = this.TimeSeriesDemandGroups[groupName].InputFileColumnNumber;
                    string path = this.TimeSeriesDemandGroups[groupName].InputFilePath;

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        throw new InvalidDataException($"Demand group '{groupName}' has no InputFilePath specified in '{this.TimeSeriesDemandModelsJSONPath}'.");
                    }

                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException($"Demand time series input file for group '{groupName}' does not exist: '{path}'.", path);
                    }

                    try
                    {
                        string demandInputUnits = string.Empty;
                        this.TimeSeriesDemandGroups[groupName].InputPattern = ReadTimeSeries.ReadTimeSeriesFromFile(path, ref demandInputUnits, column);
                    }
                    catch (Exception ex) when (ex is not FileNotFoundException and not InvalidDataException)
                    {
                        throw new InvalidDataException($"Failed to read time series data for demand group '{groupName}' from '{path}' (column {column}). {ex.Message}", ex);
                    }

                    if (this.TimeSeriesDemandGroups[groupName].InputPattern == null || this.TimeSeriesDemandGroups[groupName].InputPattern.Length == 0)
                    {
                        throw new InvalidDataException(
                            $"No valid time series data was read for demand group '{groupName}' from '{path}' (column {column}). "
                            + "Check that the file contains data and the column number is correct.");
                    }
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(this.RepeatingMonthlyDemandModelsJSONPath))
                {
                    if (!File.Exists(this.RepeatingMonthlyDemandModelsJSONPath))
                    {
                        throw new FileNotFoundException($"Repeating monthly demand models JSON file does not exist: '{this.RepeatingMonthlyDemandModelsJSONPath}'.", this.RepeatingMonthlyDemandModelsJSONPath);
                    }

                    try
                    {
                        this.RepeatingMonthlyDemandGroups = JSONSerialisation.DeserialiseFileThrowOnError<Dictionary<string, FarmDamRepeatingMonthlyDemandModel>>(this.RepeatingMonthlyDemandModelsJSONPath);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException($"Failed to deserialise repeating monthly demand models from '{this.RepeatingMonthlyDemandModelsJSONPath}'. {ex.Message}", ex);
                    }

                    if (this.RepeatingMonthlyDemandGroups == null || this.RepeatingMonthlyDemandGroups.Count == 0)
                    {
                        throw new InvalidDataException($"Repeating monthly demand models file '{this.RepeatingMonthlyDemandModelsJSONPath}' contains no demand groups.");
                    }
                }
            }
        }
    }
}
