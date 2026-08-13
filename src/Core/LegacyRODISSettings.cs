namespace RODIS.ModelSettings
{
    /// <summary>Settings container for legacy RODIS text-format scenario files, wrapping a RODISSettings instance.</summary>
    public class LegacyRODISSettings
    {
        /// <summary>Path to the legacy text scenario file.</summary>
        public string ScenarioFilePath { get; set; } = string.Empty;

        /// <summary>Inner RODIS settings populated from the legacy file.</summary>
        public RODISSettings Settings { get; set; }

        /// <summary>Legacy full output file path (if specified in the scenario file).</summary>
        public string FullOutputFilePath { get; set; } = string.Empty;

        /// <summary>Legacy flow time series output file path (.getdat format).</summary>
        public string FlowOutputFilePath { get; set; } = string.Empty;
    }
}
