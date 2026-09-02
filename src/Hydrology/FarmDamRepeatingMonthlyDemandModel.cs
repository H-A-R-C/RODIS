// <copyright file="FarmDamRepeatingMonthlyDemandModel.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    public class FarmDamRepeatingMonthlyDemandModel : BaseDemandModel
    {
        /// <summary>Gets or sets Column number for reading time series input. -1 = not set (must be configured before use).</summary>
        public int InputFileColumnNumber { get; set; } = -1;

        /// <summary>Gets or sets the proportion of annual demand in each of the 12 months, starting in January.</summary>
        public double[] MonthlyDemandProportions { get; set; }

        /// <summary>Annual volume of demand in ML.</summary>
        private double annualDemandVolume;

        /// <summary>Monthly volume of demand in each of the 12 months, starting in January.</summary>
        private double[] monthlyDemandVolume = new double[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };

        /// <summary>Initialises the repeating-monthly demand model, distributing annualDemandVolume according to MonthlyDemandProportions and (optionally) MonthlyScaleFactors.</summary>
        public override void Initialise()
        {
            if (this.MonthlyDemandProportions == null || this.MonthlyDemandProportions.Length == 0)
            {
                throw new InvalidDataException(
                    $"Demand group '{this.DemandGroup}': MonthlyDemandProportions is null or empty.");
            }

            this.annualDemandVolume = Math.Max(0, this.AnnualDemandFactor * this.DamStorageCapacityVolumeAtSpill);

            double totalProportions = 0.0;
            for (int i = 0; i < this.MonthlyDemandProportions.Length && i < this.monthlyDemandVolume.Length; i++)
            {
                totalProportions += this.MonthlyDemandProportions[i];
            }

            if (totalProportions > 0.0)
            {
                for (int i = 0; i < this.MonthlyDemandProportions.Length && i < this.monthlyDemandVolume.Length; i++)
                {
                    double scale = (this.MonthlyScaleFactors != null && i < this.MonthlyScaleFactors.Length) ? this.MonthlyScaleFactors[i] : 1.0;
                    this.monthlyDemandVolume[i] = this.annualDemandVolume * this.MonthlyDemandProportions[i] * scale / totalProportions;
                }

                // The daily rate is deliberately NOT precomputed here: it depends on the actual length of the month in the simulation year, which Initialise cannot know.
                // DailyDemandVolume(year, month) performs the conversion at the point of use in RunTimeStep.
            }
        }

        /// <summary>Runs one time step of the repeating-monthly demand model, converting each month's demand volume to a daily rate using the actual length of that month.</summary>
        /// <param name="simulationDateTime">Start-of-step simulation date/time.</param>
        /// <param name="timeStep">Length of the simulation time step.</param>
        public override void RunTimeStep(DateTime simulationDateTime, TimeSpan timeStep)
        {
            DateTime endPeriod = simulationDateTime.Add(timeStep);

            if (simulationDateTime.Month == endPeriod.Month && simulationDateTime.Year == endPeriod.Year)
            {
                // Entire time step within one month
                this.UnrestrictedDemand = this.DailyDemandVolume(simulationDateTime.Year, simulationDateTime.Month) * timeStep.TotalDays;
            }
            else
            {
                // Time step crosses at least one month boundary — sum analytically
                this.UnrestrictedDemand = 0.0;

                // Days remaining in the start month
                DateTime startOfNextMonth = new DateTime(simulationDateTime.Year, simulationDateTime.Month, 1).AddMonths(1);
                double daysInStartMonth = (startOfNextMonth - simulationDateTime).TotalDays;
                this.UnrestrictedDemand += this.DailyDemandVolume(simulationDateTime.Year, simulationDateTime.Month) * daysInStartMonth;

                // Whole months in between (if any). Each contributes its full monthly volume, because the daily rate is now that month's volume divided by its own length.
                DateTime current = startOfNextMonth;
                while (current.Month != endPeriod.Month || current.Year != endPeriod.Year)
                {
                    DateTime nextMonth = current.AddMonths(1);
                    double daysInThisMonth = (nextMonth - current).TotalDays;
                    this.UnrestrictedDemand += this.DailyDemandVolume(current.Year, current.Month) * daysInThisMonth;
                    current = nextMonth;
                }

                // Days in the final month
                double daysInEndMonth = (endPeriod - current).TotalDays;
                if (daysInEndMonth > 0.0)
                {
                    this.UnrestrictedDemand += this.DailyDemandVolume(endPeriod.Year, endPeriod.Month) * daysInEndMonth;
                }
            }
        }

        /// <summary>Returns the daily demand rate for the given calendar month, dividing that month's demand volume by the ACTUAL number of days in the month. Using the real month length
        /// rather than a nominal one ensures each month delivers exactly its share of the annual demand volume, in both leap and non-leap years.</summary>
        /// <param name="year">Calendar year of the time step, used to resolve February's length.</param>
        /// <param name="month">Calendar month of the time step (1 = January).</param>
        /// <returns>Demand rate for that month in ML/day.</returns>
        private double DailyDemandVolume(int year, int month)
        {
            return this.monthlyDemandVolume[month - 1] / DateTime.DaysInMonth(year, month);
        }
    }
}
