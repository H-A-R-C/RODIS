// <copyright file="ReadWriteGenCSV.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.InputOutput
{
    using System.Globalization;
    using System.Text;
    using RODIS.Static;

    /// <summary>
    /// Input/output helper for writing "generated" CSV outputs in a Source-compatible header format (same header blocks as .res.csv) followed by a matrix body.
    /// </summary>
    /// <remarks>
    /// This writer intentionally produces the same overall header structure as the Source .res.csv format by calling <see cref="ReadWriteResCSV.WriteFileHeaders"/>
    /// and <see cref="ReadWriteResCSV.WriteFieldHeaders"/>.
    ///
    /// The file extension is ".gen.csv" to distinguish these outputs from true Source results files while remaining compatible with downstream tooling that 
    /// expects the header sections.
    /// </remarks>
    public static class ReadWriteGenCSV
    {
        /// <summary>
        /// Default extension for generated CSV files.
        /// </summary>
        public const string DefaultExtension = ".gen.csv";

        /// <summary>
        /// Returns name of a ".gen.csv" file without directory or extension.
        /// </summary>
        /// <param name="filename">File name (optionally with path).</param>
        /// <returns>
        /// File name without extension if the file ends with <see cref="DefaultExtension"/>, otherwise an empty string.
        /// </returns>
        public static string GetFileNameWithoutExtension(string filename)
        {
            string fnWithExt = Path.GetFileName(filename);
            if (string.IsNullOrEmpty(fnWithExt))
            {
                return string.Empty;
            }

            if (fnWithExt.EndsWith(DefaultExtension, StringComparison.OrdinalIgnoreCase))
            {
                return fnWithExt.Substring(0, fnWithExt.Length - DefaultExtension.Length);
            }

            return string.Empty;
        }

        /// <summary>
        /// Returns extension for this file type.
        /// </summary>
        public static string Extension => DefaultExtension;

        /// <summary>
        /// Writes a generated data matrix to a ".gen.csv" file with Source-compatible headers.
        /// </summary>
        /// <param name="series">
        /// Data matrix [row, col] to write. Rows correspond to records and columns correspond to series/fields.
        /// </param>
        /// <param name="columnLabels"> Optional labels for each column. If provided, used for the metadata "Site" field.</param>
        /// <param name="unitLabels">Optional unit labels for each column. If provided, used for the metadata "Units" field.</param>
        /// <param name="outPath">Output path (including file name).</param>
        /// <param name="programName">Program name written to file header.</param>
        /// <param name="startTime">Start time written to file header.</param>
        /// <param name="endTime">End time written to file header.</param>
        /// <param name="firstColumnLabel">Label for the first (record) column in the field header section; default is "Record".</param>
        /// <param name="rowLabels">Optional row labels. If null, row index is written. If provided but shorter than number of rows, remaining 
        /// rows fall back to row index.</param>
        /// <param name="projectName">Optional project name written to file header.</param>
        /// <param name="missingDataOutputString">String written when a value is missing (default "-9999").</param>
        public static void WriteCSV(
            double[,] series,
            string[] columnLabels,
            string[] unitLabels,
            string outPath,
            string programName,
            DateTime startTime,
            DateTime endTime,
            string firstColumnLabel = "Record",
            string[] rowLabels = null,
            string projectName = "",
            string missingDataOutputString = "-9999")
        {
            if (series == null)
            {
                return;
            }

            // Build Source-compatible metadata dictionaries (one per column/series).
            // We keep this behaviour identical to your current implementation by
            // copying the res.csv metadata template, then modifying the key fields.
            var seriesMetadata = new List<Dictionary<string, string>>(series.GetLength(1));

            for (int j = 0; j < series.GetLength(1); j++)
            {
                Dictionary<string, string> copiedMetadata = Copiers.DictionaryCopier(ReadWriteResCSV.ResCSVMetaData);

                // Set identifying metadata for generated outputs.
                copiedMetadata["WaterFeatureType"] = "Generated data";

                if (columnLabels != null && j < columnLabels.Length)
                {
                    copiedMetadata["Site"] = columnLabels[j];
                }

                if (unitLabels != null && j < unitLabels.Length)
                {
                    copiedMetadata["Units"] = unitLabels[j];
                }

                // Ensure "Structure" has something sensible for generated outputs.
                // This avoids Name being partly empty if the template leaves Structure blank.
                if (copiedMetadata.ContainsKey("Structure") && string.IsNullOrWhiteSpace(copiedMetadata["Structure"]))
                {
                    copiedMetadata["Structure"] = "Generated";
                }

                copiedMetadata["Name"] = ReadWriteResCSV.GetMergedNameString(
                    copiedMetadata["Site"],
                    copiedMetadata["WaterFeatureType"],
                    copiedMetadata["Structure"]);

                // Fix #7: Replace bulky random filename concatenation with a stable, clean 32-char id.
                copiedMetadata["Custom"] = Guid.NewGuid().ToString("N");

                seriesMetadata.Add(copiedMetadata);
            }

            WriteCSV(
                series,
                seriesMetadata,
                outPath,
                programName,
                startTime,
                endTime,
                firstColumnLabel,
                rowLabels,
                projectName,
                missingDataOutputString);
        }

        /// <summary>
        /// Writes a generated data matrix to a ".gen.csv" file with Source-compatible headers.
        /// </summary>
        /// <param name="series">Data matrix [row, col].</param>
        /// <param name="seriesMetadata">Metadata dictionaries for each column/series. Count must match number of columns in <paramref name="series"/>.</param>
        /// <param name="outPath">Output path (including file name).</param>
        /// <param name="programName">Program name written to file header.</param>
        /// <param name="startTime">Start time written to file header.</param>
        /// <param name="endTime">End time written to file header.</param>
        /// <param name="firstColumnLabel">Label for first column (record column), default "Record".</param>
        /// <param name="rowLabels">Optional row labels; otherwise row indices are used.</param>
        /// <param name="projectName">Optional project name written to file header.</param>
        /// <param name="missingDataOutputString">String to write for missing values.</param>
        public static void WriteCSV(
            double[,] series,
            List<Dictionary<string, string>> seriesMetadata,
            string outPath,
            string programName,
            DateTime startTime,
            DateTime endTime,
            string firstColumnLabel = "Record",
            string[] rowLabels = null,
            string projectName = "",
            string missingDataOutputString = "-9999")
        {
            if (series == null)
            {
                throw new ArgumentNullException(nameof(series));
            }

            if (seriesMetadata == null)
            {
                throw new ArgumentNullException(nameof(seriesMetadata));
            }

            // (Not one of the 7 fixes, but a helpful guard for correctness.)
            if (seriesMetadata.Count != series.GetLength(1))
            {
                throw new InvalidDataException(
                    "seriesMetadata count must match the number of columns in the series matrix.");
            }

            // Fix #1: CreateDirectory can throw if outFolder is null/empty.
            string outFolder = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrWhiteSpace(outFolder))
            {
                Directory.CreateDirectory(outFolder);
            }

            using (StreamWriter sw = new StreamWriter(outPath))
            {
                // Headers (preserve current behaviour: Source-compatible header blocks)
                ReadWriteResCSV.WriteFileHeaders(sw, programName, startTime, endTime, projectName, missingDataOutputString);
                ReadWriteResCSV.WriteFieldHeaders(sw, seriesMetadata, firstColumnLabel);

                // Body (matrix)
                int nRows = series.GetLength(0);
                int nCols = series.GetLength(1);

                for (int i = 0; i < nRows; i++)
                {
                    // Determine row label (Record column).
                    string rowId;
                    if (rowLabels != null && i < rowLabels.Length)
                    {
                        rowId = rowLabels[i];
                    }
                    else
                    {
                        rowId = i.ToString(CultureInfo.InvariantCulture);
                    }

                    // Fix #7: Use StringBuilder to reduce per-cell string allocations.
                    var sb = new StringBuilder(rowId);

                    for (int j = 0; j < nCols; j++)
                    {
                        sb.Append(',');

                        double v = series[i, j];

                        // Fix #3: Apply missing data output string consistently.
                        // - Treat NaN as missing.
                        // - Treat sentinel MissingData as missing (exact/tiny tolerance).
                        if (double.IsNaN(v) || Math.Abs(v - ReadWriteResCSV.MissingData) < 1e-12)
                        {
                            sb.Append(missingDataOutputString);
                        }
                        else
                        {
                            // Fix #2: Use invariant culture to avoid locale-specific commas as decimal separators.
                            sb.Append(v.ToString(CultureInfo.InvariantCulture));
                        }
                    }

                    sw.WriteLine(sb.ToString());
                }
            }
        }
    }
}