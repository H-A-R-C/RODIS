// <copyright file="FarmDamTimeSeriesDemandModel.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    using System.Globalization;
    using RODIS.Series;

    public class FarmDamTimeSeriesDemandModel : BaseDemandModel
    {
        /// <summary>Gets or sets the path to the input file for the time series demand patterns.</summary>
        public string InputFilePath { get; set; } = string.Empty;

        /// <summary>Gets or sets the column number for reading time series input.</summary>
        public int InputFileColumnNumber { get; set; } = -1;

        /// <summary>Time series of proportional demand values read from input file. Scaled by AnnualDemandVolume during Initialise to produce timeStepDemandVolume. Must have at least 2 elements.</summary>
        public TimeSeriesValue[] InputPattern = null;

        /// <summary>Annual volume of demand in ML.</summary>
        private double annualDemandVolume;

        /// <summary>Pre-computed demand volume (ML) for each time step, scaled from InputPattern during Initialise.</summary>
        private TimeSeriesValue[] timeStepDemandVolume;

        /// <summary>Initialises the time-series demand model, deriving annualDemandVolume = AnnualDemandFactor × DamStorageCapacityVolumeAtSpill.</summary>
        public override void Initialise()
        {
            if (this.InputPattern == null || this.InputPattern.Length < 2)
            {
                throw new InvalidDataException(
                    $"Demand group '{this.DemandGroup}': InputPattern must have at least 2 time steps.");
            }

            this.ValidateInputPattern();
            this.ValidateInputPatternValues();
            this.ValidateMonthlyScaleFactors();
            this.ValidateAnnualDemandInputs();

            this.annualDemandVolume =
                this.AnnualDemandFactor * this.DamStorageCapacityVolumeAtSpill;

            // Normalise using complete calendar years only.
            double meanAnnualProportions = this.CalculateMeanAnnualProportions(out int completeYearCount);

            if (completeYearCount == 0)
            {
                Console.WriteLine($"WARNING: Demand group '{this.DemandGroup}': demand pattern '{this.InputFilePath}' does not span a complete calendar year. "
                    + "Normalising over the whole series instead, which may bias the annual demand volume.");
            }

            this.timeStepDemandVolume = new TimeSeriesValue[this.InputPattern.Length];
            for (int i = 0; i < this.InputPattern.Length; i++)
            {
                double scaledValue;
                if (meanAnnualProportions > 0.0)
                {
                    scaledValue = this.InputPattern[i].Value / meanAnnualProportions * this.annualDemandVolume;
                }
                else
                {
                    // Every proportion is zero, so the pattern carries no shape. Spread the annual volume evenly across the time steps, preserving the annual total.
                    scaledValue = this.annualDemandVolume * this.CalculateMeanTimeStepDays() / 365.25;
                }

                this.timeStepDemandVolume[i] = new TimeSeriesValue
                {
                    Time = this.InputPattern[i].Time,
                    Value = scaledValue,
                    IsValid = true,
                };
            }
        }

        /// <summary>Runs one time step of the time-series demand model, calculating the unrestricted demand for the step from the time-series with monthly scale factors applied.</summary>
        /// <param name="simulationDateTime">Start-of-step simulation date/time.</param>
        /// <param name="timeStep">Length of the simulation time step.</param>
        public override void RunTimeStep(DateTime simulationDateTime, TimeSpan timeStep)
        {
            int iTS = TimeSeriesValue.GetIndexForTimestep(this.timeStepDemandVolume, simulationDateTime);
            TimeSeriesValue demandAtTimestep = this.timeStepDemandVolume[iTS];
            this.UnrestrictedDemand = demandAtTimestep.Value * this.MonthlyScaleFactors[simulationDateTime.Month - 1];
        }

        /// <summary>Calculates the mean sum of pattern proportions per complete calendar year. A calendar year counts as complete only when the pattern spans it from
        /// 1 January to 31 December, so a partial year at either end of the file is excluded from the mean. When no complete year is present the whole-series mean is
        /// returned as a fallback, scaled to a nominal 365.25-day year.</summary>
        /// <param name="completeYearCount">Output: the number of complete calendar years found in the pattern; 0 when the fallback was used.</param>
        /// <returns>Mean sum of proportions per year, or 0.0 when every proportion is zero.</returns>
        private double CalculateMeanAnnualProportions(out int completeYearCount)
        {
            DateTime firstTime = this.InputPattern[0].Time;
            DateTime lastTime = this.InputPattern[this.InputPattern.Length - 1].Time;

            // Sum the valid proportions in each calendar year the pattern touches.
            Dictionary<int, double> proportionsByYear = new Dictionary<int, double>();
            for (int i = 0; i < this.InputPattern.Length; i++)
            {
                if (!this.InputPattern[i].IsValid)
                {
                    continue;
                }
                int year = this.InputPattern[i].Time.Year;
                proportionsByYear.TryGetValue(year, out double runningTotal);
                proportionsByYear[year] = runningTotal + this.InputPattern[i].Value;
            }

            // Keep only the years the pattern covers end to end.
            double completeYearTotal = 0.0;
            completeYearCount = 0;
            foreach (KeyValuePair<int, double> yearTotal in proportionsByYear)
            {
                if (firstTime <= new DateTime(yearTotal.Key, 1, 1) && lastTime >= new DateTime(yearTotal.Key, 12, 31))
                {
                    completeYearTotal += yearTotal.Value;
                    completeYearCount++;
                }
            }

            if (completeYearCount > 0)
            {
                return completeYearTotal / completeYearCount;
            }

            // Fallback: pattern is shorter than one calendar year, so use the whole-series mean scaled to a nominal year.
            double totalProportions = 0.0;
            for (int i = 0; i < this.InputPattern.Length; i++)
            {
                if (this.InputPattern[i].IsValid)
                {
                    totalProportions += this.InputPattern[i].Value;
                }
            }

            double totalDays = this.CalculateTotalPeriodDays();
            return (totalDays > 0.0) ? totalProportions * 365.25 / totalDays : 0.0;
        }

        /// <summary>Calculates the total period covered by the pattern in days, treating the first time step as having the same length as the second.</summary>
        /// <returns>Total period covered by the pattern, in days.</returns>
        private double CalculateTotalPeriodDays()
        {
            double totalDays = 0.0;
            for (int i = 0; i < this.InputPattern.Length; i++)
            {
                if (this.InputPattern[i].IsValid)
                {
                    TimeSpan step = (i == 0) ? this.InputPattern[1].Time - this.InputPattern[0].Time : this.InputPattern[i].Time - this.InputPattern[i - 1].Time;
                    totalDays += step.TotalDays;
                }
            }
            return totalDays;
        }

        /// <summary>Calculates the mean length of a time step in the pattern, used to spread demand evenly when the pattern carries no shape.</summary>
        /// <returns>Mean time step length in days; defaults to 1.0 when it cannot be determined.</returns>
        private double CalculateMeanTimeStepDays()
        {
            int validCount = 0;
            for (int i = 0; i < this.InputPattern.Length; i++)
            {
                if (this.InputPattern[i].IsValid)
                {
                    validCount++;
                }
            }

            double totalDays = this.CalculateTotalPeriodDays();
            return (validCount > 0 && totalDays > 0.0) ? totalDays / validCount : 1.0;
        }

        /// <summary>Validates that valid time-series entries are unique, strictly increasing and contiguous at the inferred regular timestep.</summary>
        private void ValidateInputPattern()
        {
            for (int i = 1; i < this.InputPattern.Length; i++)
            {
                DateTime previous = this.InputPattern[i - 1].Time;
                DateTime current = this.InputPattern[i].Time;

                if (current == previous)
                {
                    throw new InvalidDataException(
                        $"Demand group '{this.DemandGroup}': demand pattern '{this.InputFilePath}' contains duplicate date {current:yyyy-MM-dd}.");
                }

                if (current < previous)
                {
                    throw new InvalidDataException(
                        $"Demand group '{this.DemandGroup}': demand pattern '{this.InputFilePath}' is not in chronological order at {current:yyyy-MM-dd}.");
                }
            }

            TimeSpan expectedTimeStep = this.InputPattern[1].Time - this.InputPattern[0].Time;

            if (expectedTimeStep <= TimeSpan.Zero)
            {
                throw new InvalidDataException(
                    $"Demand group '{this.DemandGroup}': demand pattern '{this.InputFilePath}' must have a positive timestep.");
            }

            for (int i = 1; i < this.InputPattern.Length; i++)
            {
                DateTime expected = this.InputPattern[i - 1].Time.Add(expectedTimeStep);
                DateTime actual = this.InputPattern[i].Time;

                if (actual != expected)
                {
                    throw new InvalidDataException(
                        $"Demand group '{this.DemandGroup}': demand pattern '{this.InputFilePath}' is missing expected date {expected:yyyy-MM-dd}; "
                        + $"the next available date is {actual:yyyy-MM-dd}.");
                }
            }
        }

        /// <summary>Validates that all valid demand-pattern values are finite and non-negative.</summary>
        private void ValidateInputPatternValues()
        {
            for (int i = 0; i < this.InputPattern.Length; i++)
            {
                TimeSeriesValue value = this.InputPattern[i];

                if (!value.IsValid)
                {
                    throw new InvalidDataException(
                        $"Demand group '{this.DemandGroup}': demand pattern '{this.InputFilePath}' contains an entry on {value.Time:yyyy-MM-dd} that is marked as not valid.");
                }

                if (!double.IsFinite(value.Value))
                {
                    throw new InvalidDataException(
                        $"Demand group '{this.DemandGroup}': demand pattern '{this.InputFilePath}' contains non-finite value "
                        + $"{value.Value} on {value.Time:yyyy-MM-dd}.");
                }

                if (value.Value < 0.0)
                {
                    throw new InvalidDataException(
                        $"Demand group '{this.DemandGroup}': demand pattern '{this.InputFilePath}' contains negative value "
                        + $"{value.Value.ToString(CultureInfo.InvariantCulture)} on {value.Time:yyyy-MM-dd}. "
                        + "Demand-pattern values must be zero or positive.");
                }
            }
        }
    }
}
