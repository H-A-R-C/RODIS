// <copyright file="LegacySTEDIDamNode.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    /// <summary>Represents a single node in the legacy STEDI v1.2 input format, holding geometry, demand, and connectivity data used to construct the internal model network.</summary>
    public class LegacySTEDIDamNode
    {
        /// <summary>Gets or sets Unique integer identifier for this node in the legacy STEDI input array.</summary>
        public int Identifier { get; set; } = -1;

        /// <summary>Gets or sets Surface area of the water body at full supply level (m²).</summary>
        public double SurfaceAreaM2 { get; set; } = 0.0;

        /// <summary>Gets or sets Storage volume of the water body at full supply level (ML).</summary>
        public double VolumeML { get; set; } = 0.0;

        /// <summary>Gets or sets Total upstream catchment area including this node's local catchment (km²).</summary>
        public double TotalCatchmentAreaKM2 { get; set; } = 0.0;

        /// <summary>Gets or sets Local intermediate catchment area for this node (km²).</summary>
        public double IntermediateCatchmentAreaKM2 { get; set; } = 0.0;

        /// <summary>Gets or sets Catchment area contributed by upstream legacy nodes (km²).</summary>
        public double CatchmentAreaFromUpstreamLegacyNodes { get; set; } = 0.0;

        /// <summary>Gets or sets Demand group label for this node.</summary>
        public string DemandGroup { get; set; } = String.Empty;

        /// <summary>Gets or sets Results/reporting group label for this node.</summary>
        public string ResultsGroup { get; set; } = String.Empty;

        /// <summary>Gets or sets boolean variable to indicate whether this node has a pumped (winterfill) inflow.</summary>
        public bool IsWinterfill { get; set; } = false;

        /// <summary>Gets or sets Pumped inflow rate in ML/d.</summary>
        public double WinterfillRate { get; set; } = 0.0;

        /// <summary>Gets or sets Start of the annual pumped inflow (winterfill) season (year component ignored).</summary>
        public DateOnly WinterfillSeasonStartDateIgnoreYear { get; set; }

        /// <summary>Gets or sets End of the annual pumped inflow (winterfill) season (year component ignored).</summary>
        public DateOnly WinterfillSeasonEndDateIgnoreYear { get; set; }

        /// <summary>Gets or sets True if this node has a low-flow bypass.</summary>
        public bool IsBypass { get; set; } = false;

        /// <summary>Gets or sets Bypass flow capacity in ML/d.</summary>
        public double BypassCapacity { get; set; } = 0.0;

        /// <summary>Gets or sets Start of the annual bypass season (year component ignored).</summary>
        public DateOnly BypassSeasonStartDateIgnoreYear { get; set; }

        /// <summary>Gets or sets End of the annual bypass season (year component ignored).</summary>
        public DateOnly BypassSeasonEndDateIgnoreYear { get; set; }

        /// <summary>Gets or sets Index of the next downstream node in the legacy input array (1-based).</summary>
        public int NextDownstreamIdentifier { get; set; } = -1;

        /// <summary>Gets or sets Model element type for this node (WaterBodyNode or ConfluenceNode).</summary>
        public ModelElementType nodeModelType { get; set; } = ModelElementType.WaterBodyNode;

        /// <summary>Array index of this node in the WaterBodyNodes array. -1 = not a water body node.</summary>
        public int WaterBodyNodeID = -1;

        /// <summary>Array index of this node in the ConfluenceNodes array. -1 = not a confluence node.</summary>
        public int ConfluenceNodeID = -1;

        /// <summary>Array index of this node's subcatchment in the SubcatchmentInflowModels array. -1 = no subcatchment.</summary>
        public int SubcatchmentInflowID = -1;

        /// <summary>Array index of this node's demand model in the RepeatingMonthlyDemandModels array. -1 = no repeating demand.</summary>
        public int RepeatingMonthlyDemandID = -1;

        /// <summary>Array index of this node's demand model in the TimeSeriesDemandModels array. -1 = no time series demand.</summary>
        public int TimeSeriesDemandID = -1;

        /// <summary>Array index of the downstream routing link in the StraightThroughRoutingLinks array. -1 = outlet (no link).</summary>
        public int StraightThroughRoutingLinkID = -1;

        /// <summary>Array index of the next downstream water body in the WaterBodyNodes array. -1 = downstream node is a confluence.</summary>
        public int NextDownstreamWaterBodyID = -1;

        /// <summary>Array index of the next downstream confluence in the ConfluenceNodes array. -1 = downstream node is a water body.</summary>
        public int NextDownstreamConfluenceID = -1;

        /// <summary>Creates a WaterBodyModelNode from this legacy node's properties.</summary>
        /// <returns>Initialised water body model node.</returns>
        public WaterBodyModelNode GetWaterBodyModelNode()

        {
            WaterBodyModelNode result = new WaterBodyModelNode()
            {
                ModelElementType = ModelElementType.WaterBodyNode,
                Label = this.Identifier.ToString(),
                ReportingGroup = this.ResultsGroup,
                DemandGroup = this.DemandGroup,
                MaxStorageCapacityVolumeAtSpill = this.VolumeML,
                SurfaceAreaAtSpill = this.SurfaceAreaM2,

                UpstreamFlow = 0.0,
                DownstreamFlow = 0.0,
                Easting = 0.0,
                Northing = 0.0,
                Elevation = 0.0,
                TotalUpstreamCatchmentAreaKM2 = 0.0,
                TotalUpstreamNonWaterCatchmentAreaKM2 = 0.0,
                NonWaterCatchmentAreaUpstreamOfDamsKM2 = 0.0,
                NonWaterCatchmentAreaDownstreamOfDamsKM2 = 0.0,
                VolumeInStorage = 0.0,
                BypassFlowCapacity = 0.0,
                DownstreamFlowFromBypass = 0.0,
                DownstreamFlowFromSpill = 0.0,
                DownstreamFlowFromCatchment = 0.0,
                SurfaceAreaStored = 0.0,
                Rainfall = 0.0,
                RainfallVolume = 0.0,
                Evaporation = 0.0,
                EvaporationVolume = 0.0,
                NetRainfallVolume = 0.0,
                UnrestrictedDemand = 0.0,
                DemandVolumeExtracted = 0.0,
                VolumeSurfaceAreaRelationshipExponent = 1.314,
                SeepageLossRateAtFull = 0.0,
                SeepageLossVolumeRelationshipExponent = 0.0,
                SeepageLossVolume = 0.0,
                VolumeBalanceMisclosure = 0.0,
                StartBypassDate = DateTime.MinValue,
                EndBypassDate = DateTime.MaxValue,
                StartPumpedInflowDate = DateTime.MinValue,
                EndPumpedInflowDate = DateTime.MaxValue,
            };

            result.SetStartDate(DateTime.MinValue);
            result.SetEndDate(DateTime.MaxValue);

            if (this.IsBypass && this.BypassCapacity > 0.0)
            {
                result.BypassFlowCapacity = this.BypassCapacity;
                result.StartBypassDate = result.StartDate;
                result.EndBypassDate = result.EndDate;
                result.BypassSeasonStartDateIgnoreYear = this.BypassSeasonStartDateIgnoreYear;
                result.BypassSeasonEndDateIgnoreYear = this.BypassSeasonEndDateIgnoreYear;
            }

            if (this.IsWinterfill && this.WinterfillRate > 0.0)
            {
                result.PumpedInflowCapacity = this.WinterfillRate;
                result.StartPumpedInflowDate = result.StartDate;
                result.EndPumpedInflowDate = result.EndDate;
                result.PumpedInflowSeasonStartDateIgnoreYear = this.WinterfillSeasonStartDateIgnoreYear;
                result.PumpedInflowSeasonEndDateIgnoreYear = this.WinterfillSeasonEndDateIgnoreYear;
            }

            return result;
        }

        /// <summary>Creates a ConfluenceModelNode from this legacy node's properties.</summary>
        /// <returns>Initialised confluence model node.</returns>
        public ConfluenceModelNode GetConfluenceModelNode()

        {
            ConfluenceModelNode result = new ConfluenceModelNode()
            {
                ModelElementType = ModelElementType.ConfluenceNode,
                Label = this.Identifier.ToString(),
                ReportingGroup = this.ResultsGroup,

                UpstreamFlow = 0.0,
                DownstreamFlow = 0.0,
                Easting = 0.0,
                Northing = 0.0,
                Elevation = 0.0,
                TotalUpstreamCatchmentAreaKM2 = 0.0,
                TotalUpstreamNonWaterCatchmentAreaKM2 = 0.0,
                NonWaterCatchmentAreaUpstreamOfDamsKM2 = 0.0,
                NonWaterCatchmentAreaDownstreamOfDamsKM2 = 0.0,
                VolumeBalanceMisclosure = 0.0,
                BypassFlowCapacity = 0.0,
                StartBypassDate = DateTime.MinValue,
                EndBypassDate = DateTime.MaxValue,
                PumpedInflowCapacity = 0.0,
                StartPumpedInflowDate = DateTime.MinValue,
                EndPumpedInflowDate = DateTime.MaxValue,
            };

            return result;
        }


        /// <summary>Creates a SubcatchmentInflowModel from this legacy node's catchment area.</summary>
        /// <param name="isLegacySTEDICatchmentInflows">True to use legacy area calculation in BeforeRunTimeStep.</param>
        /// <returns>Initialised subcatchment inflow model.</returns>
        public SubcatchmentInflowModel GetSubcatchmentInflowModel(bool isLegacySTEDICatchmentInflows)

        {
            SubcatchmentInflowModel result = new SubcatchmentInflowModel()
            {
                AreaKM2 = this.IntermediateCatchmentAreaKM2,
                WaterBodyAreaKM2 = 0.0,
                InflowRateMLPerKM2 = 0.0,
                VolumeBalanceMisclosure = 0.0,
            };

            result.BeforeRunTimeStep(isLegacySTEDICatchmentInflows);

            return result;
        }

        /// <summary>Creates a StraightThroughRoutingLink from this legacy node's properties.</summary>
        /// <returns>Initialised straight-through routing link.</returns>
        public StraightThroughRoutingLink GetStraightThroughRoutingLinkModel()
        {
            StraightThroughRoutingLink result = new StraightThroughRoutingLink()
            {
                UpstreamFlow = 0.0,
                DownstreamFlow = 0.0,
                TotalUpstreamCatchmentAreaKM2 = this.TotalCatchmentAreaKM2,
                TotalUpstreamNonWaterCatchmentAreaKM2 = 0.0,
                NonWaterCatchmentAreaUpstreamOfDamsKM2 = 0.0,
                NonWaterCatchmentAreaDownstreamOfDamsKM2 = 0.0,
                VolumeBalanceMisclosure = 0.0,
            };

            return result;
        }

        /// <summary>Returns the calculation order index for this node (WaterBodyNode or ConfluenceNode).</summary>
        /// <returns>Model element type index with downstream routing link reference.</returns>
        public ModelElementTypeIndex GetTypeIndexForNode()
        {
            int elementIndex = this.nodeModelType == ModelElementType.ConfluenceNode
                ? this.ConfluenceNodeID
                : this.WaterBodyNodeID;
            return new ModelElementTypeIndex
            {
                ElementType = this.nodeModelType,
                IndexForElementType = elementIndex,
                NextDownstreamElementType = ModelElementType.StraightThroughRoutingLink,
                IndexForNextDownstreamElementType = this.StraightThroughRoutingLinkID,
            };
        }

        /// <summary>Returns the calculation order index for this node's subcatchment inflow.</summary>
        /// <returns>Model element type index with downstream node reference.</returns>
        public ModelElementTypeIndex GetTypeIndexForSubcatchment()
            => MakeTypeIndex(ModelElementType.SubcatchmentInflow, this.SubcatchmentInflowID, this.nodeModelType, this.WaterBodyNodeID);

        /// <summary>Returns the calculation order index for this node's repeating monthly demand.</summary>
        /// <returns>Model element type index with downstream water body or confluence reference.</returns>
        public ModelElementTypeIndex GetTypeIndexForRepeatingMonthlyDemand()
            => MakeTypeIndex(ModelElementType.RepeatingMonthlyDemand, this.RepeatingMonthlyDemandID, this.nodeModelType, this.WaterBodyNodeID);

        /// <summary>Returns the calculation order index for this node's time series demand.</summary>
        /// <returns>Model element type index with downstream water body or confluence reference.</returns>
        public ModelElementTypeIndex GetTypeIndexForTimeSeriesDemand()
            => MakeTypeIndex(ModelElementType.TimeSeriesDemand, this.TimeSeriesDemandID, this.nodeModelType, this.WaterBodyNodeID);

        /// <summary>Returns the calculation order index for this node's downstream routing link.</summary>
        /// <returns>Model element type index with downstream node reference, or Outlet if no downstream connection.</returns>
        public ModelElementTypeIndex GetTypeIndexForStraightThroughRoutingLink()
        {
            ModelElementTypeIndex result = new ModelElementTypeIndex()
            {
                ElementType = ModelElementType.StraightThroughRoutingLink,
                IndexForElementType = this.StraightThroughRoutingLinkID,
                NextDownstreamElementType = ModelElementType.WaterBodyNode,
                IndexForNextDownstreamElementType = this.NextDownstreamWaterBodyID,
            };

            if (this.NextDownstreamWaterBodyID < 0)
            {
                if (this.NextDownstreamConfluenceID >= 0)
                {
                    result.NextDownstreamElementType = ModelElementType.ConfluenceNode;
                    result.IndexForNextDownstreamElementType = this.NextDownstreamConfluenceID;
                }
                else
                {
                    // No valid downstream connection — this is the outlet routing link
                    result.NextDownstreamElementType = ModelElementType.Outlet;
                    result.IndexForNextDownstreamElementType = -1;
                }
            }

            return result;
        }

        /// <summary>Creates a ModelElementTypeIndex, adjusting the downstream index for confluence nodes.</summary>
        /// <param name="elementType">Element type for this index entry.</param>
        /// <param name="elementIndex">Array index for this element type.</param>
        /// <param name="defaultDSType">Default downstream element type.</param>
        /// <param name="defaultDSIndex">Default downstream array index (overridden for confluence nodes).</param>
        /// <returns>Configured model element type index.</returns>
        private ModelElementTypeIndex MakeTypeIndex (ModelElementType elementType, int elementIndex, ModelElementType defaultDSType, int defaultDSIndex)
        {
            var result = new ModelElementTypeIndex
            {
                ElementType = elementType,
                IndexForElementType = elementIndex,
                NextDownstreamElementType = defaultDSType,
                IndexForNextDownstreamElementType = defaultDSIndex,
            };

            if (this.nodeModelType == ModelElementType.ConfluenceNode)
            {
                result.IndexForNextDownstreamElementType = this.ConfluenceNodeID;
            }

            return result;
        }
    }
}
