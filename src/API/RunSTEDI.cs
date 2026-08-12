// <copyright file="RunSTEDI.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.CommandLineOptions
{
    using STEDI.ModelRun;
    using STEDI.ModelSettings;

    /// <summary>Command-line option that runs a single or multi-scenario STEDI model from a JSON settings file.</summary>
    public class RunSTEDI : BaseCommandLineOption
    {
        /// <inheritdoc/>
        public override string CommandLineFlag => nameof(RunSTEDI);

        /// <inheritdoc/>
        public override string OptionDescription => "Run STEDI model";

        /// <inheritdoc/>
        public override Type SettingsType => typeof(STEDISettings);

        /// <inheritdoc/>
        /// <summary>Loads settings, sets up the engine, loads scenarios, and runs all scenarios to completion.</summary>
        /// <param name="argumentPath">Path to the STEDI JSON settings file.</param>
        public override void Run(string argumentPath)
        {
            this.DisplayProgramDetailsOnConsole();

            if (string.IsNullOrWhiteSpace(argumentPath))
                throw new ArgumentException("ERROR: STEDI JSON scenario input file path is null or empty.");
            if (!File.Exists(argumentPath))
                throw new ArgumentException("ERROR: STEDI JSON input file does not exist or has incorrect file path.\n File specified was " + argumentPath);

            try
            {
                Console.WriteLine("Reading JSON scenario input file " + argumentPath);

                var engine = new STEDIEngine(this.ProgramName, this.ProgramVersion);
                STEDISettings settings = engine.LoadBaseSettings(argumentPath);

                if (!engine.SetUpFirstRun(settings))
                    return;

                engine.LoadScenariosIntoSettings(settings);

                List<List<double[]>> overallMeanAnnualValues = new();
                List<List<double[][]>> groupMeanAnnualValues = new();
                engine.RunScenarios(settings, new DateTime(2000, 1, 1), overallMeanAnnualValues, groupMeanAnnualValues);
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine($"\nERROR: Invalid or inconsistent input data.\n  {ex.Message}");
                Console.WriteLine("Please check that all input files are correctly formatted and consistent.");
            }
            catch (FormatException ex)
            {
                Console.WriteLine($"\nERROR: Could not parse a value in the settings file.\n  {ex.Message}");
                Console.WriteLine("Please check the JSON settings file format.");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"\nERROR: {ex.Message}");
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"\nERROR: A required input file was not found.");
                Console.WriteLine($"  File: {ex.FileName}");
                Console.WriteLine($"  {ex.Message}");
                Console.WriteLine("Please check that all file paths in the JSON settings are correct.");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"\nERROR: File I/O failure.\n  {ex.Message}");
                Console.WriteLine("Please check that output directories exist and files are not locked by another program.");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"\nERROR: Model execution failed.\n  {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"  Cause: {ex.InnerException.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: An unexpected error occurred during the STEDI run.");
                Console.WriteLine($"  Type: {ex.GetType().Name}");
                Console.WriteLine($"  Message: {ex.Message}");
                Console.WriteLine($"  Location: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                Console.WriteLine("Please report this error to the development team.");
            }
        }
    }
}