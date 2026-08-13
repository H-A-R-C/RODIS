
namespace RODIS.ModelSettings
{
    using RODIS.JSON;
    using RODIS.ModelRun;
    using RODIS.TimeSeries;

    /// <summary>Static utility helpers for RODISSettings validation, loading, and shared constants.</summary>
    public static class RODISSettingsHelper
    {
        /// <summary>Default output file extension for RODIS runs.</summary>
        public const string DefaultFileExtension = ".res.csv";

        /// <summary>
        /// Validates that all provided file paths exist.
        /// Throws FileNotFoundException on the first missing file or ArgumentException if any path is null/empty.
        /// </summary>
        /// <param name="paths">One or more file paths to validate.</param>
        public static void ValidateFilesExist(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                throw new ArgumentException("No file paths were provided.");

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException($"Path at index {i} is null, empty, or whitespace.");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"The file at path '{path}' does not exist.", path);
            }
        }

        /// <summary>Determines the demand model type from the demand groups defined in settings.</summary>
        /// <param name="rodisSettings">RODIS settings containing demand group definitions.</param>
        /// <returns>The demand model type (RepeatingMonthlyDemand, TimeSeriesDemand, or Missing).</returns>
        public static ModelElementType GetDemandModelType(RODISSettings rodisSettings)
        {
            ModelElementType demandModelType = ModelElementType.Missing;

            if (rodisSettings.RepeatingMonthlyDemandGroups != null &&
                rodisSettings.RepeatingMonthlyDemandGroups.Count > 0)
            {
                demandModelType = ModelElementType.RepeatingMonthlyDemand;
            }

            if (demandModelType == ModelElementType.Missing &&
                rodisSettings.TimeSeriesDemandGroups != null &&
                rodisSettings.TimeSeriesDemandGroups.Count > 0)
            {
                demandModelType = ModelElementType.TimeSeriesDemand;
            }

            return demandModelType;
        }

        /// <summary>
        /// Deserialises and validates a RODIS JSON settings file, sets default output path and optionally generates a random dam network if specific dam details are not provided.
        /// </summary>
        /// <param name="jsonPath">Path to the RODIS JSON settings file.</param>
        /// <param name="damNodes">Output array of generated dam nodes, or null if specific network details are used.</param>
        /// <returns>Fully initialised RODISSettings object.</returns>
        public static RODISSettings LoadAndValidateSettings(string jsonPath, out LegacyRODISDamNode[] damNodes)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                throw new ArgumentException("ERROR: JSON input file path is null or empty.");

            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("ERROR: JSON input file does not exist.", jsonPath);

            RODISSettings settings = JSONSerialisation.DeserialiseFileThrowOnError<RODISSettings>(jsonPath);

            settings.InitialiseFromJSON();

            // Default output path
            if (string.IsNullOrEmpty(settings.ResCSVOutputPath))
            {
                string defaultOutputFileName =
                    Path.Combine(settings.OutFolder, Path.GetFileNameWithoutExtension(jsonPath));

                if (!Directory.Exists(settings.OutFolder))
                    Directory.CreateDirectory(settings.OutFolder);

                settings.ResCSVOutputPath = defaultOutputFileName;
            }

            // Demand model type
            ModelElementType demandModelType = GetDemandModelType(settings);

            // Dam network
            damNodes = null;

            if (!settings.UseSpecificDamNetworkDetails)
            {
                double catchmentAreaKM2 = settings.GetCatchmentAreakm2();

                if (catchmentAreaKM2 <= 0)
                    throw new ArgumentException("ERROR: Catchment area must be specified.");

                if (!(demandModelType == ModelElementType.RepeatingMonthlyDemand ||
                      demandModelType == ModelElementType.TimeSeriesDemand))
                    throw new ArgumentException("ERROR: Invalid demand model type.");

                damNodes = RODISNetworkSetup.RandomlyGenerateRODISNetwork(settings, demandModelType);
            }

            return settings;
        }

        /// <summary>Parses a legacy timestep string ("daily", "weekly", "monthly") into a StandardModellingTimeSpan. Returns null if the string is not recognised.</summary>
        /// <param name="timeStepString">Timestep string from legacy scenario file (case-insensitive).</param>
        /// <returns>Corresponding StandardModellingTimeSpan, or null if unrecognised.</returns>
        public static StandardModellingTimeSpan ParseModellingTimeSpan(string timeStepString)
        {
            if (string.IsNullOrWhiteSpace(timeStepString))
                return null;

            switch (timeStepString.Trim().ToLowerInvariant())
            {
                case "daily":
                    return new StandardModellingTimeSpan(BaseModellingTimeSpan.Daily, 1.0);
                case "weekly":
                    return new StandardModellingTimeSpan(BaseModellingTimeSpan.Weekly, 1.0);
                case "monthly":
                    return new StandardModellingTimeSpan(BaseModellingTimeSpan.Monthly, 1.0);
                default:
                    return null;
            }
        }
    }
}