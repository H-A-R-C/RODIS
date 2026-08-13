// <copyright file="ConfluenceModelNode.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    /// <summary>Confluence node that passes upstream flow straight through to the downstream link without storage or demand.</summary>
    public class ConfluenceModelNode : BaseModelNode
    {
        /// <summary>Gets or sets Array of upstream inflow values for future multi-input confluence support. Currently unused.</summary>
        public double[] UpstreamInflows { get; set; } = null;

        /// <summary>Runs one time step: passes upstream flow straight through and accumulates inflow statistics.</summary>
        /// <param name="simulationDateTime">Start datetime of this time step.</param>
        /// <param name="timeStep">Duration of the modelling time step.</param>
        /// <param name="isLegacyRODISCalculationMethods">True to use legacy RODIS v1.20 calculation methods.</param>
        /// <param name="isAdoptedRun">True if this is the adopted (final) run for the time step.</param>
        public override void RunTimeStep(DateTime simulationDateTime, TimeSpan timeStep, bool isLegacyRODISCalculationMethods, bool isAdoptedRun = true)
        {
            this.VolumeBalanceMisclosure = 0;

            // Confluence nodes do not support pumped inflows
            this.PumpedInflow = 0.0;
            this.PumpedInflowCapacityAtTimeStep = 0.0;
            this.PumpedInflowCapacity = 0.0;

            // Sum upstream inflows and duration over which upstream inflows are summed
            this.SumUpstreamAndPumpedInflows += this.PumpedInflow + this.UpstreamFlow;
            this.SumDaysOfUpstreamAndPumpedInflows += timeStep.TotalDays;

            // Set downstream flows equal to upstream flows
            this.DownstreamFlow = this.UpstreamFlow;
            this.DownstreamFlowFromBypass = this.UpstreamFlowFromBypass;
            this.DownstreamFlowFromSpill = this.UpstreamFlowFromSpill;
            this.DownstreamFlowFromCatchment = this.UpstreamFlowFromCatchment;
        }
    }
}
