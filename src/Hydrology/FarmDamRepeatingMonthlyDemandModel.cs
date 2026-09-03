// <copyright file="FarmDamRepeatingMonthlyDemandModel.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    using System.Globalization;
    using System.IO;

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
        /// <summary>Initialises the repeating-monthly demand model, distributing annual demand according to the monthly proportions and scale factors.</summary>
        public override void Initialise()
        {
            this.ValidateMonthlyScaleFactors();
            this.ValidateAnnualDemandInputs();

            if (this.MonthlyDemandProportions == null)
            {
                throw new InvalidDataException(
                    $"Demand group '{this.DemandGroup}': MonthlyDemandProportions must not be null.");
            }

            if (this.MonthlyDemandProportions.Length != 12)
            {
                throw new InvalidDataException(
                    $"Demand group '{this.DemandGroup}': MonthlyDemandProportions must contain exactly 12 values, "
                    + $"but {this.MonthlyDemandProportions.Length} values were supplied.");
            }

            double scaledProportionTotal = 0.0;

            for (int monthIndex = 0; monthIndex < this.MonthlyDemandProportions.Length; monthIndex++)
            {
                double proportion = this.MonthlyDemandProportions[monthIndex];
                string monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(monthIndex + 1);

                if (!double.IsFinite(proportion))
                {
                    string invalidValue = double.IsNaN(proportion)
                        ? "NaN"
                        : double.IsPositiveInfinity(proportion)
                            ? "Infinity"
                            : "-Infinity";

                    throw new InvalidDataException(
                        $"Demand group '{this.DemandGroup}': monthly demand proportion for {monthName} "
                        + $"must be finite but was {invalidValue}.");
                }

                if (proportion < 0.0)
                {
                    throw new InvalidDataException(
                        $"Demand group '{this.DemandGroup}': monthly demand proportion for {monthName} "
                        + $"must be zero or positive but was {proportion.ToString(CultureInfo.InvariantCulture)}.");
                }

                scaledProportionTotal += proportion * this.MonthlyScaleFactors[monthIndex];
            }

            this.annualDemandVolume =
                this.AnnualDemandFactor * this.DamStorageCapacityVolumeAtSpill;

            if (scaledProportionTotal > 0.0)
            {
                for (int monthIndex = 0; monthIndex < 12; monthIndex++)
                {
                    double scaledProportion =
                        this.MonthlyDemandProportions[monthIndex]
                        * this.MonthlyScaleFactors[monthIndex];

                    this.monthlyDemandVolume[monthIndex] =
                        this.annualDemandVolume
                        * scaledProportion
                        / scaledProportionTotal;
                }
            }
            else
            {
                // The pattern contains no positive monthly weight. Spread annual demand
                // evenly across the twelve months so the annual volume is preserved.
                double uniformMonthlyDemand =
                    this.annualDemandVolume / 12.0;

                for (int monthIndex = 0; monthIndex < 12; monthIndex++)
                {
                    this.monthlyDemandVolume[monthIndex] =
                        uniformMonthlyDemand;
                }
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
