// <copyright file="SpatialRunoffCalculator.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.Statistics
{
    using System;

    /// <summary>
    /// Calculates per-node inflow multipliers from spatial gradients (location and elevation)
    /// and per-node random runoff factors. All methods are static and pure — no side effects.
    /// </summary>
    public static class SpatialRunoffCalculator
    {
        /// <summary>Minimum value for a normalisation denominator to avoid division by zero.</summary>
        private const double NormalisationEpsilon = 1.0E-10;

        /// <summary>
        /// Calculates inflow multipliers for an array of nodes given their coordinates,
        /// spatial gradient parameters, and per-node random factors.
        /// </summary>
        /// <param name="eastings">Easting coordinate of each node (m).</param>
        /// <param name="northings">Northing coordinate of each node (m).</param>
        /// <param name="elevations">Elevation of each node (m).</param>
        /// <param name="slopeLocation">Fractional change in runoff per unit of normalised projected distance. 0 = no gradient.</param>
        /// <param name="slopeElevation">Fractional change in runoff per unit of normalised elevation range. 0 = no gradient.</param>
        /// <param name="orientationDegrees">Bearing of the horizontal gradient axis, degrees clockwise from north.</param>
        /// <param name="individualRunoffFactors">Per-node random multipliers. Null = all 1.0.</param>
        /// <returns>Array of non-negative inflow multipliers, one per node.</returns>
        public static double[] CalculateMultipliers(
            double[] eastings,
            double[] northings,
            double[] elevations,
            double slopeLocation,
            double slopeElevation,
            double orientationDegrees,
            double[] individualRunoffFactors)
        {
            int n = eastings.Length;
            double[] multipliers = new double[n];

            // ── Calculate centroid ──
            double meanEasting = 0.0;
            double meanNorthing = 0.0;
            double meanElevation = 0.0;

            for (int i = 0; i < n; i++)
            {
                meanEasting += eastings[i];
                meanNorthing += northings[i];
                meanElevation += elevations[i];
            }

            if (n > 0)
            {
                meanEasting /= n;
                meanNorthing /= n;
                meanElevation /= n;
            }

            // ── Project onto orientation axis and find normalisation ranges ──
            // Bearing: 0° = north (positive Y), 90° = east (positive X)
            // Projected distance = dE × sin(θ) + dN × cos(θ)
            double orientationRad = orientationDegrees * Math.PI / 180.0;
            double sinTheta = Math.Sin(orientationRad);
            double cosTheta = Math.Cos(orientationRad);

            double[] projectedDistances = new double[n];
            double[] elevationDeviations = new double[n];
            double maxAbsProjected = 0.0;
            double maxAbsElevation = 0.0;

            for (int i = 0; i < n; i++)
            {
                double dE = eastings[i] - meanEasting;
                double dN = northings[i] - meanNorthing;
                projectedDistances[i] = dE * sinTheta + dN * cosTheta;
                elevationDeviations[i] = elevations[i] - meanElevation;

                double absProj = Math.Abs(projectedDistances[i]);
                double absElev = Math.Abs(elevationDeviations[i]);
                if (absProj > maxAbsProjected) maxAbsProjected = absProj;
                if (absElev > maxAbsElevation) maxAbsElevation = absElev;
            }

            // ── Calculate multipliers ──
            for (int i = 0; i < n; i++)
            {
                // Normalise to [-1, 1] range; if no spatial spread, gradient has no effect
                double normalisedDistance = (maxAbsProjected > NormalisationEpsilon)
                    ? projectedDistances[i] / maxAbsProjected
                    : 0.0;

                double normalisedElevation = (maxAbsElevation > NormalisationEpsilon)
                    ? elevationDeviations[i] / maxAbsElevation
                    : 0.0;

                double spatialFactor = 1.0
                    + (slopeLocation * normalisedDistance)
                    + (slopeElevation * normalisedElevation);

                double individualFactor = (individualRunoffFactors != null && i < individualRunoffFactors.Length)
                    ? individualRunoffFactors[i]
                    : 1.0;

                multipliers[i] = Math.Max(0.0, spatialFactor * individualFactor);
            }

            return multipliers;
        }

        /// <summary>
        /// Overload that takes separate arrays of water body node and confluence node coordinates,
        /// calculates multipliers for both, and returns them as two separate arrays.
        /// Individual runoff factors are only applied to water body nodes.
        /// </summary>
        /// <param name="wbEastings">Water body node eastings.</param>
        /// <param name="wbNorthings">Water body node northings.</param>
        /// <param name="wbElevations">Water body node elevations.</param>
        /// <param name="cnEastings">Confluence node eastings.</param>
        /// <param name="cnNorthings">Confluence node northings.</param>
        /// <param name="cnElevations">Confluence node elevations.</param>
        /// <param name="slopeLocation">Fractional change in runoff per unit of normalised projected distance.</param>
        /// <param name="slopeElevation">Fractional change in runoff per unit of normalised elevation range.</param>
        /// <param name="orientationDegrees">Bearing in degrees clockwise from north.</param>
        /// <param name="individualRunoffFactors">Per-water-body-node random multipliers. Null = all 1.0.</param>
        /// <param name="wbMultipliers">Output: multipliers for water body nodes.</param>
        /// <param name="cnMultipliers">Output: multipliers for confluence nodes.</param>
        public static void CalculateMultipliersByNodeType(
            double[] wbEastings, double[] wbNorthings, double[] wbElevations,
            double[] cnEastings, double[] cnNorthings, double[] cnElevations,
            double slopeLocation, double slopeElevation, double orientationDegrees,
            double[] individualRunoffFactors,
            out double[] wbMultipliers, out double[] cnMultipliers)
        {
            int nWB = wbEastings.Length;
            int nCN = cnEastings.Length;
            int nAll = nWB + nCN;

            // Combine all coordinates for centroid and normalisation range calculation
            double[] allEastings = new double[nAll];
            double[] allNorthings = new double[nAll];
            double[] allElevations = new double[nAll];

            Array.Copy(wbEastings, 0, allEastings, 0, nWB);
            Array.Copy(cnEastings, 0, allEastings, nWB, nCN);
            Array.Copy(wbNorthings, 0, allNorthings, 0, nWB);
            Array.Copy(cnNorthings, 0, allNorthings, nWB, nCN);
            Array.Copy(wbElevations, 0, allElevations, 0, nWB);
            Array.Copy(cnElevations, 0, allElevations, nWB, nCN);

            // Build a combined individual factors array:
            // water body nodes get their sampled factors, confluence nodes get 1.0
            double[] allIndividualFactors = new double[nAll];
            for (int i = 0; i < nWB; i++)
            {
                allIndividualFactors[i] = (individualRunoffFactors != null && i < individualRunoffFactors.Length)
                    ? individualRunoffFactors[i]
                    : 1.0;
            }

            for (int i = nWB; i < nAll; i++)
            {
                allIndividualFactors[i] = 1.0;
            }

            // Calculate all multipliers using shared centroid and normalisation
            double[] allMultipliers = CalculateMultipliers(
                allEastings, allNorthings, allElevations,
                slopeLocation, slopeElevation, orientationDegrees,
                allIndividualFactors);

            // Split back into separate arrays
            wbMultipliers = new double[nWB];
            cnMultipliers = new double[nCN];
            Array.Copy(allMultipliers, 0, wbMultipliers, 0, nWB);
            Array.Copy(allMultipliers, nWB, cnMultipliers, 0, nCN);
        }
    }
}