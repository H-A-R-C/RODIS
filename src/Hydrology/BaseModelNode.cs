// <copyright file="BaseModelNode.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    public abstract class BaseModelNode
    {
        /// <summary>Gets or sets Type of model element (WaterBodyNode, ConfluenceNode, etc.).</summary>
        public ModelElementType ModelElementType { get; set; }

        /// <summary>Gets or sets Label for this water body.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Gets or sets Description of this water body.</summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>Gets or sets Reporting group that this water body belongs to.</summary>
        public string ReportingGroup { get; set; } = string.Empty;

        /// <summary>Gets or sets Demand group that this water body belongs to.</summary>
        public string DemandGroup { get; set; } = string.Empty;

        /// <summary>Gets or sets Index for reporting group, for tracking reporting of outputs from water bodies.</summary>
        public int ReportingGroupIndex { get; set; } = -1;

        /// <summary>Gets Starting date for existence of the water body.</summary>
        public DateTime StartDate { get; private set; } = DateTime.MinValue;

        /// <summary>Gets End date for the existence of the water body.</summary>
        public DateTime EndDate { get; private set; } = DateTime.MaxValue;

        /// <summary>Gets or sets Maximum storage capacity volume in ML at any time.</summary>
        public double MaxStorageCapacityVolumeAtSpill { get; set; } = 0.0;

        /// <summary>Gets or sets Storage capacity volume at spill (ML) at the current time step. Zero before a water body exists or after removal.</summary>
        public double StorageCapacityVolumeAtSpill { get; set; } = 0.0;

        /// <summary>Gets or sets Surface area of the water body at spill level (m²). Zero before a water body exists or after removal.</summary>
        public double SurfaceAreaAtSpill { get; set; } = 0.0;

        /// <summary>Gets or sets Mean annual inflow in ML/year, including catchment inflows, spills, bypasses, and pumped inflows.</summary>
        public double MeanAnnualInflow { get; set; } = 0.0;

        /// <summary>Gets or sets Upstream flow entering this node (ML) for the time step.</summary>
        public double UpstreamFlow { get; set; }

        /// <summary>Gets or sets Upstream flow component originating from bypass (ML).</summary>
        public double UpstreamFlowFromBypass { get; set; }

        /// <summary>Gets or sets Upstream flow component originating from spill (ML).</summary>
        public double UpstreamFlowFromSpill { get; set; }

        /// <summary>Gets or sets Upstream flow component originating from catchment runoff (ML).</summary>
        public double UpstreamFlowFromCatchment { get; set; }

        /// <summary>Gets or sets Cumulative upstream and pumped inflow volume (ML) over all time steps where the node exists.</summary>
        public double SumUpstreamAndPumpedInflows { get; set; } = 0.0;

        /// <summary>Gets or sets Cumulative number of days over which upstream and pumped inflows have been summed.</summary>
        public double SumDaysOfUpstreamAndPumpedInflows { get; set; } = 0.0;

        /// <summary>Gets or sets Total downstream flow leaving this node (ML) for the time step.</summary>
        public double DownstreamFlow { get; set; }

        /// <summary>Gets or sets Downstream flow component originating from bypass (ML).</summary>
        public double DownstreamFlowFromBypass { get; set; }

        /// <summary>Gets or sets Downstream flow component originating from spill (ML).</summary>
        public double DownstreamFlowFromSpill { get; set; }

        /// <summary>Gets or sets Downstream flow component originating from catchment runoff (ML).</summary>
        public double DownstreamFlowFromCatchment { get; set; }

        /// <summary>Gets or sets Bypass flow capacity in ML/d.</summary>
        public double BypassFlowCapacity { get; set; } = 0.0;

        /// <summary>Gets or sets Effective bypass flow capacity for this time step (ML/d), accounting for seasonal availability.</summary>
        public double BypassFlowCapacityAtTimeStep { get; set; } = 0.0;

        /// <summary>Gets or sets Start date from which bypass is active for this node.</summary>
        public DateTime StartBypassDate { get; set; } = DateTime.MinValue;

        /// <summary>Gets or sets End date after which bypass is no longer active for this node.</summary>
        public DateTime EndBypassDate { get; set; } = DateTime.MaxValue;

        /// <summary>Gets or sets Start of the annual bypass season (year component ignored).</summary>
        public DateOnly BypassSeasonStartDateIgnoreYear { get; set; } = new DateOnly(2000, 1, 1);

        /// <summary>Gets or sets End of the annual bypass season (year component ignored).</summary>
        public DateOnly BypassSeasonEndDateIgnoreYear { get; set; } = new DateOnly(2000, 12, 31);

        /// <summary>Gets or sets Pumped inflow capacity in ML/d.</summary>
        public double PumpedInflowCapacity { get; set; } = 0.0;

        /// <summary>Gets or sets Effective pumped inflow capacity for this time step (ML/d), accounting for seasonal availability.</summary>
        public double PumpedInflowCapacityAtTimeStep { get; set; } = 0.0;

        /// <summary>Gets or sets Actual pumped inflow volume for this time step (ML), limited to spare storage capacity.</summary>
        public double PumpedInflow { get; set; } = 0.0;

        /// <summary>Gets or sets Start date from which pumped inflow is active for this node.</summary>
        public DateTime StartPumpedInflowDate { get; set; } = DateTime.MinValue;

        /// <summary>Gets or sets End date after which pumped inflow is no longer active for this node.</summary>
        public DateTime EndPumpedInflowDate { get; set; } = DateTime.MaxValue;

        /// <summary>Gets or sets Start of the annual pumped inflow season (year component ignored).</summary>
        public DateOnly PumpedInflowSeasonStartDateIgnoreYear { get; set; } = new DateOnly(2000, 1, 1);

        /// <summary>Gets or sets End of the annual pumped inflow season (year component ignored).</summary>
        public DateOnly PumpedInflowSeasonEndDateIgnoreYear { get; set; } = new DateOnly(2000, 12, 31);

        /// <summary>Gets or sets Easting coordinate of this node (m), used for spatial runoff variation.</summary>
        public double Easting { get; set; }

        /// <summary>Gets or sets Northing coordinate of this node (m), used for spatial runoff variation.</summary>
        public double Northing { get; set; }

        /// <summary>Gets or sets Elevation of this node (m AHD), used for spatial runoff variation.</summary>
        public double Elevation { get; set; }

        /// <summary>Gets or sets Total upstream catchment area draining to this node (km²).</summary>
        public double TotalUpstreamCatchmentAreaKM2 { get; set; }

        /// <summary>Gets or sets Total upstream non-water-body catchment area (km²).</summary>
        public double TotalUpstreamNonWaterCatchmentAreaKM2 { get; set; }

        /// <summary>Gets or sets Non-water-body catchment area upstream of all dams draining to this node (km²).</summary>
        public double NonWaterCatchmentAreaUpstreamOfDamsKM2 { get; set; }

        /// <summary>Gets or sets Non-water-body catchment area downstream of all dams draining to this node (km²).</summary>
        public double NonWaterCatchmentAreaDownstreamOfDamsKM2 { get; set; }

        /// <summary>Gets or sets Mass balance misclosure for this node (ML). Should be near zero.</summary>
        public double VolumeBalanceMisclosure { get; set; }

        /// <summary>Gets or sets Label of the next downstream node, for diagnostics and CSV metadata output.</summary>
        public string NextDownstreamLabel { get; set; } = string.Empty;

        /// <summary>Gets or sets Locked original start date, set once by SetBaseStartEndDates. Used by ResetToBaseStartEndDates to restore after MC date shifting.</summary>
        private DateTime BaseStartDate { get; set; } = DateTime.MinValue;

        /// <summary>Gets or sets Locked original end date, set once by SetBaseStartEndDates. Used by ResetToBaseStartEndDates to restore after MC date shifting.</summary>
        private DateTime BaseEndDate { get; set; } = DateTime.MaxValue;

        /// <summary>
        /// Runs one time step of the model for this model node.
        /// </summary>
        /// <param name="simulationDateTime">Datetime for start of time step in simulation, relative to the climate and flow data inputs.</param>
        /// <param name="timeStep">Time step Modelled, starting at the simulation date time.</param>
        /// <param name="isLegacySTEDICalculationMethods">True if calculations are to mirror legacy STEDI version 1.20 (SKM, 2012).</param>
        /// <param name="isAdoptedRun">True if this run is to be adopted. Allows re-use of code for iterative solution for unimpacted flow, with isAdoptedRun = true only on last run when iterative solution has converged.</param>
        public abstract void RunTimeStep(DateTime simulationDateTime, TimeSpan timeStep, bool isLegacySTEDICalculationMethods, bool isAdoptedRun);

        /// <summary>Calculates the mean annual inflow (ML/year) from cumulative inflow and duration totals.</summary>
        /// <returns>Mean annual inflow in ML/year, or 0.0 if no inflow days have been recorded.</returns>
        public double CalculateMeanAnnualInflow()
        {
            if (this.SumDaysOfUpstreamAndPumpedInflows <= 0)
                return 0.0;

            return this.SumUpstreamAndPumpedInflows / (this.SumDaysOfUpstreamAndPumpedInflows / 365.25);
        }

        /// <summary>Sets the current start date without modifying the locked base start date. Use for MC detection delays and scenario date shifting.</summary>
        /// <param name="startDate">New current start date.</param>
        public void SetStartDate(DateTime startDate)
        {
            this.StartDate = startDate;
        }

        /// <summary>Sets the current end date without modifying the locked base end date. Use for MC detection delays and scenario date shifting.</summary>
        /// <param name="endDate">New current end date.</param>
        public void SetEndDate(DateTime endDate)
        {
            this.EndDate = endDate;
        }

        /// <summary>
        /// Sets start and end dates to specified starting dates and times, and sets and locks private members BaseStartDate and BaseEndDate.
        /// </summary>
        /// <param name="startDate">Start date for simulation of node in model.</param>
        /// <param name="endDate">End date for simulation of node in model. End date must be the same as or after the start date.</param>
        public void SetBaseStartEndDates(DateTime startDate, DateTime endDate)
        {
            if (endDate < startDate)
            {
                Console.WriteLine($"WARNING: Water body ID {this.Label} has end date "
                    + $"{endDate:dd/MM/yyyy} preceding start date {startDate:dd/MM/yyyy}. "
                    + "End date set equal to start date.");
                endDate = startDate;
            }

            this.StartDate = startDate;
            this.EndDate = endDate;
            this.BaseStartDate = startDate;
            this.BaseEndDate = endDate;
        }

        /// <summary>
        /// Resets the start and end dates for the node to the base values of start and end date.
        /// </summary>
        public void ResetToBaseStartEndDates()
        {
            this.StartDate = this.BaseStartDate;
            this.EndDate = this.BaseEndDate;
        }
    }
}
