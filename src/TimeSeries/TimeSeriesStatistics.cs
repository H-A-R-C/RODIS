// <copyright file="TimeSeriesStatistics.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.Series
{
    using Static;

    /// <summary>
    /// Calculates statistics from time series data arrays.
    /// </summary>
    public class TimeSeriesStatistics
    {
        /// <summary>
        /// Gets or sets array of time series value data points.
        /// </summary>
        public TimeSeriesValue[] Modelled { get; set; }

        /// <summary>
        /// Gets or sets ascending sorted array of time series value data points.
        /// </summary>
        public double[] ModelledSorted { get; set; }

        /// <summary>
        /// Gets mean value from time series.
        /// </summary>
        public double MeanModelled { get; set; }

        /// <summary>
        /// Gets number of valid values in time series.
        /// </summary>
        private int countValid = 0;

        /// <summary>
        /// Mean number of days in each month, January to December.
        /// </summary>
        private double[] meanDaysInMonth = new double[] { 31, 28.25, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        /// <summary>
        /// Initialises data for analysis.
        /// </summary>
        /// <param name="_modelled">Array of time series value data points.</param>
        /// <param name="flowExponent">Optional exponent to be applied to each input value. Default value is 1.0. Value of 0.5 would take square root of each data point, etc.</param>
        public void Initialise(TimeSeriesValue[] _modelled, double flowExponent = 1.0)
        {
            List<TimeSeriesValue> modelledList = new List<TimeSeriesValue>();

            List<double> validModelled = new List<double>();

            double sumValidModelled = 0;
            countValid = 0;

            for (int i = 0; i < _modelled.Length; i++)
            {
                TimeSeriesValue modValue = new TimeSeriesValue()
                {
                    Time = _modelled[i].Time,
                    Value = _modelled[i].Value,
                    IsValid = true
                };
                
                if (_modelled[i].IsValid)
                {
                    validModelled.Add(modValue.Value);

                    sumValidModelled += modValue.Value;
                    countValid++;
                }

                modelledList.Add(modValue);
            }

            Modelled = modelledList.ToArray();

            MeanModelled = sumValidModelled / ((double)countValid);

            ModelledSorted = validModelled.ToArray();
            Array.Sort(ModelledSorted);
        }

        /// <summary>
        /// Returns array of boolean values indicating whether each of the input data values is within the specified season of the year.
        /// </summary>
        /// <param name="startMonthOfYear">Start month of season, included in season.</param>
        /// <param name="endMonthOfYear">End month of season, included in season.</param>
        /// <returns>Array of values for each data point, with True if in specified season, False if outside of season.</returns>
        public bool[] IsInSeason(MonthOfYear startMonthOfYear, MonthOfYear endMonthOfYear)
        {
            bool[] inSeason = new bool[12];

            DateOnly seasonStart = new DateOnly(2000, (int)startMonthOfYear + 1, 1);
            DateOnly seasonEnd = new DateOnly(2000, (int)endMonthOfYear + 1, 1);

            for (int i = 0; i < 12; ++i)
            {
                // Test the 15th of each month as a representative date
                DateTime testDate = new DateTime(2000, i + 1, 15);
                inSeason[i] = InSeason.IsInSeason(testDate, seasonStart, seasonEnd);
            }

            return inSeason;
        }

        /// <summary>
        /// Returns mean of all valid values within specified season and between specified overall start and end dates.
        /// </summary>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <param name="startDate">Overall start date of data to include.</param>
        /// <param name="endDate">Overall end date of data to include.</param>
        /// <returns>Mean value for season.</returns>
        public double MeanForSeason(MonthOfYear startMonthOfYear, MonthOfYear endMonthOfYear, DateTime startDate, DateTime endDate)
        {
            double sum = 0;
            int seasonCountValid = 0;
            bool[] inSeason = this.IsInSeason(startMonthOfYear, endMonthOfYear);
            for (int i = 0; i < this.Modelled.Length; ++i)
            {
                if (this.Modelled[i].IsValid && this.Modelled[i].Time >= startDate && this.Modelled[i].Time <= endDate)
                {
                    if (inSeason[this.Modelled[i].Time.Month - 1])
                    {
                        sum += this.Modelled[i].Value;
                        ++seasonCountValid;
                    }
                }
            }
            return seasonCountValid > 0 ? sum / (double)seasonCountValid : double.NaN;
        }

        /// <summary>
        /// Returns mean of all valid values within specified season.
        /// </summary>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <returns>Mean value for season.</returns>
        public double MeanForSeason(MonthOfYear startMonthOfYear = MonthOfYear.Jan, MonthOfYear endMonthOfYear = MonthOfYear.Dec)
        {
            DateTime startDate = this.Modelled[0].Time;
            DateTime endDate = this.Modelled.Last().Time;

            return this.MeanForSeason(startMonthOfYear, endMonthOfYear, startDate, endDate);
        }

        /// <summary>
        /// Returns mean of sum of all valid values within specified season and between specified overall start and end dates.
        /// </summary>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <param name="startDate">Overall start date of data to include.</param>
        /// <param name="endDate">Overall end date of data to include.</param>
        /// <returns>Mean value sum of valid values for season.</returns>
        public double MeanTotalForSeason(MonthOfYear startMonthOfYear, MonthOfYear endMonthOfYear, DateTime startDate, DateTime endDate)
        {
            double meanForTS = this.MeanForSeason(startMonthOfYear, endMonthOfYear, startDate, endDate);
            double meanDays = this.MeanNumberOfTimeStepsInSeason(startMonthOfYear, endMonthOfYear);
            return meanDays * meanForTS;
        }

        /// <summary>
        /// Returns mean of sum of all valid values within specified season.
        /// </summary>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <returns>Mean value sum of valid values for season.</returns>
        public double MeanTotalForSeason(MonthOfYear startMonthOfYear = MonthOfYear.Jan, MonthOfYear endMonthOfYear = MonthOfYear.Dec)
        {
            double meanForTS = this.MeanForSeason(startMonthOfYear, endMonthOfYear);
            double meanDays = this.MeanNumberOfTimeStepsInSeason(startMonthOfYear, endMonthOfYear);
            return meanDays * meanForTS;
        }

        /// <summary>
        /// Returns mean number of time steps in specified season.
        /// </summary>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <returns>Number of time steps.</returns>
        public double MeanNumberOfTimeStepsInSeason(MonthOfYear startMonthOfYear, MonthOfYear endMonthOfYear)
        {
            bool[] inSeason = this.IsInSeason(startMonthOfYear, endMonthOfYear);
            double sum = 0;
            double daysPerTimeStep = (Modelled[1].Time - Modelled[0].Time).TotalDays;

            for (int i = 0; i < inSeason.Length; ++i)
            {
                if (inSeason[i])
                {
                    sum += meanDaysInMonth[i] / daysPerTimeStep;
                }
            }

            return sum;
        }

        /// <summary>
        /// Returns array of ascending sorted valid values within specified season and between specified overall start and end dates.
        /// </summary>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <param name="startDate">Overall start date of data to include.</param>
        /// <param name="endDate">Overall end date of data to include.</param>
        /// <returns>Array of ascending sorted valid values for season.</returns>
        public double[] SortedValuesForSeason(MonthOfYear startMonthOfYear, MonthOfYear endMonthOfYear, DateTime startDate, DateTime endDate)
        {
            bool[] inSeason = this.IsInSeason(startMonthOfYear, endMonthOfYear);
            List<double> values = new List<double>();

            for (int i = 0; i < Modelled.Length; ++i)
            {
                DateTime date = Modelled[i].Time;
                if (inSeason[date.Month-1] && Modelled[i].IsValid && date >= startDate && date <= endDate)
                {
                    values.Add(Modelled[i].Value);
                }
            }

            double[] sortedValues = values.ToArray();
            Array.Sort(sortedValues);

            return sortedValues;
        }

        /// <summary>
        /// Returns array of interpolated percentile values at the specified probabilities of exceedence within specified season and between specified overall start and end dates.
        /// </summary>
        /// <param name="probabilitiesOfExceedance">Array of probabilities of exceedance to evaluate (0.0 to 1.0).</param>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <param name="startDate">Overall start date of data to include.</param>
        /// <param name="endDate">Overall end date of data to include.</param>
        /// <returns>Array of interpolated percentile values at the specified probabilities of exceedence.</returns>
        public double[] Percentiles(double[] probabilitiesOfExceedance, MonthOfYear startMonthOfYear, MonthOfYear endMonthOfYear, DateTime startDate, DateTime endDate)
        {
            double[] sortedValues = this.SortedValuesForSeason(startMonthOfYear, endMonthOfYear, startDate, endDate);

            PercentileProbabillityOfExceedence calculator = new PercentileProbabillityOfExceedence();
            double[] result = calculator.Percentiles(sortedValues, probabilitiesOfExceedance);

            return result;
        }

        /// <summary>
        /// Returns array of interpolated percentile values at the specified probabilities of exceedence within specified season.
        /// </summary>
        /// <param name="probabilitiesOfExceedance">Array of probabilities of exceedance to evaluate (0.0 to 1.0).</param>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <returns>Array of interpolated percentile values at the specified probabilities of exceedence.</returns>
        public double[] Percentiles(double[] probabilitiesOfExceedance, MonthOfYear startMonthOfYear = MonthOfYear.Jan, MonthOfYear endMonthOfYear = MonthOfYear.Dec)
        {
            DateTime startDate = this.Modelled[0].Time;
            DateTime endDate = this.Modelled.Last().Time;

            return this.Percentiles(probabilitiesOfExceedance, startMonthOfYear, endMonthOfYear, startDate, endDate);
        }

        /// <summary>
        /// Returns array probabilities of exceeding specified threshold values within specified season and between specified overall start and end dates.
        /// </summary>
        /// <param name="thresholdValues">Array of thresholds for which probability of exceedance is to be assessed against.</param>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <param name="startDate">Overall start date of data to include.</param>
        /// <param name="endDate">Overall end date of data to include.</param>
        /// <returns>Array of probabilities of exceeding specified threshold values.</returns>
        public double[] ProbabilitiesOfExceedance(double[] thresholdValues, MonthOfYear startMonthOfYear, MonthOfYear endMonthOfYear, DateTime startDate, DateTime endDate)
        {
            double[] sortedValues = this.SortedValuesForSeason(startMonthOfYear, endMonthOfYear, startDate, endDate);

            PercentileProbabillityOfExceedence calculator = new PercentileProbabillityOfExceedence();
            double[] result = calculator.ProbabilitiesOfExceedance(sortedValues, thresholdValues);

            return result;
        }

        /// <summary>
        /// Returns array probabilities of exceeding specified threshold values within specified season.
        /// </summary>
        /// <param name="thresholdValues">Array of thresholds for which probability of exceedance is to be assessed against.</param>
        /// <param name="startMonthOfYear">Start month of season to include.</param>
        /// <param name="endMonthOfYear">End month of season to include.</param>
        /// <returns>Array of probabilities of exceeding specified threshold values.</returns>
        public double[] ProbabilitiesOfExceedance(double[] thresholdValues, MonthOfYear startMonthOfYear = MonthOfYear.Jan, MonthOfYear endMonthOfYear = MonthOfYear.Dec)
        {
            DateTime startDate = this.Modelled[0].Time;
            DateTime endDate =  Modelled.Last().Time;

            return this.ProbabilitiesOfExceedance(thresholdValues, startMonthOfYear, endMonthOfYear, startDate, endDate);
        }
    }
}
