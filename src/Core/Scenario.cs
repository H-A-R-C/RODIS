// <copyright file="Scenario.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelSettings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using RODIS.JSON;
    using UnitsNet;

    /// <summary>Defines a single RODIS scenario with overrides for bypass, pumping, volume revision, and calculation method settings.</summary>
    public class Scenario
    {
        private static readonly DateTime MinDate = new DateTime(1800, 1, 1);
        private static readonly DateTime MaxDate = new DateTime(9999, 12, 31);

        /// <summary>
        /// Gets or sets the date in each year when dams in the catchment are revised. Constant dams assumed between each reset date.
        /// </summary>
        public DateOnly DamsRevisionDateIgnoreYear { get; set; } = new DateOnly (1900, 1, 1);

        /// <summary>
        /// Gets or sets a dictionary of years to revise storage volume and the total storage volume in each year.
        /// </summary>
        public Dictionary<uint, string> RevisionYears_TotalStorageVolume { get; set; } = new Dictionary<uint, string> ();

        /// <summary>
        /// Gets or sets the initial volume in each storage at the start of the run, as a proportion of the full storage at the start of the run.
        /// Uses the applicable maximum storage volume for the water body at the time step for the start of the run.
        /// </summary>
        public double AllStoragesProportionFullAtStartOfRun;

        /// <summary>
        /// Gets or sets a boolean variable that is true if unimpacted flow output is to be calculated for providing observed (gauged) flow input or false if observed flow output is to be calculated from unimpacted flow input.
        /// </summary>
        public bool CalculateUnimpactedGivenObserved;

        /// <summary>
        /// Gets or sets a boolean variable that is true if calculation methods are the same as legacy RODIS version 1, false if new calculation methods to be adopted.
        /// </summary>
        public bool UseLegacySTEDICalculationMethods;

        /// <summary>
        /// Gets or sets a boolean variable that is true if low flow bypasses are to be applied to all dams bigger than a single, specific threshold of volume.
        /// </summary>
        public bool UseFixedLowFlowBypassCapacity;

        /// <summary>
        /// Gets or sets the minimum dam volume (with units, e.g. "5 ML") for which low flow bypasses are applied.
        /// Parsed via UnitsNet. All smaller dams get zero bypass capacity.
        /// </summary>
        /// <remarks>Bare numbers in JSON are treated as ML for backward compatibility (e.g. 5 = 5 ML).</remarks>
        [JsonConverter(typeof(UnitStringJsonConverter), "ML")]
        public string VolumeThresholdForBypass { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the bypass capacity flow rate in ML/d per km² of total upstream catchment area.
        /// </summary>
        /// <remarks>Always in ML/d/km² — no unit conversion (UnitsNet has no compound unit for this).</remarks>
        public double BypassCapacityML_d_km2;

        /// <summary>
        /// Gets or sets the start date (year doesn't matter) of the season for which low flow bypasses are active.
        /// </summary>
        public DateOnly BypassSeasonStartDateIgnoreYear;

        /// <summary>
        /// Gets or sets the last date (year doesn't matter) of the season for which low flow bypasses are active.
        /// </summary>
        public DateOnly BypassSeasonEndDateIgnoreYear;

        /// <summary>
        /// Gets or sets the start date (year doesn't matter) of the season for which pumping is permitted.
        /// </summary>
        public DateOnly PumpingSeasonStartDateIgnoreYear;

        /// <summary>
        /// Gets or sets the last date (year doesn't matter) of the season for which pumping is permitted.
        /// </summary>
        public DateOnly PumpingSeasonEndDateIgnoreYear;

        /// <summary>
        /// An array of years for revision of total storage volume.
        /// </summary>
        private int[] revisionYears = null;

        /// <summary>
        /// An array of total storage volume in MegaLitres ML at the specified revision years.
        /// </summary>
        private double[] totalStorageVolumeML = null;

        /// <summary>
        /// Gets the years when volumes are revised.
        /// </summary>
        /// <returns>Array of years when volumes are revised in the scenario.</returns>
        public double[] GetRevisionYears() 
        {
            if (revisionYears == null)
            {
                return null;
            }
            else
            {
                double[] result = new double[revisionYears.Length];

                int iYear = 0;
                for (iYear = 0; iYear < revisionYears.Length; iYear++)
                {
                    result[iYear] = revisionYears[iYear];
                }

                return result;
            }
        }

        /// <summary>
        /// Gets the revised total storage volume across the catchment in ML at each revision year.
        /// </summary>
        /// <returns>Revised total storage volume across the catchment in ML at each revision year.</returns>
        public double[] GetTotalStorageVolumeML() 
        { 
            return totalStorageVolumeML; 
        }

        /// <summary>
        /// Parses the years and volumes for revision of total storage into valid years and storage volumes in ML.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if year supplied is outside of the valid range of MinDate to MaxDate.</exception>
        public void ParseVolumeRevisionDictionary ()
        {
            var list = new List<(DateTime Date, double Ml)>();

            foreach (var kvp in RevisionYears_TotalStorageVolume)
            {
                uint year = kvp.Key;
                string volumeText = kvp.Value;

                var date = new DateTime((int)year, 1, 1);

                if (date < MinDate || date > MaxDate)
                    throw new ArgumentOutOfRangeException(nameof(year), $"Year {year} is outside the valid range {MinDate:yyyy-MM-dd} to {MaxDate:yyyy-MM-dd}.");

                Volume volume = Volume.Parse(volumeText);

                double volumeInML = volume.Megaliters;

                list.Add((date, volumeInML));
            }

            var sorted = list.OrderBy(x => x.Date).ToArray();

            revisionYears = sorted.Select(x => x.Date.Year).ToArray();
            totalStorageVolumeML = sorted.Select(x => x.Ml).ToArray();
        }
    }
}
