// <copyright file="FarmDamRepeatingMonthlyDemandModel.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.ModelRun
{
    public class FarmDamRepeatingMonthlyDemandModel : BaseDemandModel
    {
        /// <summary>Mean number of days in each calendar month (Feb = 28.25 to account for leap years).</summary>
        private static readonly double[] DaysInMonth = { 31.0, 28.25, 31.0, 30.0, 31.0, 30.0, 31.0, 31.0, 30.0, 31.0, 30.0, 31.0 };

        /// <summary>Gets or sets Column number for reading time series input. -1 = not set (must be configured before use).</summary>
        public int InputFileColumnNumber { get; set; } = -1;

        /// <summary>Gets or sets the proportion of annual demand in each of the 12 months, starting in January.</summary>
        public double[] MonthlyDemandProportions { get; set; }

        /// <summary>Annual volume of demand in ML.</summary>
        private double annualDemandVolume;

        /// <summary>Monthly volume of demand in each of the 12 months, starting in January.</summary>
        private double[] monthlyDemandVolume = new double[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };

        /// <summary>Volume of demand in the relevant time step (ML/day, ML/week or ML/month) in each of the 12 months, starting in January.</summary>
        private double[] dailyDemandVolumeInMonth = new double[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };

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
                // Distribute annual demand according to the proportional monthly pattern, with U7 monthly scale factors applied multiplicatively.
                // MonthlyScaleFactors defaults to all 1.0 (no perturbation), so this is a no-op until U7 is enabled.
                for (int i = 0; i < this.MonthlyDemandProportions.Length && i < this.monthlyDemandVolume.Length; i++)
                {
                    double scale = (this.MonthlyScaleFactors != null && i < this.MonthlyScaleFactors.Length) ? this.MonthlyScaleFactors[i] : 1.0;
                    this.monthlyDemandVolume[i] = this.annualDemandVolume * this.MonthlyDemandProportions[i] * scale / totalProportions;
                    this.dailyDemandVolumeInMonth[i] = this.monthlyDemandVolume[i] / DaysInMonth[i];
                }
            }
        }

        /// <summary>Runs one time step of the repeating-monthly demand model, calculating the unrestricted demand from the dailyDemandVolumeInMonth lookup.</summary>
        /// <param name="simulationDateTime">Start-of-step simulation date/time.</param>
        /// <param name="timeStep">Length of the simulation time step.</param>
        public override void RunTimeStep(DateTime simulationDateTime, TimeSpan timeStep)
        {
            DateTime endPeriod = simulationDateTime.Add(timeStep);
            int startMonth = simulationDateTime.Month - 1;
            int endMonth = endPeriod.Month - 1;

            if (startMonth == endMonth && simulationDateTime.Year == endPeriod.Year)
            {
                // Entire time step within one month
                this.UnrestrictedDemand = this.dailyDemandVolumeInMonth[startMonth] * timeStep.TotalDays;
            }
            else
            {
                // Time step crosses at least one month boundary — sum analytically
                this.UnrestrictedDemand = 0.0;

                // Days remaining in the start month
                DateTime startOfNextMonth = new DateTime(simulationDateTime.Year, simulationDateTime.Month, 1).AddMonths(1);
                double daysInStartMonth = (startOfNextMonth - simulationDateTime).TotalDays;
                this.UnrestrictedDemand += this.dailyDemandVolumeInMonth[startMonth] * daysInStartMonth;

                // Whole months in between (if any)
                DateTime current = startOfNextMonth;
                while (current.Month != endPeriod.Month || current.Year != endPeriod.Year)
                {
                    int monthIndex = current.Month - 1;
                    DateTime nextMonth = current.AddMonths(1);
                    double daysInThisMonth = (nextMonth - current).TotalDays;
                    this.UnrestrictedDemand += this.dailyDemandVolumeInMonth[monthIndex] * daysInThisMonth;
                    current = nextMonth;
                }

                // Days in the final month
                double daysInEndMonth = (endPeriod - current).TotalDays;
                if (daysInEndMonth > 0.0)
                {
                    this.UnrestrictedDemand += this.dailyDemandVolumeInMonth[endMonth] * daysInEndMonth;
                }
            }
        }
    }
}
