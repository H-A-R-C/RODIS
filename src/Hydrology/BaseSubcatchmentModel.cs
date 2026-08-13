// <copyright file="BaseSubcatchmentModel.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

public abstract class BaseSubcatchmentModel
{
    /// <summary>Gets or sets Total subcatchment area in km².</summary>
    public double AreaKM2 { get; set; }

    /// <summary>Gets or sets Flow leaving the subcatchment downstream in ML for the time step.</summary>
    public double DownstreamFlow { get; set; }

    /// <summary>Runs one time step of the subcatchment inflow model.</summary>
    public abstract void RunTimeStep();

    /// <summary>Pre-calculates values needed before the time step run (e.g. non-water-body area).</summary>
    /// <param name="isLegacySTEDICalculationMethods">True to use legacy STEDI v1.20 area calculation.</param>
    public abstract void BeforeRunTimeStep(bool isLegacySTEDICalculationMethods);
}
