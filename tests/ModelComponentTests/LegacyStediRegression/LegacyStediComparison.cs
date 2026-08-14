// ============================================================================
// LegacyStediComparison.cs
//   Core parsers and comparison logic for validating RODIS legacy-STEDI runs
//   against the Fortran STEDI 1.2 (SKM, 2012) reference outputs.
//
//   Primary reference : Fortran STEDI 1.2 .fdy water-balance file (long-term truth).
//   Secondary (optional): March STEDI2025 .res.csv (interim regression aid only).
//
//   Comparison is spill-aware: differences that occur only on spill-active days
//   (spill > 0 in EITHER Fortran or RODIS) are flagged for investigation rather
//   than failed, because the two engines cascade spill slightly differently.
//   Storage is compared on its daily CHANGE so the documented, cumulative day-1
//   demand-timing offset cancels; the carried level offset is reported as a
//   diagnostic.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RODIS.Tests.Legacy
{
    /// <summary>How a metric is compared: on its daily level, or on its day-to-day change (for cumulative quantities like storage, where a carried constant offset must cancel).</summary>
    public enum ComparisonMode
    {
        /// <summary>Compare the metric's value on each day directly.</summary>
        Level,

        /// <summary>Compare the day-to-day change (RODIS change field vs Fortran Delta-Store). A constant carried offset cancels, so only genuinely new divergence is flagged.</summary>
        CumulativeChange,
    }

    /// <summary>Which documented, understood differences count as "explained" (flagged for investigation) rather than a failure, for a given metric.</summary>
    public enum ExplainedBy
    {
        /// <summary>Explained only on spill-active days (the spill-cascade difference between the engines).</summary>
        Spill,

        /// <summary>Explained on spill days OR while a storage-level offset is present (e.g. demand, whose availability depends on the carried storage offset).</summary>
        SpillOrStorageOffset,

        /// <summary>Explained on spill days OR when demand differs that day (e.g. the storage change, whose daily increment depends on demand and spill).</summary>
        SpillOrDemandDiff,
    }

    /// <summary>Identifies one comparison metric, mapping the Fortran .fdy column to the RODIS .res.csv field number, with its comparison mode and tolerance.</summary>
    public sealed class MetricSpec
    {
        /// <summary>Gets the friendly metric name used in reporting (e.g. "Impact").</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Gets the zero-based column index of this metric's level in the Fortran .fdy water-balance table.</summary>
        public int FdyColumnIndex { get; init; }

        /// <summary>Gets the one-based field number of this metric's level in the RODIS .res.csv result file.</summary>
        public int ResFieldNumber { get; init; }

        /// <summary>Gets the absolute tolerance for this metric. Default 1e-3 absorbs the Fortran 3-decimal (0.000) display rounding; widen deliberately only for a documented, understood difference.</summary>
        public double AbsTolerance { get; init; } = 1e-3;

        /// <summary>Gets the comparison mode (Level by default; CumulativeChange for storage).</summary>
        public ComparisonMode Mode { get; init; } = ComparisonMode.Level;

        /// <summary>Gets which documented differences are treated as explained (flagged) rather than failed.</summary>
        public ExplainedBy Explained { get; init; } = ExplainedBy.Spill;

        /// <summary>Gets the zero-based Fortran Delta-Store column used when Mode is CumulativeChange (-1 otherwise).</summary>
        public int DeltaFdyColumnIndex { get; init; } = -1;

        /// <summary>Gets the one-based RODIS change-in-storage field used when Mode is CumulativeChange (-1 otherwise).</summary>
        public int DeltaResFieldNumber { get; init; } = -1;
    }

    /// <summary>Holds the eight metrics compared for each scenario, plus the spill column/field used for spill-event classification.</summary>
    public static class LegacyStediMetrics
    {
        /// <summary>Fortran .fdy column order: 0 Q-impact, 1 Q-NoDams, 2 Q-wfill, 3 Q-climate, 4 Q-demand, 5 Q-spill, 6 Delta-Store, 7 Store end, 8 Q-bypass, 9 Q-unimpound, 10 Q-WithDams.</summary>
        public const int FdyColumnCount = 11;

        /// <summary>Gets the Fortran .fdy column index of Q-spill, used (with the RODIS spill field) to classify spill-active days.</summary>
        public const int FdySpillColumnIndex = 5;

        /// <summary>Gets the RODIS .res.csv field number of Spill Downstream Flow, used (with the Fortran Q-spill) to classify spill-active days.</summary>
        public const int ResSpillFieldNumber = 8;

        /// <summary>Gets the eight metrics compared between the Fortran reference and the RODIS output, keyed by friendly name.</summary>
        public static IReadOnlyList<MetricSpec> All { get; } = new List<MetricSpec>
        {
            new MetricSpec { Name = "Impact",         FdyColumnIndex = 0,  ResFieldNumber = 1  },
            new MetricSpec { Name = "Unimpacted",     FdyColumnIndex = 1,  ResFieldNumber = 4  },
            new MetricSpec { Name = "Climate",        FdyColumnIndex = 3,  ResFieldNumber = 6  },
            // Demand carries the DOCUMENTED day-1 demand-timing offset (RODIS applies demand from day 2, Fortran from day 1). Once storage is offset,
            // availability-limited demand can differ, so a demand difference is treated as explained whenever a storage-level offset is present (or on spill days).
            new MetricSpec { Name = "Demand",         FdyColumnIndex = 4,  ResFieldNumber = 7, AbsTolerance = 2e-3, Explained = ExplainedBy.SpillOrStorageOffset },
            new MetricSpec { Name = "Spills",         FdyColumnIndex = 5,  ResFieldNumber = 8 },
            // Storage is CUMULATIVE, so the day-1 demand offset leaves a permanent ~0.07 ML carried offset. Comparing the daily CHANGE (RODIS field 9 vs
            // Fortran Delta-Store) cancels that constant offset; only a genuinely new, unexplained daily change fails. 2e-3 allows the difference of two
            // 3-decimal-rounded increments. The carried storage-level offset itself is reported as a diagnostic (see MetricResult.CumulativeOffsetMax).
            new MetricSpec { Name = "Storage",        FdyColumnIndex = 7,  ResFieldNumber = 10, AbsTolerance = 2e-3,
                             Mode = ComparisonMode.CumulativeChange, DeltaFdyColumnIndex = 6, DeltaResFieldNumber = 9, Explained = ExplainedBy.SpillOrDemandDiff },
            new MetricSpec { Name = "LocalInflow",    FdyColumnIndex = 9,  ResFieldNumber = 12 },
            new MetricSpec { Name = "DownstreamFlow", FdyColumnIndex = 10, ResFieldNumber = 13 },
        };
    }

    /// <summary>Parses a Fortran STEDI 1.2 .fdy water-balance file into a date-keyed series of the eleven whitespace-delimited columns.</summary>
    public static class LegacyStediFdyReader
    {
        private static readonly Regex EightDigitDate = new Regex(@"^\d{8}$", RegexOptions.Compiled);

        /// <summary>Reads the .fdy file, skipping the banner and '!'-prefixed comment lines, and returns each day's eleven column values indexed by date.</summary>
        /// <param name="path">Full path to the Fortran .fdy water-balance file.</param>
        /// <returns>Dictionary mapping each date to its eleven-element column array (see LegacyStediMetrics for the column order).</returns>
        public static IReadOnlyDictionary<DateOnly, double[]> Read(string path)
        {
            Dictionary<DateOnly, double[]> series = new Dictionary<DateOnly, double[]>();
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '!') continue;                                    // skip banner / comment / header lines

                string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 1 + LegacyStediMetrics.FdyColumnCount || !EightDigitDate.IsMatch(parts[0])) continue;

                DateOnly date = new DateOnly(int.Parse(parts[0].Substring(0, 4)), int.Parse(parts[0].Substring(4, 2)), int.Parse(parts[0].Substring(6, 2)));
                double[] values = new double[LegacyStediMetrics.FdyColumnCount];
                for (int i = 0; i < LegacyStediMetrics.FdyColumnCount; i++)
                    values[i] = double.Parse(parts[1 + i], CultureInfo.InvariantCulture);

                series[date] = values;
            }
            return series;
        }
    }

    /// <summary>Parses a RODIS Source-format .res.csv result file into a date-keyed series keyed by field number, resolving the 'N&gt;...' column header and 'EOH' data marker.</summary>
    public static class RodisResCsvReader
    {
        private static readonly Regex FieldNumberPrefix = new Regex(@"^(\d+)\s*>", RegexOptions.Compiled);

        /// <summary>Reads the .res.csv file: locates the 'Date' header row to map field numbers to columns, skips to the 'EOH' marker, then returns each day's field values indexed by date.</summary>
        /// <param name="path">Full path to the RODIS .res.csv result file.</param>
        /// <returns>Dictionary mapping each date to an inner dictionary of {field number -&gt; value}.</returns>
        public static IReadOnlyDictionary<DateOnly, Dictionary<int, double>> Read(string path)
        {
            string[] lines = File.ReadAllLines(path);
            int headerRow = -1, eohRow = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string firstCell = FirstCell(lines[i]);
                if (firstCell == "Date") headerRow = i;
                if (firstCell == "EOH") { eohRow = i; break; }
            }
            if (headerRow < 0 || eohRow < 0) throw new InvalidDataException($"'{path}': could not locate the 'Date' header row and/or 'EOH' data marker.");

            // Map field number -> column index from the Date header row (cells look like "7>Confluence> ... > Demand Volume Extracted").
            string[] headerCells = SplitCsv(lines[headerRow]);
            Dictionary<int, int> fieldToColumn = new Dictionary<int, int>();
            for (int col = 1; col < headerCells.Length; col++)
            {
                Match m = FieldNumberPrefix.Match(headerCells[col].Trim());
                if (m.Success) fieldToColumn[int.Parse(m.Groups[1].Value)] = col;
            }

            Dictionary<DateOnly, Dictionary<int, double>> series = new Dictionary<DateOnly, Dictionary<int, double>>();
            for (int i = eohRow + 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                string[] cells = SplitCsv(lines[i]);
                if (cells.Length == 0 || cells[0].Trim().Length == 0) continue;
                if (!DateOnly.TryParse(cells[0].Length >= 10 ? cells[0].Substring(0, 10) : cells[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)) continue;

                Dictionary<int, double> row = new Dictionary<int, double>();
                foreach (KeyValuePair<int, int> fc in fieldToColumn)
                {
                    if (fc.Value >= cells.Length) continue;
                    string cell = cells[fc.Value].Trim();
                    if (cell.Length == 0 || cell == "-") continue;
                    if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) row[fc.Key] = value;
                }
                series[date] = row;
            }
            return series;
        }

        /// <summary>Returns the trimmed first comma-separated cell of a line without allocating the full split (used for cheap header/marker detection).</summary>
        /// <param name="line">Raw CSV line.</param>
        /// <returns>The trimmed content of the first cell.</returns>
        private static string FirstCell(string line)
        {
            int comma = line.IndexOf(',');
            return (comma < 0 ? line : line.Substring(0, comma)).Trim();
        }

        /// <summary>Splits a simple RODIS result CSV line on commas. The result files contain no quoted/embedded commas, so a plain split is sufficient and fast.</summary>
        /// <param name="line">Raw CSV line.</param>
        /// <returns>Array of cell strings.</returns>
        private static string[] SplitCsv(string line) => line.Split(',');
    }

    /// <summary>Three-tier outcome for a single metric: Pass, PassWithSpillDiffs (flagged for investigation) or Fail (a non-spill exceedance).</summary>
    public enum MetricTier
    {
        /// <summary>All days within tolerance.</summary>
        Pass,

        /// <summary>Exceedances occur only on explained (spill / known-difference) days; flagged for investigation, not treated as a failure.</summary>
        PassWithSpillDiffs,

        /// <summary>At least one exceedance occurs on an unexplained day; treated as a genuine regression.</summary>
        Fail,
    }

    /// <summary>Comparison statistics for one metric across all overlapping days.</summary>
    public sealed class MetricResult
    {
        /// <summary>Gets the metric name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Gets the number of overlapping days compared for this metric.</summary>
        public int DayCount { get; init; }

        /// <summary>Gets the maximum absolute difference across all compared days.</summary>
        public double MaxAbs { get; init; }

        /// <summary>Gets the root-mean-square difference across all compared days.</summary>
        public double Rmse { get; init; }

        /// <summary>Gets the count of exceedance days that are explained (spill / known difference).</summary>
        public int SpillExceedances { get; init; }

        /// <summary>Gets the count of exceedance days that are unexplained (these drive a Fail).</summary>
        public int NonSpillExceedances { get; init; }

        /// <summary>Gets the date of the worst (largest absolute) difference, or null if the metric matched exactly.</summary>
        public DateOnly? WorstDate { get; init; }

        /// <summary>Gets the three-tier outcome for this metric.</summary>
        public MetricTier Tier { get; init; }

        /// <summary>Gets the maximum carried storage-level offset (|RODIS - Fortran| on the level) for a CumulativeChange metric; 0 for level metrics. Diagnostic only.</summary>
        public double CumulativeOffsetMax { get; init; }
    }

    /// <summary>Aggregated comparison result for one scenario across all metrics.</summary>
    public sealed class ScenarioResult
    {
        /// <summary>Gets the scenario number (1-50).</summary>
        public int Scenario { get; init; }

        /// <summary>Gets the per-metric results keyed by metric name.</summary>
        public IReadOnlyList<MetricResult> Metrics { get; init; } = Array.Empty<MetricResult>();

        /// <summary>Gets the number of overlapping days shared by the two series.</summary>
        public int OverlapDays { get; init; }

        /// <summary>Gets a value indicating whether any metric failed on an unexplained day (i.e. a genuine regression).</summary>
        public bool HasFailure => Metrics.Any(m => m.Tier == MetricTier.Fail);

        /// <summary>Gets a value indicating whether any metric was flagged with explained (spill / known) differences for investigation.</summary>
        public bool HasSpillDiffs => Metrics.Any(m => m.Tier == MetricTier.PassWithSpillDiffs);
    }

    /// <summary>Compares a Fortran reference series against a RODIS output series metric-by-metric, applying spill-aware three-tier classification and cumulative-change handling.</summary>
    public static class LegacyStediComparer
    {
        /// <summary>Compares the Fortran .fdy series against the RODIS .res.csv series for all eight metrics, classifying each as Pass, PassWithSpillDiffs or Fail.</summary>
        /// <param name="scenario">Scenario number (1-50) for reporting.</param>
        /// <param name="fortran">Fortran reference series (date -&gt; eleven column values).</param>
        /// <param name="rodis">RODIS output series (date -&gt; {field number -&gt; value}).</param>
        /// <returns>Aggregated scenario result. Each metric is judged against its own MetricSpec.AbsTolerance, comparison mode and explained-difference rule.</returns>
        public static ScenarioResult Compare(int scenario, IReadOnlyDictionary<DateOnly, double[]> fortran,
                                             IReadOnlyDictionary<DateOnly, Dictionary<int, double>> rodis)
        {
            List<DateOnly> commonDays = fortran.Keys.Where(rodis.ContainsKey).OrderBy(d => d).ToList();

            // Pre-compute the per-day flags that classify whether a difference is "explained": spill activity, a demand difference, and a carried storage-level offset.
            Dictionary<DateOnly, bool> spillActiveOn = new Dictionary<DateOnly, bool>(commonDays.Count);
            Dictionary<DateOnly, bool> demandDiffersOn = new Dictionary<DateOnly, bool>(commonDays.Count);
            Dictionary<DateOnly, bool> storageOffsetOn = new Dictionary<DateOnly, bool>(commonDays.Count);
            foreach (DateOnly day in commonDays)
            {
                double[] fRow = fortran[day];
                Dictionary<int, double> rRow = rodis[day];
                spillActiveOn[day] = fRow[LegacyStediMetrics.FdySpillColumnIndex] > 1e-6
                                  || (rRow.TryGetValue(LegacyStediMetrics.ResSpillFieldNumber, out double rSpill) && rSpill > 1e-6);
                demandDiffersOn[day] = rRow.TryGetValue(7, out double rDem) && Math.Abs(fRow[4] - rDem) > 1e-3;                 // Fortran Q-demand col 4, RODIS demand field 7
                storageOffsetOn[day] = rRow.TryGetValue(10, out double rStor) && Math.Abs(fRow[7] - rStor) > 1e-3;               // Fortran Store-end col 7, RODIS storage field 10
            }

            List<MetricResult> metricResults = new List<MetricResult>();
            foreach (MetricSpec metric in LegacyStediMetrics.All)
            {
                double maxAbs = 0.0, sumSquares = 0.0, cumulativeOffsetMax = 0.0;
                int dayCount = 0, spillExceed = 0, nonSpillExceed = 0;
                DateOnly? worst = null;

                foreach (DateOnly day in commonDays)
                {
                    double[] fRow = fortran[day];
                    Dictionary<int, double> rRow = rodis[day];

                    // Choose the compared quantity: the daily level, or the day-to-day change for a cumulative metric.
                    double diff;
                    if (metric.Mode == ComparisonMode.CumulativeChange)
                    {
                        if (!rRow.TryGetValue(metric.DeltaResFieldNumber, out double rDelta)) continue;
                        diff = Math.Abs(fRow[metric.DeltaFdyColumnIndex] - rDelta);
                        // Track the carried storage-LEVEL offset as a diagnostic (this is the difference the change-based comparison deliberately cancels).
                        if (rRow.TryGetValue(metric.ResFieldNumber, out double rLevel))
                            cumulativeOffsetMax = Math.Max(cumulativeOffsetMax, Math.Abs(fRow[metric.FdyColumnIndex] - rLevel));
                    }
                    else
                    {
                        if (!rRow.TryGetValue(metric.ResFieldNumber, out double rValue)) continue;
                        diff = Math.Abs(fRow[metric.FdyColumnIndex] - rValue);
                    }

                    sumSquares += diff * diff;
                    dayCount++;
                    if (diff > maxAbs) { maxAbs = diff; worst = day; }

                    if (diff > metric.AbsTolerance)
                    {
                        if (IsExplained(metric.Explained, day, spillActiveOn, demandDiffersOn, storageOffsetOn)) spillExceed++;
                        else nonSpillExceed++;
                    }
                }

                MetricTier tier = maxAbs <= metric.AbsTolerance ? MetricTier.Pass
                                : nonSpillExceed == 0 ? MetricTier.PassWithSpillDiffs
                                : MetricTier.Fail;

                metricResults.Add(new MetricResult
                {
                    Name = metric.Name, DayCount = dayCount, MaxAbs = maxAbs,
                    Rmse = dayCount > 0 ? Math.Sqrt(sumSquares / dayCount) : 0.0,
                    SpillExceedances = spillExceed, NonSpillExceedances = nonSpillExceed,
                    WorstDate = worst, Tier = tier, CumulativeOffsetMax = cumulativeOffsetMax,
                });
            }

            return new ScenarioResult { Scenario = scenario, Metrics = metricResults, OverlapDays = commonDays.Count };
        }

        /// <summary>Returns whether a difference on the given day is explained (flagged rather than failed), per the metric's ExplainedBy rule and the pre-computed per-day flags.</summary>
        /// <param name="explained">The metric's explained-difference rule.</param>
        /// <param name="day">The day being classified.</param>
        /// <param name="spill">Per-day spill-active flags.</param>
        /// <param name="demandDiff">Per-day demand-difference flags.</param>
        /// <param name="storageOffset">Per-day storage-level-offset flags.</param>
        /// <returns>True if the difference is explained (counts as a flagged spill/known difference); false if it is a genuine failure.</returns>
        private static bool IsExplained(ExplainedBy explained, DateOnly day,
                                        IReadOnlyDictionary<DateOnly, bool> spill,
                                        IReadOnlyDictionary<DateOnly, bool> demandDiff,
                                        IReadOnlyDictionary<DateOnly, bool> storageOffset) => explained switch
        {
            ExplainedBy.SpillOrStorageOffset => spill[day] || storageOffset[day],
            ExplainedBy.SpillOrDemandDiff => spill[day] || demandDiff[day],
            _ => spill[day],
        };
    }
}
