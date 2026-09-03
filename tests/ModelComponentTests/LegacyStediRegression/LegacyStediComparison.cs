// ============================================================================
// LegacyStediComparison.cs
//   Core parsers and comparison logic for validating RODIS legacy-STEDI runs
//   against the Fortran STEDI 1.2 (SKM, 2012) reference outputs.
//
//   Primary reference : Fortran STEDI 1.2 .fdy water-balance file (long-term truth).
//   Secondary (optional): March STEDI2025 .res.csv (interim regression aid only).
//
//   ACCEPTED DIFFERENCES
//   Three differences between the engines are deliberate and documented, so the
//   comparison reports them without failing:
//
//   1. Spill / bypass labelling. Fortran Q-bypass is the raw volume released
//      through any dam's bypass, including water that then flows into another
//      dam, and Fortran excludes it from Q-WithDams; with an upstream bypass it
//      therefore breaks its own identity Q-WithDams = Q-spill + Q-bypass +
//      Q-unimpound. RODIS reports bypass water that reaches the outlet, tracked
//      through the network. Spills and Bypass are reported as informational and
//      the convention-invariant DamReleasedFlow carries the assertion.
//
//   2. Winterfill timing. RODIS limits pumping to the room available at the
//      START of the timestep; Fortran resolves the water balance within the step
//      and tops the dam up using room created during the day. RODIS therefore
//      pumps up to one day's volume less when a full dam begins to draw down,
//      and will pump into a dam that is already spilling. This is an accepted
//      simplification: it avoids an implicit solver and the effect is about 0.1%
//      of capacity. The daily difference is bounded by one day of pumping by
//      construction, so a per-day assertion could never fail and Winterfill is
//      reported as informational. Sensitivity is kept by asserting the TOTAL
//      pumped volume instead (see ExplainedGuards.MaxWinterfillVolumeDifference),
//      which a real defect such as pumping in the wrong season would change.
//
//   3. Day-1 demand timing. RODIS applies demand from day 2, Fortran from day 1.
//      Storage is compared on its daily CHANGE so this cumulative offset cancels;
//      the carried level offset is reported and bounded separately.
//
//   GUARD RAILS (see ExplainedGuards): the explained allowance is deliberately
//   bounded. If a metric is flagged on more than MaxExplainedDayFraction of days,
//   or the carried storage offset exceeds MaxCarriedStorageOffsetML, the result
//   is escalated to a failure.
// ============================================================================

