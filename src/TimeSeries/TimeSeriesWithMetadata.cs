// <copyright file="TimeSeriesWithMetadata.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.Series
{
    using STEDI.InputOutput;
    using STEDI.TimeSeries;

    /// <summary>A time series with associated metadata (units, site, scenario, etc.) for res.csv output and aggregation.</summary>
    public class TimeSeriesWithMetadata
    {
        /// <summary>
        /// Gets or sets a Source metadata item for the units.
        /// </summary>
        public string Units { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a Source metadata item for the run name.
        /// </summary>
        public string RunName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a Source metadata item for the scenario name.
        /// </summary>
        public string ScenarioName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a Source metadata item for the scenario input set name.
        /// </summary>
        public string ScenarioInputSetName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a Source metadata item for the site name.
        /// </summary>
        public string Site { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a Source metadata item for the element name.
        /// </summary>
        public string ElementName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a Source metadata item for the water feature type.
        /// </summary>
        public string WaterFeatureType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a Source metadata item for the element type.
        /// </summary>
        public string ElementType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a Source metadata item for the structure of this output in Source.
        /// </summary>
        public string Structure { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a random 32 character string.
        /// </summary>
        public string Custom { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the standard modelling time span, as a multiplier of standard increments (daily, weekly, monthly etc.).
        /// </summary>
        public StandardModellingTimeSpan ModellingTimeSpan { get; set; } = new StandardModellingTimeSpan(BaseModellingTimeSpan.Daily, 1.0);

        /// <summary>
        /// Gets or sets the list of the stored data.
        /// </summary>
        public List<TimeSeriesValue> Data { get; set; } = new List<TimeSeriesValue>();

        /// <summary>
        /// Returns a time series value from the time series at the first time step at or after the specified time.
        /// </summary>
        /// <param name="dateTime">Specified datetime to read.</param>
        /// <returns>Time series value.</returns>
        public TimeSeriesValue GetValueAt (DateTime dateTime)
        {
            TimeSeriesValue value = null;

            TimeSeriesValue[] dataArray = this.Data.ToArray();

            int foundIndex = TimeSeriesValue.GetIndexForTimestep(dataArray, dateTime);

            if (foundIndex >= 0)
            {
                value = dataArray[foundIndex].DeepCopy();
            }

            return value;
        }

        /// <summary>
        /// Gets a Dictionary containing the meta data items from the class.
        /// </summary>
        /// <returns>Dictionary containing the meta data items from the class.</returns>
        public Dictionary<string, string> GetMetadata()
        {
            Dictionary<string, string> result = new Dictionary<string, string>()
            {
                ["Units"] = this.Units,
                ["RunName"] = this.RunName,
                ["ScenarioName"] = this.ScenarioName,
                ["ScenarioInputSetName"] = this.ScenarioInputSetName,
                ["Name"] = ReadWriteResCSV.GetMergedNameString(this.Site, this.WaterFeatureType, this.Structure),
                ["Site"] = this.Site,
                ["ElementName"] = this.ElementName,
                ["WaterFeatureType"] = this.WaterFeatureType,
                ["ElementType"] = this.ElementType,
                ["Structure"] = this.Structure,
                ["Custom"] = this.Custom,
            };

            return result;
        }

        /// <summary>Populates metadata properties from a dictionary (inverse of <see cref="GetMetadata"/>).</summary>
        /// <param name="metaData">Dictionary of metadata key-value pairs.</param>
        public void SetMetaData(Dictionary<string, string> metaData)
        {
            this.Units = metaData["Units"];
            this.RunName = metaData["RunName"];
            this.ScenarioName = metaData["ScenarioName"];
            this.ScenarioInputSetName = metaData["ScenarioInputSetName"];
            this.Site = metaData["Site"];
            this.ElementName = metaData["ElementName"];
            this.WaterFeatureType = metaData["WaterFeatureType"];
            this.ElementType = metaData["ElementType"];
            this.Structure = metaData["Structure"];
            this.Custom = metaData["Custom"];
        }

        /// <summary>
        /// Returns a deep or complete copy of the class.
        /// </summary>
        /// <returns>Complete copy of the class.</returns>
        public TimeSeriesWithMetadata DeepCopy ()
        {
            TimeSeriesWithMetadata result = this.CopyMetadataWithBlankData();

            foreach (TimeSeriesValue value in this.Data)
            {
                result.Data.Add(value.DeepCopy());
            }

            return result;
        }

        /// <summary>
        /// Returns copy of metadata for item but containing no data.
        /// </summary>
        /// <returns>Copy of metadata for item but containing no data.</returns>
        public TimeSeriesWithMetadata CopyMetadataWithBlankData ()
        {
            TimeSeriesWithMetadata result = new TimeSeriesWithMetadata()
            {
                Units = this.Units,
                RunName = this.RunName,
                ScenarioName = this.ScenarioName,
                ScenarioInputSetName = this.ScenarioInputSetName,
                Site = this.Site,
                ElementName = this.ElementName,
                WaterFeatureType = this.WaterFeatureType,
                ElementType = this.ElementType,
                Structure = this.Structure,
                Custom = this.Custom,
                ModellingTimeSpan = new StandardModellingTimeSpan(this.ModellingTimeSpan.BaseTimeSpan, this.ModellingTimeSpan.NumberOfBaseTimeSpans),
                Data = new List<TimeSeriesValue>(),
            };

            return result;
        }

        /// <summary>
        /// Produces a series of data aggregated to the nominated modelling time span.
        /// </summary>
        /// <param name="timeSpanToAggregate">Modelling time span for aggregation.</param>
        /// <returns>Time series of data aggregated to the nominated modelling time span.</returns>
        public TimeSeriesWithMetadata AggregateNonOverlapping(StandardModellingTimeSpan timeSpanToAggregate, OutputType inputDataType = OutputType.Total)
        {
            this.ThrowIfDataEmpty(nameof(this.AggregateNonOverlapping));

            DateTime startAggregationDate = this.Data.First().Time;

            return this.AggregateNonOverlapping(timeSpanToAggregate, startAggregationDate, inputDataType);
        }

        /// <summary>
        /// Returns an annual (water year) time series summing all of the valid values in each water year.
        /// </summary>
        /// <param name="startOfWaterYearIgnoreYear">Start month and day of each water year, ignores the year.</param>
        /// <returns>Annual (water year) time series aggregating all valid values in each water year.</returns>
        public TimeSeriesWithMetadata AggregateToWaterYears(
            DateTime startOfWaterYearIgnoreYear,
            OutputType inputDataType = OutputType.Total)
        {
            this.ThrowIfDataEmpty(nameof(this.AggregateToWaterYears));

            StandardModellingTimeSpan timeSpanToAggregate = new StandardModellingTimeSpan(BaseModellingTimeSpan.Monthly, 12);

            DateTime startDate = this.Data.First().Time;
            DateTime startAggregationDate = this.Data.First().Time;

            if (startDate.Month == startOfWaterYearIgnoreYear.Month && startDate.Day == startOfWaterYearIgnoreYear.Day)
            {
                return this.AggregateNonOverlapping(timeSpanToAggregate, startAggregationDate, inputDataType);
            } 
            else
            {
                startAggregationDate = new DateTime(startDate.Year, startOfWaterYearIgnoreYear.Month, startOfWaterYearIgnoreYear.Day);

                int foundIndex = TimeSeriesValue.GetIndexForTimestep(this.Data.ToArray(), startAggregationDate);

                if (foundIndex <= 0)
                {
                    startAggregationDate = new DateTime(startDate.Year + 1, startOfWaterYearIgnoreYear.Month, startOfWaterYearIgnoreYear.Day);
                }

                return this.AggregateNonOverlapping(timeSpanToAggregate, startAggregationDate, inputDataType);
            }
        }

        /// <summary>
        /// Returns an annual (water year) time series selecting the last value in each water year.
        /// </summary>
        /// <param name="startOfWaterYearIgnoreYear">Start month and day of each water year, ignores the year.</param>
        /// <returns>Annual (water year) time series selecting the last value in each water year.</returns>
        public TimeSeriesWithMetadata EndOfWaterYears(DateTime startOfWaterYearIgnoreYear)
        {
            this.ThrowIfDataEmpty(nameof(this.EndOfWaterYears));

            StandardModellingTimeSpan timeSpanToAggregate = new StandardModellingTimeSpan(BaseModellingTimeSpan.Monthly, 12);

            DateTime startDate = this.Data.First().Time;
            DateTime startAggregationDate = this.Data.First().Time;

            if (startDate.Month == startOfWaterYearIgnoreYear.Month && startDate.Day == startOfWaterYearIgnoreYear.Day)
            {
                return this.EndOfPeriodValues(timeSpanToAggregate, startAggregationDate);
            }
            else
            {
                startAggregationDate = new DateTime(startDate.Year, startOfWaterYearIgnoreYear.Month, startOfWaterYearIgnoreYear.Day);

                int foundIndex = TimeSeriesValue.GetIndexForTimestep(this.Data.ToArray(), startAggregationDate);

                if (foundIndex <= 0)
                {
                    startAggregationDate = new DateTime(startDate.Year + 1, startOfWaterYearIgnoreYear.Month, startOfWaterYearIgnoreYear.Day);
                }

                return this.EndOfPeriodValues(timeSpanToAggregate, startAggregationDate);
            }
        }

        /// <summary>
        /// Returns the mean of valid values, using all of the data.
        /// </summary>
        /// <param name="inputDataType">Type of input in time series.</param>
        /// <returns>Mean of valid values.</returns>
        public double GetMeanValidValue(OutputType inputDataType = OutputType.Total)
        {
            this.ThrowIfDataEmpty(nameof(this.GetMeanValidValue));

            DateTime startDate = this.Data.First().Time;
            DateTime endDate = this.Data.Last().Time;
            endDate = endDate.Add(this.ModellingTimeSpan.GetAsTimeSpan(endDate));

            return this.GetMeanValidValue(startDate, endDate, inputDataType);
        }

        /// <summary>
        /// Returns the mean annual of valid values, using all of the data.
        /// </summary>
        /// <param name="inputDataType">Type of input in time series.</param>
        /// <returns>Mean annual value of valid values.</returns>
        public double GetMeanAnnualValidValue(OutputType inputDataType = OutputType.Total)
        {
            this.ThrowIfDataEmpty(nameof(this.GetMeanAnnualValidValue));

            double meanPerTimestep = this.GetMeanValidValue(inputDataType);
            double daysPerTimestep = this.ModellingTimeSpan.GetAsTimeSpan().TotalDays;
            double timestepsPerYear = 365.25 / daysPerTimestep;

            if (inputDataType == OutputType.Rate)
                return meanPerTimestep * 365.25;
            else
                return meanPerTimestep * timestepsPerYear;
        }

        /// <summary>Returns the mean annual of valid values, using data between the specified start and end dates.</summary>
        /// <param name="startMeanDate">Start date for calculating mean values.</param>
        /// <param name="endMeanDate">End date for calculating mean values.</param>
        /// <param name="inputDataType">Type of input in time series.</param>
        /// <returns>Mean annual value of valid values.</returns>
        public double GetMeanAnnualValidValue(DateTime startMeanDate, DateTime endMeanDate, OutputType inputDataType = OutputType.Total)
        {
            double meanPerTimestep = this.GetMeanValidValue(startMeanDate, endMeanDate, inputDataType);
            double daysPerTimestep = this.ModellingTimeSpan.GetAsTimeSpan().TotalDays;
            double timestepsPerYear = 365.25 / daysPerTimestep;

            if (inputDataType == OutputType.Rate)
                return meanPerTimestep * 365.25;
            else
                return meanPerTimestep * timestepsPerYear;
        }

        /// <summary>
        /// Returns the mean of valid values, using data between the specified start and end dates.
        /// </summary>
        /// <param name="startMeanDate">Start date for calculating mean values. Must be on or after the first date in the series.</param>
        /// <param name="endMeanDate">End date for calculating mean values. Must be on or before the last date in the series.</param>
        /// <param name="inputDataType">Type of input in time series.</param>
        /// <returns>Mean of valid values.</returns>
        public double GetMeanValidValue(DateTime startMeanDate, DateTime endMeanDate, OutputType inputDataType = OutputType.Total)
        {
            double result = double.NaN;

            int foundIndex = TimeSeriesValue.GetIndexForTimestep(this.Data.ToArray(), startMeanDate);

            if (foundIndex >= 0 && foundIndex < this.Data.Count)
            {
                DateTime startAddDateTime = this.Data[foundIndex].Time;
                DateTime endAddDateTime = endMeanDate.Add(this.ModellingTimeSpan.GetAsTimeSpan(endMeanDate));

                double sumValidValues = 0;
                double sumValidDays = 0;
                int countValid = 0;
                int countInvalid = 0;

                foreach (TimeSeriesValue value in this.Data)
                {
                    if (value.Time >= startAddDateTime && value.Time < endAddDateTime)
                    {
                        if (value.IsValid)
                        {
                            double thisTSinDays = this.ModellingTimeSpan.GetAsTimeSpan(value.Time).TotalDays;
                            sumValidDays += thisTSinDays;

                            if (inputDataType == OutputType.Total)
                            {
                                sumValidValues += value.Value;
                            }
                            else
                            {
                                sumValidValues += value.Value * thisTSinDays;
                            }

                            ++countValid;
                        }
                        else
                        {
                            ++countInvalid;
                        }
                    }
                }

                if (countValid > 0)
                {
                    if (inputDataType == OutputType.Total)
                    {
                        result = sumValidValues / (double)countValid;
                    }
                    else
                    {
                        result = sumValidValues / sumValidDays;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns time series with metadata aggregated to specified modelling time span.
        /// </summary>
        /// <param name="timeSpanToAggregate">Modelling time span to aggregate.</param>
        /// <param name="startAggregationDate">Start of aggregation period.</param>
        /// <returns>Time series with metadata aggregated to specified modelling time span.</returns>
        public TimeSeriesWithMetadata AggregateNonOverlapping(
            StandardModellingTimeSpan timeSpanToAggregate,
            DateTime startAggregationDate,
            OutputType inputDataType = OutputType.Total)
        {
            TimeSeriesWithMetadata result = this.CopyMetadataWithBlankData();

            // Aggregation results in changing units from ML/d to ML
            const string PERDAY = ".day^-1";
            if (result.Units.Trim().EndsWith(PERDAY))
            {
                result.Units = result.Units.Trim().Substring(0, result.Units.Trim().Length - PERDAY.Length);
            }

            int foundIndex = TimeSeriesValue.GetIndexForTimestep(this.Data.ToArray(), startAggregationDate);

            if (foundIndex >= 0 && foundIndex < this.Data.Count)
            {
                DateTime startAddDateTime = this.Data[foundIndex].Time;
                DateTime endAddDateTime = startAddDateTime.Add(timeSpanToAggregate.GetAsTimeSpan(startAddDateTime));

                double sumValidValues = 0;
                double sumValidDays = 0;
                int countValid = 0;
                int countInvalid = 0;

                foreach (TimeSeriesValue value in this.Data)
                {
                    if (value.Time >= startAddDateTime) 
                    {
                        if (value.Time >= endAddDateTime)
                        {
                            TimeSeriesValue sumOut = new TimeSeriesValue()
                            {
                                Time = startAddDateTime,
                                Value = sumValidValues,
                            };

                            if (countValid > 0 && countInvalid == 0)
                            {
                                sumOut.IsValid = true;
                            }
                            else
                            {
                                sumOut.IsValid = false;
                            }

                            result.Data.Add(sumOut);

                            // Reset counters
                            sumValidValues = 0;
                            sumValidDays = 0;
                            countValid = 0;
                            countInvalid = 0;

                            // Reset period
                            startAddDateTime = value.Time;
                            endAddDateTime = startAddDateTime.Add(timeSpanToAggregate.GetAsTimeSpan(startAddDateTime));
                        }

                        if (value.IsValid)
                        {
                            double thisTSinDays = this.ModellingTimeSpan.GetAsTimeSpan(value.Time).TotalDays;
                            sumValidDays += thisTSinDays;

                            if (inputDataType == OutputType.Total)
                            {
                                sumValidValues += value.Value;
                            }
                            else
                            {
                                sumValidValues += value.Value * thisTSinDays;
                            }

                            ++countValid;
                        }
                        else
                        {
                            ++countInvalid;
                        }
                    }
                }

                if (this.Data.Last().Time.Add(this.ModellingTimeSpan.GetAsTimeSpan()) >= endAddDateTime && countValid > 0 && countInvalid == 0)
                {
                    TimeSeriesValue sumOut = new TimeSeriesValue()
                    {
                        Time = startAddDateTime,
                        Value = sumValidValues / sumValidDays,
                        IsValid = true,
                    };

                    if (inputDataType == OutputType.Total)
                    {
                        sumOut.Value = sumValidValues;
                    }

                    result.Data.Add(sumOut);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns time series with metadata selecting the last value at the end of the specified time span.
        /// </summary>
        /// <param name="timeSpanToAggregate">Modelling time span to aggregate.</param>
        /// <param name="startAggregationDate">Start of aggregation period.</param>
        /// <returns>Time series with metadata selecting the last value at the end of each aggregation period.</returns>
        public TimeSeriesWithMetadata EndOfPeriodValues(StandardModellingTimeSpan timeSpanToAggregate, DateTime startAggregationDate)
        {
            TimeSeriesWithMetadata result = this.CopyMetadataWithBlankData();

            int foundIndex = TimeSeriesValue.GetIndexForTimestep(this.Data.ToArray(), startAggregationDate);

            if (foundIndex >= 0 && foundIndex < this.Data.Count)
            {
                DateTime startAddDateTime = this.Data[foundIndex].Time;
                DateTime endAddDateTime = startAddDateTime.Add(timeSpanToAggregate.GetAsTimeSpan(startAddDateTime));

                TimeSeriesValue lastOut = null;

                foreach (TimeSeriesValue value in this.Data)
                {
                    if (value.Time >= startAddDateTime)
                    {
                        if (value.Time.Add(this.ModellingTimeSpan.GetAsTimeSpan()) >= endAddDateTime)
                        {
                            lastOut = new TimeSeriesValue()
                            {
                                Time = startAddDateTime,
                                Value = value.Value,
                                IsValid = value.IsValid,
                            };

                            result.Data.Add(lastOut);

                            // Reset period
                            startAddDateTime = value.Time;
                            endAddDateTime = startAddDateTime.Add(timeSpanToAggregate.GetAsTimeSpan(startAddDateTime));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Throws if the Data list is empty, with a message identifying the calling method and element.</summary>
        /// <param name="callerMethod">Name of the calling method for the error message.</param>
        private void ThrowIfDataEmpty(string callerMethod)
        {
            if (this.Data == null || this.Data.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{callerMethod} cannot operate on an empty time series"
                    + (string.IsNullOrEmpty(this.ElementName) ? "." : $" (Element: '{this.ElementName}')."));
            }
        }
    }
}
