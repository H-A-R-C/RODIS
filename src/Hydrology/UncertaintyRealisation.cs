// <copyright file="UncertaintyRealisation.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.MonteCarlo
{
    /// <summary>
    /// Holds sampled uncertainty parameter values for a single Monte Carlo iteration.
    /// A single realisation is shared across all scenarios within an iteration to ensure consistent treatment of dams.
    /// </summary>
    public class UncertaintyRealisation
    {
        /// <summary>Gets iteration index (0-based).</summary>
        public int IterationIndex { get; }

        /// <summary>Gets random seed used for this iteration, for reproducibility.</summary>
        public ulong IterationSeed { get; }

        // -- Phase A: U3, U6, U11 -------------------------------------------------------------------
        /// <summary>Gets or sets multiplicative volume error per dam (U3). 1.0 = no error.</summary>
        public double[] DamVolumeMultipliers { get; set; }

        /// <summary>Gets or sets annual demand as a fraction of adjusted storage volume per dam (U6). 0.0 = no demand.</summary>
        public double[] DamDemandFactors { get; set; }

        /// <summary>Gets or sets spatial tilt coefficient for runoff distribution (U11). 0.0 = uniform runoff per unit area.</summary>
        public double TiltCoefficient { get; set; }

        /// <summary>Gets or sets random runoff perturbation per dam (U11, additive, applied before flow-conservation rescaling).</summary>
        public double[] DamRunoffPerturbations { get; set; }

        // -- Phase B: U8, U7, U2, U1 ----------------------------------------------------------------
        /// <summary>Gets or sets per-dam seepage rate in mm/day at full supply level (U8). 0.0 = no seepage. Null when disabled.</summary>
        public double[] DamSeepageRates { get; set; }

        /// <summary>Gets or sets per-dam perturbed monthly demand proportions [numDams][12] (U7). Null = use defaults.</summary>
        public double[][] DamMonthlyDemandProportions { get; set; }

        /// <summary>Gets or sets per-dam detection delay in years (U2). Null when disabled.</summary>
        public int[] DetectionDelayYears { get; set; }

        /// <summary>Gets or sets per-dam flag: true if classified as a natural water body (U1). Null when disabled.</summary>
        public bool[] IsNaturalWaterBody { get; set; }

        // -- Phase C: U4, U9, U10, U5 ---------------------------------------------------------------
        /// <summary>Gets or sets per-dam catchment area multiplicative error (U4). Null when disabled.</summary>
        public double[] CatchmentAreaMultipliers { get; set; }

        /// <summary>Gets or sets per-dam rainfall multiplicative factor (U9). 1.0 = no adjustment. Null when disabled.</summary>
        public double[] RainfallMultipliers { get; set; }

        /// <summary>Gets or sets per-dam evaporation multiplicative factor (U10). 1.0 = no adjustment. Null when disabled.</summary>
        public double[] EvaporationMultipliers { get; set; }

        /// <summary>Gets or sets per-dam flag: true if this dam ignores upstream dam spill/bypass inflows (U5 simplified). Null when disabled.</summary>
        public bool[] UseIndependentTopology { get; set; }

        // -- Experimental Treatments: E2, E6 --------------------------------------------------------
        /// <summary>Gets or sets per-dam flag: true if surveyed in this iteration (E2). Null when disabled.</summary>
        public bool[] IsSurveyed { get; set; }

        /// <summary>Gets or sets per-dam flag: true if monitored in this iteration (E6). Null when disabled.</summary>
        public bool[] IsMonitored { get; set; }

        // -- Unused Placeholders (reserved for future full-topology rewiring) ------------------------
        /// <summary>Gets or sets per-dam downstream recipient index (U5 full rewiring, not yet implemented). Null when disabled.</summary>
        public int[] DownstreamRecipientIndex { get; set; }

        /// <summary>Gets or sets per-dam demand pattern library index (reserved, not yet implemented). Null when disabled.</summary>
        public int[] DemandPatternIndices { get; set; }

        /// <summary>
        /// Initialises a new UncertaintyRealisation for the specified iteration.
        /// Arrays are not allocated here — they are populated by <see cref="MonteCarloUncertaintySampler"/>.
        /// </summary>
        /// <param name="iterationIndex">Zero-based iteration index.</param>
        /// <param name="iterationSeed">Random seed for this iteration.</param>
        public UncertaintyRealisation(int iterationIndex, ulong iterationSeed)
        {
            IterationIndex = iterationIndex;
            IterationSeed = iterationSeed;
        }

        // TODO: Confirm whether remaining methods in this class are needed anymore.

        /// <summary>
        /// Returns true if the Phase A parameters (U3, U6, U11) have been populated with valid arrays of the expected length.
        /// </summary>
        /// <param name="expectedDamCount">Number of dams in the simulation.</param>
        public bool ValidatePhaseA(int expectedDamCount)
        {
            if (DamVolumeMultipliers == null || DamVolumeMultipliers.Length != expectedDamCount)
                return false;

            if (DamDemandFactors == null || DamDemandFactors.Length != expectedDamCount)
                return false;

            if (DamRunoffPerturbations == null || DamRunoffPerturbations.Length != expectedDamCount)
                return false;

            for (int i = 0; i < expectedDamCount; i++)
            {
                if (DamVolumeMultipliers[i] <= 0.0)
                    return false;
                if (DamDemandFactors[i] < 0.0)
                    return false;
            }

            return true;
        }

        /// <summary>Returns a concise summary string for logging, e.g. "Iter 42: Tilt=0.12, VolMult[0]=1.34, DemFac[0]=0.41".</summary>
        public override string ToString()
        {
            string volSample = DamVolumeMultipliers != null && DamVolumeMultipliers.Length > 0 ? $"{DamVolumeMultipliers[0]:F3}" : "n/a";
            string demSample = DamDemandFactors != null && DamDemandFactors.Length > 0 ? $"{DamDemandFactors[0]:F3}" : "n/a";
            return $"Iter {IterationIndex}: Seed={IterationSeed}, Tilt={TiltCoefficient:F4}, VolMult[0]={volSample}, DemFac[0]={demSample}";
        }
    }

}