using System.Globalization;
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

        /// <summary>Gets the maximum carried storage-level offset (ML) tolerated before the result is escalated to a failure. Calibrated at 0.5 ML: after the days-in-month fix the passing
        /// scenarios carry 0.0006-0.0024 ML, so this bound leaves ample headroom while still catching a systematic divergence.</summary>
        public const double MaxCarriedStorageOffsetML = 0.5;

        /// <summary>Gets the largest relative difference tolerated between the two engines' TOTAL pumped winterfill volume over the scenario. The accepted start-of-step versus
        /// end-of-step timing difference costs about one day of pumping each time a full dam begins to draw down. That cost grows with the number of dams: it is 0.16% of the
        /// total in the single-dam scenario 21 and 0.35% in scenario 25, but reaches 1.98% in the four-dam scenario 43 and 2.19% in scenario 44. A 5% bound covers the observed
        /// range while still catching a gross error such as pumping in the wrong season or at the wrong rate.</summary>
        public const double MaxWinterfillVolumeDifference = 0.05;

        /// <summary>Gets the minimum number of compared days before the flagged-day fraction is meaningful enough to enforce. Below this the fraction guard is skipped, so a short
        /// diagnostic series is not failed merely for having few days. Every real SimpleTests scenario has over 3,600 days, so the guard always applies in practice.</summary>
        public const int MinDaysForFractionGuard = 30;
    }

    /// <summary>Describes a scenario that fails because of a defect in the Fortran STEDI 1.2 executable rather than in RODIS, together with the envelope within which that failure is tolerated.</summary>
    public sealed class KnownFortranDefect
    {
        /// <summary>Gets the scenario number affected.</summary>
        public int Scenario { get; init; }

        /// <summary>Gets a short description of the defect and the RODIS behaviour it is measured against.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>Gets the section of the STEDI user manual that documents the behaviour RODIS implements, which the executable does not.</summary>
        public string ManualReference { get; init; } = string.Empty;

        /// <summary>Gets the names of the metrics expected to fail. A failure on any metric outside this set is a new problem and is not tolerated.</summary>
        public IReadOnlySet<string> ExpectedFailingMetrics { get; init; } = new HashSet<string>();

        /// <summary>Gets the largest absolute daily difference tolerated on the expected metrics, in ML. A larger difference means the defect has worsened or something else has changed.</summary>
        public double MaxMetricDifferenceML { get; init; }

        /// <summary>Gets the largest carried storage offset tolerated for the scenario, in ML.</summary>
        public double MaxCarriedStorageOffsetML { get; init; }
    }

    /// <summary>Registry of the scenarios whose failure is caused by a documented defect in the Fortran STEDI 1.2 executable, where RODIS follows the user manual and the executable does not.</summary>
    public static class KnownFortranDefects
    {
        private static readonly Dictionary<int, KnownFortranDefect> Defects = new List<KnownFortranDefect>
        {
            new KnownFortranDefect
            {
                Scenario = 3,
                Description = "Fortran interpolates the twelve monthly demand proportions into a smooth daily curve, with an apparent one-month phase lag, so a month does not deliver its "
                            + "stated share of annual demand. RODIS applies the step function the manual specifies, dividing each month's volume by the actual number of days in that month.",
                ManualReference = "STEDI user manual section 6.4: the demand proportions result in a step function at each change of month.",
                ExpectedFailingMetrics = new HashSet<string> { "Impact", "Demand", "DamReleasedFlow", "Storage", "DownstreamFlow" },
                MaxMetricDifferenceML = 0.25,          // observed worst 0.186 ML (Demand and Storage)
                MaxCarriedStorageOffsetML = 8.0,       // observed 6.742 ML
            },
            new KnownFortranDefect
            {
                Scenario = 48,
                Description = "Fortran applies no low-flow bypass at all when dams are entered as a volume distribution, releasing 0 ML across the run where the scenario specifies "
                            + "0.16 ML/day through the Jul-Oct season. RODIS applies the bypass as specified. Fortran's own water balance closes with Q-bypass = 0 on every day, "
                            + "so the release is genuinely absent rather than merely unreported.",
                ManualReference = "STEDI user manual section 5.1 lists low-flow bypasses as available when entering dam details as a distribution, and section 4.4 defines the "
                                + "ML/day/km2 capacity, season and minimum dam size that this scenario supplies.",
                ExpectedFailingMetrics = new HashSet<string> { "Impact", "DamReleasedFlow", "Storage", "DownstreamFlow" },
                MaxMetricDifferenceML = 0.80,          // observed worst 0.631 ML (DamReleasedFlow)
                MaxCarriedStorageOffsetML = 40.0,      // observed 32.32 ML
            },
        }.ToDictionary(d => d.Scenario);

        /// <summary>Returns the documented defect for a scenario, if one is registered.</summary>
        /// <param name="scenario">Scenario number.</param>
        /// <param name="defect">The registered defect, or null.</param>
        /// <returns>True when the scenario has a documented Fortran defect.</returns>
        public static bool TryGet(int scenario, out KnownFortranDefect? defect) => Defects.TryGetValue(scenario, out defect);

        /// <summary>Checks a scenario result against its documented defect envelope and returns the reasons it falls outside, if any. An empty list means the scenario fails only in the
        /// documented way and is therefore acceptable.</summary>
        /// <param name="defect">The documented defect for this scenario.</param>
        /// <param name="result">The comparison result to check.</param>
        /// <returns>List of reasons the result exceeds the envelope; empty when it does not.</returns>
        public static IReadOnlyList<string> BreachesEnvelope(KnownFortranDefect defect, ScenarioResult result)
        {
            List<string> breaches = new List<string>();

            foreach (MetricResult metric in result.Metrics)
            {
                bool failing = metric.Tier == MetricTier.Fail || metric.Tier == MetricTier.FailExcessiveExplained;
                if (failing && !defect.ExpectedFailingMetrics.Contains(metric.Name))
                {
                    breaches.Add($"{metric.Name} now fails but is not part of the documented defect (max_abs={metric.MaxAbs:0.###e+00} on {metric.WorstDate:yyyy-MM-dd})");
                }
                else if (failing && metric.MaxAbs > defect.MaxMetricDifferenceML)
                {
                    breaches.Add($"{metric.Name} differs by {metric.MaxAbs:0.###} ML, beyond the {defect.MaxMetricDifferenceML:0.###} ML documented for this defect");
                }
            }

            if (result.MaxCarriedStorageOffset > defect.MaxCarriedStorageOffsetML)
            {
                breaches.Add($"carried storage offset {result.MaxCarriedStorageOffset:0.###} ML is beyond the {defect.MaxCarriedStorageOffsetML:0.###} ML documented for this defect");
            }

            return breaches;
        }
    }

    /// <summary>How a metric is compared: on its daily level, on the sum or difference of two daily levels, or on its day-to-day change (for cumulative quantities like storage).</summary>
    public enum ComparisonMode
    {
        /// <summary>Compare the metric's value on each day directly.</summary>
        Level,

        /// <summary>Compare the difference between two daily values, used to isolate a component of a reported total that both engines agree on, independently of how each labels its parts.</summary>
        DifferenceOfTwoLevels,

        /// <summary>Compare the day-to-day change (RODIS change field vs Fortran Delta-Store). A constant carried offset cancels, so only genuinely new divergence is flagged.</summary>
        CumulativeChange,
    }

    /// <summary>Which documented, understood differences count as "explained" (flagged for investigation) rather than a failure. Values are flags so a metric can accept more than one cause.</summary>
    [Flags]
    public enum ExplainedBy
    {
        /// <summary>Nothing is excused; any exceedance fails.</summary>
        None = 0,

        /// <summary>Explained on spill-active days, where the two engines cascade spill differently.</summary>
        Spill = 1,

        /// <summary>Explained while a carried storage-level offset is present, e.g. demand, whose availability depends on how full the dam is.</summary>
        StorageOffset = 2,

        /// <summary>Explained on days where demand differs, e.g. the storage change, whose daily increment depends on demand.</summary>
        DemandDiff = 4,

        /// <summary>Explained on days where winterfill pumping differs, which follows from the accepted start-of-step versus end-of-step timing difference.</summary>
        WinterfillDiff = 8,
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

        /// <summary>Gets the zero-based Fortran column used as the second term when Mode is DifferenceOfTwoLevels, or the Delta-Store column when Mode is CumulativeChange (-1 otherwise).</summary>
        public int SecondFdyColumnIndex { get; init; } = -1;

        /// <summary>Gets the one-based RODIS field used as the second term when Mode is DifferenceOfTwoLevels, or the change-in-storage field when Mode is CumulativeChange (-1 otherwise).</summary>
        public int SecondResFieldNumber { get; init; } = -1;

        /// <summary>Gets a value indicating whether this metric is reported for information only and never fails. Used where the quantity's definition differs between the two engines by
        /// reporting convention rather than by water balance, so the difference is real but not a defect. The invariant combination is asserted by a separate metric.</summary>
        public bool IsInformational { get; init; }

    }

    /// <summary>Holds the metrics compared for each scenario, plus the column/field indices used to classify spill, demand, storage-offset and winterfill days.</summary>
    public static class LegacyStediMetrics
    {
        /// <summary>Fortran .fdy column order: 0 Q-impact, 1 Q-NoDams, 2 Q-wfill, 3 Q-climate, 4 Q-demand, 5 Q-spill, 6 Delta-Store, 7 Store end, 8 Q-bypass, 9 Q-unimpound, 10 Q-WithDams.</summary>
        public const int FdyColumnCount = 11;

        /// <summary>Gets the Fortran .fdy column index of Q-spill, used (with the RODIS spill field) to classify spill-active days.</summary>
        public const int FdySpillColumnIndex = 5;

        /// <summary>Gets the RODIS .res.csv field number of Spill Downstream Flow, used (with the Fortran Q-spill) to classify spill-active days.</summary>
        public const int ResSpillFieldNumber = 8;

        /// <summary>Gets the Fortran .fdy column index of Q-wfill, the winterfill pumped inflow.</summary>
        public const int FdyWinterfillColumnIndex = 2;

        /// <summary>Gets the RODIS .res.csv field number of Pumped Inflow, the winterfill equivalent.</summary>
        public const int ResWinterfillFieldNumber = 5;

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
            // Demand carries the documented day-1 timing offset. Once storage is offset, availability-limited demand can differ, so a demand difference is
            // treated as explained whenever a storage-level offset is present, or on spill days.
            new MetricSpec { Name = "Demand",         FdyColumnIndex = 4,  ResFieldNumber = 7, Explained = ExplainedBy.Spill | ExplainedBy.StorageOffset },

            // Winterfill differs by an accepted timing simplification: RODIS limits pumping to the room available at the START of the timestep, whereas Fortran resolves
            // within the step. On any given day the two rates each lie between zero and the scheme's rate, so the daily difference can never exceed one day of pumping and a
            // per-day assertion could never fail. Winterfill is therefore INFORMATIONAL, and sensitivity is kept by asserting the total pumped volume for the scenario.
            new MetricSpec { Name = "Winterfill",     FdyColumnIndex = 2,  ResFieldNumber = 5, IsInformational = true },

            // Spills and Bypass are INFORMATIONAL: the split between them is a reporting convention, not a water-balance difference. See the file header.
            new MetricSpec { Name = "Spills",         FdyColumnIndex = 5,  ResFieldNumber = 8,  IsInformational = true },
            new MetricSpec { Name = "Bypass",         FdyColumnIndex = 8,  ResFieldNumber = 11, IsInformational = true },
            // Total flow arriving at the outlet from dams, computed as downstream flow minus local catchment inflow. Invariant to how each engine splits that water
            // between its spill and bypass columns, so it asserts the physically meaningful quantity.
            new MetricSpec { Name = "DamReleasedFlow", FdyColumnIndex = 10, ResFieldNumber = 13,
                             Mode = ComparisonMode.DifferenceOfTwoLevels, SecondFdyColumnIndex = 9, SecondResFieldNumber = 12 },

            // Storage is CUMULATIVE, so the day-1 demand offset leaves a permanent carried offset. Comparing the daily CHANGE cancels it; only a genuinely new,
            // unexplained daily change fails. The daily change also moves whenever demand or winterfill differ, so both are accepted causes.
            new MetricSpec { Name = "Storage",        FdyColumnIndex = 7,  ResFieldNumber = 10,
                             Mode = ComparisonMode.CumulativeChange, SecondFdyColumnIndex = 6, SecondResFieldNumber = 9,
                             Explained = ExplainedBy.Spill | ExplainedBy.DemandDiff | ExplainedBy.WinterfillDiff },
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

        /// <summary>Gets the tolerance actually applied to this metric, which may be widened from MetricSpec.AbsTolerance by the one-day pumping bound.</summary>
        public double AppliedTolerance { get; init; }

        /// <summary>Gets the count of exceedance days that are explained (spill or another accepted cause).</summary>
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

        /// <summary>Gets the total winterfill volume pumped by the Fortran reference over the scenario, in ML; zero when the scenario has no winterfill pumping.</summary>
        public double FortranWinterfillVolume { get; init; }

        /// <summary>Gets the total winterfill volume pumped by RODIS over the scenario, in ML; zero when the scenario has no winterfill pumping.</summary>
        public double RodisWinterfillVolume { get; init; }

        /// <summary>Gets the relative difference between the two engines' total pumped winterfill volume, or zero when neither pumps. This is the sensitive check on winterfill,
        /// because the daily difference is bounded by one day of pumping by construction and so cannot be asserted directly.</summary>
        public double WinterfillVolumeDifference =>
            this.FortranWinterfillVolume > 0.0 ? Math.Abs(this.FortranWinterfillVolume - this.RodisWinterfillVolume) / this.FortranWinterfillVolume : 0.0;

        /// <summary>Gets a value indicating whether the total pumped winterfill volume differs by more than the accepted timing difference can explain.</summary>
        public bool HasExcessiveWinterfillVolumeDifference => this.WinterfillVolumeDifference > ExplainedGuards.MaxWinterfillVolumeDifference;

        /// <summary>Gets the largest carried storage-level offset reported by any metric, in ML.</summary>
        public double MaxCarriedStorageOffset => Metrics.Count > 0 ? Metrics.Max(m => m.CumulativeOffsetMax) : 0.0;

        /// <summary>Gets a value indicating whether the carried storage offset exceeds the bound in ExplainedGuards, which indicates a cause beyond the documented day-1 timing offset.</summary>
        public bool HasExcessiveCarriedOffset => MaxCarriedStorageOffset > ExplainedGuards.MaxCarriedStorageOffsetML;

        /// <summary>Gets a value indicating whether any metric failed outright or was escalated for flagging too large a fraction of the record.</summary>
        public bool HasFailure => Metrics.Any(m => m.Tier == MetricTier.Fail || m.Tier == MetricTier.FailExcessiveExplained)
                               || this.HasExcessiveCarriedOffset || this.HasExcessiveWinterfillVolumeDifference;

        /// <summary>Gets a value indicating whether any metric was flagged with explained differences within the permitted bounds.</summary>
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
                if (this.HasExcessiveWinterfillVolumeDifference)
                    reasons.Add($"total winterfill volume differs by {this.WinterfillVolumeDifference:P2} (Fortran {this.FortranWinterfillVolume:0.###} ML, "
                              + $"RODIS {this.RodisWinterfillVolume:0.###} ML), above the {ExplainedGuards.MaxWinterfillVolumeDifference:P0} limit");
                return string.Join("; ", reasons);
            }
        }
    }

    /// <summary>Compares a Fortran reference series against a RODIS output series metric-by-metric, applying the accepted-difference rules, the one-day pumping bound and the ExplainedGuards limits.</summary>
    public static class LegacyStediComparer
    {
        /// <summary>Compares the Fortran .fdy series against the RODIS .res.csv series for all metrics, classifying each metric and applying the explained-difference bounds.</summary>
        /// <param name="scenario">Scenario number (1-50) for reporting.</param>
        /// <param name="fortran">Fortran reference series (date -&gt; eleven column values).</param>
        /// <param name="rodis">RODIS output series (date -&gt; {field number -&gt; value}).</param>
        /// <returns>Aggregated scenario result. Each metric is judged against its own tolerance, comparison mode and explained-difference rules.</returns>
        public static ScenarioResult Compare(int scenario, IReadOnlyDictionary<DateOnly, double[]> fortran,
                                             IReadOnlyDictionary<DateOnly, Dictionary<int, double>> rodis)
        {
            List<DateOnly> commonDays = fortran.Keys.Where(rodis.ContainsKey).OrderBy(d => d).ToList();

            // Pre-compute the per-day flags that classify whether a difference is explained, and the largest winterfill rate either engine applies.
            Dictionary<DateOnly, ExplainedBy> causesOn = new Dictionary<DateOnly, ExplainedBy>(commonDays.Count);
            double fortranWinterfillVolume = 0.0, rodisWinterfillVolume = 0.0;
            foreach (DateOnly day in commonDays)
            {
                double[] fRow = fortran[day];
                Dictionary<int, double> rRow = rodis[day];

                double fWinterfill = fRow[LegacyStediMetrics.FdyWinterfillColumnIndex];
                rRow.TryGetValue(LegacyStediMetrics.ResWinterfillFieldNumber, out double rWinterfill);
                fortranWinterfillVolume += fWinterfill;
                rodisWinterfillVolume += rWinterfill;

                ExplainedBy causes = ExplainedBy.None;
                if (fRow[LegacyStediMetrics.FdySpillColumnIndex] > 1e-6
                    || (rRow.TryGetValue(LegacyStediMetrics.ResSpillFieldNumber, out double rSpill) && rSpill > 1e-6))
                    causes |= ExplainedBy.Spill;
                if (rRow.TryGetValue(LegacyStediMetrics.ResDemandFieldNumber, out double rDemand)
                    && Math.Abs(fRow[LegacyStediMetrics.FdyDemandColumnIndex] - rDemand) > 1e-3)
                    causes |= ExplainedBy.DemandDiff;
                if (rRow.TryGetValue(LegacyStediMetrics.ResStorageFieldNumber, out double rStorage)
                    && Math.Abs(fRow[LegacyStediMetrics.FdyStorageColumnIndex] - rStorage) > 1e-3)
                    causes |= ExplainedBy.StorageOffset;
                if (Math.Abs(fWinterfill - rWinterfill) > 1e-6)
                    causes |= ExplainedBy.WinterfillDiff;

                causesOn[day] = causes;
            }

            List<MetricResult> metricResults = new List<MetricResult>();
            foreach (MetricSpec metric in LegacyStediMetrics.All)
            {
                double tolerance = metric.AbsTolerance;

                double maxAbs = 0.0, sumSquares = 0.0, cumulativeOffsetMax = 0.0;
                int dayCount = 0, explainedExceed = 0, unexplainedExceed = 0;
                DateOnly? worst = null;

                foreach (DateOnly day in commonDays)
                {
                    double[] fRow = fortran[day];
                    Dictionary<int, double> rRow = rodis[day];

                    // Choose the compared quantity: the daily level, the difference of two levels, or the day-to-day change for a cumulative metric.
                    double diff;
                    if (metric.Mode == ComparisonMode.CumulativeChange)
                    {
                        if (!rRow.TryGetValue(metric.SecondResFieldNumber, out double rDelta)) continue;
                        diff = Math.Abs(fRow[metric.SecondFdyColumnIndex] - rDelta);
                        // Track the carried storage-LEVEL offset, the difference the change-based comparison deliberately cancels.
                        if (rRow.TryGetValue(metric.ResFieldNumber, out double rLevel))
                            cumulativeOffsetMax = Math.Max(cumulativeOffsetMax, Math.Abs(fRow[metric.FdyColumnIndex] - rLevel));
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

                    if (diff > tolerance)
                    {
                        if ((causesOn[day] & metric.Explained) != ExplainedBy.None) explainedExceed++;
                        else unexplainedExceed++;
                    }
                }

                // Classify. Informational metrics report their statistics but never fail, because their definition differs between the engines by reporting convention.
                double explainedFraction = dayCount > 0 ? (double)explainedExceed / dayCount : 0.0;
                MetricTier tier;
                if (metric.IsInformational) tier = MetricTier.Informational;
                else if (maxAbs <= tolerance) tier = MetricTier.Pass;
                else if (unexplainedExceed > 0) tier = MetricTier.Fail;
                else if (dayCount >= ExplainedGuards.MinDaysForFractionGuard && explainedFraction > ExplainedGuards.MaxExplainedDayFraction) tier = MetricTier.FailExcessiveExplained;
                else tier = MetricTier.PassWithSpillDiffs;

                metricResults.Add(new MetricResult
                {
                    Name = metric.Name,
                    DayCount = dayCount,
                    MaxAbs = maxAbs,
                    AppliedTolerance = tolerance,
                    Rmse = dayCount > 0 ? Math.Sqrt(sumSquares / dayCount) : 0.0,
                    SpillExceedances = explainedExceed,
                    NonSpillExceedances = unexplainedExceed,
                    WorstDate = worst,
                    Tier = tier,
                    CumulativeOffsetMax = cumulativeOffsetMax,
                });
            }

            return new ScenarioResult
            {
                Scenario = scenario,
                Metrics = metricResults,
                OverlapDays = commonDays.Count,
                FortranWinterfillVolume = fortranWinterfillVolume,
                RodisWinterfillVolume = rodisWinterfillVolume,
            };
        }
    }
}
