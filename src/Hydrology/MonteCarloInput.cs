// <copyright file="MonteCarloInput.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelSettings
{
    /// <summary>Defines a single Monte Carlo input variable with distribution type, bounds, and optional Box-Cox transform parameters.</summary>
    public class MonteCarloInput
    {
        /// <summary>Probability distribution type for this Monte Carlo input variable.</summary>
        public ProbabilityDistributionType DistributionType { get; set; } = ProbabilityDistributionType.Uniform;

        /// <summary>
        /// Minimum possible value that can be generated for Monte Carlo input variable.
        /// </summary>
        public double Min { get; set; } = 0.0;

        /// <summary>
        /// Maximum possible value that can be generated for Monte Carlo input variable.
        /// </summary>
        public double Max { get; set; } = 1.0;

        /// <summary>
        /// Only relevant for Box Cox transformed Gaussian - Lambda value for transformation of variable.
        /// </summary>
        public double BoxCoxLambda { get; set; } = 1.0;

        /// <summary>
        /// Only relevant for Gaussian and Box Cox transformed Gaussian - mean value of distribution.
        /// </summary>
        public double TransformedMean { get; set; } = 0.0;

        /// <summary>
        /// Only relevant for Gaussian and Box Cox transformed Gaussian - standard deviation of distribution.
        /// </summary>
        public double TransformedSD { get; set; } = 1.0;
    }
}
