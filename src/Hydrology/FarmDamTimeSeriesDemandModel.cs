// <copyright file="FarmDamTimeSeriesDemandModel.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
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
                throw new InvalidDataException($"Demand group '{this.DemandGroup}': InputPattern must have at least 2 time steps.");
            }

            this.annualDemandVolume = Math.Max(0, this.AnnualDemandFactor * this.DamStorageCapacityVolumeAtSpill);

            // Pass 1: sum proportions and total period
            double totalProportions = 0.0;
            TimeSpan totalPeriodForPattern = TimeSpan.Zero;
            for (int i = 0; i < this.InputPattern.Length; i++)
            {
                if (this.InputPattern[i].IsValid)
                {
                    totalProportions += this.InputPattern[i].Value;
                    TimeSpan step = (i == 0)
                        ? this.InputPattern[1].Time - this.InputPattern[0].Time
                        : this.InputPattern[i].Time - this.InputPattern[i - 1].Time;
                    totalPeriodForPattern += step;
                }
            }

            double totalYears = totalPeriodForPattern.Days / 365.25;
            double totalProportionsPerYear = (totalYears > 0.0) ? totalProportions / totalYears : 0.0;

            // Pass 2: build output array directly (no List, no DeepCopy)
            this.timeStepDemandVolume = new TimeSeriesValue[this.InputPattern.Length];
            for (int i = 0; i < this.InputPattern.Length; i++)
            {
                double scaledValue;
                if (totalProportionsPerYear > 0.0)
                {
                    scaledValue = this.InputPattern[i].Value / totalProportionsPerYear * this.annualDemandVolume;
                }
                else
                {
                    // All proportions zero — distribute uniformly
                    scaledValue = this.annualDemandVolume / totalYears;
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
    }
}
