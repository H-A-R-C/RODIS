// <copyright file="ReadWriteGetDatFiles.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.InputOutput
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using RODIS.Series;
    using RODIS.TimeSeries;

    /// <summary>
    /// Read/write utilities for "GetDat" style text files used by hydrologic tools
    /// (e.g., gaugeflows.fdy, rainfall.rmn, pet.eyr) and REALM-format variants.
    /// </summary>
    /// <remarks>
    /// Supports two patterns:
    /// 1) Simple GetDat: header lines start with '!' followed by data lines.
    /// 2) REALM-style GetDat: fixed structured header, followed by SEASON/YEAR headings.
    /// </remarks>
    public static class ReadWriteGetDatFiles
    {
        /// <summary>
        /// Sentinel numeric value used internally to represent missing data.
        /// </summary>
        public static double MissingData { get; set; } = -9999d;

        #region Internal header parsing (single-pass cache)

        private static readonly Dictionary<string, HeaderInfo> HeaderCache =
            new Dictionary<string, HeaderInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Container for all metadata parsed from a GetDat header.
        /// </summary>
        private sealed class HeaderInfo
        {
            public bool IsRealmFormat { get; init; }
            public int HeaderLineCount { get; set; }
            public List<string> HeaderLines { get; set; } = new List<string>();
            public StandardModellingTimeSpan TimeSpanFromHeader { get; set; }
            public string DateFormatFromHeader { get; set; }
            public string[] FormatStrings { get; set; }
            public string[] ColumnLabelsAndUnits { get; set; }
        }

        /// <summary>
        /// Parses and caches header metadata for a GetDat file.
        /// </summary>
        /// <param name="path">Path to the GetDat file.</param>
        /// <returns>
        /// A populated <see cref="HeaderInfo"/> instance containing all header-derived metadata.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Header parsing is deterministic and relatively expensive, so results are cached
        /// per file path for the lifetime of the process.
        /// </para>
        /// <para>
        /// The parsing strategy is selected based on whether the file is detected as
        /// REALM-format or simple bang-prefixed format.
        /// </para>
        /// </remarks>
        private static HeaderInfo ParseHeaderInfo(string path)
        {
            // Return cached header if already parsed
            if (HeaderCache.TryGetValue(path, out HeaderInfo cached))
            {
                return cached;
            }

            // Create new header container and detect header style
            var header = new HeaderInfo
            {
                IsRealmFormat = IsREALMFormatHeader(path)
            };

            // Parse header lines according to detected format
            if (header.IsRealmFormat)
            {
                ParseRealmHeader(path, header);
            }
            else
            {
                ParseSimpleBangHeader(path, header);
            }

            // Derive secondary metadata from parsed header content
            header.TimeSpanFromHeader = ParseTimeSpan(header.HeaderLines);
            header.FormatStrings = ParseFormatStrings(header.HeaderLines);
            header.ColumnLabelsAndUnits = ParseColumnLabels(header.HeaderLines);
            header.DateFormatFromHeader = ParseDateFormat(header.HeaderLines, header.ColumnLabelsAndUnits);

            // Cache and return
            HeaderCache[path] = header;
            return header;
        }

        /// <summary>
        /// Parses a simple GetDat header consisting of lines prefixed with '!'.
        /// </summary>
        /// <param name="path">Path to the GetDat file.</param>
        /// <param name="header">Header object to populate.</param>
        /// <remarks>
        /// <para>
        /// Header parsing stops at the first blank line or the first line that does not
        /// begin with '!'.
        /// </para>
        /// <para>
        /// Leading '!' characters and surrounding whitespace are removed before storing
        /// the header lines.
        /// </para>
        /// </remarks>
        private static void ParseSimpleBangHeader(string path, HeaderInfo header)
        {
            using var reader = new StreamReader(path);

            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                string trimmed = line.Trim();
                if (trimmed.StartsWith("!"))
                {
                    header.HeaderLines.Add(trimmed.Substring(1).Trim());
                }
                else
                {
                    break;
                }
            }

            // Number of lines consumed by the header
            header.HeaderLineCount = header.HeaderLines.Count;
        }

        /// <summary>
        /// Parses a REALM-format GetDat header.
        /// </summary>
        /// <param name="path">Path to the GetDat file.</param>
        /// <param name="header">Header object to populate.</param>
        /// <exception cref="InvalidDataException">
        /// Thrown when the REALM header structure is incomplete or malformed.
        /// </exception>
        /// <remarks>
        /// REALM headers follow a strict structure:
        /// <list type="number">
        /// <item><description>Five descriptive lines</description></item>
        /// <item><description>Output format line</description></item>
        /// <item><description>Number of columns</description></item>
        /// <item><description>One line per column name</description></item>
        /// <item><description>SEASON marker</description></item>
        /// <item><description>YEAR marker</description></item>
        /// </list>
        /// Any deviation from this structure is treated as a fatal parsing error.
        /// </remarks>
        private static void ParseRealmHeader(string path, HeaderInfo header)
        {
            using var reader = new StreamReader(path);
            var lines = new List<string>();

            // First five descriptive header lines
            for (int i = 0; i < 5; i++)
            {
                if (reader.EndOfStream)
                {
                    throw new InvalidDataException("REALM header truncated.");
                }

                lines.Add(reader.ReadLine()?.Trim() ?? string.Empty);
            }

            // Output format line
            if (reader.EndOfStream)
            {
                throw new InvalidDataException("REALM header truncated.");
            }

            lines.Add("Output format: " + reader.ReadLine()?.Trim());

            // Number of columns
            if (reader.EndOfStream)
            {
                throw new InvalidDataException("REALM header truncated.");
            }

            if (!int.TryParse(reader.ReadLine()?.Trim(), out int nCols) || nCols < 2)
            {
                throw new InvalidDataException("REALM header invalid column count.");
            }

            // Column names
            var names = new List<string>();
            for (int i = 0; i < nCols; i++)
            {
                if (reader.EndOfStream)
                {
                    throw new InvalidDataException("REALM header truncated.");
                }

                names.Add(reader.ReadLine()?.Trim());
            }

            lines.Add("Column labels: " + string.Join(" | ", names) + " |");

            // SEASON and YEAR markers
            if (!string.Equals(reader.ReadLine()?.Trim(), "SEASON", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("REALM header missing SEASON.");
            }

            if (!string.Equals(reader.ReadLine()?.Trim(), "YEAR", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("REALM header missing YEAR.");
            }

            header.HeaderLines = lines;
            header.HeaderLineCount = 5 + 1 + 1 + nCols + 2;
        }

        /// <summary>
        /// Attempts to parse a modelling time span definition from header lines.
        /// </summary>
        /// <param name="headerLines">Normalised header lines.</param>
        /// <returns>
        /// A <see cref="StandardModellingTimeSpan"/> if a recognised timebase is found;
        /// otherwise <c>null</c>.
        /// </returns>
        /// <remarks>
        /// Recognised timebase tokens are:
        /// <list type="bullet">
        /// <item><description><c>DAY</c> ? Daily</description></item>
        /// <item><description><c>WEK</c> ? Weekly</description></item>
        /// <item><description><c>MON</c> ? Monthly</description></item>
        /// </list>
        /// The first matching token is used; subsequent matches are ignored.
        /// </remarks>
        private static StandardModellingTimeSpan ParseTimeSpan(List<string> headerLines)
        {
            if (headerLines == null)
            {
                return null;
            }

            foreach (string raw in headerLines)
            {
                if (raw == null)
                {
                    continue;
                }

                if (!raw.Contains("timebase", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int colon = raw.IndexOf(':');
                if (colon < 0)
                {
                    continue;
                }

                string token = raw.Substring(colon + 1).Trim().ToUpperInvariant();
                return token switch
                {
                    "DAY" => new StandardModellingTimeSpan(BaseModellingTimeSpan.Daily, 1),
                    "WEK" => new StandardModellingTimeSpan(BaseModellingTimeSpan.Weekly, 1),
                    "MON" => new StandardModellingTimeSpan(BaseModellingTimeSpan.Monthly, 1),
                    _ => null
                };
            }

            return null;
        }

        #endregion

        #region Public API (Directory / File)

        /// <summary>
        /// Writes one or more time series to a simple daily GetDat-style file.
        /// </summary>
        /// <param name="series">
        /// Collection of time series arrays. Each entry represents one output column.
        /// All series are assumed to be aligned in time and of equal length.
        /// </param>
        /// <param name="outPath">Path to the output GetDat file.</param>
        /// <param name="programName">
        /// Name of the calling program (retained for historical compatibility; not written to file).
        /// </param>
        /// <param name="outputLabel">
        /// Column labels corresponding to each time series (excluding the date column).
        /// </param>
        /// <param name="unitsLabel">
        /// Units label (currently unused in this writer, but retained for API compatibility).
        /// </param>
        /// <remarks>
        /// This overload uses the globally configured <see cref="MissingData"/> value
        /// when writing missing or invalid data.
        /// </remarks>
        public static void WriteGetDatFile(
            List<TimeSeriesValue[]> series,
            string outPath,
            string programName,
            List<string> outputLabel,
            string unitsLabel)
        {
            WriteGetDatFile(
                series,
                outPath,
                programName,
                outputLabel,
                unitsLabel,
                MissingData.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes one or more time series to a simple daily GetDat-style file,
        /// using an explicit string for missing data values.
        /// </summary>
        /// <param name="series">
        /// Collection of time series arrays. Each entry represents one output column.
        /// All series are assumed to be aligned in time and of equal length.
        /// </param>
        /// <param name="outPath">Path to the output GetDat file.</param>
        /// <param name="programName">
        /// Name of the calling program (retained for historical compatibility; not written to file).
        /// </param>
        /// <param name="outputLabel">
        /// Column labels corresponding to each time series (excluding the date column).
        /// </param>
        /// <param name="unitsLabel">
        /// Units label (currently unused in this writer, but retained for API compatibility).
        /// </param>
        /// <param name="missingDataOutputString">
        /// String representation of missing data values to write to file
        /// (typically fixed-width padded).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="series"/>, <paramref name="outPath"/>,
        /// or <paramref name="outputLabel"/> is null.
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// Thrown if no valid series are provided or the series are empty.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Output format:
        /// </para>
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// First column is a date in <c>yyyyMMdd</c> format.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Subsequent columns are fixed-width (12 characters), right-aligned.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// Missing or invalid values are written using <paramref name="missingDataOutputString"/>.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Only the first <c>min(series.Count, outputLabel.Count)</c> series are written.
        /// </para>
        /// </remarks>
        public static void WriteGetDatFile(
            List<TimeSeriesValue[]> series,
            string outPath,
            string programName,
            List<string> outputLabel,
            string unitsLabel,
            string missingDataOutputString)
        {
            if (series == null) throw new ArgumentNullException(nameof(series));
            if (outPath == null) throw new ArgumentNullException(nameof(outPath));
            if (outputLabel == null) throw new ArgumentNullException(nameof(outputLabel));

            int numSeries = Math.Min(series.Count, outputLabel.Count);
            if (numSeries <= 0)
            {
                throw new InvalidDataException("No series/labels provided.");
            }

            int length = series[0]?.Length ?? 0;
            if (length == 0)
            {
                throw new InvalidDataException("Series is empty.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");

            using (var sw = new StreamWriter(outPath))
            {
                // Write header line (date column + data columns)
                string header = "!YYYYMMDD";
                for (int i = 0; i < numSeries; i++)
                {
                    header += " " + (outputLabel[i] ?? string.Empty).PadLeft(12);
                }
                sw.WriteLine(header);

                // Write data rows
                for (int row = 0; row < length; row++)
                {
                    string line = series[0][row].Time.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

                    for (int s = 0; s < numSeries; s++)
                    {
                        if (series[s] == null || row >= series[s].Length || !series[s][row].IsValid)
                        {
                            line += " " + missingDataOutputString.PadLeft(12);
                        }
                        else
                        {
                            line += " " + series[s][row].Value
                                .ToString("0.00", CultureInfo.InvariantCulture)
                                .PadLeft(12);
                        }
                    }

                    sw.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Writes a single time series to a simple daily GetDat-style file.
        /// </summary>
        /// <param name="series">Time series to write.</param>
        /// <param name="outPath">Path to the output GetDat file.</param>
        /// <param name="programName">
        /// Name of the calling program (retained for historical compatibility).
        /// </param>
        /// <param name="outputLabel">Column label for the series.</param>
        /// <param name="unitsLabel">
        /// Units label (currently unused in this writer, but retained for API compatibility).
        /// </param>
        /// <remarks>
        /// This is a convenience overload that wraps the single series
        /// into a list and delegates to the multi-series implementation.
        /// </remarks>
        public static void WriteGetDatFile(
            TimeSeriesValue[] series,
            string outPath,
            string programName,
            string outputLabel,
            string unitsLabel)
        {
            WriteGetDatFile(
                new List<TimeSeriesValue[]> { series },
                outPath,
                programName,
                new List<string> { outputLabel },
                unitsLabel);
        }

        /// <summary>
        /// Writes a single time series to a simple daily GetDat-style file,
        /// using an explicit string for missing data values.
        /// </summary>
        /// <param name="series">Time series to write.</param>
        /// <param name="outPath">Path to the output GetDat file.</param>
        /// <param name="programName">
        /// Name of the calling program (retained for historical compatibility).
        /// </param>
        /// <param name="outputLabel">Column label for the series.</param>
        /// <param name="unitsLabel">
        /// Units label (currently unused in this writer, but retained for API compatibility).
        /// </param>
        /// <param name="missingDataOutputString">
        /// String representation of missing data values to write to file.
        /// </param>
        /// <remarks>
        /// This overload allows explicit control over how missing values
        /// are rendered in the output file.
        /// </remarks>
        public static void WriteGetDatFile(
            TimeSeriesValue[] series,
            string outPath,
            string programName,
            string outputLabel,
            string unitsLabel,
            string missingDataOutputString)
        {
            WriteGetDatFile(
                new List<TimeSeriesValue[]> { series },
                outPath,
                programName,
                new List<string> { outputLabel },
                unitsLabel,
                missingDataOutputString);
        }

        /// <summary>
        /// Returns a time span for a specified number of weeks of the REALM year after the specified start date.
        /// </summary>
        /// <param name="startDate">Start date for period.</param>
        /// <param name="numWeeks">Double value for number of weeks to use. Will normally be 1.0, for 1 week period.</param>
        /// <returns>Time span for the period. Normally will be a multiple of 7 days but will be a multiple of 8 or 9 days if it goes into or across the last week of the REALM water year.</returns>
        public static TimeSpan REALMWeeksToTimeSpan(DateTime startDate, double numWeeks)
        {
            // TODO: Check with Kate that my interpretation of a week is correct
            // See REALM manual, version 6.28, page 70 or page 82 of the PDF file
            // "O:\2. Technical\1. Software\REALM\realm-user-manual-version-6.28.pdf"

            TimeSpan timeSpan = TimeSpan.Zero;

            if (startDate > DateTime.MinValue)
            {
                DateTime endDate = startDate.AddDays(7.0 * numWeeks);

                if (endDate.Month == 6 && endDate.Day >= 22)
                {
                    if (DateTime.IsLeapYear(endDate.Year))
                    {
                        DateTime startOfLastWeekOfWY = new DateTime(endDate.Year, 6, 22);
                        double fractionOfLastWeek = (endDate - startOfLastWeekOfWY).TotalDays / 7.0;
                        endDate = startOfLastWeekOfWY.AddDays(9.0 * fractionOfLastWeek);
                    }
                    else
                    {
                        if (endDate.Day >= 23)
                        {
                            DateTime startOfLastWeekOfWY = new DateTime(endDate.Year, 6, 23);
                            double fractionOfLastWeek = (endDate - startOfLastWeekOfWY).TotalDays / 7.0;
                            endDate = startOfLastWeekOfWY.AddDays(8.0 * fractionOfLastWeek);
                        }
                    }
                }

                timeSpan = endDate - startDate;
            }

            return timeSpan;
        }

        /// <summary>
        /// Determines whether a file appears to have a REALM-format header signature.
        /// </summary>
        /// <param name="path">Path to the GetDat file.</param>
        /// <returns>
        /// True if a valid REALM header structure is detected; otherwise false.
        /// </returns>
        public static bool IsREALMFormatHeader(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("GetDat format file not found", path);
            }

            try
            {
                using (var reader = new StreamReader(path))
                {
                    // First 5 descriptive lines
                    for (int i = 0; i < 5; i++)
                    {
                        if (reader.EndOfStream) return false;
                        reader.ReadLine();
                    }

                    // Output format line
                    if (reader.EndOfStream) return false;
                    reader.ReadLine();

                    // Number of columns
                    if (reader.EndOfStream) return false;
                    if (!int.TryParse(reader.ReadLine()?.Trim(), out int nCols) || nCols < 2)
                    {
                        return false;
                    }

                    // Column names
                    for (int i = 0; i < nCols; i++)
                    {
                        if (reader.EndOfStream) return false;
                        if (string.IsNullOrWhiteSpace(reader.ReadLine())) return false;
                    }

                    // SEASON / YEAR markers
                    if (!string.Equals(reader.ReadLine()?.Trim(), "SEASON", StringComparison.OrdinalIgnoreCase))
                        return false;

                    if (!string.Equals(reader.ReadLine()?.Trim(), "YEAR", StringComparison.OrdinalIgnoreCase))
                        return false;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reads all GetDat files in a directory that match a given file specification.
        /// </summary>
        /// <param name="directoryPath">
        /// Path to the directory containing GetDat files.
        /// </param>
        /// <param name="fileSpecificaton">
        /// File search pattern (e.g. <c>"*.fdy"</c>, <c>"*.wk"</c>). Defaults to daily files.
        /// </param>
        /// <returns>
        /// A list of time series arrays, one per file found.
        /// </returns>
        /// <exception cref="DirectoryNotFoundException">
        /// Thrown if <paramref name="directoryPath"/> does not exist.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Files are processed independently and returned in the order provided by
        /// <see cref="Directory.GetFiles(string,string)"/>.
        /// </para>
        /// <para>
        /// Each file is read using <see cref="ReadGetDatFile(string,int,string)"/> with
        /// default parsing behaviour.
        /// </para>
        /// </remarks>
        public static List<TimeSeriesValue[]> ReadAllGetDatFilesInDirectory(
            string directoryPath,
            string fileSpecificaton = "*.fdy")
        {
            if (!Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException(directoryPath);
            }

            var result = new List<TimeSeriesValue[]>();

            foreach (string file in Directory.GetFiles(directoryPath, fileSpecificaton))
            {
                result.Add(ReadGetDatFile(file));
            }

            return result;
        }

        /// <summary>
        /// Reads a single GetDat file into a time series.
        /// </summary>
        /// <param name="path">Path to the GetDat file.</param>
        /// <param name="columnIndexToRead">
        /// Index of the data column to read (0-based). Column 0 is assumed to be the date.
        /// </param>
        /// <param name="fortranFormatString">
        /// Optional Fortran-style fixed-width format specification. If <c>null</c>,
        /// whitespace-based parsing is used.
        /// </param>
        /// <returns>
        /// An array of <see cref="TimeSeriesValue"/> records parsed from the file.
        /// </returns>
        /// <exception cref="InvalidDataException">
        /// Thrown if no valid data records are found in the file.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The modelling time span is determined in the following order:
        /// </para>
        /// <list type="number">
        /// <item><description>Explicit timebase specified in the file header</description></item>
        /// <item><description>Inference from file extension</description></item>
        /// <item><description>Fallback to daily time step</description></item>
        /// </list>
        /// <para>
        /// Date parsing behaviour depends on whether the file is detected as REALM format.
        /// </para>
        /// </remarks>
        public static TimeSeriesValue[] ReadGetDatFile(
            string path,
            int columnIndexToRead = 1,
            string fortranFormatString = null)
        {
            // Parse and cache header metadata
            HeaderInfo header = ParseHeaderInfo(path);

            // Determine modelling time span
            StandardModellingTimeSpan timeSpan =
                header.TimeSpanFromHeader ??
                InferTimeSpanFromExtension(path) ??
                new StandardModellingTimeSpan(BaseModellingTimeSpan.Daily, 1);

            // Determine date format
            string dateFormat = header.DateFormatFromHeader ??
                (timeSpan.BaseTimeSpan == BaseModellingTimeSpan.Monthly ? "yyyyMM" :
                 timeSpan.BaseTimeSpan == BaseModellingTimeSpan.Weekly ? "yyyyww" :
                 "yyyyMMdd");

            var data = new List<TimeSeriesValue>();

            using var reader = new StreamReader(path);

            // Skip header lines before reading data
            for (int i = 0; i < header.HeaderLineCount; i++)
            {
                reader.ReadLine();
            }

            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // Split line into tokens (fixed-width or whitespace)
                string[] parts = fortranFormatString == null
                    ? SplitByWhitespace(line)
                    : GetFixedWidthColumns(line, fortranFormatString);

                if (parts.Length == 0)
                {
                    continue;
                }

                // Parse date/time depending on header format
                bool okDate = header.IsRealmFormat
                    ? TryParseRealmDate(parts, timeSpan, out DateTime dt)
                    : TryParseNonRealmDate(parts[0], timeSpan, dateFormat, out dt);

                if (!okDate)
                {
                    continue;
                }

                // Initialise time series value as missing
                var ts = new TimeSeriesValue
                {
                    Time = dt,
                    Value = MissingData,
                    IsValid = false
                };

                // Parse numeric value if present and valid
                if (parts.Length > columnIndexToRead &&
                    double.TryParse(
                        parts[columnIndexToRead],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double v) &&
                    Math.Abs(v - MissingData) > 1e-12)
                {
                    ts.Value = v;
                    ts.IsValid = true;
                }

                data.Add(ts);
            }

            if (data.Count == 0)
            {
                throw new InvalidDataException("No valid data records.");
            }

            return data.ToArray();
        }

        #endregion

        #region Parsing helpers

        /// <summary>
        /// Splits a line of text into tokens using whitespace as the delimiter.
        /// </summary>
        /// <param name="line">The input line to split.</param>
        /// <returns>
        /// An array of non-empty tokens obtained by splitting on any whitespace.
        /// </returns>
        /// <remarks>
        /// This is the default tokenisation strategy for free-format GetDat files
        /// where columns are separated by one or more whitespace characters.
        /// </remarks>
        private static string[] SplitByWhitespace(string line) =>
            line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// Extracts fixed-width column values from a line using a Fortran-style format string.
        /// </summary>
        /// <param name="line">The input data line.</param>
        /// <param name="fortranFormatString">
        /// Fortran-style format specification (e.g. <c>I8,F12.2,F12.2</c>).
        /// </param>
        /// <returns>
        /// An array of column values extracted according to the specified widths.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Column widths are inferred from the numeric component of each format token
        /// (e.g. <c>F12.2</c> ? width 12).
        /// </para>
        /// <para>
        /// If the line is shorter than the expected total width, missing columns
        /// are returned as empty strings.
        /// </para>
        /// </remarks>
        private static string[] GetFixedWidthColumns(string line, string fortranFormatString)
        {
            if (string.IsNullOrWhiteSpace(fortranFormatString))
            {
                return SplitByWhitespace(line);
            }

            var parts = fortranFormatString
                .Split(new[] { ' ', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);

            var cols = new List<string>();
            int pos = 0;

            foreach (string part in parts)
            {
                // Extract numeric width from the format token
                string digits = new string(part.Skip(1).TakeWhile(char.IsDigit).ToArray());
                if (!int.TryParse(digits, out int width))
                {
                    continue;
                }

                if (pos >= line.Length)
                {
                    cols.Add(string.Empty);
                }
                else
                {
                    cols.Add(line.Substring(pos, Math.Min(width, line.Length - pos)).Trim());
                }

                pos += width;
            }

            return cols.ToArray();
        }

        /// <summary>
        /// Attempts to parse a non-REALM date token into a <see cref="DateTime"/>.
        /// </summary>
        /// <param name="token">Date token extracted from the data line.</param>
        /// <param name="timeSpan">The modelling time span definition.</param>
        /// <param name="dateFormat">Normalised date format string.</param>
        /// <param name="dateTime">Parsed date value.</param>
        /// <returns>
        /// <c>true</c> if the date was successfully parsed; otherwise <c>false</c>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Weekly dates are parsed by extracting digits (e.g. <c>yyyyww</c>,
        /// <c>yyyy-ww</c>, <c>yyyy ww</c>) and interpreting them using REALM
        /// water-year semantics.
        /// </para>
        /// <para>
        /// All other cases use exact parsing with the supplied format string.
        /// </para>
        /// </remarks>
        private static bool TryParseNonRealmDate(
            string token,
            StandardModellingTimeSpan timeSpan,
            string dateFormat,
            out DateTime dateTime)
        {
            dateTime = DateTime.MinValue;

            if (timeSpan.BaseTimeSpan == BaseModellingTimeSpan.Weekly || dateFormat.Contains('w'))
            {
                string digits = new string(token.Where(char.IsDigit).ToArray());
                if (digits.Length < 6)
                {
                    return false;
                }

                int year = int.Parse(digits.Substring(0, 4));
                int week = int.Parse(digits.Substring(4, 2));
                dateTime = StartOfREALMWeek(year, week);
                return true;
            }

            return DateTime.TryParseExact(
                token,
                dateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateTime);
        }

        /// <summary>
        /// Attempts to parse a REALM-format date using SEASON and YEAR fields.
        /// </summary>
        /// <param name="parts">Tokenised data line.</param>
        /// <param name="timeSpan">The modelling time span definition.</param>
        /// <param name="dateTime">Parsed date value.</param>
        /// <returns>
        /// <c>true</c> if parsing succeeds; otherwise <c>false</c>.
        /// </returns>
        /// <remarks>
        /// Interpretation of the SEASON field depends on the modelling time span:
        /// <list type="bullet">
        /// <item><description>Daily ? day-of-year</description></item>
        /// <item><description>Monthly ? month-of-year</description></item>
        /// <item><description>Weekly ? REALM week number</description></item>
        /// </list>
        /// </remarks>
        private static bool TryParseRealmDate(
            string[] parts,
            StandardModellingTimeSpan timeSpan,
            out DateTime dateTime)
        {
            dateTime = DateTime.MinValue;

            if (!int.TryParse(parts[0], out int season) ||
                !int.TryParse(parts[1], out int year))
            {
                return false;
            }

            dateTime = timeSpan.BaseTimeSpan switch
            {
                BaseModellingTimeSpan.Daily =>
                    new DateTime(year, 1, 1).AddDays(season - 1),

                BaseModellingTimeSpan.Monthly =>
                    new DateTime(year, season, 1),

                BaseModellingTimeSpan.Weekly =>
                    StartOfREALMWeek(year, season),

                _ =>
                    DateTime.MinValue
            };

            return true;
        }

        /// <summary>
        /// Parses Fortran-style output format strings from header lines.
        /// </summary>
        /// <remarks>
        /// Currently not implemented; returns an empty array.
        /// </remarks>
        private static string[] ParseFormatStrings(List<string> headerLines) =>
            Array.Empty<string>();

        /// <summary>
        /// Parses column labels (and optional units) from header lines.
        /// </summary>
        /// <remarks>
        /// Currently not implemented; returns an empty array.
        /// </remarks>
        private static string[] ParseColumnLabels(List<string> headerLines) =>
            Array.Empty<string>();

        /// <summary>
        /// Parses the date format token from header content.
        /// </summary>
        /// <remarks>
        /// Currently not implemented; returns <c>null</c>.
        /// </remarks>
        private static string ParseDateFormat(List<string> headerLines, string[] cols) =>
            null;

        /// <summary>
        /// Infers a modelling time span from the file extension.
        /// </summary>
        /// <param name="path">Path to the GetDat file.</param>
        /// <returns>
        /// A <see cref="StandardModellingTimeSpan"/> inferred from the extension,
        /// or <c>null</c> if no inference can be made.
        /// </returns>
        /// <remarks>
        /// This is a fallback mechanism used only when the header does not explicitly
        /// specify a time base.
        /// </remarks>
        private static StandardModellingTimeSpan InferTimeSpanFromExtension(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext switch
            {
                ".dy" => new StandardModellingTimeSpan(BaseModellingTimeSpan.Daily, 1),
                ".wk" => new StandardModellingTimeSpan(BaseModellingTimeSpan.Weekly, 1),
                ".mn" => new StandardModellingTimeSpan(BaseModellingTimeSpan.Monthly, 1),
                _ => null
            };
        }

        /// <summary>
        /// Returns the start date of a REALM water-year week.
        /// </summary>
        /// <param name="year">Water year (starting 1 July).</param>
        /// <param name="week">Week number (1-based).</param>
        /// <returns>
        /// The <see cref="DateTime"/> corresponding to the start of the specified REALM week.
        /// </returns>
        public static DateTime StartOfREALMWeek(int year, int week) =>
            new DateTime(year, 7, 1).AddDays(7 * (week - 1));

        #endregion
    }
}