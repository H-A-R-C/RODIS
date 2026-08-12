// <copyright file="BaseDemandModel.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.ModelRun
{
    using System;

    /// <summary>
    /// Abstract base class for farm-dam demand models. Holds the shared state used by both the time-series and repeating-monthly subclasses so that
    /// scenario rescaling and uncertainty propagation (e.g. U3 dam-volume sampling, U7 monthly-pattern noise) can operate polymorphically over the
    /// full demand-model population without type tests.
    /// </summary>
    public abstract class BaseDemandModel
    {
        /// <summary>Demand group label that this demand model belongs to (e.g. "RunoffDams", "NonRunoffDams"); used by the engine to apply group-specific rescaling.</summary>
        public string DemandGroup { get; set; } = string.Empty;

        /// <summary>Unrestricted demand for the current time step (ML). Written by RunTimeStep on each subclass; not part of the persistent configuration state.</summary>
        public double UnrestrictedDemand { get; set; } = 0.0;

        /// <summary>Storage capacity volume of the dam when full (ML). Used together with AnnualDemandFactor to derive annualDemandVolume during Initialise.</summary>
        public double DamStorageCapacityVolumeAtSpill { get; set; } = 0.0;

        /// <summary>Annual demand factor — annual mean demand divided by storage capacity at spill; reset per scenario by the LOD scaling pipeline.</summary>
        public double AnnualDemandFactor { get; set; } = 0.0;

        /// <summary>Monthly multiplicative scale factors for U7 demand-pattern uncertainty; length 12, indexed by month (0 = Jan). Defaults to all 1.0 (no perturbation).</summary>
        public double[] MonthlyScaleFactors { get; set; } = new double[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 };

        /// <summary>Initialises the demand model's derived state. Must be called after any change to DamStorageCapacityVolumeAtSpill, AnnualDemandFactor, or MonthlyScaleFactors.</summary>
        public abstract void Initialise();

        /// <summary>Runs one time step of the demand model, calculating the unrestricted demand for that step.</summary>
        /// <param name="simulationDateTime">Start-of-step simulation date/time.</param>
        /// <param name="timeStep">Length of the simulation time step.</param>
        public abstract void RunTimeStep(DateTime simulationDateTime, TimeSpan timeStep);
    }
}