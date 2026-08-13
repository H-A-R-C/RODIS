// <copyright file="ReadTimeSeries.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.InputOutput
{
    using RODIS.Series;

    public static class ReadTimeSeries
    {
        /// <summary>
        /// Read array of time series values from input file.
        /// </summary>
        /// <param name="inputFilePath">Path to input file.</param>
        /// <param name="unitsInInputFile">Writes the units stored in the input file to this string, if provided.</param>
        /// <param name="columnToRead">Column containing data to read, 0 indexed, default value is 1 = 2nd column from left.</param>
        /// <param name="dateColumnToRead">Column containing datetime data, 0 indexed, default value is 0 = 1st column from left.</param>
        /// <returns>Array of time series values.</returns>
        public static TimeSeriesValue[] ReadTimeSeriesFromFile(string inputFilePath, ref string unitsInInputFile, int columnToRead = 1, int dateColumnToRead = 0)
        {
            TimeSeriesValue[] result = null;

            try
            {
                if (inputFilePath.ToLower().EndsWith(".res.csv"))
                {
                    // Read data from a .res.csv standard Source output format file
                    result = ReadWriteResCSV.ReadResCSV(inputFilePath, columnToRead);
                    unitsInInputFile = ReadWriteResCSV.ReadResCSVFieldUnits(inputFilePath, columnToRead);
                }
                else
                {
                    // Read data from a vanilla .csv output format file
                    if (Path.GetExtension(inputFilePath).ToLower() == ".csv")
                    {
                        // Read data from a vanilla .csv output format file
                        const int numHeaderLines = 1;
                        result = ReadWriteSimpleCSV.ReadSimpleCSV(inputFilePath, columnToRead, numHeaderLines, dateColumnToRead);
                    }
                    else
                    {
                        // Read data from a GetDat format file
                        result = ReadWriteGetDatFiles.ReadGetDatFile(inputFilePath, columnToRead);
                    }
                }

                if (result == null)
                {
                    throw new InvalidDataException($"Input data file {inputFilePath} contains no valid data in specified column number to read {columnToRead}");
                }

                return result;
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
    }
}
