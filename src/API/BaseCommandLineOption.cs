// <copyright file="BaseCommandLineOption.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.CommandLineOptions
{
    using RODIS.JSON;

    /// <summary>Abstract base class for all RODIS command-line run options.</summary>
    public abstract class BaseCommandLineOption
    {
        /// <summary>Gets the command-line flag used to invoke this option.</summary>
        public abstract string CommandLineFlag { get; }

        /// <summary>Gets the description of this option.</summary>
        public abstract string OptionDescription { get; }

        /// <summary>Gets the settings type for JSON template export, or null if no settings file is needed.</summary>
        public abstract Type SettingsType { get; }

        /// <summary>Name of calling program assembly.</summary>
        protected string ProgramName = typeof(Program).Assembly.GetName().Name;

        /// <summary>Version number of calling program assembly.</summary>
        protected string ProgramVersion = typeof(Program).Assembly.GetName().Version.ToString();

        /// <summary>Runs the command-line option with the specified argument path.</summary>
        /// <param name="argumentPath">Output path (if no SettingsType) or JSON settings file path.</param>
        public abstract void Run(string argumentPath);

        /// <summary>Writes program name and version to the console.</summary>
        public void DisplayProgramDetailsOnConsole ()
        {
            Console.WriteLine("Running " + this.ProgramName);
            Console.WriteLine("Version " + this.ProgramVersion);
            // Console.WriteLine(typeof(Program).Assembly.Copyright.ToString());
        }

        /// <summary>Writes help text for this option to the console, including flag, description, and argument type.</summary>
        public void DisplayHelp()
        {
            // Description
            Console.WriteLine($"FLAG: -{this.CommandLineFlag}");
            Console.WriteLine($"DESCRIPTION: {this.OptionDescription}");

            // Inform user if argument is output path or JSON settings file
            string argumentDescription = this.SettingsType == null ? "an output path" : "a JSON settings file path";
            Console.WriteLine($"Argument is {argumentDescription}");
        }

        /// <summary>Serialises a default-valued instance of this option's settings type to a JSON template file.</summary>
        /// <param name="outPath">File path for the template JSON output.</param>
        public void ExportTemplateJSONSettings(string outPath)
        {
            // Handle no settings case
            if (this.SettingsType == null)
            {
                Console.WriteLine($"No settings file needed for {this.CommandLineFlag}");
                return;
            }

            JSONSerialisation.SerialiseFileThrowOnError(Activator.CreateInstance(this.SettingsType), outPath);
        }
    }
}
