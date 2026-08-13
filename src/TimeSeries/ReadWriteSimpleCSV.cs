// <copyright file="ReadWriteSimpleCSV.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.InputOutput
{
    using RODIS.Series;
    using RODIS.Static;

    /// <summary>
    /// Input output for simple CSV files with optional multi-line headers.
    /// </summary>
    public static class ReadWriteSimpleCSV
    {
        /// <summary>
        /// Read all .csv files in directory.
        /// </summary>
        /// <param name="directoryPath">Directory.</param>
        /// <param name="numHeaderLines">Number of header lines in each input file.</param>
        /// <returns>List of time series value arrays.</returns>
        public static List<TimeSeriesValue[]> ReadAllInDirectory(string directoryPath, int numHeaderLines = 1)
        {
            try
            {
                string[] allFileNames = Directory.GetFiles(directoryPath, "*.csv");
                List<TimeSeriesValue[]> dataFromFiles = new List<TimeSeriesValue[]>();
                for (int i = 0; i < allFileNames.Length; ++i)
                {
                    dataFromFiles.Add(ReadSimpleCSV(allFileNames[i], columnToRead: 1, numHeaderLines: numHeaderLines));
                }

                // TODO: Order?

                return dataFromFiles;
            }
            catch (FileNotFoundException fnf)
            {
                Console.Error.WriteLine("ERROR: File not found: " + fnf.FileName);
                throw;
            }
            catch (InvalidDataException ide)
            {
                Console.Error.WriteLine("ERROR: Bad data: " + ide.Message);
                throw;
            }
        }

        /// <summary>Reads column headers from the first N lines of a CSV file, concatenating multi-line headers with underscores.</summary>
        /// <param name="path">Path to the CSV file.</param>
        /// <param name="numHeaderLines">Number of header lines to read and merge.</param>
        /// <returns>Array of merged column header strings.</returns>
        public static string[] ReadHeadersFromCSV(string path, int numHeaderLines = 1)
        {
            List<string> headers = new List<string>();

            using (StreamReader reader = new StreamReader(path))
            {
                int lineCount = 0;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();

                    if (line != null && lineCount < numHeaderLines)
                    {
                        string[] parts = line.Split(',');
                        int i = 0;
                        for (; i < parts.Length && i < headers.Count; ++i)
                        {
                            headers[i] += "_" + parts[i];
                        }

                        for (; i < parts.Length; ++i)
                        {
                            headers.Add(parts[i]);
                        }
                    }

                    ++lineCount;
                }
            }

            return headers.ToArray();
        }

        /// <summary>
        /// Read .csv file.
        /// </summary>
        /// <param name="path">Path to .csv file.</param>
        /// <param name="columnToRead">Column containing data to read, 0 indexed, default value is 1 = 2nd column from left.</param>
        /// <param name="numHeaderLines">Number of header lines in input file, default value is 1 header line.</param>
        /// <param name="dateColumnToRead">Column containing datetime data, 0 indexed, default value is 0 = 1st column from left.</param>
        /// <returns>Array of time series values.</returns>
        public static TimeSeriesValue[] ReadSimpleCSV(string path, int columnToRead = 1, int numHeaderLines = 1, int dateColumnToRead = 0)
        {
            List<TimeSeriesValue> data = new List<TimeSeriesValue>();

            // Check whether file exists and contains valid data
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("CSV file not found", path);
            }

            // Default missing data value if not specified
            double missingDataValue = ReadWriteResCSV.MissingData;

            // Read file line by line
            double value = 0;
            DateTime dateTime = DateTime.Now;
            using (StreamReader reader = new StreamReader(path))
            {
                if (reader.EndOfStream)
                {
                    throw new InvalidDataException("CSV file " + path + " is empty");
                }

                int lineCount = 0;
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    ++lineCount;

                    if (lineCount > numHeaderLines)
                    {
                        string[] parts = line.Split(',');
                        bool isParseOK;
                        if (parts.Length >= Math.Max(columnToRead, dateColumnToRead))
                        {
                            // Replace mssing data if specified
                            if (parts[0].ToLower().Contains("missing data value"))
                            {
                                double.TryParse(parts[1], out missingDataValue);
                            }

                            // Construct time series value
                            isParseOK = DateTime.TryParse(parts[dateColumnToRead], out dateTime);
                            if (isParseOK)
                            {
                                TimeSeriesValue dataToAdd = new TimeSeriesValue()
                                {
                                    Time = dateTime,
                                    Value = missingDataValue,
                                    IsValid = false,
                                };

                                if (parts.Length > columnToRead)
                                {
                                    isParseOK = isParseOK && double.TryParse(parts[columnToRead], out value);
                                    dataToAdd.IsValid = isParseOK;
                                    if (isParseOK)
                                    {
                                        dataToAdd.Value = value;
                                        if (Math.Abs(value - missingDataValue) < 0.01)
                                        {
                                            dataToAdd.Value = missingDataValue;
                                            dataToAdd.IsValid = false;
                                        }
                                    }
                                }

                                data.Add(dataToAdd);
                            }
                        }
                    }
                }
            }

            if (data.Count == 0)
            {
                throw new InvalidDataException("CSV file " + path + " contains invalid data");
            }

            return data.ToArray();
        }

        /// <summary>
        /// Write simple .csv file.
        /// </summary>
        /// <param name="series">List of time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="outputLabel">List of output label for each series.</param>
        /// <param name="unitsLabel">Units label.</param>
        public static void WriteSimpleCSV(List<TimeSeriesValue[]> series, string outPath, string programName, List<string> outputLabel, string unitsLabel)
        {
            WriteSimpleCSV(series, outPath, programName, outputLabel, unitsLabel, ReadWriteResCSV.MissingData.ToString());
        }

        /// <summary>
        /// Write simple .csv file.
        /// </summary>
        /// <param name="series">List of time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="outputLabel">List of output label for each series.</param>
        /// <param name="unitsLabel">Units label.</param>
        /// <param name="missingDataOutputString">String to output for missing data</param>
        public static void WriteSimpleCSV(List<TimeSeriesValue[]> series, string outPath, string programName, List<string> outputLabel, string unitsLabel, string missingDataOutputString)
        {
            Dictionary<string, string> templateTimeSeriesDetails = new Dictionary<string, string>()
            {
                ["Units"] = unitsLabel,
                ["RunName"] = string.Empty,
                ["ScenarioName"] = string.Empty,
                ["ScenarioInputSetName"] = string.Empty,
                ["Site"] = string.Empty,
                ["ElementName"] = string.Empty,
                ["WaterFeatureType"] = "Downstream Flow",
                ["ElementType"] = "Node",
                ["Structure"] = "Downstream Flow",
                ["Custom"] = Guid.NewGuid().ToString("N"),
            };
            templateTimeSeriesDetails.Add("Name", ReadWriteResCSV.GetMergedNameString(templateTimeSeriesDetails["Site"], templateTimeSeriesDetails["WaterFeatureType"], templateTimeSeriesDetails["Structure"]));

            List<Dictionary<string, string>> outputTimeSeriesDetailsList = new List<Dictionary<string, string>>();

            foreach (string label in outputLabel)
            {
                Dictionary<string, string> copiedLabel = Copiers.DictionaryCopier(templateTimeSeriesDetails);
                copiedLabel["Site"] = label;
                copiedLabel["Name"] = ReadWriteResCSV.GetMergedNameString(copiedLabel["Site"], copiedLabel["WaterFeatureType"], copiedLabel["Structure"]);
                outputTimeSeriesDetailsList.Add(copiedLabel);
            }

            WriteSimpleCSV(series, outPath, programName, outputTimeSeriesDetailsList, missingDataOutputString);
        }

        /// <summary>
        /// Write simple .csv file.
        /// </summary>
        /// <param name="series">List of time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="missingDataOutputString">String to output for missing data.</param>
        public static void WriteSimpleCSV(List<TimeSeriesWithMetadata> series, string outPath, string programName, string missingDataOutputString = "-9999")
        {
            // TODO: How general are these header labels?

            string outFolder = Path.GetDirectoryName(outPath);
            Directory.CreateDirectory(outFolder);
            using (StreamWriter sw = new StreamWriter(outPath))
            {
                // Headers
                Dictionary<string, string> metaData = series.First().GetMetadata();

                string line;

                int numSeries = series.Count;
                // TODO throw exception if number of series and output labesl do not match

                string headerLine = "Date";
                for (int iSeries = 0; iSeries < numSeries; iSeries++)
                {
                    metaData = series[iSeries].GetMetadata();
                    string columnHeader = (iSeries + 1).ToString() + ">";
                    columnHeader += metaData["Name"].Replace(":", ">").Replace(",", "_");
                    headerLine += "," + columnHeader;
                }

                sw.WriteLine(headerLine);

                // Series
                int seriesLength = series.First().Data.Count;
                for (int i = 0; i < seriesLength; i++)
                {
                    line = series.First().Data[i].Time.ToString("yyyy-MM-dd");
                    if (series.First().Data[i].Time.Hour != 0 || series.First().Data[i].Time.Minute != 0)
                    {
                        line += " " + series.First().Data[i].Time.ToString("HH:mm:ss");
                    }

                    for (int iSeries = 0; iSeries < numSeries; iSeries++)
                    {
                        if (i >= series[iSeries].Data.Count)
                        {
                            line += "," + missingDataOutputString;
                        }
                        else
                        {
                            if (series[iSeries].Data[i].IsValid == false)
                            {
                                line += "," + missingDataOutputString;
                            }
                            else
                            {
                                line += "," + series[iSeries].Data[i].Value;
                            }
                        }
                    }

                    sw.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Write simple .csv file.
        /// </summary>
        /// <param name="series">List of time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="missingDataOutputString">String to output for missing data.</param>
        public static void WriteSimpleCSV(List<TimeSeriesValue[]> series, string outPath, string programName, List<Dictionary<string, string>> outputProperties, string missingDataOutputString = "-9999")
        {
            List<TimeSeriesWithMetadata> seriesWithMetadata = new List<TimeSeriesWithMetadata>();

            for (int i = 0; i < series.Count && i < outputProperties.Count; i++)
            {
                seriesWithMetadata.Add(new TimeSeriesWithMetadata());
                seriesWithMetadata[i].SetMetaData(outputProperties[i]);

                if (series[i] != null)
                {
                    for (int j = 0; j < series[i].Length; j++)
                    {
                        seriesWithMetadata[i].Data.Add(series[i][j].DeepCopy());
                    }
                }
            }

            WriteSimpleCSV(seriesWithMetadata, outPath, programName, missingDataOutputString);
        }

        /// <summary>
        /// Write simple .csv file.
        /// </summary>
        /// <param name="series">Time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="outputLabel">Output label.</param>
        /// <param name="unitsLabel">Units label.</param>
        public static void WriteSimpleCSV(TimeSeriesValue[] series, string outPath, string programName, string outputLabel, string unitsLabel)
        {
            WriteSimpleCSV(new List<TimeSeriesValue[]> { series }, outPath, programName, new List<string> { outputLabel }, unitsLabel);
        }

        /// <summary>
        /// Write simple .csv file.
        /// </summary>
        /// <param name="series">Time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="outputLabel">Output label.</param>
        /// <param name="unitsLabel">Units label.</param>
        /// <param name="missingDataOutputString">String to output for missing data</param>
        public static void WriteSimpleCSV(TimeSeriesValue[] series, string outPath, string programName, string outputLabel, string unitsLabel, string missingDataOutputString)
        {
            WriteSimpleCSV(new List<TimeSeriesValue[]> { series }, outPath, programName, new List<string> { outputLabel }, unitsLabel, missingDataOutputString);
        }
    }
}
