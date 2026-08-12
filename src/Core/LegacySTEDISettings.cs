namespace STEDI.ModelSettings
{
    /// <summary>Settings container for legacy STEDI text-format scenario files, wrapping a STEDISettings instance.</summary>
    public class LegacySTEDISettings
    {
        /// <summary>Path to the legacy text scenario file.</summary>
        public string ScenarioFilePath { get; set; } = string.Empty;

        /// <summary>Inner STEDI settings populated from the legacy file.</summary>
        public STEDISettings Settings { get; set; }

        /// <summary>Legacy full output file path (if specified in the scenario file).</summary>
        public string FullOutputFilePath { get; set; } = string.Empty;

        /// <summary>Legacy flow time series output file path (.getdat format).</summary>
        public string FlowOutputFilePath { get; set; } = string.Empty;
    }
}
