// <copyright file="WaterBodyModelNode.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    using RODIS.Static;

    public class WaterBodyModelNode : BaseModelNode
    {
        // -- Storage state properties --
        /// <summary>Volume in storage at the start of the current time step (ML).</summary>
        public double StartTimeStepVolumeInStorage { get; set; }

        /// <summary>Volume currently in storage (ML). Updated during RunTimeStep.</summary>
        public double VolumeInStorage { get; set; }

        /// <summary>Change in storage volume over the current time step (ML).</summary>
        public double ChangeInVolumeInStorageForTimeStep { get; set; }

        /// <summary>Current water surface area (m²), calculated from volume in storage.</summary>
        public double SurfaceAreaStored { get; set; }

        // -- Climate input properties --
        /// <summary>Rainfall depth for this time step (mm).</summary>
        public double Rainfall { get; set; }

        /// <summary>Volume of rainfall falling directly on the water body surface (ML).</summary>
        public double RainfallVolume { get; set; }

        /// <summary>Evaporation depth for this time step (mm).</summary>
        public double Evaporation { get; set; }

        /// <summary>Volume of evaporation from the water body surface (ML).</summary>
        public double EvaporationVolume { get; set; }

        /// <summary>Net rainfall volume = rainfall minus evaporation (ML). Can be negative.</summary>
        public double NetRainfallVolume { get; set; }

        // -- Demand properties --
        /// <summary>Unrestricted demand for this time step (ML), set by the demand model before RunTimeStep.</summary>
        public double UnrestrictedDemand { get; set; }

        /// <summary>Actual demand volume extracted, limited to available storage (ML).</summary>
        public double DemandVolumeExtracted { get; set; }

        /// <summary>Volume (ML) lost due to dam removal at this time step. Non-zero only on the first time step after a dam is decommissioned.</summary>
        public double DamRemovalStorageLoss { get; set; } = 0.0;

        // -- Geometry properties --
        /// <summary>Exponent of the power-law relationship between surface area (m²) and volume (ML).</summary>
        public double VolumeSurfaceAreaRelationshipExponent { get; set; } = 1.0;

        // -- Seepage properties --
        /// <summary>Seepage loss rate at full supply level (ML per time step when full).</summary>
        public double SeepageLossRateAtFull { get; set; } = 0.0;

        /// <summary>Exponent controlling how seepage loss scales with volume fraction.</summary>
        public double SeepageLossVolumeRelationshipExponent { get; set; } = 0.0;

        /// <summary>Seepage loss volume for this time step (ML).</summary>
        public double SeepageLossVolume { get; set; }

        // -- Monte Carlo uncertainty analysis properties --
        /// <summary>Per-dam rainfall multiplier for U9 uncertainty. Default 1.0 = no adjustment.</summary>
        public double LocalRainfallMultiplier { get; set; } = 1.0;

        /// <summary>Per-dam evaporation multiplier for U10 uncertainty. Default 1.0 = no adjustment.</summary>
        public double LocalEvaporationMultiplier { get; set; } = 1.0;

        /// <summary>When true, this node ignores spill/bypass inflows from upstream dams — only local subcatchment runoff enters storage. Used for independent topology mode (U5).</summary>
        public bool IgnoreUpstreamDamFlows { get; set; } = false;

        /// <summary>
        /// Runs one time step of the water body model: processes bypass, net rainfall, seepage, demand extraction,
        /// inflows, pumped inflows, and spills. Updates storage volume and calculates mass balance misclosure.
        /// </summary>
        /// <param name="simulationDateTime">Current simulation date/time controlling dam existence and seasonal rules.</param>
        /// <param name="timeStep">Duration of this modelling time step.</param>
        /// <param name="isLegacySTEDICalculationMethods">If true, uses legacy STEDI v1.20 surface area assumptions.</param>
        /// <param name="isAdoptedRun">If true, advances storage state to next time step; false for iterative solution trials.</param>
        public override void RunTimeStep(DateTime simulationDateTime, TimeSpan timeStep, bool isLegacySTEDICalculationMethods, bool isAdoptedRun = true)
        {
            this.VolumeInStorage = this.StartTimeStepVolumeInStorage;

            // Mass balance: capture starting volume before the pre-existence block may zero it.
            double trueStartingVolume = this.StartTimeStepVolumeInStorage;

            // Calculate opening surface area based on starting volume.
            this.SurfaceAreaStored = this.CalculateSurfaceArea();

            if (simulationDateTime < this.StartDate || simulationDateTime >= this.EndDate)
            {
                // Before dam start date or after dam end date, so just do a straight pass through.
                this.DownstreamFlow = this.UpstreamFlow;
                this.DownstreamFlowFromBypass = this.UpstreamFlowFromBypass;
                this.DownstreamFlowFromSpill = this.UpstreamFlowFromSpill;
                this.DownstreamFlowFromCatchment = this.UpstreamFlowFromCatchment;

                this.RainfallVolume = 0.0;
                this.EvaporationVolume = 0.0;
                this.NetRainfallVolume = 0.0;
                this.SeepageLossVolume = 0.0;
                this.UnrestrictedDemand = 0.0;
                this.DemandVolumeExtracted = 0.0;
                this.PumpedInflowCapacityAtTimeStep = 0.0;
                this.PumpedInflow = 0.0;

                this.StartTimeStepVolumeInStorage = 0.0;
                this.VolumeInStorage = 0.0;
                this.SurfaceAreaStored = 0.0;
                this.StorageCapacityVolumeAtSpill = 0.0;

                this.DamRemovalStorageLoss = trueStartingVolume;
            }
            else
            {
                // Mass balance: no removal loss while the dam is active.
                this.DamRemovalStorageLoss = 0.0;

                // Dam exists, so storage capacity to spill is the maximum volume of the dam for any time step.
                this.StorageCapacityVolumeAtSpill = this.MaxStorageCapacityVolumeAtSpill;

                // First deal with bypass.
                if (simulationDateTime >= this.StartBypassDate && simulationDateTime <= this.EndBypassDate && this.BypassFlowCapacity > 0.0)
                {
                    // Bypass flow capacity is specified in ML/d, so calculate capacity for this modelling time step.
                    double bypassVolumeCapacityAtTimeStep = 0.0;
                    DateTime endPeriod = simulationDateTime.Add(timeStep);
                    TimeSpan oneDay = new TimeSpan(1, 0, 0, 0);

                    for (DateTime dateTime = simulationDateTime; dateTime < endPeriod; dateTime = dateTime.Add(oneDay))
                    {
                        if (InSeason.IsInSeason(dateTime, this.BypassSeasonStartDateIgnoreYear, this.BypassSeasonEndDateIgnoreYear))
                        {
                            bypassVolumeCapacityAtTimeStep += this.BypassFlowCapacity;
                        }
                    }

                    if (bypassVolumeCapacityAtTimeStep > 0.0)
                    {
                        this.BypassFlowCapacityAtTimeStep = bypassVolumeCapacityAtTimeStep / (endPeriod - simulationDateTime).TotalDays;
                        this.DownstreamFlowFromBypass = Math.Min(bypassVolumeCapacityAtTimeStep, this.UpstreamFlow);
                    }
                    else
                    {
                        this.BypassFlowCapacityAtTimeStep = this.DownstreamFlowFromBypass = 0.0;
                    }
                }
                else
                {
                    this.BypassFlowCapacityAtTimeStep = this.DownstreamFlowFromBypass = 0.0;
                }

                // Water body accounting order:
                // 1. Upstream inflow after bypass is added.
                // 2. Pumped inflow is added.
                // 3. Net rainfall/climate is applied, capped against water available before demand.
                // 4. Seepage is applied.
                // 5. Demand is extracted from the residual water.
                // 6. Spill is calculated from the resulting storage.
                //
                // This matches the legacy STEDI behaviour observed in the regression scenarios:
                // same-day inflow is available to the dam, climate has priority over demand,
                // and demand receives only the residual after climate and seepage losses.

                // Add upstream inflows after bypass.
                this.VolumeInStorage += this.UpstreamFlow - this.DownstreamFlowFromBypass;

                // Add pumped inflows.
                if (simulationDateTime >= this.StartPumpedInflowDate && simulationDateTime <= this.EndPumpedInflowDate && this.PumpedInflowCapacity > 0.0)
                {
                    // Pumped inflow capacity is specified in ML/d, so calculate capacity for this modelling time step.
                    double pumpVolumeCapacityAtTimeStep = 0.0;
                    DateTime endPeriod = simulationDateTime.Add(timeStep);
                    TimeSpan oneDay = new TimeSpan(1, 0, 0, 0);

                    for (DateTime dateTime = simulationDateTime; dateTime < endPeriod; dateTime = dateTime.Add(oneDay))
                    {
                        if (InSeason.IsInSeason(dateTime, this.PumpedInflowSeasonStartDateIgnoreYear, this.PumpedInflowSeasonEndDateIgnoreYear))
                        {
                            pumpVolumeCapacityAtTimeStep += this.PumpedInflowCapacity;
                        }
                    }

                    if (pumpVolumeCapacityAtTimeStep > 0.0)
                    {
                        this.PumpedInflowCapacityAtTimeStep = pumpVolumeCapacityAtTimeStep / (endPeriod - simulationDateTime).TotalDays;

                        double spareCapacity = Math.Max(0.0, this.StorageCapacityVolumeAtSpill - this.VolumeInStorage);
                        this.PumpedInflow = Math.Min(pumpVolumeCapacityAtTimeStep, spareCapacity);
                    }
                    else
                    {
                        this.PumpedInflowCapacityAtTimeStep = this.PumpedInflow = 0.0;
                    }
                }
                else
                {
                    this.PumpedInflowCapacityAtTimeStep = this.PumpedInflow = 0.0;
                }

                this.VolumeInStorage += this.PumpedInflow;

                // Sum upstream inflows and duration over which upstream inflows are summed.
                this.SumUpstreamAndPumpedInflows += this.PumpedInflow + this.UpstreamFlow;
                this.SumDaysOfUpstreamAndPumpedInflows += timeStep.TotalDays;

                // Capture the water available before demand. Climate must be capped against this value so demand cannot take water that legacy STEDI reports as climate.
                double volumeAvailableBeforeDemand = Math.Max(0.0, this.VolumeInStorage);

                // Net rainfall / climate.
                double surfaceAreaForRainfall = 0.0;
                if (isLegacySTEDICalculationMethods)
                {
                    // Legacy STEDI version 1.20 assumes surface area is constant at full level.
                    surfaceAreaForRainfall = this.SurfaceAreaAtSpill;
                }
                else
                {
                    // Use the surface area calculated from opening storage at the start of this timestep.
                    surfaceAreaForRainfall = this.SurfaceAreaStored;
                }

                // Rainfall in mm, surface area in m2, unit conversion to ML.
                this.RainfallVolume = this.Rainfall * surfaceAreaForRainfall * 1.0E-6;

                // Evaporation in mm, surface area in m2, unit conversion to ML.
                this.EvaporationVolume = this.Evaporation * surfaceAreaForRainfall * 1.0E-6;

                // Climate has priority over demand, so evaporation is capped against pre-demand available water plus same-day rainfall.
                this.EvaporationVolume = Math.Min(volumeAvailableBeforeDemand + this.RainfallVolume, this.EvaporationVolume);

                this.NetRainfallVolume = this.RainfallVolume - this.EvaporationVolume;
                this.VolumeInStorage += this.NetRainfallVolume;

                // Seepage after climate.
                this.SeepageLossVolume = this.CalculateSeepageLoss();
                this.VolumeInStorage -= this.SeepageLossVolume;

                if (this.VolumeInStorage < 0.0 && this.VolumeInStorage > -1.0E-9)
                    this.VolumeInStorage = 0.0;

                // Demand receives only the residual after climate and seepage.
                double demandRequest = Math.Max(0.0, this.UnrestrictedDemand);
                this.DemandVolumeExtracted = Math.Min(demandRequest, Math.Max(0.0, this.VolumeInStorage));
                this.VolumeInStorage -= this.DemandVolumeExtracted;

                if (this.VolumeInStorage < 0.0 && this.VolumeInStorage > -1.0E-9)
                    this.VolumeInStorage = 0.0;

                // Finally, deal with spills.
                this.DownstreamFlowFromSpill = Math.Max(0.0, this.VolumeInStorage - this.StorageCapacityVolumeAtSpill);
                this.VolumeInStorage -= this.DownstreamFlowFromSpill;

                // Re-attribute spills that originated as upstream bypass flows:
                // If upstream bypass inflow exceeded this node's bypass capacity, the excess entered storage and may have spilled.
                // Attribute that spill back to bypass rather than counting it as dam spill.
                double bypassInflowsSpilled = Math.Max(0.0, Math.Min(this.DownstreamFlowFromSpill, this.UpstreamFlowFromBypass - this.BypassFlowCapacity));
                this.DownstreamFlowFromBypass += bypassInflowsSpilled;
                this.DownstreamFlowFromSpill -= bypassInflowsSpilled;

                // Calculate total downstream flow.
                this.DownstreamFlow = this.DownstreamFlowFromBypass + this.DownstreamFlowFromSpill;
                this.DownstreamFlowFromCatchment = 0.0;
            }

            // Calculate change in volume in storage over time step.
            this.ChangeInVolumeInStorageForTimeStep = this.VolumeInStorage - trueStartingVolume;

            // Calculate mass balance misclosure.
            // NOTE: When a dam is removed (simulationDateTime >= EndDate), stored volume is set to zero.
            // Water previously in storage is not released downstream. This is a known simplification.
            this.VolumeBalanceMisclosure = this.NetRainfallVolume + this.PumpedInflow + this.UpstreamFlow
                - (this.ChangeInVolumeInStorageForTimeStep + this.SeepageLossVolume + this.DemandVolumeExtracted + this.DownstreamFlow + this.DamRemovalStorageLoss);

            if (isAdoptedRun)
            {
                // Set the volume in storage for the start of the next time step to the end-of-time-step volume in storage.
                this.StartTimeStepVolumeInStorage = this.VolumeInStorage;

                // Calculate opening surface area based on starting volume.
                this.SurfaceAreaStored = this.CalculateSurfaceArea();
            }
        }

        /// <summary>
        /// Calculates surface area in m² as a function of volume stored.
        /// Public because it is also called during model initialisation to set starting surface area.
        /// </summary>
        /// <returns>Surface area in m2 at that point in time.</returns>
        public double CalculateSurfaceArea()
        {
            if (this.StorageCapacityVolumeAtSpill <= 0 || this.VolumeInStorage <= 0)
            {
                return 0;
            }
            else
            {
                double volumeFraction = this.VolumeInStorage / this.StorageCapacityVolumeAtSpill;
                double surfaceAreaFactor = Math.Pow(volumeFraction, 1.0 / this.VolumeSurfaceAreaRelationshipExponent);
                return Math.Max(0, this.SurfaceAreaAtSpill * surfaceAreaFactor);
            }
        }

        /// <summary>
        /// Calculates seepage loss in ML as a function of volume stored in the water body.
        /// </summary>
        /// <returns>Seepage loss in ML for time step.</returns>
        private double CalculateSeepageLoss()
        {
            if (this.SeepageLossRateAtFull <= 0 || this.StorageCapacityVolumeAtSpill <= 0)
            {
                return 0;
            }
            else
            {
                double volumeFraction = this.VolumeInStorage / this.StorageCapacityVolumeAtSpill;
                double seepageFactor = Math.Pow(volumeFraction, this.SeepageLossVolumeRelationshipExponent);
                double maximumSeepageLoss = seepageFactor * this.SeepageLossRateAtFull;
                return Math.Min(maximumSeepageLoss, this.VolumeInStorage);
            }
        }
    }
}
