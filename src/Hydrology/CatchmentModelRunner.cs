// <copyright file="CatchmentModelRunner.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    using RODIS.InputOutput;
    using RODIS.ModelSettings;
    using RODIS.Series;
    using RODIS.TimeSeries;
    using UnitsNet;
    using UnitsNet.Units;

    /// <summary>Orchestrates RODIS model runs: loads input time series, executes the simulation loop, and stores output time series and statistics.</summary>
    public class CatchmentModelRunner
    {
        /// <summary>The catchment model containing all nodes, links, and demand models for this run.</summary>
        public CatchmentModel catchmentModel = new CatchmentModel();

        /// <summary>List of time series for overall outputs at the catchment or model level.</summary>
        public List<TimeSeriesWithMetadata> OverallOutputTimeSeries = new List<TimeSeriesWithMetadata>();

        /// <summary>List of lists of time series for outputs within each reporting group.</summary>
        public List<List<TimeSeriesWithMetadata>> GroupOutputTimeSeries = new List<List<TimeSeriesWithMetadata>>();

        /// <summary>Start date for calculating summary statistics from output time series. Defaults to DateTime.MinValue (use all data).</summary>
        public DateTime StartDateForStatistics = DateTime.MinValue;

        /// <summary>End date for calculating summary statistics from output time series. Defaults to DateTime.MaxValue (use all data).</summary>
        public DateTime EndDateForStatistics = DateTime.MaxValue;

        /// <summary>Gets Mean values for each overall output time series, computed by CalculateMeanAndMeanAnnualValues.</summary>
        public double[] OverallOutputMeanValues { get; private set; } = null;

        /// <summary>Gets Mean values for each output time series within each reporting group.</summary>
        public double[][] GroupOutputMeanValues { get; private set; } = null;

        /// <summary>Gets Mean annual values for each overall output time series.</summary>
        public double[] OverallOutputMeanAnnualValues { get; private set; } = null;

        /// <summary>Gets Mean annual values for each output time series within each reporting group.</summary>
        public double[][] GroupOutputMeanAnnualValues { get; private set; } = null;

        /// <summary>Readonly dictionary mapping output variable names to their unit strings (e.g. "ML" or "ML.day^-1").</summary>
        public readonly Dictionary<string, string> OutputNamesAndUnits = new Dictionary<string, string>()
        {
            { "Impact", "ML" },
            { "Impact for Unrestricted Demand Node", "ML" },
            { "Impact as Additional Inflow", "ML" },
            { "Unimpacted Flow", "ML" },
            { "Pumped Inflow", "ML" },
            { "Net Rainfall Volume", "ML" },
            { "Demand Volume Extracted", "ML" },
            { "Spill Downstream Flow", "ML" },
            { "Change in Storage Volume", "ML" },
            { "Storage Volume End of Timestep", "ML" },
            { "Bypass Downstream Flow", "ML" },
            { "Local Catchment Inflow", "ML" },
            { "Downstream Flow", "ML" },
            { "Storage Capacity at Full Supply", "ML" },
            { "Volume Balance Misclosure", "ML" },
            { "Seepage Loss Volume", "ML" },
            { "Seepage Loss Volume at Full", "ML" },
            { "Bypass Flow Capacity", "ML.day^-1" },
            { "Pumped Inflow Capacity", "ML.day^-1" },
            { "Top-Down Volume Balance Misclosure", "ML" },
            { "Total Subcatchment Runoff", "ML" },
            { "Dam Removal Storage Loss", "ML" },
            { "Cumulative Bottom-Up Misclosure", "ML" },
            { "Cumulative Top-Down Misclosure", "ML" },
        };

        /// <summary>Returns the output variable names as an array, in dictionary insertion order.</summary>
        /// <returns>Array of output variable name strings.</returns>
        public string[] GetOutputNames()

        {
            return this.OutputNamesAndUnits.Keys.ToArray();
        }

        /// <summary>Returns the output unit strings as an array, in dictionary insertion order.</summary>
        /// <returns>Array of output unit strings.</returns>
        public string[] GetOutputUnits()

        {
            return this.OutputNamesAndUnits.Values.ToArray();
        }

        /// <summary>
        /// Rainfall time series data.
        /// </summary>
        private TimeSeriesValue[] rainfallTimeSeries = null;

        /// <summary>
        /// Evaporation time series data.
        /// </summary>
        private TimeSeriesValue[] evaporationTimeSeries = null;

        /// <summary>
        /// Flow time series data - may be unimpacted flow or recorded flow, depending on settings in the model.
        /// </summary>
        private TimeSeriesValue[] flowTimeSeries = null;

        /// <summary>
        /// Impacted flow time series data.
        /// </summary>
        private TimeSeriesValue[] impactedFlowTimeSeries = null;

        /// <summary>
        /// Unimpacted flow time series data.
        /// </summary>
        private TimeSeriesValue[] unimpactedFlowTimeSeries = null;

        /// <summary>
        /// Original observed flow time series, saved before MC iterations begin.
        /// Used to restore flowTimeSeries before each iteration's calibration scenario,
        /// because ReloadInputFlowTimeSeries permanently overwrites flowTimeSeries.
        /// </summary>
        private TimeSeriesValue[] originalObservedFlowTimeSeries = null;

        /// <summary>
        /// Gets the datetime of the first valid input data time step.
        /// </summary>
        /// <returns>Datetime of first valid input data time step.</returns>
        public DateTime GetStartRun()
        {
            DateTime result = DateTime.MaxValue;

            if (this.rainfallTimeSeries != null)
            {
                result = TimeSeriesValue.GetStartDateTimeValid(this.rainfallTimeSeries);
            }

            return result;
        }

        /// <summary>
        /// Gets the datetime of the last valid input data time step.
        /// </summary>
        /// <returns>Datetime of last valid input data time step.</returns>
        public DateTime GetEndRun()
        {
            DateTime result = DateTime.MinValue;

            if (this.rainfallTimeSeries != null)
            {
                result = TimeSeriesValue.GetEndDateTimeValid(this.rainfallTimeSeries);
                StandardModellingTimeSpan modelTS = TimeSeriesValue.GetTimeStep(this.rainfallTimeSeries);
                result = result.Add(modelTS.GetAsTimeSpan());
            }

            return result;
        }

        /// <summary>
        /// Returns an array of the datetimes of all the valid values in the time series.
        /// </summary>
        /// <returns>array of the datetimes of all the valid values in the time series.</returns>
        public DateTime[] GetValidInputDateTimes ()
        {
            DateTime[] result = null;

            if (this.rainfallTimeSeries != null)
            {
                result = TimeSeriesValue.GetValidDateTimes(this.rainfallTimeSeries);
            }

            return result;
        }

        /// <summary>
        /// Returns true if modelling time spans for rainfall, evaporation and flow inputs are all the same.
        /// </summary>
        /// <returns>Returns true if modelling time spans for rainfall, evaporation and flow inputs are all the same.</returns>
        public bool AreAllInputTimeSpansTheSame()
        {
            bool result = false;

            StandardModellingTimeSpan rainfallTimeSpan = TimeSeriesValue.GetTimeStep(rainfallTimeSeries);
            StandardModellingTimeSpan evapTimeSpan = TimeSeriesValue.GetTimeStep(evaporationTimeSeries);
            StandardModellingTimeSpan flowTimeSpan = TimeSeriesValue.GetTimeStep(flowTimeSeries);

            if (rainfallTimeSpan != null)
            {
                if (evapTimeSpan != null)
                {
                    result = rainfallTimeSpan == evapTimeSpan;

                    if (flowTimeSpan != null)
                    {
                        result &= rainfallTimeSpan == flowTimeSpan;
                    }
                    else
                    {
                        result = false;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Saves a deep copy of the current flow time series as the original observed flow.
        /// Call once after LoadInputTimeSeries and before any MC iterations.
        /// </summary>
        public void SaveOriginalFlowTimeSeries()
        {
            if (this.flowTimeSeries != null)
            {
                this.originalObservedFlowTimeSeries = new TimeSeriesValue[this.flowTimeSeries.Length];
                for (int i = 0; i < this.flowTimeSeries.Length; i++)
                {
                    this.originalObservedFlowTimeSeries[i] = this.flowTimeSeries[i].DeepCopy();
                }
            }
        }

        /// <summary>
        /// Restores the flow time series to the original observed flow saved by <see cref="SaveOriginalFlowTimeSeries"/>.
        /// Creates a fresh deep copy each time, so the saved original is never consumed.
        /// Call before each MC iteration's calibration scenario (Scenario 1) to ensure the solver calibrates against observed flow, not the previous iteration's unimpacted flow.
        /// </summary>
        public void RestoreOriginalFlowTimeSeries()
        {
            if (this.originalObservedFlowTimeSeries != null)
            {
                this.flowTimeSeries = new TimeSeriesValue[this.originalObservedFlowTimeSeries.Length];
                for (int i = 0; i < this.originalObservedFlowTimeSeries.Length; i++)
                {
                    this.flowTimeSeries[i] = this.originalObservedFlowTimeSeries[i].DeepCopy();
                }
            }
        }

        /// <summary>
        /// Loads the rainfall, evaporation and flow input time series, for the common run period of all provided time series data arrays.
        /// </summary>
        /// <param name="inputRainfallTimeSeries">Rainfall time series data array.</param>
        /// <param name="inputEvaporationTimeSeries">Evaporation (PET) time series data array.</param>
        /// <param name="inputFlowTimeSeries">Flow time series data array (unimpacted or observed, per settings).</param>
        /// <param name="rodisSettings">RODIS settings, used to check demand time series consistency.</param>
        /// <returns>True if rainfall, evaporation, flow and demand time series inputs have consistent time spans and at least some overlap.</returns>
        public bool LoadInputTimeSeries(
            TimeSeriesValue[] inputRainfallTimeSeries,
            TimeSeriesValue[] inputEvaporationTimeSeries,
            TimeSeriesValue[] inputFlowTimeSeries,
            RODISSettings rodisSettings)
        {
            if (inputRainfallTimeSeries == null || inputRainfallTimeSeries.Length == 0)
                throw new ArgumentException("Rainfall time series is null or empty. Check that the rainfall input file was read successfully.");

            if (inputEvaporationTimeSeries == null || inputEvaporationTimeSeries.Length == 0)
                throw new ArgumentException("Evaporation time series is null or empty. Check that the PET input file was read successfully.");

            if (inputFlowTimeSeries == null || inputFlowTimeSeries.Length == 0)
                throw new ArgumentException("Flow time series is null or empty. Check that the flow input file was read successfully.");

            if (rodisSettings == null)
                throw new ArgumentNullException(nameof(rodisSettings), "RODIS settings object is null.");

            DateTime startRain = TimeSeriesValue.GetStartDateTimeValid(inputRainfallTimeSeries);
            DateTime startPET = TimeSeriesValue.GetStartDateTimeValid(inputEvaporationTimeSeries);
            DateTime startFlow = TimeSeriesValue.GetStartDateTimeValid(inputFlowTimeSeries);

            DateTime startAll = startRain;
            if (startPET > startAll)
            {
                startAll = startPET;
            }

            if (startFlow > startAll)
            {
                startAll = startFlow;
            }

            DateTime endRain = TimeSeriesValue.GetEndDateTimeValid(inputRainfallTimeSeries);
            DateTime endPET = TimeSeriesValue.GetEndDateTimeValid(inputEvaporationTimeSeries);
            DateTime endFlow = TimeSeriesValue.GetEndDateTimeValid(inputFlowTimeSeries);

            DateTime endAll = endRain;
            if (endPET < endAll)
            {
                endAll = endPET;
            }

            if (endFlow < endAll)
            {
                endAll = endFlow;
            }

            int iRain = TimeSeriesValue.GetIndexForTimestep(inputRainfallTimeSeries, startAll);
            int iPET = TimeSeriesValue.GetIndexForTimestep(inputEvaporationTimeSeries, startAll);
            int iFlow = TimeSeriesValue.GetIndexForTimestep(inputFlowTimeSeries, startAll);

            int iEndRain = TimeSeriesValue.GetIndexForTimestep(inputRainfallTimeSeries, endAll);
            int iEndPET = TimeSeriesValue.GetIndexForTimestep(inputEvaporationTimeSeries, endAll);
            int iEndFlow = TimeSeriesValue.GetIndexForTimestep(inputFlowTimeSeries, endAll);

            List<TimeSeriesValue> rainList = new List<TimeSeriesValue>();
            List<TimeSeriesValue> petList = new List<TimeSeriesValue>();
            List<TimeSeriesValue> flowList = new List<TimeSeriesValue>();

            for (; iRain < iEndRain && iPET < iEndPET && iFlow < iEndFlow; ++iRain, ++iPET, ++iFlow)
            {
                rainList.Add(inputRainfallTimeSeries[iRain].DeepCopy());
                petList.Add(inputEvaporationTimeSeries[iPET].DeepCopy());
                flowList.Add(inputFlowTimeSeries[iFlow].DeepCopy());
            }

            this.rainfallTimeSeries = rainList.ToArray();
            this.evaporationTimeSeries = petList.ToArray();
            this.flowTimeSeries = flowList.ToArray();

            // Set the modellingtimespan in the catchmentModel object using the input data
            bool doInputTimeSpansMatch = this.AreAllInputTimeSpansTheSame();

            if (doInputTimeSpansMatch && this.rainfallTimeSeries.Length >= 2)
            {
                StandardModellingTimeSpan rainfallTimeSpan = TimeSeriesValue.GetTimeStep(this.rainfallTimeSeries);
                this.catchmentModel.ModellingTimeSpan = new StandardModellingTimeSpan(rainfallTimeSpan.BaseTimeSpan, rainfallTimeSpan.NumberOfBaseTimeSpans);

                // Validate against settings if a legacy timestep string was parsed
                if (rodisSettings.ModellingTimeSpan != null)
                {
                    if (rodisSettings.ModellingTimeSpan != this.catchmentModel.ModellingTimeSpan)
                    {
                        Console.WriteLine(
                            $"WARNING: Calculation timestep in scenario file ({rodisSettings.ModellingTimeSpan.BaseTimeSpan}) "
                            + $"does not match the timestep inferred from input data ({this.catchmentModel.ModellingTimeSpan.BaseTimeSpan}). "
                            + "Using the timestep from the input data.");
                    }
                }

                doInputTimeSpansMatch = this.CheckDemandAndClimateTimeSeries(rodisSettings);

                return true;
            }
            else
            {
                if (!doInputTimeSpansMatch)
                {
                    StandardModellingTimeSpan rainTS = TimeSeriesValue.GetTimeStep(this.rainfallTimeSeries);
                    StandardModellingTimeSpan evapTS = TimeSeriesValue.GetTimeStep(this.evaporationTimeSeries);
                    StandardModellingTimeSpan flowTS = TimeSeriesValue.GetTimeStep(this.flowTimeSeries);
                    throw new InvalidDataException(
                        "Input time series do not have matching timesteps. "
                        + $"Rainfall: {rainTS?.BaseTimeSpan.ToString() ?? "unknown"}, "
                        + $"Evaporation: {evapTS?.BaseTimeSpan.ToString() ?? "unknown"}, "
                        + $"Flow: {flowTS?.BaseTimeSpan.ToString() ?? "unknown"}. "
                        + "All input files must use the same timestep (daily, weekly, or monthly).");
                }
                else
                {
                    throw new InvalidDataException(
                        $"Insufficient overlapping input data. Only {this.rainfallTimeSeries.Length} timestep(s) found in the common period. "
                        + "At least 2 timesteps are required. Check that input files have overlapping date ranges.");
                }
            }
        }

        /// <summary>Reloads the flow input time series and clips all three input arrays (rainfall, PET, flow) to the common overlap period.</summary>
        /// <param name="inputFlowTimeSeries">Replacement flow time series data array (unimpacted or observed, per settings).</param>
        /// <returns>True if input time spans are consistent and at least some overlap exists.</returns>
        public bool ReloadInputFlowTimeSeries(TimeSeriesValue[] inputFlowTimeSeries)
        {
            if (inputFlowTimeSeries == null || inputFlowTimeSeries.Length == 0)
                throw new ArgumentException("Replacement flow time series is null or empty.");

            DateTime startRain = TimeSeriesValue.GetStartDateTimeValid(this.rainfallTimeSeries);
            DateTime startFlow = TimeSeriesValue.GetStartDateTimeValid(inputFlowTimeSeries);

            DateTime startAll = startRain;
            if (startFlow > startAll)
            {
                startAll = startFlow;
            }

            DateTime endRain = TimeSeriesValue.GetEndDateTimeValid(this.rainfallTimeSeries);
            DateTime endFlow = TimeSeriesValue.GetEndDateTimeValid(inputFlowTimeSeries);

            DateTime endAll = endRain;
            if (endFlow < endAll)
            {
                endAll = endFlow;
            }

            int iRain = TimeSeriesValue.GetIndexForTimestep(this.rainfallTimeSeries, startAll);
            int iPET = TimeSeriesValue.GetIndexForTimestep(this.evaporationTimeSeries, startAll);
            int iFlow = TimeSeriesValue.GetIndexForTimestep(inputFlowTimeSeries, startAll);

            int iEndRain = TimeSeriesValue.GetIndexForTimestep(this.rainfallTimeSeries, endAll) + 1;
            int iEndPET = TimeSeriesValue.GetIndexForTimestep(this.evaporationTimeSeries, endAll) + 1;
            int iEndFlow = TimeSeriesValue.GetIndexForTimestep(inputFlowTimeSeries, endAll) + 1;

            iEndRain = Math.Min(this.rainfallTimeSeries.Length, iEndRain);
            iEndPET = Math.Min(this.evaporationTimeSeries.Length, iEndPET);
            iEndFlow = Math.Min(inputFlowTimeSeries.Length, iEndFlow);

            List<TimeSeriesValue> rainList = new List<TimeSeriesValue>();
            List<TimeSeriesValue> petList = new List<TimeSeriesValue>();
            List<TimeSeriesValue> flowList = new List<TimeSeriesValue>();

            for (; iRain < iEndRain && iPET < iEndPET && iFlow < iEndFlow; ++iRain, ++iPET, ++iFlow)
            {
                rainList.Add(this.rainfallTimeSeries[iRain].DeepCopy());
                petList.Add(this.evaporationTimeSeries[iPET].DeepCopy());
                flowList.Add(inputFlowTimeSeries[iFlow].DeepCopy());
            }

            this.rainfallTimeSeries = rainList.ToArray();
            this.evaporationTimeSeries = petList.ToArray();
            this.flowTimeSeries = flowList.ToArray();

            // Set the modellingtimespan in the catchmentModel object using the input data
            bool doInputTimeSpansMatch = this.AreAllInputTimeSpansTheSame();

            if (doInputTimeSpansMatch && this.rainfallTimeSeries.Length >= 2)
            {
                StandardModellingTimeSpan rainfallTimeSpan = TimeSeriesValue.GetTimeStep(this.rainfallTimeSeries);
                this.catchmentModel.ModellingTimeSpan = new StandardModellingTimeSpan(rainfallTimeSpan.BaseTimeSpan, rainfallTimeSpan.NumberOfBaseTimeSpans);

                // doInputTimeSpansMatch = this.CheckDemandAndClimateTimeSeries(rodisSettings);

                return true;
            }
            else
            {
                if (!doInputTimeSpansMatch)
                {
                    throw new InvalidDataException(
                        "Reloaded flow time series has a different timestep to the existing rainfall and evaporation data. "
                        + "All input files must use the same timestep.");
                }
                else
                {
                    throw new InvalidDataException(
                        $"Insufficient overlapping data after reloading flow time series. "
                        + $"Only {this.rainfallTimeSeries.Length} timestep(s) in the common period. At least 2 are required.");
                }
            }
        }

        /// <summary>
        /// Checks the period of input and time span for all time series demands to make sure they are consistent with the input climate data.
        /// </summary>
        /// <param name="rodisSettings">RODIS settings, which contain the time series demand input groups.</param>
        /// <returns>Returns true if modelling time spans for all time series demand inputs (if any) and climate data inputs are the same.
        /// Also returns true if there are no time series demands, just repeating monthly demand time series.</returns>
        public bool CheckDemandAndClimateTimeSeries(RODISSettings rodisSettings)
        {
            bool result = true;

            if (rodisSettings.TimeSeriesDemandGroups != null)
            {
                foreach (var group in rodisSettings.TimeSeriesDemandGroups)
                {
                    DateTime demandStart = TimeSeriesValue.GetStartDateTimeValid(group.Value.InputPattern);
                    DateTime demandEnd = TimeSeriesValue.GetEndDateTimeValid(group.Value.InputPattern);
                    string demandStartString = demandStart.ToString("dd/MM/yyyy");
                    string demandEndString = demandEnd.ToString("dd/MM/yyyy");

                    if (this.rainfallTimeSeries != null)
                    {
                        DateTime rainStart = TimeSeriesValue.GetStartDateTimeValid(this.rainfallTimeSeries);
                        DateTime rainEnd = TimeSeriesValue.GetEndDateTimeValid(this.rainfallTimeSeries);

                        if (demandStart > rainStart)
                        {
                            // Throw an exception
                            result = false;
                            throw new InvalidDataException($"Time series demand in file {group.Value.InputFilePath} starts at {demandStartString}, which is after the other data inputs. Please extend the demand time series to be consistent with the rainfall, evaporation and flow inputs.");
                        }
                        else
                        {
                            if (demandEnd < rainEnd)
                            {
                                // Throw an exception
                                result = false;
                                throw new InvalidDataException($"Time series demand in file {group.Value.InputFilePath} ends at {demandEndString}, which is before the other data inputs. Please extend the demand time series to be consistent with the rainfall, evaporation and flow inputs.");
                            }
                            else
                            {
                                StandardModellingTimeSpan demandTimeSpan = TimeSeriesValue.GetTimeStep(group.Value.InputPattern);
                                StandardModellingTimeSpan rainfallTimeSpan = TimeSeriesValue.GetTimeStep(this.rainfallTimeSeries);

                                if (demandTimeSpan != rainfallTimeSpan)
                                {
                                    // Throw an exception
                                    result = false;
                                    throw new InvalidDataException($"Time series demand in file {group.Value.InputFilePath} has a different time step to the other inputs. Please check the time step for all input time series.");
                                }
                                else
                                {
                                    if (demandStart < rainStart || demandEnd > rainEnd)
                                    {
                                        // Reset the patterns in the demand time series to be the same duration as the climate input
                                        List<TimeSeriesValue> clippedPattern = new List<TimeSeriesValue>();

                                        for (DateTime dateTime = rainStart; dateTime <= rainEnd; dateTime = dateTime.Add(demandTimeSpan.GetAsTimeSpan()))
                                        {
                                            int i = TimeSeriesValue.GetIndexForTimestep(group.Value.InputPattern, dateTime);
                                            TimeSeriesValue tsv = group.Value.InputPattern[i].DeepCopy();
                                            clippedPattern.Add(tsv);
                                        }

                                        group.Value.InputPattern = clippedPattern.ToArray();
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        result = false;
                        throw new InvalidOperationException(
                            "Cannot validate demand time series because rainfall data has not been loaded. "
                            + "Ensure LoadInputTimeSeries is called before CheckDemandAndClimateTimeSeries.");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets an array of the maximum volumes of each of the water bodies in ML as an array, listed in order of calculation in the model.
        /// </summary>
        /// <returns>Array of the maximum volumes of each of the water bodies in ML as an array, listed in order of calculation in the model.</returns>
        public double[] GetMaxWaterBodyStorageVolumes()
        {
            double[] result = new double[this.catchmentModel.WaterBodyNodes.Length];

            for (int i = 0; i < this.catchmentModel.WaterBodyNodes.Length; ++i)
            {
                result[i] = this.catchmentModel.WaterBodyNodes[i].MaxStorageCapacityVolumeAtSpill;
            }

            return result;
        }

        /// <summary>Returns an array of surface area at spill (m²) for all water body nodes, in model order.</summary>
        public double[] GetSurfaceAreasAtSpill()
        {
            double[] surfaceAreas = new double[this.catchmentModel.WaterBodyNodes.Length];
            for (int i = 0; i < surfaceAreas.Length; i++)
            {
                surfaceAreas[i] = this.catchmentModel.WaterBodyNodes[i].SurfaceAreaAtSpill;
            }

            return surfaceAreas;
        }

        /// <summary>
        /// Gets an array of the start dates of each of the water bodies as an array, listed in order of calculation in the model.
        /// </summary>
        /// <returns>Array of the start dates of each of the water bodies as an array, listed in order of calculation in the model.</returns>
        public DateTime[] GetWaterBodyStartDates()
        {
            DateTime[] dates = new DateTime[this.catchmentModel.WaterBodyNodes.Length];

            for (int i = 0; i < this.catchmentModel.WaterBodyNodes.Length; ++i)
            {
                DateTime startDate = this.catchmentModel.WaterBodyNodes[i].StartDate;
                dates[i] = startDate;
            }

            return dates;
        }

        /// <summary>
        /// Gets an array of the end dates of each of the water bodies as an array, listed in order of calculation in the model.
        /// </summary>
        /// <returns>Array of the end dates of each of the water bodies as an array, listed in order of calculation in the model.</returns>
        public DateTime[] GetWaterBodyEndDates()
        {
            DateTime[] dates = new DateTime[this.catchmentModel.WaterBodyNodes.Length];

            for (int i = 0; i < this.catchmentModel.WaterBodyNodes.Length; ++i)
            {
                DateTime endDate = this.catchmentModel.WaterBodyNodes[i].EndDate;
                StandardModellingTimeSpan timeSpan = TimeSeriesValue.GetTimeStep(this.rainfallTimeSeries);
                endDate = endDate.Add(timeSpan.GetAsTimeSpan());
                dates[i] = endDate;
            }

            return dates;
        }

        /// <summary>
        /// Gets an array of the impacted flows in ML/d at the outlet of the model.
        /// </summary>
        /// <returns>Array of time series values for the impacted flows in ML/d at the outlet of the model.</returns>
        public TimeSeriesValue[] GetImpactedOutletFlow()
        {
            List<TimeSeriesValue> result = new List<TimeSeriesValue>();

            if (this.impactedFlowTimeSeries != null)
            {
                for (int i = 0; i < this.impactedFlowTimeSeries.Length; ++i) 
                {
                    TimeSeriesValue tsValue = this.impactedFlowTimeSeries[i].DeepCopy();
                    result.Add(tsValue);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Gets an array of the unimpacted flows in ML/d at the outlet of the model.
        /// </summary>
        /// <returns>Array of time series values for the unimpacted flows in ML/d at the outlet of the model.</returns>
        public TimeSeriesValue[] GetUnimpactedOutletFlow()
        {
            List<TimeSeriesValue> result = new List<TimeSeriesValue>();

            if (this.unimpactedFlowTimeSeries != null)
            {
                for (int i = 0; i < this.unimpactedFlowTimeSeries.Length; ++i)
                {
                    TimeSeriesValue tsValue = this.unimpactedFlowTimeSeries[i].DeepCopy();
                    result.Add(tsValue);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Returns a time series of total storage volume applicable at each time step of the model run.
        /// </summary>
        /// <returns>Time series of total storage volume applicable at each time step of the model run.</returns>
        public TimeSeriesValue[] GetTotalStorageVolume()
        {
            List<TimeSeriesValue> totalVolumeML = new List<TimeSeriesValue>();

            for (int i = 0; i < this.rainfallTimeSeries.Length; ++i)
            {
                TimeSeriesValue timeSeriesValue = new TimeSeriesValue()
                {
                    Time = this.rainfallTimeSeries[i].Time,
                    Value = 0.0,
                    IsValid = true,
                };

                if (this.catchmentModel != null)
                {
                    if (this.catchmentModel.WaterBodyNodes != null)
                    {
                        for (int j = 0; j < this.catchmentModel.WaterBodyNodes.Length; ++j)
                        {
                            if (rainfallTimeSeries[i].Time >= this.catchmentModel.WaterBodyNodes[j].StartDate && rainfallTimeSeries[i].Time < this.catchmentModel.WaterBodyNodes[j].EndDate)
                            {
                                timeSeriesValue.Value += this.catchmentModel.WaterBodyNodes[j].MaxStorageCapacityVolumeAtSpill;
                            }
                        }
                    }

                    totalVolumeML.Add(timeSeriesValue);
                }
            }

            return totalVolumeML.ToArray();
        }

        /// <summary>Appends a single calculated output value to a time series.</summary>
        /// <param name="outputs">Target time series to append to.</param>
        /// <param name="simulationDateTime">Date and time of the current simulation time step.</param>
        /// <param name="outputValue">Calculated output value to append.</param>
        private void AppendCalculatedOutput(TimeSeriesWithMetadata outputs, DateTime simulationDateTime, double outputValue)
        {
            TimeSeriesValue timeSeriesValue = new TimeSeriesValue()
            {
                Time = simulationDateTime,
                Value = outputValue,
                IsValid = true,
            };

            outputs.Data.Add(timeSeriesValue);
        }

        /// <summary>Configures overall and per-reporting-group output time series lists with metadata from RODIS settings.</summary>
        /// <param name="rodisSettings">RODIS settings providing run name, scenario name, and outlet details.</param>
        public void SetOutputTimeSeriesDetails(RODISSettings rodisSettings)
        {
            string[] reportingGroups = this.catchmentModel.GetReportingGroups();

            TimeSeriesWithMetadata templateTimeSeries = new TimeSeriesWithMetadata()
            {
                Units = "ML.day^-1",
                RunName = rodisSettings.RunName,
                ScenarioName = rodisSettings.ScenarioName,
                ScenarioInputSetName = string.Empty,
                Site = rodisSettings.GetOutletNumberStreamName(),
                ElementName = "Downstream Flow",
                WaterFeatureType = "Confluence",
                ElementType = "Node",
                Structure = "Downstream Flow",
                ModellingTimeSpan = new StandardModellingTimeSpan(this.catchmentModel.ModellingTimeSpan.BaseTimeSpan, this.catchmentModel.ModellingTimeSpan.NumberOfBaseTimeSpans),
            };

            for (int i = 0; i < this.GetOutputNames().Length; i++)
            {
                this.OverallOutputTimeSeries.Add(templateTimeSeries.DeepCopy());
                this.OverallOutputTimeSeries[i].Structure = this.GetOutputNames()[i];
                this.OverallOutputTimeSeries[i].ElementName = this.GetOutputNames()[i];
                this.OverallOutputTimeSeries[i].Units = this.OutputNamesAndUnits[this.GetOutputNames()[i]];
            }

            for (int iRG = 0; iRG < reportingGroups.Length; ++iRG)
            {
                this.GroupOutputTimeSeries.Add(new List<TimeSeriesWithMetadata>());

                for (int i = 0; i < this.GetOutputNames().Length; i++)
                {
                    this.GroupOutputTimeSeries[iRG].Add(templateTimeSeries.DeepCopy());

                    this.GroupOutputTimeSeries[iRG][i].Structure = this.GetOutputNames()[i];
                    this.GroupOutputTimeSeries[iRG][i].ElementName = this.GetOutputNames()[i];
                    this.GroupOutputTimeSeries[iRG][i].Units = this.OutputNamesAndUnits[this.GetOutputNames()[i]];
                }
            }
        }

        /// <summary>
        /// Run the model for all valid input data.
        /// </summary>
        public void RunAll()
        {
            if (this.rainfallTimeSeries == null || this.rainfallTimeSeries.Length == 0)
            {
                throw new InvalidOperationException(
                    "Cannot run simulation: no rainfall time series data has been loaded. "
                    + "Ensure LoadInputTimeSeries completes successfully before calling RunAll.");
            }

            DateTime start = this.rainfallTimeSeries.First().Time;
            DateTime end = this.rainfallTimeSeries.Last().Time;
            this.Run(start, end);
        }

        /// <summary>
        /// Run the model from the start to the end date times specified.
        /// </summary>
        /// <param name="start">Start of run.</param>
        /// <param name="end">End of run.</param>
        public void Run(DateTime start, DateTime end)
        {
            // Clear the memory of output time series before the start of the run
            for (int i = 0; i < OverallOutputTimeSeries.Count; ++i)
            {
                this.OverallOutputTimeSeries[i].Data.Clear();
            }

            for (int i = 0; i < GroupOutputTimeSeries.Count; ++i)
            {
                for (int j = 0; j < GroupOutputTimeSeries[i].Count; ++j)
                {
                    this.GroupOutputTimeSeries[i][j].Data.Clear();
                }
            }

            this.catchmentModel.ResetMassBalanceAccumulators();

            string[] reportingGroups = this.catchmentModel.GetReportingGroups();

            int iTS = TimeSeriesValue.GetIndexForTimestep(this.rainfallTimeSeries, start);
            int endTS = TimeSeriesValue.GetIndexForTimestep(this.rainfallTimeSeries, end) + 1;

            DateTime demandPatternStartDate = this.catchmentModel.GetStartTimeSeriesDemandPatterns();
            DateTime demandPatternEndDate = this.catchmentModel.GetEndTimeSeriesDemandPatterns();

            if (demandPatternStartDate > start)
            {
                iTS = TimeSeriesValue.GetIndexForTimestep(this.rainfallTimeSeries, demandPatternStartDate);
            }

            if (demandPatternEndDate < end)
            {
                endTS = TimeSeriesValue.GetIndexForTimestep(this.rainfallTimeSeries, demandPatternEndDate) + 1;
            }

            endTS = Math.Min(endTS, this.rainfallTimeSeries.Length);

            List<TimeSeriesValue> impactedFlow = new List<TimeSeriesValue>();
            List<TimeSeriesValue> unimpactedFlow = new List<TimeSeriesValue>();

            const int numTimestepsForProgressUpdate = 1000;
            int iTSStart = iTS;

            for (; iTS < endTS; ++iTS)
            {
                if ((iTS - iTSStart) % numTimestepsForProgressUpdate == 0)
                {
                    Console.Write(".");
                }

                this.catchmentModel.SimulationDateTime = this.rainfallTimeSeries[iTS].Time;

                try
                {
                    if (this.rainfallTimeSeries[iTS].IsValid && this.evaporationTimeSeries[iTS].IsValid && this.flowTimeSeries[iTS].IsValid)
                    {
                        // CatchmentModelRunner reads raw data; CatchmentModel owns the scaling logic
                        this.catchmentModel.Rainfall = this.rainfallTimeSeries[iTS].Value;
                        this.catchmentModel.Evaporation = this.evaporationTimeSeries[iTS].Value;

                        if (this.catchmentModel.CalculateUnimpactedGivenObserved)
                        {
                            this.catchmentModel.ObservedDownstreamFlow = this.flowTimeSeries[iTS].Value;
                        }
                        else
                        {
                            this.catchmentModel.UnimpactedFlow = this.flowTimeSeries[iTS].Value;
                        }

                        this.catchmentModel.RunTimeStep();

                        TimeSeriesValue flowOutput = new TimeSeriesValue()
                        {
                            Time = this.catchmentModel.SimulationDateTime,
                            Value = this.catchmentModel.DownstreamFlow,
                            IsValid = true,
                        };
                        impactedFlow.Add(flowOutput);

                        TimeSeriesValue unimpacted = flowOutput.DeepCopy();
                        unimpacted.Value = this.catchmentModel.UnimpactedFlow;
                        unimpactedFlow.Add(unimpacted);

                        // Append overall results for model to lists
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[0], this.catchmentModel.SimulationDateTime, this.catchmentModel.NetImpactOnFlow);
                        if (this.catchmentModel.NetImpactOnFlow >= 0)
                        {
                            this.AppendCalculatedOutput(this.OverallOutputTimeSeries[1], this.catchmentModel.SimulationDateTime, this.catchmentModel.NetImpactOnFlow);
                            this.AppendCalculatedOutput(this.OverallOutputTimeSeries[2], this.catchmentModel.SimulationDateTime, 0.0);
                        }
                        else
                        {
                            this.AppendCalculatedOutput(this.OverallOutputTimeSeries[1], this.catchmentModel.SimulationDateTime, 0.0);
                            this.AppendCalculatedOutput(this.OverallOutputTimeSeries[2], this.catchmentModel.SimulationDateTime, -this.catchmentModel.NetImpactOnFlow);
                        }

                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[3], this.catchmentModel.SimulationDateTime, this.catchmentModel.UnimpactedFlow);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[4], this.catchmentModel.SimulationDateTime, this.catchmentModel.PumpedInflow);

                        if (this.catchmentModel.IsLegacyRODISCalculationMethods)
                        {
                            this.AppendCalculatedOutput(this.OverallOutputTimeSeries[5], this.catchmentModel.SimulationDateTime, -this.catchmentModel.NetRainfallVolume);
                        }
                        else
                        {
                            this.AppendCalculatedOutput(this.OverallOutputTimeSeries[5], this.catchmentModel.SimulationDateTime, this.catchmentModel.NetRainfallVolume);
                        }

                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[6], this.catchmentModel.SimulationDateTime, this.catchmentModel.DemandVolumeExtracted);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[7], this.catchmentModel.SimulationDateTime, this.catchmentModel.DownstreamFlowFromSpill);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[8], this.catchmentModel.SimulationDateTime, this.catchmentModel.ChangeInVolumeInStorage);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[9], this.catchmentModel.SimulationDateTime, this.catchmentModel.VolumeInStorage);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[10], this.catchmentModel.SimulationDateTime, this.catchmentModel.DownstreamFlowFromBypass);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[11], this.catchmentModel.SimulationDateTime, this.catchmentModel.DownstreamFlowFromCatchment);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[12], this.catchmentModel.SimulationDateTime, this.catchmentModel.DownstreamFlow);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[13], this.catchmentModel.SimulationDateTime, this.catchmentModel.StorageCapacityVolumeAtSpill);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[14], this.catchmentModel.SimulationDateTime, this.catchmentModel.VolumeBalanceMisclosure);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[15], this.catchmentModel.SimulationDateTime, this.catchmentModel.SeepageLossVolume);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[16], this.catchmentModel.SimulationDateTime, this.catchmentModel.SeepageLossRateAtFull);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[17], this.catchmentModel.SimulationDateTime, this.catchmentModel.BypassFlowCapacity);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[18], this.catchmentModel.SimulationDateTime, this.catchmentModel.PumpedInflowCapacity);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[19], this.catchmentModel.SimulationDateTime, this.catchmentModel.TopDownVolumeBalanceMisclosure);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[20], this.catchmentModel.SimulationDateTime, this.catchmentModel.TotalSubcatchmentRunoff);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[21], this.catchmentModel.SimulationDateTime, this.catchmentModel.DamRemovalStorageLoss);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[22], this.catchmentModel.SimulationDateTime, this.catchmentModel.CumulativeBottomUpMisclosure);
                        this.AppendCalculatedOutput(this.OverallOutputTimeSeries[23], this.catchmentModel.SimulationDateTime, this.catchmentModel.CumulativeTopDownMisclosure);

                        // Append summary results by reporting groups to lists
                        for (int iRG = 0; iRG < reportingGroups.Length; iRG++)
                        {
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][0], this.catchmentModel.SimulationDateTime, this.catchmentModel.NetImpactOnFlowByReportingGroup[iRG]);
                            if (this.catchmentModel.NetImpactOnFlowByReportingGroup[iRG] >= 0)
                            {
                                this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][1], this.catchmentModel.SimulationDateTime, this.catchmentModel.NetImpactOnFlowByReportingGroup[iRG]);
                                this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][2], this.catchmentModel.SimulationDateTime, 0.0);
                            }
                            else
                            {
                                this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][1], this.catchmentModel.SimulationDateTime, 0.0);
                                this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][2], this.catchmentModel.SimulationDateTime, -this.catchmentModel.NetImpactOnFlowByReportingGroup[iRG]);
                            }

                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][3], this.catchmentModel.SimulationDateTime, this.catchmentModel.UnimpactedFlowByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][4], this.catchmentModel.SimulationDateTime, this.catchmentModel.PumpedInflowByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][5], this.catchmentModel.SimulationDateTime, this.catchmentModel.NetRainfallVolumeByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][6], this.catchmentModel.SimulationDateTime, this.catchmentModel.DemandVolumeExtractedByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][7], this.catchmentModel.SimulationDateTime, this.catchmentModel.SpillDownstreamFlowByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][8], this.catchmentModel.SimulationDateTime, this.catchmentModel.ChangeInVolumeInStorageByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][9], this.catchmentModel.SimulationDateTime, this.catchmentModel.VolumeInStorageByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][10], this.catchmentModel.SimulationDateTime, this.catchmentModel.BypassDownstreamFlowByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][11], this.catchmentModel.SimulationDateTime, this.catchmentModel.LocalCatchmentInflowByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][12], this.catchmentModel.SimulationDateTime, this.catchmentModel.DownstreamFlowByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][13], this.catchmentModel.SimulationDateTime, this.catchmentModel.StorageCapacityVolumeAtSpillByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][14], this.catchmentModel.SimulationDateTime, this.catchmentModel.VolumeBalanceMisclosureByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][15], this.catchmentModel.SimulationDateTime, this.catchmentModel.SeepageVolumeByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][16], this.catchmentModel.SimulationDateTime, this.catchmentModel.SeepageLossRateAtFullByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][17], this.catchmentModel.SimulationDateTime, this.catchmentModel.BypassFlowCapacityByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][18], this.catchmentModel.SimulationDateTime, this.catchmentModel.PumpedInflowCapacityByReportingGroup[iRG]);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][19], this.catchmentModel.SimulationDateTime, 0.0);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][20], this.catchmentModel.SimulationDateTime, 0.0);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][21], this.catchmentModel.SimulationDateTime, 0.0);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][22], this.catchmentModel.SimulationDateTime, 0.0);
                            this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][23], this.catchmentModel.SimulationDateTime, 0.0);
                        }
                    }
                    else
                    {
                        // Set all outputs to invalid data
                        for (int j = 0; j < this.OverallOutputTimeSeries.Count; ++j)
                        {
                            this.AppendCalculatedOutput(this.OverallOutputTimeSeries[j], this.catchmentModel.SimulationDateTime, 0);
                            this.OverallOutputTimeSeries[j].Data.Last().IsValid = false;
                        }

                        for (int iRG = 0; iRG < reportingGroups.Length; iRG++)
                        {
                            for (int j = 0; j < this.GroupOutputTimeSeries[iRG].Count; ++j)
                            {
                                this.AppendCalculatedOutput(this.GroupOutputTimeSeries[iRG][j], this.catchmentModel.SimulationDateTime, 0);
                                this.GroupOutputTimeSeries[iRG][j].Data.Last().IsValid = false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Simulation failed at timestep {this.catchmentModel.SimulationDateTime:dd/MM/yyyy} "
                        + $"(index {iTS} of {endTS}). {ex.Message}", ex);
                }
            }

            //Console.WriteLine();

            this.catchmentModel.WriteMassBalanceSummary();

            this.impactedFlowTimeSeries = impactedFlow.ToArray();
            this.unimpactedFlowTimeSeries = unimpactedFlow.ToArray();
        }

        /// <summary>
        /// Resets the start and end dates for all water body nodes to the start and end dates for the base scenario.
        /// </summary>
        public void ResetToBaseStartEndDates()
        {
            for (int i = 0; i < this.catchmentModel.WaterBodyNodes.Count(); ++i)
            {
                this.catchmentModel.WaterBodyNodes[i].ResetToBaseStartEndDates();
            }
        }

        /// <summary>Returns an array of year numbers that are run, as doubles — always spans past the start and end dates of the run.</summary>
        /// <returns>Array of year numbers as doubles, from one year before run start to one year after run end.</returns>
        public double[] GetYearsToRun()
        {
            List<double> yearsAsDouble = new List<double>();

            DateTime startRun = this.GetStartRun();
            DateTime endRun = this.GetEndRun();

            for (int year = startRun.Year - 1; year <= (endRun.Year + 1); ++year)
            {
                yearsAsDouble.Add((double) year);
            }

            return yearsAsDouble.ToArray();
        }

        /// <summary>
        /// Returns a list of timeserieswithmetadata aggregated to full water years, starting at the start date.
        /// </summary>
        /// <param name="inputTS">List of input time series data.</param>
        /// <param name="WaterYearsStartIgnoreYear">Start date of each water year, ignores the year and uses month and day only.</param>
        /// <returns>List of time series with meta data at water year time step. Dates are the start of each water year.</returns>
        public List<TimeSeriesWithMetadata> GetWaterYearOutputTimeSeries(List<TimeSeriesWithMetadata> inputTS, DateTime WaterYearsStartIgnoreYear)
        {
            List<TimeSeriesWithMetadata> result = new List<TimeSeriesWithMetadata> ();

            // Clear the memory of output time series before the start of the run
            if (inputTS != null)
            {
                for (int i = 0; i < inputTS.Count; ++i)
                {
                    switch (inputTS[i].ElementName)
                    {
                        case "Storage Volume End of Timestep":
                        case "Storage Capacity at Full Supply":
                        case "Bypass Flow Capacity":
                        case "Pumped Inflow Capacity":
                        case "Cumulative Bottom-Up Misclosure":
                        case "Cumulative Top-Down Misclosure":
                            result.Add(this.OverallOutputTimeSeries[i].EndOfWaterYears(WaterYearsStartIgnoreYear));
                            break;

                        default:
                            result.Add(this.OverallOutputTimeSeries[i].AggregateToWaterYears(WaterYearsStartIgnoreYear));
                            break;
                    }
                }
            }

            return result;
        }

        /// <summary>Calculates mean and mean annual values for all overall and per-group output time series, within the statistics date range.</summary>
        public void CalculateMeanAndMeanAnnualValues ()
        {
            this.GroupOutputMeanValues = new double[GroupOutputTimeSeries.Count][];
            this.GroupOutputMeanAnnualValues = new double[GroupOutputTimeSeries.Count][];

            this.OverallOutputMeanValues = this.GetMeanValues(OverallOutputTimeSeries);
            this.OverallOutputMeanAnnualValues = this.GetMeanAnnualValues(OverallOutputTimeSeries);

            for (int iGrp = 0; iGrp < this.GroupOutputTimeSeries.Count; ++iGrp)
            {
                this.GroupOutputMeanValues[iGrp] = this.GetMeanValues(this.GroupOutputTimeSeries[iGrp]);
                this.GroupOutputMeanAnnualValues[iGrp] = this.GetMeanAnnualValues(this.GroupOutputTimeSeries[iGrp]);
            }

            // Several outputs don't make sense to aggregate the mean annual value from mean values but to just use the straight mean values
            // Therefore, this loop re-sets the mean annual values for those variables to the calculated mean value from the run
            for (int iOutput = 0; 
                iOutput < Math.Min(Math.Min(this.GetOutputNames().Length, this.OverallOutputMeanValues.Length), this.OverallOutputMeanAnnualValues.Length);
                ++iOutput)
            {
                switch (this.GetOutputNames()[iOutput])
                {
                    case "Storage Volume End of Timestep":
                    case "Storage Capacity at Full Supply":
                    case "Bypass Flow Capacity":
                    case "Pumped Inflow Capacity":
                    case "Cumulative Bottom-Up Misclosure":
                    case "Cumulative Top-Down Misclosure":
                        this.OverallOutputMeanAnnualValues[iOutput] = this.OverallOutputMeanValues[iOutput];
                        for (int iGrp = 0; iGrp < this.GroupOutputTimeSeries.Count; ++iGrp)
                        {
                            this.GroupOutputMeanAnnualValues[iGrp][iOutput] = this.GroupOutputMeanValues[iGrp][iOutput];
                        }
                        break;

                }
            }
        }

        /// <summary>Calculates mean values for each time series in the list, within the statistics date range.</summary>
        /// <param name="inputTS">List of output time series to compute means for.</param>
        /// <returns>Array of mean values, one per time series.</returns>
        private double[] GetMeanValues(List<TimeSeriesWithMetadata> inputTS)
        {
            // TO DO clean up start and end of water years

            double[] meanValues = new double[inputTS.Count];

            for (int i = 0; i < inputTS.Count; ++i)
            {
                OutputType outputType = OutputType.Total;

                StandardVolumeFlowUnit volumeFlowUnit = new StandardVolumeFlowUnit();
                bool isRate = StandardVolumeFlow.TryParseUnit(inputTS[i].Units, out volumeFlowUnit);

                if (isRate)
                {
                    outputType = OutputType.Rate;
                }
                else
                {
                    outputType = OutputType.Total;
                }

                    //switch (inputTS[i].ElementName)
                    //{
                    //    case "Bypass Flow Capacity":
                    //    case "Pumped Inflow Capacity":
                    //        outputType = OutputType.Rate;
                    //        break;

                    //    default:
                    //        outputType = OutputType.Total;
                    //        break;
                    //}

                meanValues[i] = inputTS[i].GetMeanValidValue(this.StartDateForStatistics, this.EndDateForStatistics, outputType);
            }

            return meanValues;
        }

        /// <summary>Calculates mean annual values for each time series in the list, within the statistics date range.</summary>
        /// <param name="inputTS">List of output time series to compute mean annual values for.</param>
        /// <returns>Array of mean annual values, one per time series.</returns>
        private double[] GetMeanAnnualValues(List<TimeSeriesWithMetadata> inputTS)
        {
            // TO DO clean up start and end of water years

            double[] meanValues = new double[inputTS.Count];

            for (int i = 0; i < inputTS.Count; ++i)
            {
                OutputType outputType = OutputType.Total;

                switch (inputTS[i].ElementName)
                {
                    case "Bypass Flow Capacity":
                    case "Pumped Inflow Capacity":
                        outputType = OutputType.Rate;
                        break;

                    default:
                        outputType = OutputType.Total;
                        break;
                }

                meanValues[i] = inputTS[i].GetMeanAnnualValidValue(this.StartDateForStatistics, this.EndDateForStatistics, outputType);
            }

            return meanValues;
        }
    }
}
