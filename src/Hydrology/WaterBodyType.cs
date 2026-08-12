// <copyright file="WaterBodyType.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.ModelRun
{
    using STEDI.JSON;

    public class WaterBodyType
    {
        /// <summary>Whether to include this water body type group in the model.</summary>
        public bool Include { get; set; } = true;

        /// <summary>Surface area scaling factor applied to water bodies of this type.</summary>
        public double SAFactor { get; set; } = 0.0;

        /// <summary>Demand group label assigned to water bodies of this type.</summary>
        public string DemandGroup { get; set; }

        /// <summary>Results/reporting group label assigned to water bodies of this type.</summary>
        public string ResultGroup { get; set; }

        /// <summary>Demand factor override for water bodies of this type.</summary>
        public double DemandFactor { get; set; } = 0.0;

        /// <summary>Minimum volume (ML) for a water body to be included in this type group.</summary>
        public double MinVolume { get; set; } = 0.0;

        /// <summary>Maximum volume (ML) for a water body to be included in this type group. 10,000,000 = effectively unlimited.</summary>
        public double MaxVolume { get; set; } = 10000000;

        /// <summary>Filter for on-waterway status: "Yes", "No", or "Both" (default).</summary>
        public string OnWaterway { get; set; } = "Both";

        public static Dictionary<string, WaterBodyType> DeserialiseDamTypeGroupProperties(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath))
                return null;

            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"Dam type group properties file not found: {jsonPath}");

            return JSONSerialisation.DeserialiseFileThrowOnError<Dictionary<string, WaterBodyType>>(jsonPath);
        }
    }
}
