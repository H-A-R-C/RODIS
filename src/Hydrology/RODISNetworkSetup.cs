// <copyright file="RODISNetworkSetup.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    using RODIS.ModelSettings;
    using RODIS.Static;

    /// <summary>Static helper methods for building and configuring legacy STEDI model networks.</summary>
    public class RODISNetworkSetup
    {
        /// <summary>
        /// Calculates intermediate catchment areas and assigns element type IDs, demand IDs, and downstream connectivity for an array of legacy STEDI model nodes.
        /// </summary>
        /// <param name="legacySTEDIDamNodes">Array of legacy nodes to update in-place.</param>
        /// <param name="demandModelType">Demand model type to assign (RepeatingMonthlyDemand or TimeSeriesDemand).</param>
        public static void UpdateLegacySTEDINodeProperties (LegacySTEDIDamNode[] legacySTEDIDamNodes, ModelElementType demandModelType)
        {
            List<int> legacyNodeIDList = new List<int>();

            int waterBodyNodeCount = 0;
            int confluenceNodeCount = 0;
            int uniformInflowCatchmentsCount = 0;
            int repeatingMonthlyDemandModelsCount = 0;
            int timeSeriesDemandModelsCount = 0;
            int straightThroughRoutingCount = 0;

            for (int i = 0; i < legacySTEDIDamNodes.Length; i++)
            {
                legacySTEDIDamNodes[i].CatchmentAreaFromUpstreamLegacyNodes = 0.0;
                legacyNodeIDList.Add(legacySTEDIDamNodes[i].Identifier);
            }

            for (int i = legacySTEDIDamNodes.Length - 1; i >= 0; i--)
            {
                legacySTEDIDamNodes[i].IntermediateCatchmentAreaKM2 = Math.Max(0, legacySTEDIDamNodes[i].TotalCatchmentAreaKM2 - legacySTEDIDamNodes[i].CatchmentAreaFromUpstreamLegacyNodes);
                int nextDSID = legacySTEDIDamNodes[i].NextDownstreamIdentifier - 1;
                if (nextDSID >= 0)
                {
                    legacySTEDIDamNodes[nextDSID].CatchmentAreaFromUpstreamLegacyNodes += legacySTEDIDamNodes[i].TotalCatchmentAreaKM2;
                }

                if (legacySTEDIDamNodes[i].VolumeML <= 0 || legacySTEDIDamNodes[i].NextDownstreamIdentifier <= 0)
                {
                    legacySTEDIDamNodes[i].nodeModelType = ModelElementType.ConfluenceNode;
                    legacySTEDIDamNodes[i].ConfluenceNodeID = confluenceNodeCount;
                    ++confluenceNodeCount;
                }
                else
                {
                    legacySTEDIDamNodes[i].nodeModelType = ModelElementType.WaterBodyNode;

                    legacySTEDIDamNodes[i].WaterBodyNodeID = waterBodyNodeCount;
                    ++waterBodyNodeCount;

                    switch (demandModelType)
                    {
                        case ModelElementType.RepeatingMonthlyDemand:
                            legacySTEDIDamNodes[i].RepeatingMonthlyDemandID = repeatingMonthlyDemandModelsCount;
                            ++repeatingMonthlyDemandModelsCount;
                            break;

                        case ModelElementType.TimeSeriesDemand:
                            legacySTEDIDamNodes[i].TimeSeriesDemandID = timeSeriesDemandModelsCount;
                            ++timeSeriesDemandModelsCount;
                            break;
                    }
                }

                if (legacySTEDIDamNodes[i].IntermediateCatchmentAreaKM2 > 0)
                {
                    legacySTEDIDamNodes[i].SubcatchmentInflowID = uniformInflowCatchmentsCount;
                    ++uniformInflowCatchmentsCount;
                }

                legacySTEDIDamNodes[i].StraightThroughRoutingLinkID = straightThroughRoutingCount;
                ++straightThroughRoutingCount;
            }

            for (int i = legacySTEDIDamNodes.Length - 1; i > 0; i--)
            {
                int dsLegacyPosition = legacyNodeIDList.IndexOf(legacySTEDIDamNodes[i].NextDownstreamIdentifier);
                if (dsLegacyPosition >= 0)
                {
                    switch (legacySTEDIDamNodes[dsLegacyPosition].nodeModelType)
                    {
                        case ModelElementType.WaterBodyNode:
                            legacySTEDIDamNodes[i].NextDownstreamWaterBodyID = legacySTEDIDamNodes[dsLegacyPosition].WaterBodyNodeID;
                            break;
                        case ModelElementType.ConfluenceNode:
                            legacySTEDIDamNodes[i].NextDownstreamConfluenceID = legacySTEDIDamNodes[dsLegacyPosition].ConfluenceNodeID;
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Randomly generates a network of legacy STEDI model nodes, using the relevant information in the settings.
        /// </summary>
        /// <param name="settings">RODIS settings containing probability distribution and catchment parameters.</param>
        /// <param name="demandModelType">Type of demand model to assign to generated nodes.</param>
        /// <returns>Array of generated legacy STEDI dam nodes with network topology assigned.</returns>
        public static LegacySTEDIDamNode[] RandomlyGenerateLegacySTEDINetwork(RODISSettings settings, ModelElementType demandModelType)
        {
            List<LegacySTEDIDamNode> legacyNetwork = new List<LegacySTEDIDamNode>();

            List<double> randomDamVolumes = new List<double>();

            // Randomly generate dam volumes from probability distribution up to the total volume required
            List<double> maxVolumesList = new List<double>();
            List<double> cumulativeProbabilityList = new List<double>();

            const double minIntervalProbability = 0.000001;

            Dictionary<double,double> maxVolumesMLAndIntervalProbabilities = settings.GetMaxVolumesAndIntervalProbabilities();
            foreach (KeyValuePair<double, double> kvp in maxVolumesMLAndIntervalProbabilities)
            {
                if (cumulativeProbabilityList.Count == 0)
                {
                    maxVolumesList.Add(kvp.Key);
                    cumulativeProbabilityList.Add(0.0);
                }
                else
                {
                    // Check probabilities before adding
                    if (cumulativeProbabilityList.Last() < 1.0)
                    {
                        maxVolumesList.Add(kvp.Key);

                        double checkedCumulativeProb = cumulativeProbabilityList.Last() + Math.Max(kvp.Value, minIntervalProbability);
                        checkedCumulativeProb = Math.Min(checkedCumulativeProb, 1.0);

                        cumulativeProbabilityList.Add(checkedCumulativeProb);
                    }
                }
            }

            double[] maxVolumesArray = maxVolumesList.ToArray();
            double[] cumulativeProbabilities = cumulativeProbabilityList.ToArray();

            double totalVolumeGenerated = 0.0;

            Random rng = new Random(settings.RNGSeed);

            while (totalVolumeGenerated < settings.ProbabilityDistributionTotalDamVolume - 0.000001)
            {
                double randomProb = rng.NextDouble();
                bool isInterpolationError = false;
                double randomVolume = Interpolation.InterpLinLin(randomProb, cumulativeProbabilities, maxVolumesArray, ref isInterpolationError);

                if (!isInterpolationError)
                {
                    randomVolume = Math.Min(randomVolume, settings.ProbabilityDistributionTotalDamVolume - totalVolumeGenerated);
                    randomDamVolumes.Add(randomVolume);
                    totalVolumeGenerated += randomVolume;
                }
            }

            // Calculate surface areas for each generated dam from generated volumes
            List<double> surfaceAreas = new List<double>();
            foreach (double volume in randomDamVolumes)
            {
                surfaceAreas.Add(settings.SolveForSurfaceAreaFromVolume(volume));
            }

            // Assign catchment areas to each generated dam
            double totalOfLocalCatchmentAreas = 0.0;
            List<double> localCatchmentAreas = new List<double>();
            foreach (double volume in randomDamVolumes)
            {
                localCatchmentAreas.Add(settings.EvaluateVolumeCatchmentAreaEquation(volume));
                totalOfLocalCatchmentAreas += localCatchmentAreas.Last();
            }

            // Check and rescale catchment areas, if necessary
            settings.MaximumProportionOfCatchmentImpounded = Math.Max(settings.LowerLimitMaximumProportionOfCatchmentImpounded, settings.MaximumProportionOfCatchmentImpounded);
            settings.MaximumProportionOfCatchmentImpounded = Math.Min(settings.UpperLimitMaximumProportionOfCatchmentImpounded, settings.MaximumProportionOfCatchmentImpounded);

            double maxTotalLocalCatchmentAreas = settings.GetCatchmentAreakm2() * settings.MaximumProportionOfCatchmentImpounded;

            if (totalOfLocalCatchmentAreas > maxTotalLocalCatchmentAreas)
            {
                // Need to rescale catchment areas to fit within the maximum limit of allowable impounded catchment area
                Console.WriteLine("WARNING   : Total catchment area impounded upstream of dams is " + totalOfLocalCatchmentAreas.ToString("#,##0.00") + " km²");
                Console.WriteLine("WARNING   : Impounded area = " + (totalOfLocalCatchmentAreas / settings.GetCatchmentAreakm2()).ToString("0.00%") + " of catchment area, which exceeds allowable limit of " + settings.MaximumProportionOfCatchmentImpounded.ToString("0.00%"));
                Console.WriteLine("RESOLUTION: Rescaling catchment areas upstream of each dam to maximum allowable impounded area in catchment.");

                double rescaleFactor = maxTotalLocalCatchmentAreas / totalOfLocalCatchmentAreas;

                for (int i = 0; i < localCatchmentAreas.Count; i++)
                {
                    localCatchmentAreas[i] *= rescaleFactor;
                }
            }

            // Now set up legacy STEDI format network

            // Start with catchment outlet node
            LegacySTEDIDamNode outletNode = new LegacySTEDIDamNode()
            {
                Identifier = 1,
                SurfaceAreaM2 = 0,
                VolumeML = 0,
                TotalCatchmentAreaKM2 = settings.GetCatchmentAreakm2(),
                DemandGroup = "None",
                ResultsGroup = "Outlet",
                WinterfillRate = 0,
                BypassCapacity = 0,
                NextDownstreamIdentifier = -99,
            };

            legacyNetwork.Add(outletNode);

            for (int i = 0; i < randomDamVolumes.Count && i < localCatchmentAreas.Count && i < surfaceAreas.Count; ++i)
            {
                LegacySTEDIDamNode newNode = new LegacySTEDIDamNode()
                {
                    Identifier = i + 2,
                    SurfaceAreaM2 = surfaceAreas[i],
                    VolumeML = randomDamVolumes[i],
                    TotalCatchmentAreaKM2 = localCatchmentAreas[i],
                    DemandGroup = "Default",
                    ResultsGroup = "Default",
                    WinterfillRate = 0,
                    BypassCapacity = 0,
                    NextDownstreamIdentifier = outletNode.Identifier,
                };

                string demandGroupIndex = settings.GetGroupDemandModelIndexByVolume(newNode.VolumeML, demandModelType);

                if (!string.IsNullOrEmpty(demandGroupIndex) && demandModelType != ModelElementType.Missing)
                {
                    switch (demandModelType)
                    {
                        case ModelElementType.RepeatingMonthlyDemand:
                            newNode.DemandGroup = settings.RepeatingMonthlyDemandGroups[demandGroupIndex].DemandGroup;
                            newNode.ResultsGroup = newNode.DemandGroup;
                            break;
                        case ModelElementType.TimeSeriesDemand:
                            newNode.DemandGroup = settings.TimeSeriesDemandGroups[demandGroupIndex].DemandGroup;
                            newNode.ResultsGroup = newNode.DemandGroup;
                            break;
                    }

                }

                legacyNetwork.Add(newNode);
            }

            RODISNetworkSetup.SetGeneralBypassCapacities(legacyNetwork, settings);

            LegacySTEDIDamNode[] legacySTEDIDamNodes = legacyNetwork.ToArray();

            RODISNetworkSetup.UpdateLegacySTEDINodeProperties(legacySTEDIDamNodes, demandModelType);

            return legacySTEDIDamNodes;
        }

        /// <summary>Assigns bypass capacity and season dates to all nodes in the network that exceed the volume threshold.</summary>
        /// <param name="legacyNetwork">List of legacy nodes to update.</param>
        /// <param name="settings">RODIS settings providing bypass capacity, threshold, and season parameters.</param>
        public static void SetGeneralBypassCapacities(List<LegacySTEDIDamNode> legacyNetwork, RODISSettings settings)
        {
            foreach (var node in legacyNetwork)
            {
                bool isBypassSetOK = false;

                if (settings.UseFixedLowFlowBypassCapacity && settings.BypassCapacityML_d_km2 > 0)
                {
                    if (node.VolumeML > settings.GetVolumeThresholdMLForBypass())
                    {
                        double bypassCapacity = node.TotalCatchmentAreaKM2 * settings.BypassCapacityML_d_km2;
                        bypassCapacity = Math.Max(0, bypassCapacity);
                        if (bypassCapacity > 0)
                        {
                            isBypassSetOK = true;
                            node.IsBypass = true;
                            node.BypassCapacity = bypassCapacity;
                            node.BypassSeasonStartDateIgnoreYear = settings.BypassSeasonStartDateIgnoreYear;
                            node.BypassSeasonEndDateIgnoreYear = settings.BypassSeasonEndDateIgnoreYear;
                        }
                    }
                }

                if (!isBypassSetOK)
                {
                    node.IsBypass = false;
                    node.BypassCapacity = 0;
                }
            }
        }
    }
}
