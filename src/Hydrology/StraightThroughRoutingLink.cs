// <copyright file="StraightThroughRoutingLink.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.ModelRun
{
    public class StraightThroughRoutingLink
    {
        /// <summary>Upstream flow entering this routing link (ML).</summary>
        public double UpstreamFlow { get; set; }

        /// <summary>Upstream flow component originating from bypass (ML).</summary>
        public double UpstreamFlowFromBypass { get; set; }

        /// <summary>Upstream flow component originating from spill (ML).</summary>
        public double UpstreamFlowFromSpill { get; set; }

        /// <summary>Upstream flow component originating from catchment runoff (ML).</summary>
        public double UpstreamFlowFromCatchment { get; set; }

        /// <summary>Downstream flow leaving this routing link (ML).</summary>
        public double DownstreamFlow { get; set; }

        /// <summary>Downstream flow component originating from bypass (ML).</summary>
        public double DownstreamFlowFromBypass { get; set; }

        /// <summary>Downstream flow component originating from spill (ML).</summary>
        public double DownstreamFlowFromSpill { get; set; }

        /// <summary>Downstream flow component originating from catchment runoff (ML).</summary>
        public double DownstreamFlowFromCatchment { get; set; }

        /// <summary>Total upstream catchment area draining through this link (km²).</summary>
        public double TotalUpstreamCatchmentAreaKM2 { get; set; }

        /// <summary>Total upstream non-water-body catchment area (km²).</summary>
        public double TotalUpstreamNonWaterCatchmentAreaKM2 { get; set; }

        /// <summary>Non-water-body catchment area upstream of all dams (km²).</summary>
        public double NonWaterCatchmentAreaUpstreamOfDamsKM2 { get; set; }

        /// <summary>Non-water-body catchment area downstream of all dams (km²).</summary>
        public double NonWaterCatchmentAreaDownstreamOfDamsKM2 { get; set; }

        /// <summary>Volume balance misclosure (ML). Always 0.0 for straight-through routing.</summary>
        public double VolumeBalanceMisclosure { get; set; }

        /// <summary>Routes flow straight through — downstream flow equals upstream flow.</summary>
        public void RunTimeStep()
        {
            this.DownstreamFlow = this.UpstreamFlow;
            this.DownstreamFlowFromBypass = this.UpstreamFlowFromBypass;
            this.DownstreamFlowFromSpill = this.UpstreamFlowFromSpill;
            this.DownstreamFlowFromCatchment = this.UpstreamFlowFromCatchment;
            this.VolumeBalanceMisclosure = this.UpstreamFlow - this.DownstreamFlow;
        }
    }
}
