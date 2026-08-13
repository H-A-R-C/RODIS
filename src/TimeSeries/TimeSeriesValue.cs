// <copyright file="TimeSeriesValue.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.Series
{
    using RODIS.TimeSeries;

    /// <summary>A single timestamped data point with a value and validity flag, plus static helpers for searching and interrogating time series arrays.</summary>
    public class TimeSeriesValue
    {
        /// <summary>
        /// Gets or sets time of value.
        /// </summary>
        public DateTime Time { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Gets or sets value.
        /// </summary>
        public double Value { get; set; } = 0.0;

        /// <summary>True if this data point contains valid data; false if missing or invalid.</summary>
        public bool IsValid { get; set; } = true;

        /// <summary>Creates an independent copy of this time series value.</summary>
        /// <returns>New TimeSeriesValue with the same Time, Value, and IsValid.</returns>
        public TimeSeriesValue DeepCopy()
        {
            TimeSeriesValue result = new TimeSeriesValue()
            {
                Time = this.Time,
                Value = this.Value,
                IsValid = this.IsValid,
            };

            return result;
        }

        /// <summary>
        /// Find index for timestep.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <param name="toFind">Time to find.</param>
        /// <returns>Index.</returns>
        public static int GetIndexForTimestep(TimeSeriesValue[] values, DateTime toFind)
        {
            if (values == null || values.Length == 0)
                return -1;

            if (values[0].Time > toFind)
                return -1;

            if (values.Length == 1)
                return values[0].Time >= toFind ? 0 : values.Length;

            int iTS = -1;

            // Speed up process a bit here by working out where to search initially
            TimeSpan timeIncrement = values[1].Time - values[0].Time;
            TimeSpan fromStartToTime = toFind - values[0].Time;
            int fastStartIndex = (int)(fromStartToTime.TotalDays / timeIncrement.TotalDays) - 2;
            fastStartIndex = Math.Max(0, fastStartIndex);
            for (iTS = fastStartIndex; iTS < values.Length; iTS++)
            {
                if (values[iTS].Time >= toFind)
                {
                    return iTS;
                }
            }

            // If the fast search doesn't work, go through all of the values
            for (iTS = 0; iTS < values.Length; iTS++)
            {
                if (values[iTS].Time >= toFind)
                {
                    return iTS;
                }
            }

            return values.Length;
        }

        /// <summary>
        /// Returns the position in the time series values array that has the first valid data.
        /// </summary>
        /// <param name="values">Array of time series values.</param>
        /// <returns>Integer position in the array. Returns -1 if no valid data in the array.</returns>
        public static int GetFirstValid (TimeSeriesValue[] values)
        {
            int iTSFound = -1;

            if (values != null)
            {
                for (int iTS = 0; iTSFound < 0 && iTS < values.Length; ++iTS)
                {
                    if (values[iTS].IsValid)
                    {
                        iTSFound = iTS;
                    }
                }
            }

            return iTSFound;
        }

        /// <summary>
        /// Returns the position in the time series values array that has the last valid data.
        /// </summary>
        /// <param name="values">Array of time series values.</param>
        /// <returns>Integer position in the array. Returns -1 if no valid data in the array.</returns>
        public static int GetLastValid(TimeSeriesValue[] values)
        {
            int iTSFound = -1;

            if (values != null)
            {
                for (int iTS = values.Length-1; iTSFound < 0 && iTS >= 0; --iTS)
                {
                    if (values[iTS].IsValid)
                    {
                        iTSFound = iTS;
                    }
                }
            }

            return iTSFound;
        }

        /// <summary>
        /// Returns the datetime of the first valid data.
        /// </summary>
        /// <param name="values">Array of time series values.</param>
        /// <returns>Date time of the first entry in the array with valid data. Returns maximum date time value if no valid data in the array.</returns>
        public static DateTime GetStartDateTimeValid (TimeSeriesValue[] values)
        {
            DateTime dtFound = DateTime.MaxValue;

            int iTSFound = GetFirstValid(values);
            if (iTSFound >= 0)
            {
                dtFound = values[iTSFound].Time;
            }

            return dtFound;
        }

        /// <summary>
        /// Returns the datetime of the last valid data.
        /// </summary>
        /// <param name="values">Array of time series values.</param>
        /// <returns>Date time of the last entry in the array with valid data. Returns minimum date time value if no valid data in the array.</returns>
        public static DateTime GetEndDateTimeValid(TimeSeriesValue[] values)
        {
            DateTime dtFound = DateTime.MinValue;

            int iTSFound = GetLastValid(values);
            if (iTSFound >= 0)
            {
                dtFound = values[iTSFound].Time;
            }

            return dtFound;
        }

        /// <summary>
        /// Returns an array of the datetimes of all the valid values in the time series.
        /// </summary>
        /// <param name="values">Array of time series values.</param>
        /// <returns>array of the datetimes of all the valid values in the time series.</returns>
        public static DateTime[] GetValidDateTimes(TimeSeriesValue[] values)
        {
            List<DateTime> result = new List<DateTime>();

            int iTSFound = GetFirstValid(values);

            for (int iTS = iTSFound; iTS < values.Length; ++iTS)
            {
                if (values[iTS].IsValid)
                {
                    result.Add(values[iTS].Time);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Returns the modelling time span between first two valid values in the time series values array.
        /// </summary>
        /// <param name="values">Array of time series values.</param>
        /// <returns>Time span between first two valid values in the time series values array.</returns>
        public static StandardModellingTimeSpan GetTimeStep(TimeSeriesValue[] values)
        {
            StandardModellingTimeSpan modelTimeSpan = null;

            try
            {
                if (values != null)
                {
                    DateTime startValidTime = GetStartDateTimeValid(values);
                    if (GetEndDateTimeValid(values) > startValidTime)
                    {
                        int startTS = GetIndexForTimestep(values, startValidTime);
                        int nextTS = startTS + 1;
                        if (nextTS < values.Length)
                        {
                            if (values[nextTS].IsValid && values[nextTS].Time > startValidTime)
                            {
                                TimeSpan timeSpan = values[nextTS].Time - startValidTime;
                                modelTimeSpan = new StandardModellingTimeSpan(BaseModellingTimeSpan.Daily, 1.0);
                                modelTimeSpan = modelTimeSpan.SetFromTimeSpan(timeSpan);
                            }
                        }
                        else
                        {
                            throw new InvalidDataException($"Invalid modelling time span for input data.");
                        }
                    }
                    else
                    {
                        throw new InvalidDataException($"Invalid modelling time span for input data.");
                    }
                }
                else
                {
                    throw new InvalidDataException($"Invalid modelling time span for input data.");
                }
            }
            catch (InvalidDataException ide)
            {
                Console.WriteLine("ERROR: " + ide.ToString());
            }

            return modelTimeSpan;
        }
    }
}
