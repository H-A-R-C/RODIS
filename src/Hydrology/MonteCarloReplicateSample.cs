// <copyright file="MonteCarloReplicateSample.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.MonteCarlo
{
    /// <summary>
    /// Holds all sampled values for one Monte Carlo replicate.
    /// Immutable once constructed — pass to the engine, don't let the engine modify it.
    /// </summary>
    public class MonteCarloReplicateSample
    {
        // ── Storage volume ──

        /// <summary>Scale factor applied to total storage capacity across all water bodies (per-replicate). Default 1.0 = no change.</summary>
        public double TotalStorageCapacityFactor { get; init; } = 1.0;

        /// <summary>Per-node scale factors applied to individual water body capacities. Null = no perturbation.</summary>
        public double[] IndividualCapacityFactors { get; init; } = null;

        // ── Seepage ──

        /// <summary>Base seepage loss rate in mm/d at full supply level, applied to all nodes before individual factors. NaN = don't override base settings.</summary>
        public double MeanSeepageLossRate_mmPerDay { get; init; } = double.NaN;

        /// <summary>Per-node multipliers on seepage loss rate. Null = no perturbation (all nodes get factor 1.0).</summary>
        public double[] IndividualSeepageFactors { get; init; } = null;

        // ── Demand ──

        /// <summary>Sampled mean annual demand ratio to storage capacity. NaN = don't override base settings.</summary>
        public double MeanAnnualDemandRatio { get; init; } = double.NaN;

        // ── Climate ──

        /// <summary>Multiplier applied to rainfall at every time step. 1.0 = no change.</summary>
        public double RainfallFactor { get; init; } = 1.0;

        /// <summary>Multiplier applied to PET at every time step. 1.0 = no change.</summary>
        public double PETFactor { get; init; } = 1.0;

        // ── Spatial runoff variation ──

        /// <summary>Fractional change in mean runoff per unit of normalised projected distance across catchment. 0.0 = no spatial gradient.</summary>
        public double SlopeRunoffWithLocation { get; init; } = 0.0;

        /// <summary>Fractional change in mean runoff per unit of normalised elevation range. 0.0 = no elevation gradient.</summary>
        public double SlopeRunoffWithElevation { get; init; } = 0.0;

        /// <summary>Orientation of the horizontal runoff gradient axis, in degrees clockwise from north. 0.0 = north–south axis.</summary>
        public double OrientationDegrees { get; init; } = 0.0;

        /// <summary>Per-node random multipliers on mean runoff. Null = no random spatial variation (all nodes get factor 1.0).</summary>
        public double[] IndividualRunoffFactors { get; init; } = null;

        // ── Detection of historical dams ──

        /// <summary>
        /// Sampled annual probability of non-detection for historical dam mapping.
        /// 0.0 = all dams detected in their actual construction year (no delay).
        /// Stored for audit/logging; the actual delays are in <see cref="NodeStartDateDelayYears"/>.
        /// </summary>
        public double HistoricalNonDetectionProbability { get; init; } = 0.0;

        /// <summary>
        /// Per-node number of years to delay each water body's StartDate, drawn from a geometric distribution parameterised by <see cref="HistoricalNonDetectionProbability"/>.
        /// Null = no detection delay (all nodes start at their base date).
        /// </summary>
        public int[] NodeStartDateDelayYears { get; init; } = null;
    }
}