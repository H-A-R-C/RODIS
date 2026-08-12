// <copyright file="PercentileProbabillityOfExceedence.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace STEDI.Static
{
    /// <summary>
    /// Calculation of percentiles and probabilities of exceeding thresholds, from arrays.
    /// </summary>
    public class PercentileProbabillityOfExceedence
    {
        /// <summary>
        /// Calculates interpolated percentile values at the specified probabilities of exceedence, from unsorted or sorted data.
        /// </summary>
        /// <param name="values">Array of input values.</param>
        /// <param name="probabilitiesOfExceedance">Specified probabilities of exceedance, between 0 and 1.</param>
        /// <returns>Array of interpolated percentile values at the specified probabilities of exceedance.</returns>
        /// <remarks>
        /// For exc. prob. = 0, returns the maximum input value; for exc. prob. = 1, returns the minimum.
        /// Returns NaN for probabilities less than 0 or greater than 1.
        /// </remarks>
        public double[] Percentiles(double[] values, double[] probabilitiesOfExceedance)
        {
            if (this.IsAscendingSorted(values))
            {
                return this.PercentilesOfAscendingSorted(values, probabilitiesOfExceedance);
            }
            else
            {
                double[] sortedValues = values.ToArray();
                Array.Sort(sortedValues);
                return this.PercentilesOfAscendingSorted(sortedValues, probabilitiesOfExceedance);
            }
        }

        /// <summary>
        /// Calculates probabilities of exceeding specified threshold values, from unsorted or sorted data.
        /// </summary>
        /// <param name="values">Array of input values.</param>
        /// <param name="thresholdValues">Specified threshold values.</param>
        /// <returns>Array of probabilities of exceeding specified threshold values.</returns>
        public double[] ProbabilitiesOfExceedance(double[] values, double[] thresholdValues)
        {
            if (this.IsAscendingSorted(values))
            {
                return this.ProbabilitiesOfExceedanceFromAscendingSorted(values, thresholdValues);
            }
            else
            {
                double[] sortedValues = values.ToArray();
                Array.Sort(sortedValues);
                return this.ProbabilitiesOfExceedanceFromAscendingSorted(sortedValues, thresholdValues);
            }
        }

        /// <summary>
        /// Calculates interpolated percentile values at the specified probabilities of exceedence, from ASCENDING sorted data.
        /// </summary>
        /// <param name="sortedValues">ASCENDING sorted data, with no missing values.</param>
        /// <param name="probabilitiesOfExceedance">Specified probabilities of exceedance, between 0 and 1.</param>
        /// <returns>Array of interpolated percentile values at the specified probabilities of exceedance.</returns>
        /// <remarks>
        /// For exc. prob. = 0, returns the maximum input value; for exc. prob. = 1, returns the minimum.
        /// Returns NaN for probabilities less than 0 or greater than 1.
        /// </remarks>
        public double[] PercentilesOfAscendingSorted(double[] sortedValues, double[] probabilitiesOfExceedance)
        {
            if (!this.IsAscendingSorted(sortedValues))
            {
                return null;
            }

            double[] result = new double[probabilitiesOfExceedance.Length];

            for (int i = 0; i < probabilitiesOfExceedance.Length; ++i)
            {
                if (probabilitiesOfExceedance[i] < 0)
                {
                    result[i] = double.NaN;
                }
                else
                {
                    if (probabilitiesOfExceedance[i] > 1)
                    {
                        result[i] = double.NaN;
                    }
                    else
                    {
                        if (probabilitiesOfExceedance[i] == 0)
                        {
                            result[i] = sortedValues.Last();
                        }
                        else
                        {
                            if (probabilitiesOfExceedance[i] == 1)
                            {
                                result[i] = sortedValues.First();
                            }
                            else
                            {
                                double position = (1.0 - probabilitiesOfExceedance[i]) * (double)(sortedValues.Length - 1);
                                int intPosition = (int)position;
                                result[i] = sortedValues[intPosition] + (position - (double)intPosition) * (sortedValues[intPosition + 1] - sortedValues[intPosition]);
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Calculates probabilities of exceeding specified threshold values, from ASCENDING sorted data.
        /// </summary>
        /// <param name="sortedValues">ASCENDING sorted data, with no missing values.</param>
        /// <param name="thresholdValues">Specified threshold values.</param>
        /// <returns>Array of probabilities of exceeding specified threshold values.</returns>
        public double[] ProbabilitiesOfExceedanceFromAscendingSorted(double[] sortedValues, double[] thresholdValues)
        {
            if (!this.IsAscendingSorted(sortedValues))
            {
                return null;
            }

            double[] result = new double[thresholdValues.Length];

            for (int i = 0; i < thresholdValues.Length; ++i)
            {
                if (thresholdValues[i] <= sortedValues.First())
                {
                    result[i] = 1.0;
                }
                else
                {
                    if (thresholdValues[i] >= sortedValues.Last())
                    {
                        result[i] = 0.0;
                    }
                    else
                    {
                        int intPosition = -1;
                        for (int j = sortedValues.Length - 1; j >= 0 && intPosition < 0; --j)
                        {
                            if (thresholdValues[i] > sortedValues[j])
                            {
                                intPosition = j;
                            }
                        }

                        result[i] = ((double)(sortedValues.Length - intPosition - 1)) / ((double)(sortedValues.Length - 1));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Checks whether the values are sorted in Ascending order.
        /// </summary>
        /// <param name="values">Array of input values.</param>
        /// <returns>True if values are sorted in ascending order.</returns>
        private bool IsAscendingSorted(double[] values)
        {
            bool result = true;

            for (int i = 1; i < values.Length && result; ++i)
            {
                if (values[i - 1] > values[i])
                {
                    result = false;
                }
            }

            return result;
        }
    }
}
