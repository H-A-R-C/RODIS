// <copyright file="CatchmentModel.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    using CsvHelper;
    using CsvHelper.Configuration;
    using RODIS.JSON;
    using RODIS.ModelSettings;
    using RODIS.Static;
    using RODIS.TimeSeries;
    using System.Diagnostics;
    using System.Globalization;

    /// <summary>
    /// Valid types of RODIS model calculation elements.
    /// </summary>
    public enum ModelElementType { ConfluenceNode, WaterBodyNode, SubcatchmentInflow, RepeatingMonthlyDemand, TimeSeriesDemand, StraightThroughRoutingLink, Outlet, Missing }

    /// <summary>Identifies a model element and its downstream connection in the calculation order.</summary>
    public struct ModelElementTypeIndex
    {
        /// <summary>Type of this model element.</summary>
        public ModelElementType ElementType;

        /// <summary>Array index for this element within its typed array.</summary>
        public int IndexForElementType;

        /// <summary>Type of the next downstream model element.</summary>
        public ModelElementType NextDownstreamElementType;

        /// <summary>Array index of the next downstream element within its typed array.</summary>
        public int IndexForNextDownstreamElementType;
    }

    /// <summary>Catchment-scale RODIS model containing all nodes, links, demands, and time-step simulation logic.</summary>
    public class CatchmentModel
    {
        /// <summary>Maximum allowable misclosure (ML) between calculated and observed downstream flow for the iterative solver.</summary>
        public const double MaximumAllowableDownstreamFlowMisclosure = 0.00001;

        /// <summary>
        /// Gets or sets the standard modelling time span, as a multiplier of standard increments (daily, weekly, monthly etc.).
        /// </summary>
        public StandardModellingTimeSpan ModellingTimeSpan { get; set; } = new StandardModellingTimeSpan(BaseModellingTimeSpan.Daily, 1.0);

        /// <summary>Array of subcatchment inflow models, one per subcatchment with non-zero area.</summary>
        public SubcatchmentInflowModel[] SubcatchmentsInflowModels;

        /// <summary>Array of water body model nodes in calculation order.</summary>
        public WaterBodyModelNode[] WaterBodyNodes;

        /// <summary>Array of confluence model nodes in calculation order.</summary>
        public ConfluenceModelNode[] ConfluenceNodes;

        /// <summary>Array of straight-through routing links connecting nodes in the network.</summary>
        public StraightThroughRoutingLink[] StraightThroughRoutingLinks;

        /// <summary>Array of repeating monthly demand models, one per water body with this demand type.</summary>
        public FarmDamRepeatingMonthlyDemandModel[] RepeatingMonthlyDemandModels;

        /// <summary>Array of time series demand models, one per water body with this demand type.</summary>
        public FarmDamTimeSeriesDemandModel[] TimeSeriesDemandModels;

        /// <summary>Ordered array defining the sequence in which model elements are calculated each time step.</summary>
        public ModelElementTypeIndex[] ElementModelCalculationOrder;

        /// <summary>True to use legacy RODIS v1.20 calculation methods for surface area, rainfall and area calculations.</summary>
        public bool IsLegacyRODISCalculationMethods = false;

        /// <summary>Gets or sets Observed downstream flow (ML) for the current time step. -9999 = missing data.</summary>
        public double ObservedDownstreamFlow { get; set; } = -9999.0;

        /// <summary>Gets Calculated downstream flow (ML) at the catchment outlet for the current time step.</summary>
        public double DownstreamFlow { get; private set; }

        /// <summary>Gets or sets Unimpacted flow (ML) at the catchment outlet for the current time step.</summary>
        public double UnimpactedFlow { get; set; }

        /// <summary>Gets Net impact on flow (ML) = unimpacted minus downstream flow for the current time step.</summary>
        public double NetImpactOnFlow { get; private set; }

        /// <summary>Gets Total upstream catchment area (km²) at the catchment outlet.</summary>
        public double TotalCatchmentAreaKM2 { get; private set; }

        /// <summary>Gets Total non-water-body catchment area (km²) at the catchment outlet.</summary>
        public double TotalNonWaterBodyAreaKM2 { get; private set; }

        /// <summary>Gets Non-water-body catchment area downstream of all dams (km²) at the catchment outlet.</summary>
        public double NonWaterbodyCatchmentAreaDownstreamOfDamsKM2 { get; private set; }

        /// <summary>Gets Volume balance misclosure (ML) summed across all model elements for the current time step.</summary>
        public double VolumeBalanceMisclosure { get; private set; }

        /// <summary>Gets Total volume in storage (ML) across all water bodies for the current time step.</summary>
        public double VolumeInStorage { get; private set; }

        /// <summary>Gets Total change in storage volume (ML) across all water bodies for the current time step.</summary>
        public double ChangeInVolumeInStorage { get; private set; }

        /// <summary>Gets Total storage capacity at spill (ML) across all active water bodies for the current time step.</summary>
        public double StorageCapacityVolumeAtSpill { get; private set; }

        /// <summary>Gets Total bypass flow capacity (ML/d) across all active water bodies for the current time step.</summary>
        public double BypassFlowCapacity { get; private set; }

        /// <summary>Gets Total downstream flow from bypass (ML) across all water bodies for the current time step.</summary>
        public double DownstreamFlowFromBypass { get; private set; }

        /// <summary>Gets Total downstream flow from spill (ML) across all water bodies for the current time step.</summary>
        public double DownstreamFlowFromSpill { get; private set; }

        /// <summary>Gets Total downstream flow from catchment runoff (ML) at the outlet for the current time step.</summary>
        public double DownstreamFlowFromCatchment { get; private set; }

        /// <summary>Gets Total surface area at spill (m²) across all active water bodies for the current time step.</summary>
        public double SurfaceAreaAtSpill { get; private set; }

        /// <summary>Gets Total stored surface area (m²) across all water bodies for the current time step.</summary>
        public double SurfaceAreaStored { get; private set; }

        /// <summary>Gets or sets simulation date and time for the time step being processed.</summary>
        public DateTime SimulationDateTime { get; set; }

        /// <summary>Gets or sets Rainfall depth (mm) for the current time step, before any multiplier.</summary>
        public double Rainfall { get; set; }

        /// <summary>Gets Total rainfall volume (ML) on water body surfaces for the current time step.</summary>
        public double RainfallVolume { get; private set; }

        /// <summary>Gets or sets Evaporation depth (mm) for the current time step, before any multiplier.</summary>
        public double Evaporation { get; set; }

        /// <summary>Gets Total evaporation volume (ML) from water body surfaces for the current time step.</summary>
        public double EvaporationVolume { get; private set; }

        /// <summary>Gets Total net rainfall volume (ML) = rainfall minus evaporation across all water bodies.</summary>
        public double NetRainfallVolume { get; private set; }

        /// <summary>Gets Total seepage loss rate at full (ML/d) across all water bodies.</summary>
        public double SeepageLossRateAtFull { get; private set; }

        /// <summary>Gets Total seepage loss volume (ML) across all water bodies for the current time step.</summary>
        public double SeepageLossVolume { get; private set; }

        /// <summary>Gets Total unrestricted demand (ML) across all water bodies for the current time step.</summary>
        public double UnrestrictedDemand { get; private set; }

        /// <summary>Gets Total demand volume actually extracted (ML) across all water bodies for the current time step.</summary>
        public double DemandVolumeExtracted { get; private set; }

        //  Mass Balance Diagnostics
        /// <summary>Absolute misclosure tolerance (ML) per time step. Warnings are raised when either check exceeds this value.</summary>
        public const double MassBalanceTolerance = 0.01;

        /// <summary>Total subcatchment runoff (ML) entering the network at the current time step, summed across all SubcatchmentInflowModels.</summary>
        public double TotalSubcatchmentRunoff { get; private set; }

        /// <summary>Top-down catchment-scale mass balance misclosure (ML). Independent check from aggregate system-boundary fluxes.</summary>
        public double TopDownVolumeBalanceMisclosure { get; private set; }

        /// <summary>Cumulative bottom-up mass balance misclosure (ML) from simulation start. Sum of per-element misclosures across all time steps.</summary>
        public double CumulativeBottomUpMisclosure { get; private set; }

        /// <summary>Cumulative top-down mass balance misclosure (ML) from simulation start.</summary>
        public double CumulativeTopDownMisclosure { get; private set; }

        /// <summary>Total volume (ML) lost due to dam removal at this time step. Known simplification — not counted as misclosure.</summary>
        public double DamRemovalStorageLoss { get; private set; }

        /// <summary>Cumulative volume (ML) lost due to dam removal across all time steps.</summary>
        public double CumulativeDamRemovalStorageLoss { get; private set; }

        /// <summary>Maximum absolute bottom-up misclosure (ML) observed at any single time step.</summary>
        public double MaxAbsBottomUpMisclosure { get; private set; }

        /// <summary>Date at which maximum absolute bottom-up misclosure occurred.</summary>
        public DateTime MaxAbsBottomUpMisclosureDate { get; private set; }

        /// <summary>Maximum absolute top-down misclosure (ML) observed at any single time step.</summary>
        public double MaxAbsTopDownMisclosure { get; private set; }

        /// <summary>Date at which maximum absolute top-down misclosure occurred.</summary>
        public DateTime MaxAbsTopDownMisclosureDate { get; private set; }

        /// <summary>Count of time steps where bottom-up or top-down misclosure exceeded MassBalanceTolerance.</summary>
        public int MassBalanceWarningCount { get; private set; }

        /// <summary>Gets Total pumped inflow capacity (ML/d) across all active water bodies for the current time step.</summary>
        public double PumpedInflowCapacity { get; private set; } = 0.0;

        /// <summary>Gets Total pumped inflow volume (ML) across all water bodies for the current time step.</summary>
        public double PumpedInflow { get; private set; } = 0.0;

        /// <summary>True to calculate unimpacted flow from observed flow; false to calculate impacted flow from unimpacted flow.</summary>
        public bool CalculateUnimpactedGivenObserved { get; set; } = false;

        /// <summary>Array of unique reporting group names defined across all nodes.</summary>
        private string[] ReportingGroups = null;

        /// <summary>Returns the array of reporting group names.</summary>
        /// <returns>Array of reporting group name strings.</returns>
        public string[] GetReportingGroups()
        {
            return this.ReportingGroups;
        }

        /// <summary>Gets Net impact on flow (ML) for each reporting group at the current time step.</summary>
        public double[] NetImpactOnFlowByReportingGroup { get; private set; } = null;

        /// <summary>Gets Unimpacted flow (ML) for each reporting group at the current time step.</summary>
        public double[] UnimpactedFlowByReportingGroup { get; private set; } = null;

        /// <summary>Gets Local catchment inflow (ML) for each reporting group at the current time step.</summary>
        public double[] LocalCatchmentInflowByReportingGroup { get; private set; } = null;

        /// <summary>Gets Downstream flow (ML) for each reporting group at the current time step.</summary>
        public double[] DownstreamFlowByReportingGroup { get; private set; } = null;

        /// <summary>Gets Volume in storage (ML) for each reporting group at the current time step.</summary>
        public double[] VolumeInStorageByReportingGroup { get; private set; } = null;

        /// <summary>Gets Storage capacity at spill (ML) for each reporting group at the current time step.</summary>
        public double[] StorageCapacityVolumeAtSpillByReportingGroup { get; private set; } = null;

        /// <summary>Gets Change in storage volume (ML) for each reporting group at the current time step.</summary>
        public double[] ChangeInVolumeInStorageByReportingGroup { get; private set; } = null;

        /// <summary>Gets Bypass flow capacity (ML/d) for each reporting group at the current time step.</summary>
        public double[] BypassFlowCapacityByReportingGroup { get; private set; } = null;

        /// <summary>Gets Bypass downstream flow (ML) for each reporting group at the current time step.</summary>
        public double[] BypassDownstreamFlowByReportingGroup { get; private set; } = null;

        /// <summary>Gets Pumped inflow capacity (ML/d) for each reporting group at the current time step.</summary>
        public double[] PumpedInflowCapacityByReportingGroup { get; private set; } = null;

        /// <summary>Gets Pumped inflow volume (ML) for each reporting group at the current time step.</summary>
        public double[] PumpedInflowByReportingGroup { get; private set; } = null;

        /// <summary>Gets Spill downstream flow (ML) for each reporting group at the current time step.</summary>
        public double[] SpillDownstreamFlowByReportingGroup { get; private set; } = null;

        /// <summary>Gets Surface area at spill (m²) for each reporting group at the current time step.</summary>
        public double[] SurfaceAreaAtSpillByReportingGroup { get; private set; } = null;

        /// <summary>Gets Stored surface area (m²) for each reporting group at the current time step.</summary>
        public double[] SurfaceAreaStoredByReportingGroup { get; private set; } = null;

        /// <summary>Gets Rainfall volume (ML) for each reporting group at the current time step.</summary>
        public double[] RainfallVolumeByReportingGroup { get; private set; } = null;

        /// <summary>Gets Evaporation volume (ML) for each reporting group at the current time step.</summary>
        public double[] EvaporationVolumeByReportingGroup { get; private set; } = null;

        /// <summary>Gets Net rainfall volume (ML) for each reporting group at the current time step.</summary>
        public double[] NetRainfallVolumeByReportingGroup { get; private set; } = null;

        /// <summary>Gets Seepage loss volume (ML) for each reporting group at the current time step.</summary>
        public double[] SeepageVolumeByReportingGroup { get; private set; } = null;

        /// <summary>Gets Unrestricted demand (ML) for each reporting group at the current time step.</summary>
        public double[] UnrestrictedDemandByReportingGroup { get; private set; } = null;

        /// <summary>Gets Demand volume extracted (ML) for each reporting group at the current time step.</summary>
        public double[] DemandVolumeExtractedByReportingGroup { get; private set; } = null;

        /// <summary>Gets Seepage loss rate at full (ML/d) for each reporting group at the current time step.</summary>
        public double[] SeepageLossRateAtFullByReportingGroup { get; private set; } = null;

        /// <summary>Gets Bottom-up volume balance misclosure (ML) for each reporting group at the current time step.</summary>
        public double[] VolumeBalanceMisclosureByReportingGroup { get; private set; } = null;

        /// <summary>Multiplier applied to rainfall input each time step. Default 1.0 = no scaling.</summary>
        public double RainfallMultiplier { get; set; } = 1.0;

        /// <summary>Multiplier applied to PET input each time step. Default 1.0 = no scaling.</summary>
        public double PETMultiplier { get; set; } = 1.0;

        /// <summary>
        /// Initialises the RODIS model with information from an array of legacy RODIS model dam nodes and legacy RODIS model settings.
        /// </summary>
        /// <param name="legacyRODISDamNodes">Array of legacy RODIS model farm dam and water body nodes.</param>
        /// <param name="settings">Overall settings of legacy RODIS model.</param>
        public void Initialise(LegacyRODISDamNode[] legacyRODISDamNodes, RODISSettings settings) 
        {
            this.CalculateUnimpactedGivenObserved =settings.CalculateUnimpactedGivenObserved;

            List<WaterBodyModelNode> waterBodyNodesList = new List<WaterBodyModelNode>();
            List<SubcatchmentInflowModel> uniformInflowSubcatchmentList = new List<SubcatchmentInflowModel>();
            List<ConfluenceModelNode> confluenceNodesList = new List<ConfluenceModelNode>();
            List<StraightThroughRoutingLink> straightThroughRoutingLinkList = new List<StraightThroughRoutingLink>();
            List<FarmDamRepeatingMonthlyDemandModel> repeatingMonthlyDemandList = new List<FarmDamRepeatingMonthlyDemandModel>();
            List<FarmDamTimeSeriesDemandModel> timeSeriesDemandList = new List<FarmDamTimeSeriesDemandModel>();

            List<ModelElementTypeIndex> calculationOrderList = new List<ModelElementTypeIndex>();

            HashSet<string> reportingGroupsHashSet = new HashSet<string>();

            List<int> legacyIndexForWaterBody = new List<int>();
            List<int> legacyIndexForUniformSubcatchmentInflow = new List<int>();
            List<int> legacyIndexForConfluence = new List<int>();
            List<int> legacyIndexSTRouting = new List<int>();
            List<int> legacyIndexRMonthlyDemand = new List<int>();
            List<int> legacyIndexTSDemand = new List<int>();

            for (int i = legacyRODISDamNodes.Length - 1; i >= 0; i--)
            {
                if (legacyRODISDamNodes[i].TotalCatchmentAreaKM2 > 0)
                {
                    uniformInflowSubcatchmentList.Add(legacyRODISDamNodes[i].GetSubcatchmentInflowModel(this.IsLegacyRODISCalculationMethods));
                    calculationOrderList.Add(legacyRODISDamNodes[i].GetTypeIndexForSubcatchment());
                    legacyIndexForUniformSubcatchmentInflow.Add(legacyRODISDamNodes[i].Identifier);
                }

                // If over-ride setting is set to true, recalculate volume of water body from it's surface area
                if (settings.RecalculateDamVolumesFromSurfaceAreas 
                    && legacyRODISDamNodes[i].nodeModelType == ModelElementType.WaterBodyNode
                    && legacyRODISDamNodes[i].SurfaceAreaM2 > 0)
                {
                    double recalculatedVolume = settings.EvaluateSurfaceAreaVolumeEquation(legacyRODISDamNodes[i].SurfaceAreaM2);
                    if (double.IsNormal(recalculatedVolume))
                    {
                        legacyRODISDamNodes[i].VolumeML = recalculatedVolume;
                    }
                }

                if (legacyRODISDamNodes[i].SurfaceAreaM2 > 0 && legacyRODISDamNodes[i].VolumeML > 0)
                {
                    ModelElementType demandNodeType = settings.GetDemandModelType(2);

                    string demandGroupIndex = settings.GetGroupDemandModelIndexByVolume(legacyRODISDamNodes[i].VolumeML, demandNodeType);

                    if (string.IsNullOrEmpty(demandGroupIndex))
                    {
                        // Add demands by demand group
                        demandGroupIndex = settings.GetRepeatingMonthlyDemandModelIndex(legacyRODISDamNodes[i]);
                        if (!string.IsNullOrEmpty(demandGroupIndex))
                        {
                            demandNodeType = ModelElementType.RepeatingMonthlyDemand;
                        }
                        else
                        {
                            demandGroupIndex = settings.GetTimeSeriesDemandModelIndex(legacyRODISDamNodes[i]);
                            if (!string.IsNullOrEmpty(demandGroupIndex))
                            {
                                demandNodeType = ModelElementType.TimeSeriesDemand;
                            }
                            else
                            {
                                demandNodeType = ModelElementType.Missing;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(demandGroupIndex) && demandNodeType != ModelElementType.Missing)
                    {
                        if (demandNodeType == ModelElementType.RepeatingMonthlyDemand)
                        {
                            repeatingMonthlyDemandList.Add(this.GetRepeatingMonthlyDemandModel(legacyRODISDamNodes[i], settings, demandGroupIndex));
                            calculationOrderList.Add(legacyRODISDamNodes[i].GetTypeIndexForRepeatingMonthlyDemand());
                            legacyIndexRMonthlyDemand.Add(legacyRODISDamNodes[i].Identifier);
                        }
                        else
                        {
                            if (demandNodeType == ModelElementType.TimeSeriesDemand)
                            {
                                timeSeriesDemandList.Add(this.GetTimeSeriesDemandModel(legacyRODISDamNodes[i], settings, demandGroupIndex));
                                calculationOrderList.Add(legacyRODISDamNodes[i].GetTypeIndexForTimeSeriesDemand());
                                legacyIndexTSDemand.Add(legacyRODISDamNodes[i].Identifier);
                            }
                        }
                    }
                    else
                    {
                        throw new InvalidDataException(
                            $"Node {legacyRODISDamNodes[i].Identifier}: demand group '{legacyRODISDamNodes[i].DemandGroup}' "
                            + "is not defined in the scenario settings. Check demand group names in the JSON file.");
                    }

                    waterBodyNodesList.Add(legacyRODISDamNodes[i].GetWaterBodyModelNode());

                    double surfaceAreaVolumeExponent = this.FitSurfaceAreaVolumeExponent(settings, legacyRODISDamNodes[i].VolumeML);

                    waterBodyNodesList.Last().VolumeSurfaceAreaRelationshipExponent = surfaceAreaVolumeExponent;
                    legacyIndexForWaterBody.Add(legacyRODISDamNodes[i].Identifier);
                } 
                else
                {
                    // Surface area or volume are 0 or negative, so this is a confluence not a water body node
                    confluenceNodesList.Add(legacyRODISDamNodes[i].GetConfluenceModelNode());
                    legacyIndexForConfluence.Add(legacyRODISDamNodes[i].Identifier);
                }

                calculationOrderList.Add(legacyRODISDamNodes[i].GetTypeIndexForNode());

                if (string.IsNullOrEmpty(legacyRODISDamNodes[i].ResultsGroup.Trim()))
                {
                    legacyRODISDamNodes[i].ResultsGroup = "null";
                }

                reportingGroupsHashSet.Add(legacyRODISDamNodes[i].ResultsGroup);

                if (i > 0)
                {
                    // Add a link downstream of all nodes except the last one, which is the catchment outlet
                    straightThroughRoutingLinkList.Add(legacyRODISDamNodes[i].GetStraightThroughRoutingLinkModel());

                    ModelElementTypeIndex routingLinkIndex = legacyRODISDamNodes[i].GetTypeIndexForStraightThroughRoutingLink();
                    if (legacyRODISDamNodes[legacyRODISDamNodes[i].NextDownstreamIdentifier - 1].VolumeML <= 0)
                    {
                        routingLinkIndex.NextDownstreamElementType = ModelElementType.ConfluenceNode;
                    }
                    calculationOrderList.Add(routingLinkIndex);

                    legacyIndexSTRouting.Add(legacyRODISDamNodes[i].Identifier);
                }
            }

            this.SubcatchmentsInflowModels = uniformInflowSubcatchmentList.ToArray();
            this.WaterBodyNodes = waterBodyNodesList.ToArray();
            this.ConfluenceNodes = confluenceNodesList.ToArray();
            this.StraightThroughRoutingLinks = straightThroughRoutingLinkList.ToArray();
            this.RepeatingMonthlyDemandModels = repeatingMonthlyDemandList.ToArray();
            this.TimeSeriesDemandModels = timeSeriesDemandList.ToArray();

            this.ReportingGroups = reportingGroupsHashSet.ToArray();
            List<string> reportingGroupsList = reportingGroupsHashSet.ToList();

            this.ElementModelCalculationOrder = calculationOrderList.ToArray();

            this.AssignReportingGroupIndices(reportingGroupsList);

            this.CalculateTotalCatchmentAreas();

            this.InitialiseReportingGroupArrays();
        }

        /// <summary>Initialises the RODIS model from spatial GIS data files and settings.</summary>
        /// <param name="settings">Overall RODIS model settings including GIS file paths.</param>
        /// <param name="startRunDate">Start date of the simulation run.</param>
        /// <param name="endRunDate">End date of the simulation run.</param>
        public void Initialise(RODISSettings settings, DateTime startRunDate, DateTime endRunDate)
        {
            this.CalculateUnimpactedGivenObserved = settings.CalculateUnimpactedGivenObserved;
            int damRevisionMonthOfYear = settings.DamsRevisionDateIgnoreYear.Month;
            int damRevisionDayOfMonth = settings.DamsRevisionDateIgnoreYear.Day;

            // Start by reading data in from GIS shape files / geopackages

            // Check that both spatial data files exist and can be read from

            // Check that the fields required from the spatial data files are defined in the spatial data files and are the correct type
            Dictionary<string, WaterBodyType> damTypeGroups = WaterBodyType.DeserialiseDamTypeGroupProperties(settings.DamTypeGroupsJSONPath);
            Dictionary<string, string> waterBodyFieldLabelsDictionary = JSONSerialisation.DeserialiseFileThrowOnError<Dictionary<string, string>>(settings.WaterBodyFieldsToReadJSONPath);
            Dictionary<string, string> catchmentFieldLabelsDictionary = JSONSerialisation.DeserialiseFileThrowOnError<Dictionary<string, string>>(settings.CatchmentFieldsToReadJSONPath);

            Dictionary<string, string> limitationsSpecification = null;
            if (!string.IsNullOrEmpty(settings.WaterBodiesToIncludeJSONPath))
            {
                if (File.Exists(settings.WaterBodiesToIncludeJSONPath))
                {
                    limitationsSpecification = JSONSerialisation.DeserialiseFileThrowOnError<Dictionary<string, string>>(settings.WaterBodiesToIncludeJSONPath);
                }
            }

            // Configure GDAL and OGR
            Console.WriteLine("Configuring GDAL....");
            GdalConfiguration.ConfigureGdal();
            Console.WriteLine("Configuring OGR ....");
            GdalConfiguration.ConfigureOgr();

            List<WaterBodyWithCatchment> allWaterBodiesList = WaterBodyWithCatchment.ReadWaterBodiesFromSpatialDataFile
                (settings.WaterBodyPolygonsGISFilePath, waterBodyFieldLabelsDictionary, null, damRevisionMonthOfYear, damRevisionDayOfMonth);

            int numCatchmentsAdded = WaterBodyWithCatchment.AddCatchmentDataToWaterBodies(allWaterBodiesList, settings.CatchmentPolygonsGISFilePath, catchmentFieldLabelsDictionary);

            if (numCatchmentsAdded <= 0)
            {
                Console.WriteLine("Error!");
            }
            else
            {
                WaterBodyWithCatchment[] allWaterBodies = allWaterBodiesList.ToArray();

                WaterBodyWithCatchment.CheckLimitationsForWaterBodies(allWaterBodies, limitationsSpecification);

                // First pass: assign downstream positions from GIS CatchmentID matching
                List<int> downstreamArrayKeys = new List<int>();
                this.AllocateDownstreamPositions(downstreamArrayKeys, allWaterBodies);

                // Topological sort: ensures leaves are processed first, outlet last
                TopologicalSortWaterBodies(allWaterBodies);

                // Re-assign downstream positions after sort (positions have changed)
                this.AllocateDownstreamPositions(downstreamArrayKeys, allWaterBodies);

                List<WaterBodyModelNode> waterBodyNodesList = new List<WaterBodyModelNode>();
                List<SubcatchmentInflowModel> uniformInflowSubcatchmentList = new List<SubcatchmentInflowModel>();
                List<ConfluenceModelNode> confluenceNodesList = new List<ConfluenceModelNode>();
                List<StraightThroughRoutingLink> straightThroughRoutingLinkList = new List<StraightThroughRoutingLink>();
                List<FarmDamRepeatingMonthlyDemandModel> repeatingMonthlyDemandList = new List<FarmDamRepeatingMonthlyDemandModel>();
                List<FarmDamTimeSeriesDemandModel> timeSeriesDemandList = new List<FarmDamTimeSeriesDemandModel>();

                List<ModelElementTypeIndex> calculationOrderList = new List<ModelElementTypeIndex>();

                HashSet<string> reportingGroupsHashSet = new HashSet<string>();

                List<LegacyRODISDamNode> legacyNodesList = new List<LegacyRODISDamNode>();

                int numWaterBodyNodes = 0;
                int numConfluenceNodes = 0;
                int numUniformInflowCatchments = 0;
                int numStraightThroughRoutingLinks = 0;

                for (int i = 0; i < allWaterBodies.Length; ++i)
                {
                    // If over-ride setting is set to true, recalculate volume of water body from it's surface area
                    if (settings.RecalculateDamVolumesFromSurfaceAreas && allWaterBodies[i].SurfaceAream2 > 0)
                    {
                        double recalculatedVolume = settings.EvaluateSurfaceAreaVolumeEquation(allWaterBodies[i].SurfaceAream2);
                        if (double.IsNormal(recalculatedVolume))
                        {
                            allWaterBodies[i].VolumeML = recalculatedVolume;
                        }
                    }

                    LegacyRODISDamNode legacyNode = new LegacyRODISDamNode()
                    {
                        Identifier = i,
                        SurfaceAreaM2 = allWaterBodies[i].SurfaceAream2,
                        VolumeML = allWaterBodies[i].VolumeML,
                        IntermediateCatchmentAreaKM2 = allWaterBodies[i].CatchmentAreakm2,
                        DemandGroup = allWaterBodies[i].DemandGroup,
                        ResultsGroup = allWaterBodies[i].ResultsGroup,
                        IsWinterfill = allWaterBodies[i].PumpedInflowCapacity > 0.0,
                        WinterfillRate = Math.Max(0.0, allWaterBodies[i].PumpedInflowCapacity),
                        IsBypass = allWaterBodies[i].BypassFlowRate > 0.0,
                        BypassCapacity = Math.Max(0.0, allWaterBodies[i].BypassFlowRate),
                        nodeModelType = ModelElementType.WaterBodyNode,
                        BypassSeasonStartDateIgnoreYear = settings.BypassSeasonStartDateIgnoreYear,
                        BypassSeasonEndDateIgnoreYear = settings.BypassSeasonEndDateIgnoreYear,
                        WinterfillSeasonStartDateIgnoreYear = settings.PumpingSeasonStartDateIgnoreYear,
                        WinterfillSeasonEndDateIgnoreYear = settings.PumpingSeasonEndDateIgnoreYear,
                    };

                    // Check for consistency in start dates
                    if (allWaterBodies[i].StartDate < startRunDate)
                    {
                        allWaterBodies[i].StartDate = startRunDate;
                    }

                    if (allWaterBodies[i].StartBypassDate < allWaterBodies[i].StartDate)
                    {
                        allWaterBodies[i].StartBypassDate = allWaterBodies[i].StartDate;
                    }

                    if (allWaterBodies[i].StartPumpedInflowDate < allWaterBodies[i].StartDate)
                    {
                        allWaterBodies[i].StartPumpedInflowDate = allWaterBodies[i].StartDate;
                    }

                    // Check for consistency in end dates
                    if (allWaterBodies[i].EndDate > endRunDate)
                    {
                        allWaterBodies[i].EndDate = endRunDate;
                    }

                    if (allWaterBodies[i].EndBypassDate > allWaterBodies[i].EndDate)
                    {
                        allWaterBodies[i].EndBypassDate = allWaterBodies[i].EndDate;
                    }

                    if (allWaterBodies[i].EndPumpedInflowDate > allWaterBodies[i].EndDate)
                    {
                        allWaterBodies[i].EndPumpedInflowDate = allWaterBodies[i].EndDate;
                    }

                    if (allWaterBodies[i].CatchmentAreakm2 > 0)
                    {
                        legacyNode.SubcatchmentInflowID = numUniformInflowCatchments;
                        ++numUniformInflowCatchments;
                    }

                    if (allWaterBodies[i].SurfaceAream2 > 0 || allWaterBodies[i].VolumeML > 0)
                    {
                        legacyNode.WaterBodyNodeID = numWaterBodyNodes;
                        ++numWaterBodyNodes;

                        ModelElementType demandNodeType = settings.GetDemandModelType(2);

                        string demandGroupIndex = settings.GetGroupDemandModelIndexByVolume(allWaterBodies[i].VolumeML, demandNodeType);

                        if (string.IsNullOrEmpty(demandGroupIndex))
                        {
                            // Add demands by demand group
                            demandGroupIndex = settings.GetRepeatingMonthlyDemandModelIndex(legacyNode);
                            if (!string.IsNullOrEmpty(demandGroupIndex))
                            {
                                demandNodeType = ModelElementType.RepeatingMonthlyDemand;
                            }
                            else
                            {
                                demandGroupIndex = settings.GetTimeSeriesDemandModelIndex(legacyNode);
                                if (!string.IsNullOrEmpty(demandGroupIndex))
                                {
                                    demandNodeType = ModelElementType.TimeSeriesDemand;
                                }
                                else
                                {
                                    demandNodeType = ModelElementType.Missing;
                                }
                            }
                        }

                        legacyNode.DemandGroup = demandGroupIndex;
                        if (string.IsNullOrEmpty(legacyNode.ResultsGroup))
                        {
                            legacyNode.ResultsGroup = demandGroupIndex;
                        }

                        if (!string.IsNullOrEmpty(demandGroupIndex) && demandNodeType != ModelElementType.Missing)
                        {
                            if (demandNodeType == ModelElementType.RepeatingMonthlyDemand)
                            {
                                legacyNode.RepeatingMonthlyDemandID = repeatingMonthlyDemandList.Count;
                                repeatingMonthlyDemandList.Add(this.GetRepeatingMonthlyDemandModel(legacyNode, settings, demandGroupIndex));
                            }
                            else
                            {
                                if (demandNodeType == ModelElementType.TimeSeriesDemand)
                                {
                                    legacyNode.TimeSeriesDemandID = timeSeriesDemandList.Count;
                                    timeSeriesDemandList.Add(this.GetTimeSeriesDemandModel(legacyNode, settings, demandGroupIndex));
                                }
                            }
                        }
                        else
                        {
                            throw new InvalidDataException(
                                $"Water body {i} (label: '{allWaterBodies[i].Label}'): demand group '{legacyNode.DemandGroup}' "
                                + "is not defined in the settings. Check demand group names in the JSON file.");
                        }
                    }
                    else
                    {
                        legacyNode.nodeModelType = ModelElementType.ConfluenceNode;
                        legacyNode.ConfluenceNodeID = numConfluenceNodes;
                        ++numConfluenceNodes;
                    }

                    legacyNode.StraightThroughRoutingLinkID = numStraightThroughRoutingLinks;
                    ++numStraightThroughRoutingLinks;

                    legacyNodesList.Add(legacyNode);
                }

                for (int i = 0; i < allWaterBodies.Length && i < legacyNodesList.Count; i++)
                {
                    int j = allWaterBodies[i].NextDownstreamArrayPosition;
                    if (j >= 0 && j < legacyNodesList.Count)
                    {
                        legacyNodesList[i].NextDownstreamConfluenceID = legacyNodesList[j].ConfluenceNodeID;
                        legacyNodesList[i].NextDownstreamWaterBodyID = legacyNodesList[j].WaterBodyNodeID;
                    }
                    else
                    {
                        legacyNodesList[i].StraightThroughRoutingLinkID = -1;
                    }
                }

                for (int i = 0; i < allWaterBodies.Length; ++i)
                {
                    LegacyRODISDamNode legacyNode = legacyNodesList[i];

                    if (allWaterBodies[i].CatchmentAreakm2 > 0)
                    {
                        uniformInflowSubcatchmentList.Add(legacyNode.GetSubcatchmentInflowModel(this.IsLegacyRODISCalculationMethods));
                        calculationOrderList.Add(legacyNode.GetTypeIndexForSubcatchment());
                    }

                    if (allWaterBodies[i].SurfaceAream2 > 0 && allWaterBodies[i].VolumeML > 0)
                    {
                        if (settings.GetDemandModelType(2) == ModelElementType.RepeatingMonthlyDemand)
                        {
                            calculationOrderList.Add(legacyNode.GetTypeIndexForRepeatingMonthlyDemand());
                        }
                        else
                        {
                            if (settings.GetDemandModelType(2) == ModelElementType.TimeSeriesDemand)
                            {
                                calculationOrderList.Add(legacyNode.GetTypeIndexForTimeSeriesDemand());
                            }
                        }

                        WaterBodyModelNode waterBodyToAdd = legacyNode.GetWaterBodyModelNode();
                        double surfaceAreaVolumeExponent = this.FitSurfaceAreaVolumeExponent(settings, allWaterBodies[i].VolumeML);

                        waterBodyToAdd.Label = allWaterBodies[i].Label;
                        waterBodyToAdd.Comment = allWaterBodies[i].Comment;
                        waterBodyToAdd.SetBaseStartEndDates(allWaterBodies[i].StartDate, allWaterBodies[i].EndDate);
                        waterBodyToAdd.StartBypassDate = allWaterBodies[i].StartBypassDate;
                        waterBodyToAdd.EndBypassDate = allWaterBodies[i].EndBypassDate;
                        waterBodyToAdd.StartPumpedInflowDate = allWaterBodies[i].StartPumpedInflowDate;
                        waterBodyToAdd.EndPumpedInflowDate = allWaterBodies[i].EndPumpedInflowDate;
                        waterBodyToAdd.VolumeSurfaceAreaRelationshipExponent = surfaceAreaVolumeExponent;
                        waterBodyToAdd.NextDownstreamLabel = allWaterBodies[i].NextDownstreamCatchmentID + "; " + allWaterBodies[i].NextDownstreamArrayPosition.ToString();
                        waterBodyToAdd.Easting = allWaterBodies[i].Easting;
                        waterBodyToAdd.Northing = allWaterBodies[i].Northing;
                        waterBodyToAdd.Elevation = allWaterBodies[i].Elevation;

                        if (startRunDate >= waterBodyToAdd.StartDate && startRunDate < waterBodyToAdd.EndDate)
                        {
                            waterBodyToAdd.StorageCapacityVolumeAtSpill = waterBodyToAdd.MaxStorageCapacityVolumeAtSpill;

                            // Set initial storage volume and surface area
                            // Note: Legacy RODIS did not have this option, so only use initial storage proportion full if allowing new calculation methods
                            if (!settings.UseLegacyRODIS1CalculationMethods)
                            {
                                double startFraction = Math.Max(Math.Min(settings.AllStoragesProportionFullAtStartOfRun, 1.0), 0.0);
                                waterBodyToAdd.VolumeInStorage = startFraction * waterBodyToAdd.StorageCapacityVolumeAtSpill;
                                waterBodyToAdd.StartTimeStepVolumeInStorage = waterBodyToAdd.VolumeInStorage;
                                waterBodyToAdd.SurfaceAreaStored = waterBodyToAdd.CalculateSurfaceArea();
                            }
                        }

                        waterBodyNodesList.Add(waterBodyToAdd);
                    }
                    else
                    {
                        // Surface area or volume are 0 or negative, so this is a confluence not a water body node
                        confluenceNodesList.Add(legacyNode.GetConfluenceModelNode());
                        confluenceNodesList.Last().Label = allWaterBodies[i].Label;
                        confluenceNodesList.Last().Comment = allWaterBodies[i].Comment;
                        confluenceNodesList.Last().Easting = allWaterBodies[i].Easting;
                        confluenceNodesList.Last().Northing = allWaterBodies[i].Northing;
                        confluenceNodesList.Last().Elevation = allWaterBodies[i].Elevation;
                    }

                    calculationOrderList.Add(legacyNode.GetTypeIndexForNode());
                    if (string.IsNullOrEmpty(legacyNode.ResultsGroup.Trim()))
                    {
                        legacyNode.ResultsGroup = "null";
                    }
                    reportingGroupsHashSet.Add(legacyNode.ResultsGroup);

                    if (allWaterBodies[i].NextDownstreamArrayPosition < legacyNodesList.Count)
                    {
                        // Add a link downstream of all nodes except the last one, which is the catchment outlet
                        straightThroughRoutingLinkList.Add(legacyNode.GetStraightThroughRoutingLinkModel());
                        ModelElementTypeIndex routingLinkIndex = legacyNode.GetTypeIndexForStraightThroughRoutingLink();

                        // When the downstream node is a ConfluenceNode, correct BOTH the element type AND the array index.
                        // GetTypeIndexForStraightThroughRoutingLink() defaults both to WaterBodyNode.
                        // This mirrors the equivalent logic in the legacy initialisation path (see Initialise (LegacyRODISDamNode[], RODISSettings) at ~L240).
                        int dsPos = allWaterBodies[i].NextDownstreamArrayPosition;
                        if (dsPos >= 0 && dsPos < allWaterBodies.Length
                            && !(allWaterBodies[dsPos].SurfaceAream2 > 0 && allWaterBodies[dsPos].VolumeML > 0))
                        {
                            routingLinkIndex.NextDownstreamElementType = ModelElementType.ConfluenceNode;
                            routingLinkIndex.IndexForNextDownstreamElementType = legacyNode.NextDownstreamConfluenceID;
                        }

                        calculationOrderList.Add(routingLinkIndex);
                    }
                }

                this.SubcatchmentsInflowModels = uniformInflowSubcatchmentList.ToArray();
                this.WaterBodyNodes = waterBodyNodesList.ToArray();
                this.ConfluenceNodes = confluenceNodesList.ToArray();
                this.StraightThroughRoutingLinks = straightThroughRoutingLinkList.ToArray();
                this.RepeatingMonthlyDemandModels = repeatingMonthlyDemandList.ToArray();
                this.TimeSeriesDemandModels = timeSeriesDemandList.ToArray();

                this.ReportingGroups = reportingGroupsHashSet.ToArray();
                List<string> reportingGroupsList = reportingGroupsHashSet.ToList();

                this.ElementModelCalculationOrder = calculationOrderList.ToArray();
                this.ElementModelCalculationOrder[this.ElementModelCalculationOrder.Length - 1].NextDownstreamElementType = ModelElementType.Outlet;

                this.AssignReportingGroupIndices(reportingGroupsList);

                this.CalculateTotalCatchmentAreas();

                this.InitialiseReportingGroupArrays();
            }
        }

        /// <summary>Allocates downstream array positions for each water body by matching catchment IDs.</summary>
        /// <param name="downstreamArrayKeys">Output list of downstream array indices, one per water body.</param>
        /// <param name="allWaterBodies">Array of water bodies to process.</param>
        private void AllocateDownstreamPositions (List<int> downstreamArrayKeys, WaterBodyWithCatchment[] allWaterBodies)
        {
            downstreamArrayKeys.Clear();

            for (int i = 0; i < allWaterBodies.Length; ++i)
            {
                if (string.IsNullOrEmpty(allWaterBodies[i].NextDownstreamCatchmentID))
                {
                    allWaterBodies[i].NextDownstreamArrayPosition = allWaterBodies.Length + 1;
                }
                else
                {
                    allWaterBodies[i].NextDownstreamArrayPosition = allWaterBodies.Length + 1;
                    for (int j = 0; j < allWaterBodies.Length && allWaterBodies[i].NextDownstreamArrayPosition > allWaterBodies.Length; ++j)
                    {
                        if (i != j)
                        {
                            if (allWaterBodies[i].NextDownstreamCatchmentID == allWaterBodies[j].CatchmentID)
                            {
                                allWaterBodies[i].NextDownstreamArrayPosition = j;
                            }
                        }
                    }
                }

                downstreamArrayKeys.Add(allWaterBodies[i].NextDownstreamArrayPosition);
            }
        }

        /// <summary>Runs one time step of the RODIS model.</summary>
        public void RunTimeStep()
        {
            TimeSpan timeStep = this.ModellingTimeSpan.GetAsTimeSpan(this.SimulationDateTime);

            if (this.CalculateUnimpactedGivenObserved && this.ObservedDownstreamFlow >= 0)
            {
                // Implement iterative solution to match calculated and observed downstream flows
                // Set up upper bound on unimpacted flow
                this.CalculateNonWaterbodyAreas();
                double upperBoundUimpactedFlow = this.ObservedDownstreamFlow * this.TotalCatchmentAreaKM2 / this.NonWaterbodyCatchmentAreaDownstreamOfDamsKM2;
                this.UnimpactedFlow = upperBoundUimpactedFlow;
                this.CalculateUnimpactedFlowsAtTimeStep();
                this.CalculateFlowsAtTimeStep(timeStep, false);
                double upperBoundDownstreamFlow = this.DownstreamFlow;
                double upperBoundFlowDifference = upperBoundDownstreamFlow - this.ObservedDownstreamFlow;

                // Set up lower bound on unimpacted flow
                this.CalculateNonWaterbodyAreas();
                double lowerBoundUimpactedFlow = Math.Max(0, this.ObservedDownstreamFlow);
                this.UnimpactedFlow = lowerBoundUimpactedFlow;
                this.CalculateUnimpactedFlowsAtTimeStep();
                this.CalculateFlowsAtTimeStep(timeStep, false);
                double lowerBoundDownstreamFlow = this.DownstreamFlow;
                double lowerBoundFlowDifference = lowerBoundDownstreamFlow - this.ObservedDownstreamFlow;

                // For test on the first loop, set trial to the lower bound
                double trialUnimpactedFlow = lowerBoundUimpactedFlow;
                double trialDownstreamFlow = this.DownstreamFlow;
                double trialFlowDifference = trialDownstreamFlow - this.ObservedDownstreamFlow;

                // Count iterations to avoid getting caught in an infinite loop
                int i = 0;
                const int MAXITERATIONS = 10000;

                // Loop until trial flow difference is within maximum difference in the downstream flow
                // OR upper and lower bound flow differences are the same (so can't divide through)
                // OR maximum number of iterations is hit
                while (Math.Abs(trialFlowDifference) > MaximumAllowableDownstreamFlowMisclosure && upperBoundFlowDifference - lowerBoundFlowDifference > 0 && i < MAXITERATIONS)
                {
                    this.CalculateNonWaterbodyAreas();
                    trialUnimpactedFlow = lowerBoundUimpactedFlow - lowerBoundFlowDifference * (upperBoundUimpactedFlow - lowerBoundUimpactedFlow) / (upperBoundFlowDifference - lowerBoundFlowDifference);
                    this.UnimpactedFlow = trialUnimpactedFlow;
                    this.CalculateUnimpactedFlowsAtTimeStep();
                    this.CalculateFlowsAtTimeStep(timeStep, false);
                    trialDownstreamFlow = this.DownstreamFlow;
                    trialFlowDifference = trialDownstreamFlow - this.ObservedDownstreamFlow;

                    if (trialFlowDifference < 0)
                    {
                        // Replace lower bound with trial as it is less than 0 but closer to 0
                        lowerBoundUimpactedFlow = trialUnimpactedFlow;
                        lowerBoundFlowDifference = trialFlowDifference;
                        lowerBoundDownstreamFlow = trialDownstreamFlow;
                    } 
                    else
                    {
                        // Replace upper bound with trial as it is more than 0 but closer to 0
                        upperBoundUimpactedFlow = trialUnimpactedFlow;
                        upperBoundFlowDifference = trialFlowDifference;
                        upperBoundDownstreamFlow = trialDownstreamFlow;
                    }

                    ++i;
                }

                if (i >= MAXITERATIONS)
                {
                    Console.WriteLine(
                        $"WARNING: Iterative flow solution did not converge within {MAXITERATIONS} iterations "
                        + $"at {this.SimulationDateTime:yyyy-MM-dd}. "
                        + $"Residual = {trialFlowDifference:E3} ML (tolerance = {MaximumAllowableDownstreamFlowMisclosure:E3} ML). "
                        + "Results for this time step may be approximate.");
                }
            }

            // Run once or run again for iterative solution
            this.CalculateNonWaterbodyAreas();
            this.CalculateUnimpactedFlowsAtTimeStep();
            this.CalculateFlowsAtTimeStep(timeStep, true);
            this.CalculateVolumeBalanceMisclosure();
            this.SumTotalsForTimeStep();
            this.SumByReportingGroupForTimeStep();
            this.CalculateTopDownMassBalance();
        }

        /// <summary>
        /// Rescales the maximum storage volume of all storages in the model by a scale factor, updates the starting conditions of those storages and rescales the demands with storage volume.
        /// </summary>
        /// <param name="volumeScaleFactorWithSurfaceAreaChange">Scale factor for maximum storage volumes of all storages, relative to the current values in the catchment model. Rescales the surface areas with the volumes (assumes actual expansion).</param>
        /// <param name="rodisSettings">Settings for this run.</param>
        /// <param name="runStartDate">Start date for this run, so that initial conditions can be set.</param>
        /// <param name="baseScenarioMaxWaterBodyVolumes">Array of starting water body volumes at full supply level from the base scenario, to use as a basis for the adjustment.</param>
        /// <param name="volumeScaleFactorNoSurfaceAreaChange">Optional parameter that is true if we want to re-scale storage volumes without rescaling the surface areas (mimics surface area to volume estimation error).</param>
        /// <param name="useAnalyticalSurfAreaRecalc">If true, uses the per-node power-law exponent to analytically recalculate surface area from volume (fast). If false, uses the iterative SolveForSurfaceAreaFromVolume solver (slow but exact for non-power-law relationships).</param>
        /// <param name="baseScenarioSurfaceAreasAtSpill">Array of starting water body surface areas at full supply level from the base scenario, as a fast initialiser for adjustment.</param>
        public void RescaleWaterBodiesAndDemands(
            double volumeScaleFactorWithSurfaceAreaChange,
            RODISSettings rodisSettings,
            DateTime runStartDate,
            double[] baseScenarioMaxWaterBodyVolumes,
            double volumeScaleFactorNoSurfaceAreaChange = 1.0,
            bool useAnalyticalSurfAreaRecalc = true,
            double[] baseScenarioSurfaceAreasAtSpill = null,
            double[] baseScenarioTSDemandCapacities = null,
            double[] baseScenarioRMDemandCapacities = null)
        {
            var sw = Stopwatch.StartNew();
            long lastMs = 0;

            // NOTE: If demands are changed to scale with mean annual inflow instead of storage volume, update scaling logic here.
            // Scale volumes of water body nodes, recalculate surface areas and set starting storage volume
            if (this.WaterBodyNodes != null)
            {
                //Console.WriteLine("Updating catchment configuration and water bodies for scenario");
                double startFraction = Math.Max(Math.Min(rodisSettings.AllStoragesProportionFullAtStartOfRun, 1.0), 0.0);

                bool useBaseScenaroVolumes = false;
                if (baseScenarioMaxWaterBodyVolumes != null)
                {
                    if (baseScenarioMaxWaterBodyVolumes.Length == this.WaterBodyNodes.Length)
                    {
                        useBaseScenaroVolumes = true;
                    }
                }

                // -- Checkpoint 1: Volume scaling --
                for (int i = 0; i < this.WaterBodyNodes.Length; ++i)
                {
                    if (useBaseScenaroVolumes)
                    {
                        this.WaterBodyNodes[i].MaxStorageCapacityVolumeAtSpill = volumeScaleFactorWithSurfaceAreaChange * baseScenarioMaxWaterBodyVolumes[i];
                    }
                    else
                    {
                        this.WaterBodyNodes[i].MaxStorageCapacityVolumeAtSpill *= volumeScaleFactorWithSurfaceAreaChange;
                    }
                }

                //Console.WriteLine($"    RVD checkpoint 1 (volume scaling):          {sw.ElapsedMilliseconds - lastMs,6}ms  [{this.WaterBodyNodes.Length} nodes]");
                lastMs = sw.ElapsedMilliseconds;

                // -- Checkpoint 2: Surface area recalculation --
                // If base SA values are provided, reset SA to base values and apply analytical scaling
                // directly. This avoids all calls to EvaluateSurfaceAreaVolumeEquation (~2ms each),
                // reducing the cost from ~6s to <1ms for 1542 dams.
                if (Math.Abs(volumeScaleFactorWithSurfaceAreaChange - 1.0) > 0.001)
                {
                    bool hasBaseSA = baseScenarioSurfaceAreasAtSpill != null && baseScenarioSurfaceAreasAtSpill.Length == this.WaterBodyNodes.Length;

                    if (hasBaseSA)
                    {
                        // Fast path: reset SA to base values, then scale analytically.
                        // SA_new = SA_base × scaleFactor^(1/n)
                        // No forward evaluations needed because we're working from known base values.
                        for (int i = 0; i < this.WaterBodyNodes.Length; ++i)
                        {
                            double baseSA = baseScenarioSurfaceAreasAtSpill[i];
                            double exponent = this.WaterBodyNodes[i].VolumeSurfaceAreaRelationshipExponent;

                            if (baseSA > 0.0 && exponent > 0.0)
                            {
                                this.WaterBodyNodes[i].SurfaceAreaAtSpill = baseSA * Math.Pow(volumeScaleFactorWithSurfaceAreaChange, 1.0 / exponent);
                            }
                            else
                            {
                                this.WaterBodyNodes[i].SurfaceAreaAtSpill = baseSA;
                            }
                        }

                        //Console.WriteLine($"    RVD checkpoint 2 (SA recalc ANALYTICAL):    {sw.ElapsedMilliseconds - lastMs,6}ms [scaleFactor={volumeScaleFactorWithSurfaceAreaChange:F6}, from base SA]");
                    }
                    else
                    {
                        // Fallback: hybrid analytical + Newton-Raphson (used when base SA not available)
                        const double SATolerance = 0.001;
                        const int MaxNewtonIterations = 10;
                        const double DerivativeStepFraction = 0.001;

                        int analyticalAcceptCount = 0;
                        int newtonRefinedCount = 0;
                        int totalNewtonIterations = 0;
                        int unconvergedCount = 0;

                        for (int i = 0; i < this.WaterBodyNodes.Length; ++i)
                        {
                            double currentSA = this.WaterBodyNodes[i].SurfaceAreaAtSpill;
                            double targetVolume = this.WaterBodyNodes[i].MaxStorageCapacityVolumeAtSpill;

                            if (currentSA <= 0.0 || targetVolume <= 0.0)
                                continue;

                            double exponent = this.WaterBodyNodes[i].VolumeSurfaceAreaRelationshipExponent;

                            double currentVolume = rodisSettings.EvaluateSurfaceAreaVolumeEquation(currentSA);
                            double volumeRatio = (currentVolume > 0.0)
                                ? targetVolume / currentVolume
                                : volumeScaleFactorWithSurfaceAreaChange;

                            double estimatedSA = currentSA;
                            if (exponent > 0.0 && Math.Abs(volumeRatio - 1.0) > 1e-12)
                            {
                                estimatedSA = currentSA * Math.Pow(volumeRatio, 1.0 / exponent);
                            }

                            double checkVolume = rodisSettings.EvaluateSurfaceAreaVolumeEquation(estimatedSA);
                            double relativeError = Math.Abs(checkVolume - targetVolume) / targetVolume;

                            if (relativeError <= SATolerance)
                            {
                                this.WaterBodyNodes[i].SurfaceAreaAtSpill = estimatedSA;
                                ++analyticalAcceptCount;
                            }
                            else
                            {
                                double sa = estimatedSA;
                                bool converged = false;

                                for (int iter = 0; iter < MaxNewtonIterations; ++iter)
                                {
                                    double evaluatedVolume = rodisSettings.EvaluateSurfaceAreaVolumeEquation(sa);
                                    double residual = evaluatedVolume - targetVolume;

                                    if (Math.Abs(residual) / targetVolume <= SATolerance)
                                    {
                                        converged = true;
                                        break;
                                    }

                                    double h = Math.Max(sa * DerivativeStepFraction, 1e-6);
                                    double vPlus = rodisSettings.EvaluateSurfaceAreaVolumeEquation(sa + h);
                                    double vMinus = rodisSettings.EvaluateSurfaceAreaVolumeEquation(sa - h);
                                    double dVdSA = (vPlus - vMinus) / (2.0 * h);

                                    if (Math.Abs(dVdSA) < 1e-20)
                                        break;

                                    sa -= residual / dVdSA;

                                    if (sa <= 0.0)
                                        sa = estimatedSA * 0.5;

                                    ++totalNewtonIterations;
                                }

                                if (!converged)
                                    ++unconvergedCount;

                                this.WaterBodyNodes[i].SurfaceAreaAtSpill = sa;
                                ++newtonRefinedCount;
                            }
                        }

                        string diagnosticMsg = $"    RVD checkpoint 2 (SA recalc HYBRID):        {sw.ElapsedMilliseconds - lastMs,6}ms  "
                                             + $"[scaleFactor={volumeScaleFactorWithSurfaceAreaChange:F6}, "
                                             + $"analytical={analyticalAcceptCount}, newton={newtonRefinedCount}, "
                                             + $"newtonIters={totalNewtonIterations}";
                        if (unconvergedCount > 0)
                            diagnosticMsg += $", UNCONVERGED={unconvergedCount}";
                        diagnosticMsg += "]";
                        Console.WriteLine(diagnosticMsg);
                    }
                }
                else
                {
                    //Console.WriteLine($"    RVD checkpoint 2 (SA recalc SKIPPED):       {sw.ElapsedMilliseconds - lastMs,6}ms  [scaleFactor={volumeScaleFactorWithSurfaceAreaChange:F6}]");
                }

                lastMs = sw.ElapsedMilliseconds;

                // -- Checkpoint 3: Volume scale without SA change --
                for (int i = 0; i < this.WaterBodyNodes.Length; ++i)
                {
                    this.WaterBodyNodes[i].MaxStorageCapacityVolumeAtSpill *= volumeScaleFactorNoSurfaceAreaChange;
                }

                //Console.WriteLine($"    RVD checkpoint 3 (vol no-SA scale):         {sw.ElapsedMilliseconds - lastMs,6}ms");
                lastMs = sw.ElapsedMilliseconds;

                // -- Checkpoint 4: Initial conditions --
                for (int i = 0; i < this.WaterBodyNodes.Length; ++i)
                {
                    // Reset per-node climate multipliers to neutral (U9/U10 Apply methods will overwrite if active)
                    this.WaterBodyNodes[i].LocalRainfallMultiplier = 1.0;
                    this.WaterBodyNodes[i].LocalEvaporationMultiplier = 1.0;
                    this.WaterBodyNodes[i].IgnoreUpstreamDamFlows = false;

                    if (runStartDate >= this.WaterBodyNodes[i].StartDate && runStartDate < this.WaterBodyNodes[i].EndDate)
                    {
                        this.WaterBodyNodes[i].StorageCapacityVolumeAtSpill = this.WaterBodyNodes[i].MaxStorageCapacityVolumeAtSpill;
                        this.WaterBodyNodes[i].VolumeInStorage = startFraction * this.WaterBodyNodes[i].StorageCapacityVolumeAtSpill;
                        this.WaterBodyNodes[i].StartTimeStepVolumeInStorage = this.WaterBodyNodes[i].VolumeInStorage;
                        this.WaterBodyNodes[i].SurfaceAreaStored = this.WaterBodyNodes[i].CalculateSurfaceArea();
                    }
                    else
                    {
                        this.WaterBodyNodes[i].StorageCapacityVolumeAtSpill = 0.0;
                        this.WaterBodyNodes[i].VolumeInStorage = 0.0;
                        this.WaterBodyNodes[i].StartTimeStepVolumeInStorage = 0.0;
                        this.WaterBodyNodes[i].SurfaceAreaStored = 0.0;
                    }
                }

                //Console.WriteLine($"    RVD checkpoint 4 (initial conditions):      {sw.ElapsedMilliseconds - lastMs,6}ms");
                lastMs = sw.ElapsedMilliseconds;
        }

            // Checkpoint 5: TS demand
            ResetDemandModelCapacities(this.TimeSeriesDemandModels, volumeScaleFactorWithSurfaceAreaChange, baseScenarioTSDemandCapacities);

            // Checkpoint 6: RM demand
            ResetDemandModelCapacities(this.RepeatingMonthlyDemandModels, volumeScaleFactorWithSurfaceAreaChange, baseScenarioRMDemandCapacities);

            //Console.WriteLine($"    RVD checkpoint 6 (RM demand init):          {sw.ElapsedMilliseconds - lastMs,6}ms  [{this.RepeatingMonthlyDemandModels?.Length ?? 0} models]");
            //Console.WriteLine($"    RVD TOTAL:                                  {sw.ElapsedMilliseconds,6}ms");
        }

        /// <summary>Resets demand-model storage capacities to base × scaleFactor when base capacities are supplied; otherwise falls back to legacy *= behaviour for single-run callers.</summary>
        /// <param name="models">Demand-model array (TS or RM) to update; null-safe.</param>
        /// <param name="scaleFactor">Scenario LOD volume scale factor with surface-area change applied.</param>
        /// <param name="baseCapacities">Pristine base capacities captured before the MC loop; null for non-MC callers.</param>
        private static void ResetDemandModelCapacities(BaseDemandModel[] models, double scaleFactor, double[] baseCapacities)
        {
            if (models == null) return;
            bool useBase = baseCapacities != null && baseCapacities.Length == models.Length;
            for (int i = 0; i < models.Length; i++)
            {
                models[i].DamStorageCapacityVolumeAtSpill = useBase
                    ? scaleFactor * baseCapacities[i]
                    : scaleFactor * models[i].DamStorageCapacityVolumeAtSpill;
                Array.Fill(models[i].MonthlyScaleFactors, 1.0);
                models[i].Initialise();
            }
        }

        /// <summary>
        /// Performs a topological sort of the water bodies array so that upstream (leaf) nodes come first and the outlet comes last, using Kahn's algorithm (BFS-based).
        /// The array is reordered in-place. If a cycle is detected (not all nodes processed), a warning is written to the console and unprocessed entries retain their original positions.
        /// After sorting, downstream positions must be reassigned via <see cref="AllocateDownstreamPositions"/>.
        /// </summary>
        /// <param name="allWaterBodies">Array of water bodies to sort in-place.</param>
        internal static void TopologicalSortWaterBodies(WaterBodyWithCatchment[] allWaterBodies)
        {
            int n = allWaterBodies.Length;

            // Count how many nodes drain INTO each node (in-degree in the reversed graph)
            int[] inDegree = new int[n];
            for (int i = 0; i < n; i++)
            {
                int ds = allWaterBodies[i].NextDownstreamArrayPosition;
                if (ds >= 0 && ds < n)
                {
                    inDegree[ds]++;
                }
            }

            // Start with leaf nodes (no upstream nodes draining into them)
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (inDegree[i] == 0)
                {
                    queue.Enqueue(i);
                }
            }

            // BFS: process each node, then "remove" it by decrementing downstream node's in-degree
            int[] sortedOrder = new int[n];
            int count = 0;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                sortedOrder[count++] = current;

                int ds = allWaterBodies[current].NextDownstreamArrayPosition;
                if (ds >= 0 && ds < n)
                {
                    inDegree[ds]--;
                    if (inDegree[ds] == 0)
                    {
                        queue.Enqueue(ds);
                    }
                }
            }

            if (count != n)
            {
                Console.WriteLine($"WARNING: Topological sort processed {count} of {n} nodes. Possible cycle in network.");
            }

            // Reorder the array in-place using the sorted indices
            WaterBodyWithCatchment[] sorted = new WaterBodyWithCatchment[n];
            for (int i = 0; i < count; i++)
            {
                sorted[i] = allWaterBodies[sortedOrder[i]];
            }

            Array.Copy(sorted, allWaterBodies, n);
        }

        /// <summary>
        /// Gets repeating monthly demand model for the node of a legacy RODIS model from the demand group index.
        /// </summary>
        /// <param name="legacyRODISDamNode">Legacy RODIS farm dam node.</param>
        /// <param name="settings">Overall settings of legacy RODIS model, which contain the repeating monthly demand group patterns.</param>
        /// <param name="demandGroupIndex">Index of demand pattern group for this farm dam node.</param>
        /// <returns>Repeating monthly demand model for water body node.</returns>
        private FarmDamRepeatingMonthlyDemandModel GetRepeatingMonthlyDemandModel(LegacyRODISDamNode legacyRODISDamNode, RODISSettings settings, string demandGroupIndex)
        {
            FarmDamRepeatingMonthlyDemandModel selectedModel = settings.RepeatingMonthlyDemandGroups[demandGroupIndex];

            FarmDamRepeatingMonthlyDemandModel newDemandModel = new FarmDamRepeatingMonthlyDemandModel()
            {
                AnnualDemandFactor = selectedModel.AnnualDemandFactor,
                DamStorageCapacityVolumeAtSpill = legacyRODISDamNode.VolumeML,
                DemandGroup = legacyRODISDamNode.DemandGroup,
            };

            newDemandModel.MonthlyDemandProportions = new double[selectedModel.MonthlyDemandProportions.Length];
            for (int i = 0; i < selectedModel.MonthlyDemandProportions.Length; ++i)
            {
                newDemandModel.MonthlyDemandProportions[i] = selectedModel.MonthlyDemandProportions[i];
            }

            newDemandModel.Initialise();

            return newDemandModel;
        }

        /// <summary>
        /// Gets time series demand model for the node of a legacy RODIS model from the demand group index.
        /// </summary>
        /// <param name="legacyRODISDamNode">Legacy RODIS farm dam node.</param>
        /// <param name="settings">Overall settings of legacy RODIS model, which contain the time series demand data.</param>
        /// <param name="demandGroupIndex">Index of demand pattern group for this farm dam node.</param>
        /// <returns>Time series demand model for water body node.</returns>
        private FarmDamTimeSeriesDemandModel GetTimeSeriesDemandModel(LegacyRODISDamNode legacyRODISDamNode, RODISSettings settings, string demandGroupIndex)
        {
            FarmDamTimeSeriesDemandModel selectedModel = settings.TimeSeriesDemandGroups[demandGroupIndex];

            FarmDamTimeSeriesDemandModel newDemandModel = new FarmDamTimeSeriesDemandModel()
            {
                AnnualDemandFactor = selectedModel.AnnualDemandFactor,
                DamStorageCapacityVolumeAtSpill = legacyRODISDamNode.VolumeML,
                DemandGroup = legacyRODISDamNode.DemandGroup,
            };

            newDemandModel.InputPattern = new Series.TimeSeriesValue[selectedModel.InputPattern.Length];
            for (int i = 0; i < selectedModel.InputPattern.Length; ++i)
            {
                newDemandModel.InputPattern[i] = selectedModel.InputPattern[i].DeepCopy();
            }

            newDemandModel.Initialise();

            return newDemandModel;
        }

        /// <summary>
        /// Calculates total upstream catchment areas at all nodes by traversing the network in ElementModelCalculationOrder. 
        /// TotalCatchmentAreaKM2 is read from the outlet node's traversal result. A diagnostic check verifies that the traversal result matches the direct sum of 
        /// all SubcatchmentInflowModel.AreaKM2 values — a mismatch indicates a routing or calculation order defect.
        /// </summary>
        private void CalculateTotalCatchmentAreas()
        {
            // -- Zero all node-level accumulators --
            for (int i = 0; i < this.WaterBodyNodes.Length; i++)
            {
                this.WaterBodyNodes[i].TotalUpstreamCatchmentAreaKM2 = 0.0;
            }

            for (int i = 0; i < this.ConfluenceNodes.Length; i++)
            {
                this.ConfluenceNodes[i].TotalUpstreamCatchmentAreaKM2 = 0.0;
            }

            // -- Traverse network to accumulate upstream catchment area at each node --
            for (int i = 0; i < this.ElementModelCalculationOrder.Length; i++)
            {
                int thisIndex = this.ElementModelCalculationOrder[i].IndexForElementType;
                int dsIndex = this.ElementModelCalculationOrder[i].IndexForNextDownstreamElementType;
                ModelElementType dsType = this.ElementModelCalculationOrder[i].NextDownstreamElementType;

                switch (this.ElementModelCalculationOrder[i].ElementType)
                {
                    case ModelElementType.SubcatchmentInflow:
                        if (dsType == ModelElementType.WaterBodyNode)
                        {
                            this.WaterBodyNodes[dsIndex].TotalUpstreamCatchmentAreaKM2 += this.SubcatchmentsInflowModels[thisIndex].AreaKM2;
                        }
                        else if (dsType == ModelElementType.ConfluenceNode)
                        {
                            this.ConfluenceNodes[dsIndex].TotalUpstreamCatchmentAreaKM2 += this.SubcatchmentsInflowModels[thisIndex].AreaKM2;
                        }
                        break;

                    case ModelElementType.WaterBodyNode:
                        if (dsType == ModelElementType.StraightThroughRoutingLink && dsIndex >= 0 && dsIndex < this.StraightThroughRoutingLinks.Length)
                        {
                            this.StraightThroughRoutingLinks[dsIndex].TotalUpstreamCatchmentAreaKM2 = this.WaterBodyNodes[thisIndex].TotalUpstreamCatchmentAreaKM2;
                        }
                        break;

                    case ModelElementType.ConfluenceNode:
                        if (dsType == ModelElementType.StraightThroughRoutingLink && dsIndex >= 0 && dsIndex < this.StraightThroughRoutingLinks.Length)
                        {
                            this.StraightThroughRoutingLinks[dsIndex].TotalUpstreamCatchmentAreaKM2 = this.ConfluenceNodes[thisIndex].TotalUpstreamCatchmentAreaKM2;
                        }
                        break;

                    case ModelElementType.StraightThroughRoutingLink:
                        if (dsType == ModelElementType.WaterBodyNode)
                        {
                            this.WaterBodyNodes[dsIndex].TotalUpstreamCatchmentAreaKM2 += this.StraightThroughRoutingLinks[thisIndex].TotalUpstreamCatchmentAreaKM2;
                        }
                        else if (dsType == ModelElementType.ConfluenceNode)
                        {
                            this.ConfluenceNodes[dsIndex].TotalUpstreamCatchmentAreaKM2 += this.StraightThroughRoutingLinks[thisIndex].TotalUpstreamCatchmentAreaKM2;
                        }
                        break;
                }
            }

            // -- Set TotalCatchmentAreaKM2 from the outlet traversal result --
            int lastIndex = this.ElementModelCalculationOrder.Last().IndexForElementType;
            switch (this.ElementModelCalculationOrder.Last().ElementType)
            {
                case ModelElementType.ConfluenceNode:
                    this.TotalCatchmentAreaKM2 = this.ConfluenceNodes[lastIndex].TotalUpstreamCatchmentAreaKM2;
                    break;
                case ModelElementType.WaterBodyNode:
                    this.TotalCatchmentAreaKM2 = this.WaterBodyNodes[lastIndex].TotalUpstreamCatchmentAreaKM2;
                    break;
                case ModelElementType.StraightThroughRoutingLink:
                    this.TotalCatchmentAreaKM2 = this.StraightThroughRoutingLinks[lastIndex].TotalUpstreamCatchmentAreaKM2;
                    break;
            }

            // -- Diagnostic: verify direct sum matches traversal --
            double directSumArea = 0.0;
            for (int i = 0; i < this.SubcatchmentsInflowModels.Length; i++)
            {
                directSumArea += this.SubcatchmentsInflowModels[i].AreaKM2;
            }

            if (Math.Abs(this.TotalCatchmentAreaKM2 - directSumArea) > 1.0E-6)
            {
                Console.WriteLine(
                    $"ERROR: TotalCatchmentAreaKM2 from traversal ({this.TotalCatchmentAreaKM2:F6} km²) "
                    + $"differs from direct sum ({directSumArea:F6} km²). "
                    + $"Difference = {directSumArea - this.TotalCatchmentAreaKM2:F6} km². "
                    + "Check calculation order and routing link downstream types.");
            }
        }

        /// <summary>
        /// Calculates non-water body areas upstream of all model elements (nodes and links), as RODIS handles catchment runoff separately from direct net rainfall and evaporation on water bodies.
        /// </summary>
        private void CalculateNonWaterbodyAreas()
        {
            // First set the total upstream areas at all the nodes to 0
            for (int i = 0; i < this.WaterBodyNodes.Length; i++)
            {
                this.WaterBodyNodes[i].TotalUpstreamNonWaterCatchmentAreaKM2 = 0.0;
                this.WaterBodyNodes[i].NonWaterCatchmentAreaDownstreamOfDamsKM2 = 0.0;
                this.WaterBodyNodes[i].NonWaterCatchmentAreaUpstreamOfDamsKM2 = 0.0;
            }

            for (int i = 0; i < this.ConfluenceNodes.Length; i++)
            {
                this.ConfluenceNodes[i].TotalUpstreamNonWaterCatchmentAreaKM2 = 0.0;
                this.ConfluenceNodes[i].NonWaterCatchmentAreaDownstreamOfDamsKM2 = 0.0;
                this.ConfluenceNodes[i].NonWaterCatchmentAreaUpstreamOfDamsKM2 = 0.0;
            }

            // Need to traverse network to work out non-water body catchment area downstream of all dams
            for (int i = 0; i < this.ElementModelCalculationOrder.Length; i++)
            {
                int thisIndex = this.ElementModelCalculationOrder[i].IndexForElementType;
                int dsIndex = this.ElementModelCalculationOrder[i].IndexForNextDownstreamElementType;
                ModelElementType dsType = this.ElementModelCalculationOrder[i].NextDownstreamElementType;

                switch (this.ElementModelCalculationOrder[i].ElementType)
                {
                    case ModelElementType.SubcatchmentInflow:
                        {
                            if (dsType == ModelElementType.WaterBodyNode)
                            {
                                this.SubcatchmentsInflowModels[thisIndex].WaterBodyAreaKM2 = this.WaterBodyNodes[dsIndex].SurfaceAreaStored * 1.0E-6;
                                this.SubcatchmentsInflowModels[thisIndex].BeforeRunTimeStep(this.IsLegacyRODISCalculationMethods);

                                this.WaterBodyNodes[dsIndex].TotalUpstreamNonWaterCatchmentAreaKM2 += this.SubcatchmentsInflowModels[thisIndex].NonWaterBodyAreaKM2;
                                this.WaterBodyNodes[dsIndex].NonWaterCatchmentAreaUpstreamOfDamsKM2 = this.WaterBodyNodes[dsIndex].TotalUpstreamNonWaterCatchmentAreaKM2;
                                this.WaterBodyNodes[dsIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2 = 0.0;
                            }
                            else
                            {
                                if (dsType == ModelElementType.ConfluenceNode)
                                {
                                    this.SubcatchmentsInflowModels[thisIndex].WaterBodyAreaKM2 = 0;
                                    this.SubcatchmentsInflowModels[thisIndex].BeforeRunTimeStep(this.IsLegacyRODISCalculationMethods);
                                    this.ConfluenceNodes[dsIndex].TotalUpstreamNonWaterCatchmentAreaKM2 += this.SubcatchmentsInflowModels[thisIndex].NonWaterBodyAreaKM2;
                                    this.ConfluenceNodes[dsIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2 += this.SubcatchmentsInflowModels[thisIndex].NonWaterBodyAreaKM2;
                                }
                            }
                            break;
                        }

                    case ModelElementType.WaterBodyNode:
                        if (dsType == ModelElementType.StraightThroughRoutingLink && dsIndex >= 0 && dsIndex < this.StraightThroughRoutingLinks.Length)
                        {
                            this.StraightThroughRoutingLinks[dsIndex].TotalUpstreamNonWaterCatchmentAreaKM2 = this.WaterBodyNodes[thisIndex].TotalUpstreamNonWaterCatchmentAreaKM2;
                            this.StraightThroughRoutingLinks[dsIndex].NonWaterCatchmentAreaUpstreamOfDamsKM2 = this.WaterBodyNodes[thisIndex].NonWaterCatchmentAreaUpstreamOfDamsKM2;
                            this.StraightThroughRoutingLinks[dsIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2 = this.WaterBodyNodes[thisIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2;
                        }
                        break;

                    case ModelElementType.ConfluenceNode:
                        if (dsType == ModelElementType.StraightThroughRoutingLink && dsIndex >= 0 && dsIndex < this.StraightThroughRoutingLinks.Length)
                        {
                            this.StraightThroughRoutingLinks[dsIndex].TotalUpstreamNonWaterCatchmentAreaKM2 = this.ConfluenceNodes[thisIndex].TotalUpstreamNonWaterCatchmentAreaKM2;
                            this.StraightThroughRoutingLinks[dsIndex].NonWaterCatchmentAreaUpstreamOfDamsKM2 = this.ConfluenceNodes[thisIndex].NonWaterCatchmentAreaUpstreamOfDamsKM2;
                            this.StraightThroughRoutingLinks[dsIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2 = this.ConfluenceNodes[thisIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2;
                        }
                        break;

                    case ModelElementType.StraightThroughRoutingLink:
                        {
                            if (dsType == ModelElementType.WaterBodyNode)
                            {
                                this.WaterBodyNodes[dsIndex].TotalUpstreamNonWaterCatchmentAreaKM2 += this.StraightThroughRoutingLinks[thisIndex].TotalUpstreamNonWaterCatchmentAreaKM2;
                                this.WaterBodyNodes[dsIndex].NonWaterCatchmentAreaUpstreamOfDamsKM2 = this.WaterBodyNodes[dsIndex].TotalUpstreamNonWaterCatchmentAreaKM2;
                                this.WaterBodyNodes[dsIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2 = 0.0;
                            }
                            else
                            {
                                if (dsType == ModelElementType.ConfluenceNode)
                                {
                                    this.ConfluenceNodes[dsIndex].TotalUpstreamNonWaterCatchmentAreaKM2 += this.StraightThroughRoutingLinks[thisIndex].TotalUpstreamNonWaterCatchmentAreaKM2;
                                    this.ConfluenceNodes[dsIndex].NonWaterCatchmentAreaUpstreamOfDamsKM2 += this.StraightThroughRoutingLinks[thisIndex].NonWaterCatchmentAreaUpstreamOfDamsKM2;
                                    this.ConfluenceNodes[dsIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2 += this.StraightThroughRoutingLinks[thisIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2;
                                }
                            }
                            break;
                        }
                }
            }

            int lastIndex = this.ElementModelCalculationOrder.Last().IndexForElementType;
            switch (this.ElementModelCalculationOrder.Last().ElementType)
            {
                case ModelElementType.ConfluenceNode:
                    this.TotalNonWaterBodyAreaKM2 = this.ConfluenceNodes[lastIndex].TotalUpstreamNonWaterCatchmentAreaKM2;
                    this.NonWaterbodyCatchmentAreaDownstreamOfDamsKM2 = this.ConfluenceNodes[lastIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2;
                    break;

                case ModelElementType.WaterBodyNode:
                    this.TotalNonWaterBodyAreaKM2 = this.WaterBodyNodes[lastIndex].TotalUpstreamNonWaterCatchmentAreaKM2;
                    this.NonWaterbodyCatchmentAreaDownstreamOfDamsKM2 = this.WaterBodyNodes[lastIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2;
                    break;

                case ModelElementType.StraightThroughRoutingLink:
                    this.TotalNonWaterBodyAreaKM2 = this.StraightThroughRoutingLinks[lastIndex].TotalUpstreamNonWaterCatchmentAreaKM2;
                    this.NonWaterbodyCatchmentAreaDownstreamOfDamsKM2 = this.StraightThroughRoutingLinks[lastIndex].NonWaterCatchmentAreaDownstreamOfDamsKM2;
                    break;
            }
        }

        /// <summary>
        /// Calculates unimpacted inflows from upstream catchments at the time step.
        /// </summary>
        private void CalculateUnimpactedFlowsAtTimeStep()
        {
            double unimpactedFlowMLperKM2 = 0;

            if (this.TotalCatchmentAreaKM2 > 0)
            {
                unimpactedFlowMLperKM2 = Math.Max(0, this.UnimpactedFlow / this.TotalCatchmentAreaKM2);
            }

            for (int i = 0; i < this.ElementModelCalculationOrder.Length; i++)
            {
                int thisIndex = this.ElementModelCalculationOrder[i].IndexForElementType;

                switch (this.ElementModelCalculationOrder[i].ElementType)
                {
                    case ModelElementType.SubcatchmentInflow:
                        this.SubcatchmentsInflowModels[thisIndex].InflowRateMLPerKM2 = unimpactedFlowMLperKM2;
                        break;
                }
            }
        }

        /// <summary>Calculates flows at the time step through the RODIS model network for a single run.</summary>
        /// <param name="timeStep">Datetime of this time step, which controls which water bodies are in place, which farm dams have bypasses etc.</param>
        /// <param name="isAdoptedRun">Flag set to true if this is a single run (for impacted from unimpacted) or the last run to adopt for the time step.</param>
        private void CalculateFlowsAtTimeStep(TimeSpan timeStep, bool isAdoptedRun = true)
        {
            // First set the upstream flows at all the nodes to 0
            for (int i = 0; i < this.WaterBodyNodes.Length; i++)
            {
                this.WaterBodyNodes[i].UpstreamFlow = 0.0;
                this.WaterBodyNodes[i].UpstreamFlowFromBypass = 0.0;
                this.WaterBodyNodes[i].UpstreamFlowFromSpill = 0.0;
                this.WaterBodyNodes[i].UpstreamFlowFromCatchment = 0.0;
            }

            for (int i = 0; i < this.ConfluenceNodes.Length; i++)
            {
                this.ConfluenceNodes[i].UpstreamFlow = 0.0;
                this.ConfluenceNodes[i].UpstreamFlowFromBypass = 0.0;
                this.ConfluenceNodes[i].UpstreamFlowFromSpill = 0.0;
                this.ConfluenceNodes[i].UpstreamFlowFromCatchment = 0.0;
            }

            // Traverse the network in element model calculation order
            for (int i = 0; i < this.ElementModelCalculationOrder.Length; i++)
            {
                int thisIndex = this.ElementModelCalculationOrder[i].IndexForElementType;
                int dsIndex = this.ElementModelCalculationOrder[i].IndexForNextDownstreamElementType;
                ModelElementType dsType = this.ElementModelCalculationOrder[i].NextDownstreamElementType;

                switch (this.ElementModelCalculationOrder[i].ElementType)
                {
                    case ModelElementType.SubcatchmentInflow:
                        {
                            this.SubcatchmentsInflowModels[thisIndex].RunTimeStep();

                            // Add inflow to relevant node model
                            if (dsType == ModelElementType.WaterBodyNode)
                            {
                                this.WaterBodyNodes[dsIndex].UpstreamFlow += this.SubcatchmentsInflowModels[thisIndex].DownstreamFlow;
                                this.WaterBodyNodes[dsIndex].UpstreamFlowFromCatchment += this.SubcatchmentsInflowModels[thisIndex].DownstreamFlow;
                            }
                            else
                            {
                                if (dsType == ModelElementType.ConfluenceNode)
                                {
                                    this.ConfluenceNodes[dsIndex].UpstreamFlow += this.SubcatchmentsInflowModels[thisIndex].DownstreamFlow;
                                    this.ConfluenceNodes[dsIndex].UpstreamFlowFromCatchment += this.SubcatchmentsInflowModels[thisIndex].DownstreamFlow;
                                }
                            }
                            break;
                        }

                    case ModelElementType.RepeatingMonthlyDemand:
                        {
                            this.RepeatingMonthlyDemandModels[thisIndex].RunTimeStep(this.SimulationDateTime, timeStep);

                            // Set unrestricted demand for relevant node model
                            if (dsType == ModelElementType.WaterBodyNode)
                            {
                                this.WaterBodyNodes[dsIndex].UnrestrictedDemand = this.RepeatingMonthlyDemandModels[thisIndex].UnrestrictedDemand;
                            }
                            break;
                        }

                    case ModelElementType.TimeSeriesDemand:
                        {
                            this.TimeSeriesDemandModels[thisIndex].RunTimeStep(this.SimulationDateTime, timeStep);

                            // Set unrestricted demand for relevant node model
                            if (dsType == ModelElementType.WaterBodyNode)
                            {
                                this.WaterBodyNodes[dsIndex].UnrestrictedDemand = this.TimeSeriesDemandModels[thisIndex].UnrestrictedDemand;
                            }
                            break;
                        }

                    case ModelElementType.WaterBodyNode:
                        {
                            // -- U5: Independent topology — strip out upstream dam cascade flows --
                            // When IgnoreUpstreamDamFlows is set, this dam only receives local subcatchment runoff. Upstream dam spill and bypass flows are stripped before RunTimeStep
                            // and passed through to the downstream routing link afterwards, preserving system mass balance.

                            double strippedUpstreamDamFlow = 0.0;
                            if (this.WaterBodyNodes[thisIndex].IgnoreUpstreamDamFlows)
                            {
                                strippedUpstreamDamFlow = this.WaterBodyNodes[thisIndex].UpstreamFlow - this.WaterBodyNodes[thisIndex].UpstreamFlowFromCatchment;
                                this.WaterBodyNodes[thisIndex].UpstreamFlow = this.WaterBodyNodes[thisIndex].UpstreamFlowFromCatchment;
                                this.WaterBodyNodes[thisIndex].UpstreamFlowFromBypass = 0.0;
                                this.WaterBodyNodes[thisIndex].UpstreamFlowFromSpill = 0.0;
                            }

                            this.WaterBodyNodes[thisIndex].Rainfall = this.Rainfall * this.RainfallMultiplier * this.WaterBodyNodes[thisIndex].LocalRainfallMultiplier;
                            this.WaterBodyNodes[thisIndex].Evaporation = this.Evaporation * this.PETMultiplier * this.WaterBodyNodes[thisIndex].LocalEvaporationMultiplier;
                            this.WaterBodyNodes[thisIndex].RunTimeStep(this.SimulationDateTime, timeStep, this.IsLegacyRODISCalculationMethods, isAdoptedRun);

                            // U5: Pass stripped upstream dam flows through — they bypass this dam entirely and continue downstream as catchment-attributed flow to preserve flow attribution identity:
                            // DownstreamFlow = DownstreamFlowFromBypass + DownstreamFlowFromSpill + DownstreamFlowFromCatchment
                            if (strippedUpstreamDamFlow > 0.0)
                            {
                                this.WaterBodyNodes[thisIndex].DownstreamFlow += strippedUpstreamDamFlow;
                                this.WaterBodyNodes[thisIndex].DownstreamFlowFromCatchment += strippedUpstreamDamFlow;
                            }

                            // Set downstream flow as upstream flow for relevant downstream link
                            if (dsType == ModelElementType.StraightThroughRoutingLink && dsIndex >= 0 && dsIndex < this.StraightThroughRoutingLinks.Length)
                            {
                                this.StraightThroughRoutingLinks[dsIndex].UpstreamFlow = this.WaterBodyNodes[thisIndex].DownstreamFlow;
                                this.StraightThroughRoutingLinks[dsIndex].UpstreamFlowFromBypass = this.WaterBodyNodes[thisIndex].DownstreamFlowFromBypass;
                                this.StraightThroughRoutingLinks[dsIndex].UpstreamFlowFromSpill = this.WaterBodyNodes[thisIndex].DownstreamFlowFromSpill;
                                this.StraightThroughRoutingLinks[dsIndex].UpstreamFlowFromCatchment = this.WaterBodyNodes[thisIndex].DownstreamFlowFromCatchment;
                            }

                            break;
                        }

                    case ModelElementType.ConfluenceNode:
                        {
                            this.ConfluenceNodes[thisIndex].RunTimeStep(this.SimulationDateTime, timeStep, this.IsLegacyRODISCalculationMethods);

                            // Set downstream flow as upstream flow for relevant downstream link
                            if (dsType == ModelElementType.StraightThroughRoutingLink && dsIndex >= 0 && dsIndex < this.StraightThroughRoutingLinks.Length)
                            {
                                this.StraightThroughRoutingLinks[dsIndex].UpstreamFlow = this.ConfluenceNodes[thisIndex].DownstreamFlow;
                                this.StraightThroughRoutingLinks[dsIndex].UpstreamFlowFromBypass = this.ConfluenceNodes[thisIndex].DownstreamFlowFromBypass;
                                this.StraightThroughRoutingLinks[dsIndex].UpstreamFlowFromSpill = this.ConfluenceNodes[thisIndex].DownstreamFlowFromSpill;
                                this.StraightThroughRoutingLinks[dsIndex].UpstreamFlowFromCatchment = this.ConfluenceNodes[thisIndex].DownstreamFlowFromCatchment;
                            }
                            break;
                        }

                    case ModelElementType.StraightThroughRoutingLink:
                        {
                            this.StraightThroughRoutingLinks[thisIndex].RunTimeStep();

                            // Add downstream flow to upstream flow for next node downstream
                            if (dsType == ModelElementType.WaterBodyNode)
                            {
                                this.WaterBodyNodes[dsIndex].UpstreamFlow += this.StraightThroughRoutingLinks[thisIndex].DownstreamFlow;
                                this.WaterBodyNodes[dsIndex].UpstreamFlowFromBypass += this.StraightThroughRoutingLinks[thisIndex].DownstreamFlowFromBypass;
                                this.WaterBodyNodes[dsIndex].UpstreamFlowFromSpill += this.StraightThroughRoutingLinks[thisIndex].DownstreamFlowFromSpill;
                                this.WaterBodyNodes[dsIndex].UpstreamFlowFromCatchment += this.StraightThroughRoutingLinks[thisIndex].DownstreamFlowFromCatchment;
                            }
                            else
                            {
                                if (dsType == ModelElementType.ConfluenceNode)
                                {
                                    this.ConfluenceNodes[dsIndex].UpstreamFlow += this.StraightThroughRoutingLinks[thisIndex].DownstreamFlow;
                                    this.ConfluenceNodes[dsIndex].UpstreamFlowFromBypass += this.StraightThroughRoutingLinks[thisIndex].DownstreamFlowFromBypass;
                                    this.ConfluenceNodes[dsIndex].UpstreamFlowFromSpill += this.StraightThroughRoutingLinks[thisIndex].DownstreamFlowFromSpill;
                                    this.ConfluenceNodes[dsIndex].UpstreamFlowFromCatchment += this.StraightThroughRoutingLinks[thisIndex].DownstreamFlowFromCatchment;
                                }
                            }
                            break;
                        }
                }
            }

            // get the flows at the outlet nodes
            int lastIndex = this.ElementModelCalculationOrder.Last().IndexForElementType;
            switch (this.ElementModelCalculationOrder.Last().ElementType)
            {
                case ModelElementType.ConfluenceNode:
                    this.DownstreamFlow = this.ConfluenceNodes[lastIndex].DownstreamFlow;
                    this.DownstreamFlowFromBypass = this.ConfluenceNodes[lastIndex].DownstreamFlowFromBypass;
                    this.DownstreamFlowFromSpill = this.ConfluenceNodes[lastIndex].DownstreamFlowFromSpill;
                    this.DownstreamFlowFromCatchment = this.ConfluenceNodes[lastIndex].DownstreamFlowFromCatchment;
                    break;

                case ModelElementType.WaterBodyNode:
                    this.DownstreamFlow = this.WaterBodyNodes[lastIndex].DownstreamFlow;
                    this.DownstreamFlowFromBypass = this.WaterBodyNodes[lastIndex].DownstreamFlowFromBypass;
                    this.DownstreamFlowFromSpill = this.WaterBodyNodes[lastIndex].DownstreamFlowFromSpill;
                    this.DownstreamFlowFromCatchment = this.WaterBodyNodes[lastIndex].DownstreamFlowFromCatchment;
                    break;

                case ModelElementType.StraightThroughRoutingLink:
                    this.DownstreamFlow = this.StraightThroughRoutingLinks[lastIndex].DownstreamFlow;
                    this.DownstreamFlowFromBypass = this.StraightThroughRoutingLinks[lastIndex].DownstreamFlowFromBypass;
                    this.DownstreamFlowFromSpill = this.StraightThroughRoutingLinks[lastIndex].DownstreamFlowFromSpill;
                    this.DownstreamFlowFromCatchment = this.StraightThroughRoutingLinks[lastIndex].DownstreamFlowFromCatchment;
                    break;
            }
        }

        /// <summary>
        /// Assign index numbers for each reporting group to water body and confluence nodes.
        /// </summary>
        /// <param name="reportingGroupsList">List of reporting group names to compare to those in the nodes.</param>
        public void AssignReportingGroupIndices (List<string> reportingGroupsList)
        {
            for (int j = 0; j < this.WaterBodyNodes.Length; j++)
            {
                int reportingGroupIndex = reportingGroupsList.IndexOf(this.WaterBodyNodes[j].ReportingGroup);
                if (reportingGroupIndex >= 0)
                {
                    this.WaterBodyNodes[j].ReportingGroupIndex = reportingGroupIndex;
                }
            }

            for (int j = 0; j < this.ConfluenceNodes.Length; j++)
            {
                int reportingGroupIndex = reportingGroupsList.IndexOf(this.ConfluenceNodes[j].ReportingGroup);
                if (reportingGroupIndex >= 0)
                {
                    this.ConfluenceNodes[j].ReportingGroupIndex = reportingGroupIndex;
                }
            }
        }

        /// <summary>
        /// Initialises the arrays for storing all results by reporting group.
        /// </summary>
        public void InitialiseReportingGroupArrays()
        {
            if (this.ReportingGroups != null)
            {
                if (this.ReportingGroups.Length > 0)
                {
                    this.NetImpactOnFlowByReportingGroup = new double[this.ReportingGroups.Length];
                    this.UnimpactedFlowByReportingGroup = new double[this.ReportingGroups.Length];
                    this.LocalCatchmentInflowByReportingGroup = new double[this.ReportingGroups.Length];
                    this.DownstreamFlowByReportingGroup = new double[this.ReportingGroups.Length];
                    this.VolumeInStorageByReportingGroup = new double[this.ReportingGroups.Length];
                    this.StorageCapacityVolumeAtSpillByReportingGroup = new double[this.ReportingGroups.Length];
                    this.ChangeInVolumeInStorageByReportingGroup = new double[this.ReportingGroups.Length];
                    this.BypassFlowCapacityByReportingGroup = new double[this.ReportingGroups.Length];
                    this.BypassDownstreamFlowByReportingGroup = new double[this.ReportingGroups.Length];
                    this.PumpedInflowByReportingGroup = new double[this.ReportingGroups.Length];
                    this.PumpedInflowCapacityByReportingGroup = new double[this.ReportingGroups.Length];
                    this.SpillDownstreamFlowByReportingGroup = new double[this.ReportingGroups.Length];
                    this.SurfaceAreaAtSpillByReportingGroup = new double[this.ReportingGroups.Length];
                    this.SurfaceAreaStoredByReportingGroup = new double[this.ReportingGroups.Length];
                    this.RainfallVolumeByReportingGroup = new double[this.ReportingGroups.Length];
                    this.EvaporationVolumeByReportingGroup = new double[this.ReportingGroups.Length];
                    this.NetRainfallVolumeByReportingGroup = new double[this.ReportingGroups.Length];
                    this.SeepageVolumeByReportingGroup = new double[this.ReportingGroups.Length];
                    this.UnrestrictedDemandByReportingGroup = new double[this.ReportingGroups.Length];
                    this.DemandVolumeExtractedByReportingGroup = new double[this.ReportingGroups.Length];
                    this.SeepageLossRateAtFullByReportingGroup = new double[this.ReportingGroups.Length];
                    this.VolumeBalanceMisclosureByReportingGroup = new double[this.ReportingGroups.Length];
                }
            }
        }

        /// <summary>
        /// Sums results across all reporting groups for time step.
        /// </summary>
        public void SumByReportingGroupForTimeStep()
        {
            int iRG = -1;
            for (iRG = 0; iRG < this.ReportingGroups.Length; ++iRG)
            {
                this.UnimpactedFlowByReportingGroup[iRG] = 0.0;
                this.DownstreamFlowByReportingGroup[iRG] = 0.0;
                this.NetImpactOnFlowByReportingGroup[iRG] = 0.0;
                this.LocalCatchmentInflowByReportingGroup[iRG] = 0.0;
                this.VolumeInStorageByReportingGroup[iRG] = 0.0;
                this.StorageCapacityVolumeAtSpillByReportingGroup[iRG] = 0.0;
                this.ChangeInVolumeInStorageByReportingGroup[iRG] = 0.0;
                this.BypassFlowCapacityByReportingGroup[iRG] = 0.0;
                this.BypassDownstreamFlowByReportingGroup[iRG] = 0.0;
                this.PumpedInflowByReportingGroup[iRG] = 0.0;
                this.PumpedInflowCapacityByReportingGroup[iRG] = 0.0;
                this.SpillDownstreamFlowByReportingGroup[iRG] = 0.0;
                this.SurfaceAreaAtSpillByReportingGroup[iRG] = 0.0;
                this.SurfaceAreaStoredByReportingGroup[iRG] = 0.0;
                this.RainfallVolumeByReportingGroup[iRG] = 0.0;
                this.EvaporationVolumeByReportingGroup[iRG] = 0.0;
                this.NetRainfallVolumeByReportingGroup[iRG] = 0.0;
                this.SeepageVolumeByReportingGroup[iRG] = 0.0;
                this.UnrestrictedDemandByReportingGroup[iRG] = 0.0;
                this.DemandVolumeExtractedByReportingGroup[iRG] = 0.0;
                this.SeepageLossRateAtFullByReportingGroup[iRG] = 0.0;
                this.VolumeBalanceMisclosureByReportingGroup[iRG] = 0.0;
            }

            for (int i = 0; i < this.ElementModelCalculationOrder.Length; i++)
            {
                int thisIndex = this.ElementModelCalculationOrder[i].IndexForElementType;

                switch (this.ElementModelCalculationOrder[i].ElementType)
                {
                    case ModelElementType.WaterBodyNode:
                        iRG = this.WaterBodyNodes[thisIndex].ReportingGroupIndex;

                        if (iRG >= 0)
                        {
                            this.UnimpactedFlowByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].UpstreamFlow;
                            this.DownstreamFlowByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].DownstreamFlow;
                            this.LocalCatchmentInflowByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].UpstreamFlowFromCatchment;
                            this.NetImpactOnFlowByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].UpstreamFlow - this.WaterBodyNodes[thisIndex].DownstreamFlow;
                            this.VolumeInStorageByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].VolumeInStorage;
                            this.StorageCapacityVolumeAtSpillByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].StorageCapacityVolumeAtSpill;
                            this.ChangeInVolumeInStorageByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].ChangeInVolumeInStorageForTimeStep;
                            this.BypassFlowCapacityByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].BypassFlowCapacityAtTimeStep;
                            this.BypassDownstreamFlowByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].DownstreamFlowFromBypass;
                            this.PumpedInflowByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].PumpedInflow;
                            this.PumpedInflowCapacityByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].PumpedInflowCapacityAtTimeStep;
                            this.SpillDownstreamFlowByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].DownstreamFlowFromSpill;
                            this.SurfaceAreaAtSpillByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].SurfaceAreaAtSpill;
                            this.SurfaceAreaStoredByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].SurfaceAreaStored;
                            this.RainfallVolumeByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].RainfallVolume;
                            this.EvaporationVolumeByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].EvaporationVolume;
                            this.NetRainfallVolumeByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].NetRainfallVolume;
                            this.SeepageVolumeByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].SeepageLossVolume;
                            this.UnrestrictedDemandByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].UnrestrictedDemand;
                            this.DemandVolumeExtractedByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].DemandVolumeExtracted;
                            this.SeepageLossRateAtFullByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].SeepageLossRateAtFull;
                            this.VolumeBalanceMisclosureByReportingGroup[iRG] += this.WaterBodyNodes[thisIndex].VolumeBalanceMisclosure;
                        }
                        break;

                    case ModelElementType.ConfluenceNode:
                        iRG = this.ConfluenceNodes[thisIndex].ReportingGroupIndex;
                        if (iRG >= 0)
                        {
                            this.UnimpactedFlowByReportingGroup[iRG] += this.ConfluenceNodes[thisIndex].UpstreamFlow;
                            this.DownstreamFlowByReportingGroup[iRG] += this.ConfluenceNodes[thisIndex].DownstreamFlow;
                            this.LocalCatchmentInflowByReportingGroup[iRG] += this.ConfluenceNodes[thisIndex].UpstreamFlowFromCatchment;
                        }
                        break;

                    case ModelElementType.SubcatchmentInflow:
                    case ModelElementType.RepeatingMonthlyDemand:
                    case ModelElementType.TimeSeriesDemand:
                    case ModelElementType.StraightThroughRoutingLink:
                        break;
                }
            }
        }

        /// <summary>Sums total catchment level results across all elements for the time step.</summary>
        public void SumTotalsForTimeStep()
        {
            this.VolumeInStorage = 0.0;
            this.StorageCapacityVolumeAtSpill = 0.0;
            this.BypassFlowCapacity = 0.0;
            this.PumpedInflow = 0.0;
            this.PumpedInflowCapacity = 0.0;
            this.SurfaceAreaAtSpill = 0.0;
            this.SurfaceAreaStored = 0.0;
            this.RainfallVolume = 0.0;
            this.EvaporationVolume = 0.0;
            this.NetRainfallVolume = 0.0;
            this.SeepageLossVolume = 0.0;
            this.UnrestrictedDemand = 0.0;
            this.DemandVolumeExtracted = 0.0;
            this.SeepageLossRateAtFull = 0.0;
            this.ChangeInVolumeInStorage = 0.0;
            this.TotalSubcatchmentRunoff = 0.0;
            this.DamRemovalStorageLoss = 0.0;

            for (int i = 0; i < this.ElementModelCalculationOrder.Length; i++)
            {
                int thisIndex = this.ElementModelCalculationOrder[i].IndexForElementType;
                switch (this.ElementModelCalculationOrder[i].ElementType)
                {
                    case ModelElementType.WaterBodyNode:
                        this.VolumeInStorage += this.WaterBodyNodes[thisIndex].VolumeInStorage;
                        this.ChangeInVolumeInStorage += this.WaterBodyNodes[thisIndex].ChangeInVolumeInStorageForTimeStep;
                        this.StorageCapacityVolumeAtSpill += this.WaterBodyNodes[thisIndex].StorageCapacityVolumeAtSpill;
                        this.BypassFlowCapacity += this.WaterBodyNodes[thisIndex].BypassFlowCapacityAtTimeStep;
                        this.PumpedInflow += this.WaterBodyNodes[thisIndex].PumpedInflow;
                        this.PumpedInflowCapacity += this.WaterBodyNodes[thisIndex].PumpedInflowCapacityAtTimeStep;
                        this.SurfaceAreaAtSpill += this.WaterBodyNodes[thisIndex].SurfaceAreaAtSpill;
                        this.SurfaceAreaStored += this.WaterBodyNodes[thisIndex].SurfaceAreaStored;
                        this.RainfallVolume += this.WaterBodyNodes[thisIndex].RainfallVolume;
                        this.EvaporationVolume += this.WaterBodyNodes[thisIndex].EvaporationVolume;
                        this.NetRainfallVolume += this.WaterBodyNodes[thisIndex].NetRainfallVolume;
                        this.SeepageLossVolume += this.WaterBodyNodes[thisIndex].SeepageLossVolume;
                        this.UnrestrictedDemand += this.WaterBodyNodes[thisIndex].UnrestrictedDemand;
                        this.DemandVolumeExtracted += this.WaterBodyNodes[thisIndex].DemandVolumeExtracted;
                        this.SeepageLossRateAtFull += this.WaterBodyNodes[thisIndex].SeepageLossRateAtFull;
                        this.DamRemovalStorageLoss += this.WaterBodyNodes[thisIndex].DamRemovalStorageLoss;
                        break;

                    case ModelElementType.ConfluenceNode:
                        this.PumpedInflow += this.ConfluenceNodes[thisIndex].PumpedInflow;
                        this.PumpedInflowCapacity += this.ConfluenceNodes[thisIndex].PumpedInflowCapacityAtTimeStep;
                        break;

                    case ModelElementType.SubcatchmentInflow:
                        this.TotalSubcatchmentRunoff += this.SubcatchmentsInflowModels[thisIndex].DownstreamFlow;
                        break;

                    case ModelElementType.RepeatingMonthlyDemand:
                    case ModelElementType.TimeSeriesDemand:
                    case ModelElementType.StraightThroughRoutingLink:
                        break;
                }
            }

            this.NetImpactOnFlow = this.UnimpactedFlow - this.DownstreamFlow;
        }

        /// <summary>
        /// Calculates the misclosure in the volume of water across all elements of the model.
        /// </summary>
        private void CalculateVolumeBalanceMisclosure ()
        {
            this.VolumeBalanceMisclosure = 0;

            for (int i = 0; i < this.ElementModelCalculationOrder.Length; i++)
            {
                int thisIndex = this.ElementModelCalculationOrder[i].IndexForElementType;

                switch (this.ElementModelCalculationOrder[i].ElementType)
                {
                    case ModelElementType.SubcatchmentInflow:
                        this.VolumeBalanceMisclosure += this.SubcatchmentsInflowModels[thisIndex].VolumeBalanceMisclosure;
                        break;

                    case ModelElementType.RepeatingMonthlyDemand:
                    case ModelElementType.TimeSeriesDemand:
                        this.VolumeBalanceMisclosure += 0.0;
                        break;

                    case ModelElementType.WaterBodyNode:
                        this.VolumeBalanceMisclosure += this.WaterBodyNodes[thisIndex].VolumeBalanceMisclosure;
                        break;

                    case ModelElementType.ConfluenceNode:
                        this.VolumeBalanceMisclosure += this.ConfluenceNodes[thisIndex].VolumeBalanceMisclosure;
                        break;

                    case ModelElementType.StraightThroughRoutingLink:
                        this.VolumeBalanceMisclosure += this.StraightThroughRoutingLinks[thisIndex].VolumeBalanceMisclosure;
                        break;
                }
            }
        }

        /// <summary>
        /// Computes a top-down mass balance using TotalSubcatchmentRunoff as the system runoff input.
        /// This is an independent check against the bottom-up (per-node) balance.
        ///
        ///   Inputs  = TotalSubcatchmentRunoff + RainfallVolume + PumpedInflow
        ///   Outputs = DownstreamFlow + EvaporationVolume + SeepageLossVolume
        ///           + DemandVolumeExtracted + ?Storage + DamRemovalStorageLoss
        ///   TopDownMisclosure = Inputs - Outputs
        ///
        /// TotalSubcatchmentRunoff is the direct sum of all SubcatchmentInflowModel.DownstreamFlow values, computed in SumTotalsForTimeStep(). 
        /// It reflects the actual runoff entering the system at each time step, including the effect of dam surface area deductions.
        /// </summary>
        private void CalculateTopDownMassBalance()
        {
            double totalInputs = this.TotalSubcatchmentRunoff + this.RainfallVolume + this.PumpedInflow;

            double totalOutputsAndStorage =
                this.DownstreamFlow
                + this.EvaporationVolume
                + this.SeepageLossVolume
                + this.DemandVolumeExtracted
                + this.ChangeInVolumeInStorage
                + this.DamRemovalStorageLoss;

            this.TopDownVolumeBalanceMisclosure = totalInputs - totalOutputsAndStorage;

            // -- Update cumulative trackers --
            this.CumulativeBottomUpMisclosure += this.VolumeBalanceMisclosure;
            this.CumulativeTopDownMisclosure += this.TopDownVolumeBalanceMisclosure;
            this.CumulativeDamRemovalStorageLoss += this.DamRemovalStorageLoss;

            // -- Track worst-case bottom-up misclosure --
            double absBottomUp = Math.Abs(this.VolumeBalanceMisclosure);
            if (absBottomUp > this.MaxAbsBottomUpMisclosure)
            {
                this.MaxAbsBottomUpMisclosure = absBottomUp;
                this.MaxAbsBottomUpMisclosureDate = this.SimulationDateTime;
            }

            // -- Track worst-case top-down misclosure --
            double absTopDown = Math.Abs(this.TopDownVolumeBalanceMisclosure);
            if (absTopDown > this.MaxAbsTopDownMisclosure)
            {
                this.MaxAbsTopDownMisclosure = absTopDown;
                this.MaxAbsTopDownMisclosureDate = this.SimulationDateTime;
            }

            // -- Warning if either check exceeds tolerance --
            if (absBottomUp > MassBalanceTolerance || absTopDown > MassBalanceTolerance)
            {
                this.MassBalanceWarningCount++;

                if (this.MassBalanceWarningCount <= 10)
                {
                    Console.WriteLine(
                        $"WARNING: Mass balance misclosure at {this.SimulationDateTime:yyyy-MM-dd} — "
                        + $"bottom-up={this.VolumeBalanceMisclosure:E3} ML, "
                        + $"top-down={this.TopDownVolumeBalanceMisclosure:E3} ML "
                        + $"(tolerance={MassBalanceTolerance:E3} ML).");
                }
                else if (this.MassBalanceWarningCount == 11)
                {
                    Console.WriteLine("WARNING: Further per-step mass balance warnings suppressed. See end-of-run summary.");
                }
            }
        }

        /// <summary>
        /// Resets all cumulative mass balance trackers to zero. Call before each MC iteration or scenario run
        /// to ensure accumulators reflect only the current run.
        /// </summary>
        public void ResetMassBalanceAccumulators()
        {
            this.CumulativeBottomUpMisclosure = 0.0;
            this.CumulativeTopDownMisclosure = 0.0;
            this.CumulativeDamRemovalStorageLoss = 0.0;
            this.MaxAbsBottomUpMisclosure = 0.0;
            this.MaxAbsBottomUpMisclosureDate = DateTime.MinValue;
            this.MaxAbsTopDownMisclosure = 0.0;
            this.MaxAbsTopDownMisclosureDate = DateTime.MinValue;
            this.MassBalanceWarningCount = 0;
        }

        /// <summary>
        /// Writes a mass balance summary to the console. Prints the full diagnostic report only if one or more time steps exceeded the tolerance. Otherwise prints a single
        /// confirmation line to keep MC output concise.
        /// </summary>
        public void WriteMassBalanceSummary()
        {
            if (this.MassBalanceWarningCount > 0)
            {
                Console.WriteLine("Run completed.");
                Console.WriteLine("-- Mass Balance Summary ------------------------------------------------------");
                Console.WriteLine($"  Water body nodes:    {this.WaterBodyNodes.Length} total");
                Console.WriteLine($"  Bottom-up (sum of node misclosures):");
                Console.WriteLine($"    Cumulative:      {this.CumulativeBottomUpMisclosure,14:E4} ML");
                Console.WriteLine($"    Max absolute:    {this.MaxAbsBottomUpMisclosure,14:E4} ML  at {this.MaxAbsBottomUpMisclosureDate:yyyy-MM-dd}");
                Console.WriteLine($"  Top-down (node-existence-aware system boundary check):");
                Console.WriteLine($"    Cumulative:      {this.CumulativeTopDownMisclosure,14:E4} ML");
                Console.WriteLine($"    Max absolute:    {this.MaxAbsTopDownMisclosure,14:E4} ML  at {this.MaxAbsTopDownMisclosureDate:yyyy-MM-dd}");
                Console.WriteLine($"  Dam removal losses:");
                Console.WriteLine($"    Cumulative:      {this.CumulativeDamRemovalStorageLoss,14:F4} ML");
                Console.WriteLine($"  Warning count:     {this.MassBalanceWarningCount} time steps exceeded tolerance ({MassBalanceTolerance:E3} ML)");
                Console.WriteLine("  ? Non-zero misclosure detected. Investigate dam removal events or routing logic.");
                Console.WriteLine("------------------------------------------------------------------------------");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("Run completed: ? Mass balance OK.");
            }
        }

        /// <summary>
        /// Fits the exponent of a power-law relationship between surface area in m² and storage volume in ML.
        /// General solution, which works for both simple power law relationships and more complicated relationships, such as line segments applying over different volume ranges.
        /// </summary>
        /// <param name="rodisSettings">Settings of RODIS model, containing the surface area evaluatedVolume volume relationship.</param>
        /// <param name="volumeAtFull">Storage volume at full for this water body.</param>
        /// <returns>Fitted exponent of surface area to volume relationship.</returns>
        private double FitSurfaceAreaVolumeExponent (RODISSettings rodisSettings, double volumeAtFull)
        {
            double surfaceAreaVolumeExponent = 1.3;

            string volumeSAEquation = rodisSettings.VolumeSurfaceAreaEquation.Equation;

            if (!string.IsNullOrEmpty(volumeSAEquation))
            {
                // Fast way: if the equation is simply of the form a*SA^b then just parse out the exponent from after the carat character
                string[] parts = volumeSAEquation.Split(new char[] { '*', '^' }, StringSplitOptions.RemoveEmptyEntries);
                int multiplyPos = volumeSAEquation.IndexOf('*');
                int caratPos = volumeSAEquation.IndexOf('^');

                if (parts.Length == 3 && caratPos > multiplyPos && multiplyPos > 0)
                {
                    bool isParseOK = double.TryParse(volumeSAEquation.Substring(caratPos + 1).Trim(), out surfaceAreaVolumeExponent);
                }
                else
                {
                    // Slow way: try fitting a log-log regression to fit an approximate power law equation to the relationship provided
                    if (volumeAtFull > 0)
                    {
                        double surfaceAreaAtFull = rodisSettings.SolveForSurfaceAreaFromVolume(volumeAtFull);

                        const int numPoints = 10;
                        double[] logSurfAreas = new double[numPoints];
                        double[] logVolumes = new double[numPoints];

                        for (int i = 0; i < numPoints; i++)
                        {
                            double surfArea = ((double)(i + 1) / (double)numPoints) * surfaceAreaAtFull;
                            double volume = rodisSettings.EvaluateSurfaceAreaVolumeEquation(surfArea);
                            logSurfAreas[i] = Math.Log(surfArea);
                            logVolumes[i] = Math.Log(volume);
                        }

                        SimpleLinearRegression logLogRegression = SimpleLinearRegression.Fit(logSurfAreas, logVolumes);
                        surfaceAreaVolumeExponent = logLogRegression.Slope;
                    }
                }
            }

            return surfaceAreaVolumeExponent;
        }

        /// <summary>
        /// Gets the latest date and time for the start of any time series demand models.
        /// </summary>
        /// <returns>Latest date and time for the start of any time series demand models.</returns>
        public DateTime GetStartTimeSeriesDemandPatterns()
        {
            DateTime result = DateTime.MinValue;

            if (this.TimeSeriesDemandModels != null)
            {
                foreach (FarmDamTimeSeriesDemandModel model in this.TimeSeriesDemandModels)
                {
                    if (model.InputPattern != null)
                    {
                        if (model.InputPattern.Length > 0)
                        {
                            if (model.InputPattern.First().Time > result)
                            {
                                result = model.InputPattern.First().Time;
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the earliest date and time for the end of any time series demand models.
        /// </summary>
        /// <returns>Earliest date and time for the end of any time series demand models.</returns>
        public DateTime GetEndTimeSeriesDemandPatterns()
        {
            DateTime result = DateTime.MaxValue;

            if (this.TimeSeriesDemandModels != null)
            {
                foreach (FarmDamTimeSeriesDemandModel model in this.TimeSeriesDemandModels)
                {
                    if (model.InputPattern != null)
                    {
                        if (model.InputPattern.Length > 0)
                        {
                            if (model.InputPattern.Last().Time < result)
                            {
                                result = model.InputPattern.Last().Time;
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Clamps bypass and pumped inflow existence dates on all water body nodes
        /// so they fall within each node's current StartDate–EndDate window.
        /// Call after any operation that modifies water body existence dates.
        /// </summary>
        public void ClampBypassAndPumpingDatesToWaterBodyExistence()
        {
            if (this.WaterBodyNodes == null)
                return;

            for (int i = 0; i < this.WaterBodyNodes.Length; ++i)
            {
                var node = this.WaterBodyNodes[i];

                // Bypass existence dates
                if (node.StartBypassDate < node.StartDate)
                    node.StartBypassDate = node.StartDate;
                if (node.EndBypassDate > node.EndDate)
                    node.EndBypassDate = node.EndDate;

                // If clamping has inverted the range, disable bypass for this node
                if (node.StartBypassDate >= node.EndBypassDate)
                {
                    node.StartBypassDate = DateTime.MaxValue;
                    node.EndBypassDate = DateTime.MaxValue;
                }

                // Pumped inflow existence dates
                if (node.StartPumpedInflowDate < node.StartDate)
                    node.StartPumpedInflowDate = node.StartDate;
                if (node.EndPumpedInflowDate > node.EndDate)
                    node.EndPumpedInflowDate = node.EndDate;

                // If clamping has inverted the range, disable pumping for this node
                if (node.StartPumpedInflowDate >= node.EndPumpedInflowDate)
                {
                    node.StartPumpedInflowDate = DateTime.MaxValue;
                    node.EndPumpedInflowDate = DateTime.MaxValue;
                }
            }
        }

        /// <summary>Writes all water body and confluence node metadata to a CSV file.</summary>
        /// <param name="outputFilePath">Output CSV file path.</param>
        public void WriteAllNodesToCSV (string outputFilePath)
        {
            List<BaseModelNode> allNodes = new List<BaseModelNode>();

            foreach (WaterBodyModelNode node in this.WaterBodyNodes)
            {
                node.MeanAnnualInflow = node.CalculateMeanAnnualInflow();
                allNodes.Add((BaseModelNode) node);
            }

            foreach (ConfluenceModelNode node in this.ConfluenceNodes)
            {
                node.MeanAnnualInflow = node.CalculateMeanAnnualInflow();
                allNodes.Add((BaseModelNode)node);
            }

            if (!Directory.Exists(Path.GetDirectoryName(outputFilePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));
            }

            WriteMetadataCsv(outputFilePath, allNodes);
        }

        /// <summary>CsvHelper class map defining column order and headers for BaseModelNode CSV output.</summary>
        public sealed class BaseModelNodeMap : ClassMap<BaseModelNode>
        {
            public BaseModelNodeMap()
            {
                Map(m => m.Label).Index(0).Name("Label");
                Map(m => m.Comment).Index(1).Name("Comment");
                Map(m => m.ReportingGroup).Index(2).Name("Reporting Group");
                Map(m => m.DemandGroup).Index(3).Name("Demand Group");
                Map(m => m.MeanAnnualInflow).Index(4).Name("Mean Annual Inflow (ML/year)");

                Map(m => m.StartDate).Index(5).Name("Start Existence Date");
                Map(m => m.EndDate).Index(6).Name("End Existence Date");

                Map(m => m.MaxStorageCapacityVolumeAtSpill).Index(7).Name("Storage Capacity when Full (ML)");
                Map(m => m.SurfaceAreaAtSpill).Index(8).Name("Surface Area when Full (m²)");

                Map(m => m.NonWaterCatchmentAreaUpstreamOfDamsKM2).Index(9).Name("Non-Water Catchment Area Upstream of Dams (km²)");
                Map(m => m.NonWaterCatchmentAreaDownstreamOfDamsKM2).Index(10).Name("Non-Water Catchment Area Downstream of Dams (km²)");
                Map(m => m.TotalUpstreamCatchmentAreaKM2).Index(11).Name("Total Upstream Catchment Area (km²)");
                Map(m => m.TotalUpstreamNonWaterCatchmentAreaKM2).Index(12).Name("Total Upstream Non-Water Catchment Area (km²)");

                Map(m => m.BypassFlowCapacity).Index(13).Name("Bypass Capacity (ML/d)");
                Map(m => m.StartBypassDate).Index(14).Name("Start Bypass Date");
                Map(m => m.EndBypassDate).Index(15).Name("End Bypass Date");
                Map(m => m.BypassSeasonStartDateIgnoreYear).Index(16).Name("Start of Bypass Season");
                Map(m => m.BypassSeasonEndDateIgnoreYear).Index(17).Name("End of Bypass Season");

                Map(m => m.PumpedInflowCapacity).Index(18).Name("Pumped Inflow Capacity (ML/d)");
                Map(m => m.StartPumpedInflowDate).Index(19).Name("Start Pumped Inflow Date");
                Map(m => m.EndPumpedInflowDate).Index(20).Name("End Pumped Inflow Date");
                Map(m => m.PumpedInflowSeasonStartDateIgnoreYear).Index(21).Name("Start of Pumped Inflow Season");
                Map(m => m.PumpedInflowSeasonEndDateIgnoreYear).Index(22).Name("End of Pumped Inflow Season");

                Map(m => m.Easting).Index(23).Name("Easting (m)");
                Map(m => m.Northing).Index(24).Name("Northing (m)");
                Map(m => m.Elevation).Index(25).Name("Elevation (m)");
                Map(m => m.ModelElementType).Index(26).Name("Node Model Type");
                Map(m => m.NextDownstreamLabel).Index(27).Name("Next Node Downstream Label");
            }
        }


        /// <summary>Writes node metadata records to a CSV file using CsvHelper.</summary>
        /// <param name="path">Output CSV file path.</param>
        /// <param name="items">Collection of model nodes to write.</param>
        public static void WriteMetadataCsv(string path, IEnumerable<BaseModelNode> items)
        {
            var config = new CsvConfiguration(CultureInfo.CurrentCulture)
            {
                HasHeaderRecord = true,
                Quote = '"',
                Escape = '"'
            };

            using var writer = new StreamWriter(path);
            using var csv = new CsvHelper.CsvWriter(writer, config);
            csv.Context.RegisterClassMap<BaseModelNodeMap>();
            csv.WriteHeader<BaseModelNode>();
            csv.NextRecord();
            csv.WriteRecords(items);
        }


        /// <summary>Reads node metadata records from a CSV file using CsvHelper.</summary>
        /// <param name="path">Input CSV file path.</param>
        /// <returns>List of deserialised model nodes.</returns>
        public static List<BaseModelNode> ReadMetadataCsv(string path)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Quote = '"',
                Escape = '"'
            };

            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, config);
            csv.Context.RegisterClassMap<BaseModelNodeMap>();
            var records = new List<BaseModelNode>();
            records.AddRange(csv.GetRecords<BaseModelNode>());
            return records;
        }
    }
}
