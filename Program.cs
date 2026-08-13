// <copyright file="Program.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS
{
    using RODIS.CommandLineOptions;

    /// <summary>
    /// Entry point.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Args.</param>
        public static void Main(string[] args)
        {
            // Add command line options
            Dictionary<string, BaseCommandLineOption> options = OptionsToDictionary(new List<BaseCommandLineOption>()
            {
                new RunRODIS(),
                new RunSTEDILegacyVersion(),
                new GenerateRandomValues(),
            });

            // Find command line flag of matching option
            const string jsonSampleFlag = "-JSONSample";
            string error = null;
            string searchFlag = null;
            if (args.Length == 3 && args[0] == jsonSampleFlag)
            {
                searchFlag = args[1];
            }
            else if (args.Length == 2)
            {
                searchFlag = args[0];
            }
            else
            {
                error = $"Unexpected number of command line arguments: {args.Length}";
            }

            // Find matching option
            BaseCommandLineOption selectedOption = null;
            if (error == null)
            {
                if (options.ContainsKey(searchFlag))
                {
                    selectedOption = options[searchFlag];
                }
                else
                {
                    error = $"Command line flag not found: {searchFlag}";
                }
            }

            // If any checks failed then show usage
            if (error != null)
            {
                // Command line options
                Console.WriteLine("COMMAND LINE OPTION USAGE: [FLAG] [JSON SETTINGS PATH or OUTPUT PATH]");
                Console.WriteLine();
                foreach (BaseCommandLineOption option in options.Values)
                {
                    option.DisplayHelp();
                    Console.WriteLine();
                }

                // Sample JSON
                Console.WriteLine($"SAMPLE JSON USAGE: {jsonSampleFlag} [FLAG] [OUTJSONPATH]");
                Console.WriteLine("Use to generate an example JSON settings file for the command line option specified by [FLAG]");

                // Finally show error
                Console.WriteLine();
                Console.WriteLine($"ERROR: {error}");
                return;
            }

            // Sample JSON
            if (args.Length == 3)
            {
                selectedOption.ExportTemplateJSONSettings(args[2]);
                return;
            }

            // Command line option
            selectedOption.Run(args[1]);
        }

        private static Dictionary<string, BaseCommandLineOption> OptionsToDictionary(List<BaseCommandLineOption> options)
        {
            Dictionary<string, BaseCommandLineOption> optionDictionary = new Dictionary<string, BaseCommandLineOption>();
            foreach (BaseCommandLineOption option in options)
            {
                optionDictionary.Add($"-{option.CommandLineFlag}", option);
            }

            return optionDictionary;
        }
    }
}

