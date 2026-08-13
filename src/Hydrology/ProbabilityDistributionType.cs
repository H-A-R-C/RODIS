// <copyright file="ProbabilityDistributionType.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelSettings
{
    /// <summary>Supported probability distribution types for Monte Carlo sampling.</summary>
    public enum ProbabilityDistributionType { Uniform, BoxCoxTransfomedGaussian, Gaussian };
}
