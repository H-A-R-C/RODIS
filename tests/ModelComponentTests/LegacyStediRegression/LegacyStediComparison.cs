// ============================================================================
// LegacyStediComparison.cs
//   Core parsers and comparison logic for validating RODIS legacy-STEDI runs
//   against the Fortran STEDI 1.2 (SKM, 2012) reference outputs.
//
//   Primary reference : Fortran STEDI 1.2 .fdy water-balance file (long-term truth).
//   Secondary (optional): March STEDI2025 .res.csv (interim regression aid only).
//
//   Comparison is spill-aware: differences on spill-active days (spill > 0 in
//   EITHER Fortran or RODIS) are flagged for investigation rather than failed,
//   because the two engines cascade spill slightly differently. Storage is
//   compared on its daily CHANGE so the documented, cumulative day-1 demand
//   timing offset cancels; the carried level offset is reported separately.
//
//   SPILL / BYPASS ACCOUNTING
//   RODIS and legacy STEDI label the same water differently. Where an upstream
//   dam's bypass release reaches a downstream dam and leaves it, RODIS continues
//   to report that water as bypass, whereas legacy STEDI re-labels it as spill at
//   the downstream dam. The split between the two columns is therefore a
//   reporting convention, not a water-balance difference: their SUM is pinned to
//   downstream flow, and the identity
//         (dSpill + dBypass) == dDownstreamFlow
//   holds to within display rounding. Spills and Bypass are consequently reported
//   as informational metrics (max_abs and flag counts shown, never failing) while
//   SpillAndBypass carries the assertion. No sensitivity is lost: any difference
//   that is not a pure re-labelling changes the sum and still fails.
//
//   GUARD RAILS (see ExplainedGuards): the "explained difference" allowance is
//   deliberately bounded. If a metric is flagged on more than
//   MaxExplainedDayFraction of days, or the carried storage offset exceeds
//   MaxCarriedStorageOffsetML, the result is escalated to a failure.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RODISUnitTests.LegacyStediRegression
{
    /// <summary>Test category names used to filter runs, so tests needing the O: drive can be excluded when it is not mapped.</summary>
    public static class TestCategories
    {
        /// <summary>Tests that are fully self-contained (inline fixtures written to the temp folder) and safe to run anywhere, including CI.</summary>
        public const string SelfContained = "SelfContained";

        /// <summary>Tests that read the 2_SimpleTests scenario tree from the O: drive (or RODIS_SIMPLETESTS_ROOT) and must be excluded when it is unavailable.</summary>
        public const string RequiresSimpleTestsData = "RequiresSimpleTestsData";
    }

    /// <summary>Bounds on how much "explained" difference is tolerated before a result is escalated to a failure. These stop a systematic divergence hiding behind a long run of flagged days.</summary>
    public static class ExplainedGuards
    {
        /// <summary>Gets the maximum fraction of compared days a metric may be flagged on before the result is escalated to a failure. Calibrated at 0.10: the validated single-dam, two-dam-series and
        /// two-dam-parallel scenarios flag on roughly 2-3% of days, whereas scenarios with a systematic divergence flag on 29-66%.</summary>
        public const double MaxExplainedDayFraction = 0.10;

        /// <summary>Gets the maximum carried storage-level offset (ML) tolerated before the result is escalated to a failure. Calibrated at 0.5 ML: stock-and-domestic demand scenarios carry
        /// 0.03-0.15 ML from the documented day-1 timing offset, whereas scenarios with an unresolved divergence carry 0.8 ML or more.</summary>
        public const double MaxCarriedStorageOffsetML = 0.5;

        /// <summary>Gets the minimum number of compared days before the flagged-day fraction is meaningful enough to enforce. Below this the fraction guard is skipped, so a short
        /// diagnostic series is not failed merely for having few days. Every real SimpleTests scenario has over 3,600 days, so the guard always applies in practice.</summary>
        public const int MinDaysForFractionGuard = 30;
    }

    /// <summary>How a metric is compared: on its daily level, on the sum of two daily levels, or on its day-to-day change (for cumulative quantities like storage).</summary>
    public enum ComparisonMode
    {
        /// <summary>Compare the metric's value on each day directly.</summary>
        Level,

        /// <summary>Compare the sum of two daily values, used where the split between two reported columns is a labelling convention but their total is physically meaningful.</summary>
        SumOfTwoLevels,

        /// <summary>Compare the day-to-day change (RODIS change field vs Fortran Delta-Store). A constant carried offset cancels, so only genuinely new divergence is flagged.</summary>
        CumulativeChange,

        /// <summary>Compare the difference between two daily values, used to isolate a component of a reported total that both engines agree on, independently of how each labels its parts.</summary>
        DifferenceOfTwoLevels,
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

        /// <summary>Gets the absolute tolerance for this metric. Default 2e-3 absorbs the Fortran 3-decimal (0.000) display rounding: each value carries up to 0.0005 of rounding error,
        /// and the reported difference is a subtraction of two independently rounded quantities, so a genuine match can still show up to about 0.002.</summary>
        public double AbsTolerance { get; init; } = 2e-3;

        /// <summary>Gets the comparison mode (Level by default).</summary>
        public ComparisonMode Mode { get; init; } = ComparisonMode.Level;

        /// <summary>Gets which documented differences are treated as explained (flagged) rather than failed.</summary>
        public ExplainedBy Explained { get; init; } = ExplainedBy.Spill;

        /// <summary>Gets the zero-based Fortran column used as the second term when Mode is SumOfTwoLevels, or the Delta-Store column when Mode is CumulativeChange (-1 otherwise).</summary>
        public int SecondFdyColumnIndex { get; init; } = -1;

        /// <summary>Gets the one-based RODIS field used as the second term when Mode is SumOfTwoLevels, or the change-in-storage field when Mode is CumulativeChange (-1 otherwise).</summary>
        public int SecondResFieldNumber { get; init; } = -1;

        /// <summary>Gets a value indicating whether this metric is reported for information only and never fails. Used where the quantity's definition differs between the two engines by
        /// reporting convention rather than by water balance, so the difference is real but not a defect. The invariant combination is asserted by a separate metric.</summary>
        public bool IsInformational { get; init; }

        /// <summary>Gets a short note explaining why an informational metric cannot be asserted directly; included in the reported output.</summary>
        public string InformationalNote { get; init; } = string.Empty;
    }

    /// <summary>Holds the metrics compared for each scenario, plus the column/field indices used to classify spill, demand and storage-offset days.</summary>
    public static class LegacyStediMetrics
    {
        /// <summary>Fortran .fdy column order: 0 Q-impact, 1 Q-NoDams, 2 Q-wfill, 3 Q-climate, 4 Q-demand, 5 Q-spill, 6 Delta-Store, 7 Store end, 8 Q-bypass, 9 Q-unimpound, 10 Q-WithDams.</summary>
        public const int FdyColumnCount = 11;

        /// <summary>Gets the Fortran .fdy column index of Q-spill, used (with the RODIS spill field) to classify spill-active days.</summary>
        public const int FdySpillColumnIndex = 5;

        /// <summary>Gets the RODIS .res.csv field number of Spill Downstream Flow, used (with the Fortran Q-spill) to classify spill-active days.</summary>
        public const int ResSpillFieldNumber = 8;

        /// <summary>Gets the Fortran .fdy column index of Q-bypass.</summary>
        public const int FdyBypassColumnIndex = 8;

        /// <summary>Gets the RODIS .res.csv field number of Bypass Downstream Flow.</summary>
        public const int ResBypassFieldNumber = 11;

        /// <summary>Gets the Fortran .fdy column index of Q-demand, used to flag days where demand differs.</summary>
        public const int FdyDemandColumnIndex = 4;

        /// <summary>Gets the RODIS .res.csv field number of Demand Volume Extracted, used to flag days where demand differs.</summary>
        public const int ResDemandFieldNumber = 7;

        /// <summary>Gets the Fortran .fdy column index of Store-end, used to flag days where a carried storage-level offset is present.</summary>
        public const int FdyStorageColumnIndex = 7;

        /// <summary>Gets the RODIS .res.csv field number of Storage Volume End of Timestep, used to flag days where a carried storage-level offset is present.</summary>
        public const int ResStorageFieldNumber = 10;

        /// <summary>Gets the metrics compared between the Fortran reference and the RODIS output.</summary>
        public static IReadOnlyList<MetricSpec> All { get; } = new List<MetricSpec>
        {
            new MetricSpec { Name = "Impact",         FdyColumnIndex = 0,  ResFieldNumber = 1  },
            new MetricSpec { Name = "Unimpacted",     FdyColumnIndex = 1,  ResFieldNumber = 4  },
            new MetricSpec { Name = "Climate",        FdyColumnIndex = 3,  ResFieldNumber = 6  },
            // Demand carries the documented day-1 demand-timing offset (RODIS applies demand from day 2, Fortran from day 1). Once storage is offset,
            // availability-limited demand can differ, so a demand difference is treated as explained whenever a storage-level offset is present (or on spill days).
            new MetricSpec { Name = "Demand",         FdyColumnIndex = 4,  ResFieldNumber = 7, Explained = ExplainedBy.SpillOrStorageOffset },

            // Spills and Bypass are INFORMATIONAL. RODIS reports water released through an upstream dam's bypass as bypass for its whole journey, whereas legacy STEDI
            // re-labels it as spill once it leaves a downstream dam. The split is a reporting convention; the physically meaningful quantity is their sum, asserted below.
            new MetricSpec { Name = "Spills",         FdyColumnIndex = 5,  ResFieldNumber = 8,  IsInformational = true,
                             InformationalNote = "spill/bypass split is a reporting convention; SpillAndBypass carries the assertion" },
            new MetricSpec { Name = "Bypass",         FdyColumnIndex = 8,  ResFieldNumber = 11, IsInformational = true,
                             InformationalNote = "spill/bypass split is a reporting convention; SpillAndBypass carries the assertion" },
            // Total flow arriving at the outlet from dams, computed as downstream flow minus local catchment inflow. This is invariant to how each engine splits that water between
            // its spill and bypass columns, so it asserts the physically meaningful quantity while leaving the labelling difference to the two informational metrics above.
            new MetricSpec { Name = "DamReleasedFlow", FdyColumnIndex = 10, ResFieldNumber = 13,
                             Mode = ComparisonMode.DifferenceOfTwoLevels, SecondFdyColumnIndex = 9, SecondResFieldNumber = 12 },

            // Storage is CUMULATIVE, so the day-1 demand offset leaves a permanent carried offset. Comparing the daily CHANGE (RODIS field 9 vs Fortran
            // Delta-Store) cancels that constant offset; only a genuinely new, unexplained daily change fails.
            // The carried storage-level offset is reported separately and bounded by ExplainedGuards.MaxCarriedStorageOffsetML.
            new MetricSpec { Name = "Storage",        FdyColumnIndex = 7,  ResFieldNumber = 10,
                             Mode = ComparisonMode.CumulativeChange, SecondFdyColumnIndex = 6, SecondResFieldNumber = 9, Explained = ExplainedBy.SpillOrDemandDiff },
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

    /// <summary>Outcome for a single metric.</summary>
    public enum MetricTier
    {
        /// <summary>All days within tolerance.</summary>
        Pass,

        /// <summary>Exceedances occur only on explained days AND stay within the ExplainedGuards bounds; flagged for investigation, not treated as a failure.</summary>
        PassWithSpillDiffs,

        /// <summary>Exceedances are all on explained days but occur on too large a fraction of the record to be credible as an edge case; treated as a failure.</summary>
        FailExcessiveExplained,

        /// <summary>At least one exceedance occurs on an unexplained day; treated as a genuine regression.</summary>
        Fail,

        /// <summary>Differences are reported for information only, because the quantity's split between columns is a reporting convention rather than a water-balance difference.</summary>
        Informational,
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

        /// <summary>Gets the total number of days on which this metric exceeded its tolerance, whether explained or not.</summary>
        public int TotalExceedances => this.SpillExceedances + this.NonSpillExceedances;

        /// <summary>Gets the fraction of compared days on which this metric was flagged as an explained exceedance.</summary>
        public double ExplainedDayFraction => DayCount > 0 ? (double)SpillExceedances / DayCount : 0.0;

        /// <summary>Gets the date of the worst (largest absolute) difference, or null if the metric matched exactly.</summary>
        public DateOnly? WorstDate { get; init; }

        /// <summary>Gets the outcome for this metric.</summary>
        public MetricTier Tier { get; init; }

        /// <summary>Gets the maximum carried storage-level offset (|RODIS - Fortran| on the level) for a CumulativeChange metric; 0 for other metrics.</summary>
        public double CumulativeOffsetMax { get; init; }
    }

    /// <summary>Aggregated comparison result for one scenario across all metrics.</summary>
    public sealed class ScenarioResult
    {
        /// <summary>Gets the scenario number (1-50).</summary>
        public int Scenario { get; init; }

        /// <summary>Gets the per-metric results.</summary>
        public IReadOnlyList<MetricResult> Metrics { get; init; } = Array.Empty<MetricResult>();

        /// <summary>Gets the number of overlapping days shared by the two series.</summary>
        public int OverlapDays { get; init; }

        /// <summary>Gets the largest carried storage-level offset reported by any metric, in ML.</summary>
        public double MaxCarriedStorageOffset => Metrics.Count > 0 ? Metrics.Max(m => m.CumulativeOffsetMax) : 0.0;

        /// <summary>Gets a value indicating whether the carried storage offset exceeds the bound in ExplainedGuards, which indicates a cause beyond the documented day-1 timing offset.</summary>
        public bool HasExcessiveCarriedOffset => MaxCarriedStorageOffset > ExplainedGuards.MaxCarriedStorageOffsetML;

        /// <summary>Gets a value indicating whether any metric failed outright or was escalated for flagging too large a fraction of the record.</summary>
        public bool HasFailure => Metrics.Any(m => m.Tier == MetricTier.Fail || m.Tier == MetricTier.FailExcessiveExplained) || this.HasExcessiveCarriedOffset;

        /// <summary>Gets a value indicating whether any metric was flagged with explained (spill / known) differences within the permitted bounds.</summary>
        public bool HasSpillDiffs => Metrics.Any(m => m.Tier == MetricTier.PassWithSpillDiffs);

        /// <summary>Gets a short human-readable summary of why the scenario failed, or an empty string when it did not.</summary>
        public string FailureSummary
        {
            get
            {
                List<string> reasons = new List<string>();
                foreach (MetricResult m in this.Metrics.Where(x => x.Tier == MetricTier.Fail))
                    reasons.Add($"{m.Name}: {m.NonSpillExceedances} unexplained day(s), max_abs={m.MaxAbs:0.###e+00} on {m.WorstDate:yyyy-MM-dd}");
                foreach (MetricResult m in this.Metrics.Where(x => x.Tier == MetricTier.FailExcessiveExplained))
                    reasons.Add($"{m.Name}: flagged on {m.ExplainedDayFraction:P0} of days ({m.SpillExceedances}/{m.DayCount}), above the {ExplainedGuards.MaxExplainedDayFraction:P0} limit, max_abs={m.MaxAbs:0.###e+00}");
                if (this.HasExcessiveCarriedOffset)
                    reasons.Add($"carried storage offset {this.MaxCarriedStorageOffset:0.###} ML exceeds the {ExplainedGuards.MaxCarriedStorageOffsetML:0.###} ML limit");
                return string.Join("; ", reasons);
            }
        }
    }

    /// <summary>Compares a Fortran reference series against a RODIS output series metric-by-metric, applying spill-aware classification, cumulative-change handling and the ExplainedGuards bounds.</summary>
    public static class LegacyStediComparer
    {
        /// <summary>Compares the Fortran .fdy series against the RODIS .res.csv series for all metrics, classifying each metric and applying the explained-difference bounds.</summary>
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
                demandDiffersOn[day] = rRow.TryGetValue(LegacyStediMetrics.ResDemandFieldNumber, out double rDem)
                                    && Math.Abs(fRow[LegacyStediMetrics.FdyDemandColumnIndex] - rDem) > 1e-3;
                storageOffsetOn[day] = rRow.TryGetValue(LegacyStediMetrics.ResStorageFieldNumber, out double rStor)
                                    && Math.Abs(fRow[LegacyStediMetrics.FdyStorageColumnIndex] - rStor) > 1e-3;
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

                    // Choose the compared quantity: the daily level, the sum of two levels, or the day-to-day change for a cumulative metric.
                    double diff;
                    if (metric.Mode == ComparisonMode.CumulativeChange)
                    {
                        if (!rRow.TryGetValue(metric.SecondResFieldNumber, out double rDelta)) continue;
                        diff = Math.Abs(fRow[metric.SecondFdyColumnIndex] - rDelta);
                        // Track the carried storage-LEVEL offset (the difference the change-based comparison deliberately cancels), bounded by ExplainedGuards.
                        if (rRow.TryGetValue(metric.ResFieldNumber, out double rLevel))
                            cumulativeOffsetMax = Math.Max(cumulativeOffsetMax, Math.Abs(fRow[metric.FdyColumnIndex] - rLevel));
                    }
                    else if (metric.Mode == ComparisonMode.SumOfTwoLevels)
                    {
                        if (!rRow.TryGetValue(metric.ResFieldNumber, out double rFirst)) continue;
                        if (!rRow.TryGetValue(metric.SecondResFieldNumber, out double rSecond)) continue;
                        diff = Math.Abs((fRow[metric.FdyColumnIndex] + fRow[metric.SecondFdyColumnIndex]) - (rFirst + rSecond));
                    }
                    else if (metric.Mode == ComparisonMode.DifferenceOfTwoLevels)
                    {
                        if (!rRow.TryGetValue(metric.ResFieldNumber, out double rFirst)) continue;
                        if (!rRow.TryGetValue(metric.SecondResFieldNumber, out double rSecond)) continue;
                        diff = Math.Abs((fRow[metric.FdyColumnIndex] - fRow[metric.SecondFdyColumnIndex]) - (rFirst - rSecond));
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

                // Classify. Informational metrics report their statistics but never fail, because their definition differs between the engines by reporting convention.
                double explainedFraction = dayCount > 0 ? (double)spillExceed / dayCount : 0.0;
                MetricTier tier;
                if (metric.IsInformational) tier = MetricTier.Informational;
                else if (maxAbs <= metric.AbsTolerance) tier = MetricTier.Pass;
                else if (nonSpillExceed > 0) tier = MetricTier.Fail;
                else if (dayCount >= ExplainedGuards.MinDaysForFractionGuard && explainedFraction > ExplainedGuards.MaxExplainedDayFraction) tier = MetricTier.FailExcessiveExplained;
                else tier = MetricTier.PassWithSpillDiffs;

                metricResults.Add(new MetricResult
                {
                    Name = metric.Name,
                    DayCount = dayCount,
                    MaxAbs = maxAbs,
                    Rmse = dayCount > 0 ? Math.Sqrt(sumSquares / dayCount) : 0.0,
                    SpillExceedances = spillExceed,
                    NonSpillExceedances = nonSpillExceed,
                    WorstDate = worst,
                    Tier = tier,
                    CumulativeOffsetMax = cumulativeOffsetMax,
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
        /// <returns>True if the difference is explained; false if it is a genuine failure.</returns>
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
