
namespace STEDI.CommandLineOptions
{
    using STEDI.InputOutput;
    using STEDI.JSON;
    using STEDI.ModelSettings;
    using STEDI.Statistics;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class GenerateRandomValues : BaseCommandLineOption
    {
        /// <summary>
        /// .res.csv is default output file extension.
        /// </summary>
        public const string DefaultFileExtension = ".csv";

        /// <inheritdoc/>
        public override string CommandLineFlag => nameof(GenerateRandomValues);

        /// <inheritdoc/>
        public override string OptionDescription => "Generate random variables from a distribution";

        /// <inheritdoc/>
        public override Type SettingsType => typeof(RandomVariableSettings);

        /// <inheritdoc/>
        public override void Run(string argumentPath)
        {
            this.DisplayProgramDetailsOnConsole();

            if (!string.IsNullOrEmpty(argumentPath))
            {
                if (File.Exists(argumentPath))
                {
                    Console.WriteLine("Reading JSON input file for generating random variables " + argumentPath);

                    RandomVariableSettings settings = JSONSerialisation.DeserialiseFileThrowOnError<RandomVariableSettings>(argumentPath);

                    double[,] generatedValues = GenerateFromXORShift(settings, this.ProgramName, argumentPath);

                    if (generatedValues != null)
                    {
                        string[] variableNames = settings.ProbabilityDistributionSpecifications.Keys.ToArray();

                        // TODO Populate unit labels via JSON input file
                        string[] unitLabels = null;

                        string[] replicateLabels = new string[settings.NumberOfReplicates];
                        for (ulong i = 0; i < settings.NumberOfReplicates; i++)
                        {
                            replicateLabels[(int)i] = i.ToString();
                        }

                        // Write to file, if valid file exists
                        if (settings.OutputFilePath != null)
                        {
                            if (Directory.Exists(Path.GetDirectoryName(settings.OutputFilePath)))
                            {
                                ReadWriteGenCSV.WriteCSV(generatedValues, variableNames, unitLabels, settings.OutputFilePath, 
                                    this.ProgramName + " version " + this.ProgramVersion, DateTime.MinValue, DateTime.MinValue, 
                                    "Replicate", replicateLabels, argumentPath);
                            }
                        }
                    }
                }
            }
        }

        public static double[,] GenerateFromXORShift (RandomVariableSettings settings, string programName = "", string projectName = "")
        {
            double[,] values = null;

            XORShift random = new XORShift(settings.RNGSeed);

            try
            {
                if (settings.ProbabilityDistributionSpecifications == null)
                {
                    throw new InvalidDataException("No specifications provided for probability distributions.");
                }
                else
                {
                    if (settings.ProbabilityDistributionSpecifications.Count == 0)
                    {
                        throw new InvalidDataException("No specifications provided for probability distributions.");
                    }
                    else
                    {
                        values = new double[settings.NumberOfReplicates, settings.ProbabilityDistributionSpecifications.Count];

                        string[] variableNames = settings.ProbabilityDistributionSpecifications.Keys.ToArray();

                        for (int j = 0; j < settings.ProbabilityDistributionSpecifications.Count; ++j)
                        {
                            MonteCarloInput distributionSpec = settings.ProbabilityDistributionSpecifications[variableNames[j]];

                            double allowableRange = distributionSpec.Max - distributionSpec.Min;

                            for (ulong i = 0; i < settings.NumberOfReplicates; i++)
                            {
                                switch (distributionSpec.DistributionType)
                                {
                                    case ProbabilityDistributionType.Uniform:
                                        values[i, j] = distributionSpec.Min + (allowableRange * random.NextDouble());
                                        break;

                                    case ProbabilityDistributionType.Gaussian:
                                        double genValue = NormalDistribution.NextGaussian(random, distributionSpec.TransformedMean, distributionSpec.TransformedSD);
                                        values[i, j] = Math.Max(distributionSpec.Min, Math.Min(genValue, distributionSpec.Max));
                                        break;

                                    case ProbabilityDistributionType.BoxCoxTransfomedGaussian:
                                        double genNormalValue = NormalDistribution.NextGaussian(random, distributionSpec.TransformedMean, distributionSpec.TransformedSD);
                                        double genTransValue = BoxCoxTransformation.InverseBoxCox(genNormalValue, distributionSpec.BoxCoxLambda);
                                        values[i, j] = Math.Max(distributionSpec.Min, Math.Min(genTransValue, distributionSpec.Max));
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            catch (InvalidDataException invalidDataException)
            {
                Console.WriteLine(invalidDataException.ToString());
            }

            return values;
        }
    }

    /// <summary>
    /// JSON settings.
    /// </summary>
    public class RandomVariableSettings
    {
        //public string InputPath { get; set; } = string.Empty;

        /// <summary>
        /// File path for ouptut.
        /// </summary>
        public string OutputFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Number of replicates to generate for each variable.
        /// </summary>
        public ulong NumberOfReplicates { get; set; } = 100L;

        /// <summary>
        /// Seed for random number generator.
        /// </summary>
        public ulong RNGSeed { get; set; } = 2026;

        /// <summary>
        /// Dictionary of specifications for probability distributions to generate random data from.
        /// </summary>
        public Dictionary<string, MonteCarloInput> ProbabilityDistributionSpecifications = new Dictionary<string, MonteCarloInput>();
    }
}
