// <copyright file="SubcatchmentInflowModel.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    /// <summary>
    /// Subcatchment inflow model that generates runoff as a function of non-water-body area, a uniform inflow rate (ML/km²), and an optional spatial inflow multiplier.
    /// When InflowMultiplier is 1.0 (the default), inflow is uniform across all subcatchments.
    /// </summary>
    public class SubcatchmentInflowModel : BaseSubcatchmentModel
    {
        /// <summary>Area of the downstream water body surface within this subcatchment (km²).</summary>
        public double WaterBodyAreaKM2 { get; set; }

        /// <summary>Non-water-body area within this subcatchment (km²). Calculated in BeforeRunTimeStep.</summary>
        public double NonWaterBodyAreaKM2 { get; set; }

        /// <summary>Uniform inflow rate applied to all subcatchments (ML per km²) for this time step.</summary>
        public double InflowRateMLPerKM2 { get; set; }

        /// <summary>Volume balance misclosure for this subcatchment (ML). Always 0.0 — no storage.</summary>
        public double VolumeBalanceMisclosure { get; set; } = 0.0;

        /// <summary>Spatial runoff multiplier for this subcatchment. Default 1.0 = no modification.</summary>
        public double InflowMultiplier { get; set; } = 1.0;

        public override void BeforeRunTimeStep(bool isLegacySTEDICalculationMethods)
        {
            if (isLegacySTEDICalculationMethods)
            {
                // Consistent with legacy STEDI
                this.NonWaterBodyAreaKM2 = this.AreaKM2;
            }
            else
            {
                // Below is actually the correct formula but it is inconsistent with legacy STEDI
                this.NonWaterBodyAreaKM2 = Math.Max(0, this.AreaKM2 - this.WaterBodyAreaKM2);
            }
        }

        public override void RunTimeStep()
        {
            this.DownstreamFlow = this.NonWaterBodyAreaKM2 * this.InflowRateMLPerKM2 * this.InflowMultiplier;
            this.VolumeBalanceMisclosure = 0.0;
        }
    }
}
