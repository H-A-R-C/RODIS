// <copyright file="ResCSV.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.InputOutput
{
    using RODIS.Series;
    using RODIS.Static;

    /// <summary>
    /// Input output for eWater Source .res.csv format.
    /// </summary>
    public static class ReadWriteResCSV
    {
        /// <summary>
        /// Sentinel numeric value used internally to represent missing data.
        /// </summary>
        public const double MissingData = -9999;

        /// <summary>
        /// Gets default extension for this file type.
        /// </summary>
        public const string DefaultExtension = ".res.csv";

        /// <summary>
        /// Empty dictionary containing standard metadata for res.csv field headers.
        /// </summary>
        public static Dictionary<string, string> ResCSVMetaData { get; set; } = new Dictionary<string, string>()
        {
            {"Field", string.Empty },
            {"Units", string.Empty },
            {"RunName", string.Empty },
            {"ScenarioName", string.Empty },
            {"ScenarioInputSetName", string.Empty },
            {"Name", string.Empty },
            {"Site", string.Empty },
            {"ElementName", string.Empty },
            {"WaterFeatureType", string.Empty },
            {"ElementType", string.Empty },
            {"Structure", string.Empty },
            {"Custom", string.Empty },
        };

        /// <summary>
        /// Returns name of a .res.csv file without directory or extension.
        /// </summary>
        /// <param name="filename">File name and path of file.</param>
        /// <returns>File name without extension.</returns>
        public static string GetFileNameWithoutExtension(string filename)
        {
            string result = string.Empty;

            string fnWithExt = Path.GetFileName(filename);

            if (fnWithExt != null)
            {
                if (fnWithExt.ToLower().EndsWith(DefaultExtension.ToLower()))
                {
                    // This is a valid .res.csv file, so create a result
                    result = fnWithExt.Substring(0, fnWithExt.Length - DefaultExtension.Length);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns extension of a .res.csv file.
        /// </summary>
        /// <param name="filename">File name and path of file.</param>
        /// <returns>File extension.</returns>
        public static string GetExtension(string filename)
        {
            return DefaultExtension;
        }

        /// <summary>
        /// Read all .res.csv files in directory.
        /// </summary>
        /// <param name="directoryPath">Directory.</param>
        /// <returns>List of time series values.</returns>
        public static List<TimeSeriesValue[]> ReadAllInDirectory(string directoryPath)
        {
            try
            {
                string[] allFileNames = Directory.GetFiles(directoryPath, "*" + DefaultExtension);
                List<TimeSeriesValue[]> dataFromFiles = new List<TimeSeriesValue[]>();
                for (int i = 0; i < allFileNames.Length; ++i)
                {
                    dataFromFiles.Add(ReadResCSV(allFileNames[i]));
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

        /// <summary>
        /// Read .res.csv file.
        /// </summary>
        /// <param name="path">Path to .res.csv file.</param>
        /// <returns>Time series values.</returns>
        public static TimeSeriesValue[] ReadResCSV(string path, int columnToRead = 1)
        {
            return ReadWriteSimpleCSV.ReadSimpleCSV(path, columnToRead, -1);
        }

        /// <summary>
        /// Returns the string for units in specified column of file header, if provided in header.
        /// </summary>
        /// <param name="path">Path to .res.csv file.</param>
        /// <param name="columnToRead">Column of res.csv file to read.</param>
        /// <returns>Units of specified column as string, same format as in res.csv file.</returns>
        public static string ReadResCSVFieldUnits(string path, int columnToRead)
        {
            string units = string.Empty;

            Dictionary<string, string> headersForColumn = ReadResCSVFieldHeader(path, columnToRead);
            if (headersForColumn != null)
            {
                if (headersForColumn.ContainsKey("Units"))
                {
                    units = headersForColumn["Units"];
                }
            }

            return units;
        }

        /// <summary>
        /// Gets Dictionary of field headers for specified column in res.csv file.
        /// </summary>
        /// <param name="path">Path to .res.csv file.</param>
        /// <param name="columnToRead">Column of res.csv file to read.</param>
        /// <returns>Dictionary of field headers for specified column in res.csv file.</returns>
        public static Dictionary<string, string> ReadResCSVFieldHeader(string path, int columnToRead)
        {
            List<Dictionary<string, string>> allFieldHeaders = ReadResCSVFieldHeader(path);
            if (columnToRead < allFieldHeaders.Count)
            {
                return allFieldHeaders[columnToRead];
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Gets list of dictionary of all field headers in the file.
        /// </summary>
        /// <param name="path">Path to .res.csv file.</param>
        /// <returns>List of dictionary of all field headers in the file.</returns>
        public static List<Dictionary<string, string>> ReadResCSVFieldHeader(string path)
        {
            List<Dictionary<string, string>> fieldHeaders = new List<Dictionary<string, string>>();

            using (StreamReader sr = new StreamReader(path))
            {
                int fieldsToGet = -1;
                string line = string.Empty;
                string previousLine = string.Empty;
                string[] fieldKeys = null;

                while (!sr.EndOfStream && fieldsToGet <= 0)
                {
                    previousLine = line;
                    line = sr.ReadLine();

                    if (line.Trim() == "EOC")
                    {
                        fieldKeys = previousLine.Split(',');

                        line = sr.ReadLine();
                        int.TryParse(line, out fieldsToGet);
                    }
                }

                for (int i = 0; i < fieldsToGet && !sr.EndOfStream; ++i)
                {
                    line = sr.ReadLine();
                    string[] parts = line.Split(',');

                    Dictionary<string, string> metadata = Copiers.DictionaryCopier(ResCSVMetaData);

                    for (int j = 0; j < parts.Length && j < fieldKeys.Length; ++j)
                    {
                        if (metadata.ContainsKey(fieldKeys[j]))
                        {
                            metadata[fieldKeys[j]] = parts[j];
                        }
                    }

                    fieldHeaders.Add(metadata);
                }
            }

            return fieldHeaders;
        }

        /// <summary>
        /// Write .res.csv.
        /// </summary>
        /// <param name="series">List of time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="outputLabel">List of output label for each series.</param>
        /// <param name="unitsLabel">Units label.</param>
        public static void WriteResCSV(List<TimeSeriesValue[]> series, string outPath, string programName, List<string> outputLabel, List<string> unitsLabel)
        {
            WriteResCSV(series, outPath, programName, outputLabel, unitsLabel, MissingData.ToString());
        }

        /// <summary>
        /// Write .res.csv.
        /// </summary>
        /// <param name="series">List of time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="outputLabel">List of output label for each series.</param>
        /// <param name="unitsLabel">Units label.</param>
        /// <param name="missingDataOutputString">String to output for missing data</param>
        public static void WriteResCSV(List<TimeSeriesValue[]> series, string outPath, string programName, List<string> outputLabel, List<string> unitsLabel, string missingDataOutputString)
        {
            Dictionary<string, string> templateTimeSeriesDetails = Copiers.DictionaryCopier(ResCSVMetaData);
            templateTimeSeriesDetails["Units"] = unitsLabel.FirstOrDefault("ML.day^-1");
            templateTimeSeriesDetails["WaterFeatureType"] = "Downstream Flow";
            templateTimeSeriesDetails["ElementType"] = "Node";
            templateTimeSeriesDetails["Structure"] = "Downstream Flow";
            templateTimeSeriesDetails["Custom"] = Guid.NewGuid().ToString("N");
            templateTimeSeriesDetails.Add("Name", GetMergedNameString(templateTimeSeriesDetails["Site"], templateTimeSeriesDetails["WaterFeatureType"], templateTimeSeriesDetails["Structure"]));

            List<Dictionary<string, string>> outputTimeSeriesDetailsList = new List<Dictionary<string, string>>();

            int i = 0;
            foreach (string label in outputLabel)
            {
                Dictionary<string, string> copiedLabel = Copiers.DictionaryCopier(templateTimeSeriesDetails);
                copiedLabel["Site"] = label;
                if (i < unitsLabel.Count)
                {
                    copiedLabel["Units"] = unitsLabel[i];
                }
                copiedLabel["Name"] = GetMergedNameString(copiedLabel["Site"], copiedLabel["WaterFeatureType"], copiedLabel["Structure"]);
                outputTimeSeriesDetailsList.Add(copiedLabel);
                ++i;
            }

            WriteResCSV(series, outPath, programName, outputTimeSeriesDetailsList, missingDataOutputString);
        }

        /// <summary>
        /// Write overall headers to the res.csv file.
        /// </summary>
        /// <param name="outputFile">Stream writer for file, already opened.</param>
        /// <param name="programName">Name of program writing data.</param>
        /// <param name="start">Start date of data to write to file.</param>
        /// <param name="end">End date of data to write to file.</param>
        /// <param name="projectName">Name of project.</param>
        /// <param name="missingDataOutputString">String for missing data (default = -9999).</param>
        public static void WriteFileHeaders(StreamWriter outputFile, string programName, DateTime start, DateTime end, string projectName = "", string missingDataOutputString = "-9999")
        {
            outputFile.WriteLine("File version,3");
            outputFile.WriteLine($"Missing data value,{missingDataOutputString}");
            outputFile.WriteLine("EOM");
            outputFile.WriteLine($"Project name,{projectName}");
            outputFile.WriteLine($"Program,{programName}");
            outputFile.WriteLine($"Latest result run time,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            if (start > DateTime.MinValue && end > DateTime.MinValue)
            {
                outputFile.WriteLine($"Simulation time,{start:yyyy-MM-dd} - {end:yyyy-MM-dd}");
            }
            else
            {
                outputFile.WriteLine($"Simulation time,Not applicable");
            }
        }

        /// <summary>
        /// Write headers for fields to the res.csv file.
        /// </summary>
        /// <param name="outputFile">Stream writer for file, already opened.</param>
        /// <param name="metaData">Dictionary of metadata to write to file.</param>
        /// <param name="firstColumnLabel">Label for first column, default = Date.</param>
        public static void WriteFieldHeaders(StreamWriter outputFile, List<Dictionary<string, string>> metaData, string firstColumnLabel = "Date")
        {
            string line;
            line = "Field";
            foreach (KeyValuePair<string, string> kvp in metaData.First())
            {
                line += "," + kvp.Key;
            }
            outputFile.WriteLine(line);

            outputFile.WriteLine("EOC");
            int numSeries = metaData.Count;
            outputFile.WriteLine(numSeries);

            string headerLine = firstColumnLabel;
            for (int i = 0; i < numSeries; i++)
            {
                line = (i + 1).ToString();
                foreach (KeyValuePair<string, string> kvp in metaData[i])
                {
                    line += "," + kvp.Value;
                }

                outputFile.WriteLine(line);

                string columnHeader = (i + 1).ToString() + ">";
                columnHeader += metaData[i]["Name"].Replace(":", ">");
                headerLine += "," + columnHeader;
            }

            outputFile.WriteLine(headerLine);
            outputFile.WriteLine("EOH");
        }

        /// <summary>
        /// Write .res.csv.
        /// </summary>
        /// <param name="series">List of time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="projectName">Name of project.</param>
        /// <param name="missingDataOutputString">String for missing data (default = -9999).</param>
        public static void WriteResCSV(
            List<TimeSeriesWithMetadata> series,
            string outPath,
            string programName,
            string projectName = "",
            string missingDataOutputString = "-9999")
        {
            string outFolder = Path.GetDirectoryName(outPath);
            Directory.CreateDirectory(outFolder);
            using (StreamWriter sw = new StreamWriter(outPath))
            {
                // Headers
                WriteFileHeaders(sw, programName, series.First().Data.First().Time, series.First().Data.Last().Time, projectName, missingDataOutputString);
                List<Dictionary<string, string>> metaData = new List<Dictionary<string, string>>();

                int numSeries = series.Count;
                for (int i = 0; i < numSeries; ++i)
                {
                    metaData.Add(series[i].GetMetadata());
                }

                WriteFieldHeaders(sw, metaData);

                // Series
                string line;

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
        /// Write .res.csv file.
        /// </summary>
        /// <param name="series">List of time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="outputProperties">Dictionary of output metadata for each field.</param>
        /// <param name="projectName">Project name for header.</param>
        /// <param name="missingDataOutputString">String to output for missing data</param>
        public static void WriteResCSV(
            List<TimeSeriesValue[]> series,
            string outPath,
            string programName,
            List<Dictionary<string, string>> outputProperties,
            string projectName = "",
            string missingDataOutputString = "-9999")
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

            WriteResCSV(seriesWithMetadata, outPath, programName, projectName, missingDataOutputString);
        }

        /// <summary>
        /// Write .res.csv.
        /// </summary>
        /// <param name="series">Time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="outputLabel">Output label.</param>
        /// <param name="unitsLabel">Units label.</param>
        public static void WriteResCSV(TimeSeriesValue[] series, string outPath, string programName, string outputLabel, List<string> unitsLabel)
        {
            WriteResCSV(new List<TimeSeriesValue[]> { series }, outPath, programName, new List<string> { outputLabel }, unitsLabel);
        }

        /// <summary>
        /// Write .res.csv.
        /// </summary>
        /// <param name="series">Time series.</param>
        /// <param name="outPath">Out path.</param>
        /// <param name="programName">Program name for header.</param>
        /// <param name="outputLabel">Output label.</param>
        /// <param name="unitsLabel">Units label.</param>
        /// <param name="missingDataOutputString">String to output for missing data</param>
        public static void WriteResCSV(TimeSeriesValue[] series, string outPath, string programName, string outputLabel, List<string> unitsLabel, string missingDataOutputString)
        {
            WriteResCSV(new List<TimeSeriesValue[]> { series }, outPath, programName, new List<string> { outputLabel }, unitsLabel, missingDataOutputString);
        }

        /// <summary>
        /// Creates a merged name string for the header of a .res.csv file.
        /// </summary>
        /// <param name="site">Site name in program (e.g. Barwon EFN Reach 1).</param>
        /// <param name="waterfeature">Water feature type (e.g. Environmental Flow, Gauge).</param>
        /// <param name="structure">Structure of output (e.g. Downstream Flow@VEWH, Functions@UrbanDemands@BW_Geelong_Demands@Geelong_DemandSplits@$ts_Total_ColacDemand).</param>
        /// <returns>Merged name string for the header of a .res.csv file.</returns>
        public static string GetMergedNameString(string site, string waterfeature, string structure)
        {
            string reformatStructure = structure.Replace('@', ':');
            return string.Concat(waterfeature, ": ", site, ": ", reformatStructure);
        }
    }
}
