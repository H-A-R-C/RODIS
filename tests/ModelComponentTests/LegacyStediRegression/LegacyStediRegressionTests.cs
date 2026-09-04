// ============================================================================
// LegacyStediRegressionTests.cs
//   These tests require public RODIS outputs and separately held private
//   Fortran STEDI reference outputs. Configure their locations through:
//
//       RODIS_SIMPLETESTS_ROOT
//       RODIS_STEDI_REFERENCE_ROOT
//
//   When either location is unavailable, no data rows are generated.
// ============================================================================

using System.Globalization;
using System.Text;

namespace RODISUnitTests.LegacyStediRegression
{
    /// <summary>Regression tests comparing public RODIS SimpleTests outputs against private Fortran STEDI 1.2 reference outputs.</summary>
    public static class SimpleTestsPaths
    {
        /// <summary>Gets the public SimpleTests root configured through RODIS_SIMPLETESTS_ROOT.</summary>
        internal static readonly string? Root =
            Environment.GetEnvironmentVariable("RODIS_SIMPLETESTS_ROOT");

        /// <summary>Gets the private Fortran-reference root configured through RODIS_STEDI_REFERENCE_ROOT.</summary>
        internal static readonly string? StediReferenceRoot =
            Environment.GetEnvironmentVariable("RODIS_STEDI_REFERENCE_ROOT");

        /// <summary>Gets a value indicating whether both configured comparison-data roots are available.</summary>
        public static bool IsAvailable =>
            !string.IsNullOrWhiteSpace(Root)
            && !string.IsNullOrWhiteSpace(StediReferenceRoot)
            && Directory.Exists(Root)
            && Directory.Exists(StediReferenceRoot);

        /// <summary>Builds the scenario folder name for a scenario number, for example Scenario07.</summary>
        /// <param name="scenario">Scenario number (1-48).</param>
        /// <returns>Zero-padded scenario folder name.</returns>
        public static string Folder(int scenario) => $"Scenario{scenario:D2}";

        /// <summary>Returns the full path to the RODIS whole-catchment result for a scenario.</summary>
        /// <param name="scenario">Scenario number (1-48).</param>
        /// <returns>Full path to the RODIS result file.</returns>
        public static string RodisRes(int scenario)
        {
            if (string.IsNullOrWhiteSpace(Root))
            {
                throw new InvalidOperationException(
                    "RODIS_SIMPLETESTS_ROOT is not configured.");
            }

            return Path.Combine(
                Root,
                Folder(scenario),
                "RODISOutputs",
                $"RODIS_RunSTEDILegacyVersion_{Folder(scenario)}.res.csv");
        }

        /// <summary>Returns the full path to the private Fortran STEDI reference for a scenario.</summary>
        /// <param name="scenario">Scenario number (1-48).</param>
        /// <returns>Full path to the Fortran reference file.</returns>
        public static string FortranFdy(int scenario)
        {
            if (string.IsNullOrWhiteSpace(StediReferenceRoot))
            {
                throw new InvalidOperationException(
                    "RODIS_STEDI_REFERENCE_ROOT is not configured.");
            }

            return Path.Combine(
                StediReferenceRoot,
                Folder(scenario),
                $"LegacySTEDI_{Folder(scenario)}.fdy");
        }
    }

    /// <summary>Regression and validation tests comparing RODIS legacy-STEDI outputs against the Fortran STEDI 1.2 reference (primary) and March STEDI2025 (optional).
    /// All tests in this class require the 2_SimpleTests tree on the O: drive (or RODIS_SIMPLETESTS_ROOT).</summary>
    [TestClass]
    public class LegacyStediRegressionTests
    {
        private static readonly List<ScenarioResult> RollUp = new List<ScenarioResult>();

        /// <summary>Gets or sets the MSTest-injected test context (used to write per-scenario tables and the roll-up).</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Enumerates the scenarios that have BOTH a Fortran reference and a RODIS output present on disk, yielding one test case per scenario.
        /// Returns nothing when the SimpleTests root is unavailable, so the class simply reports no cases rather than throwing.</summary>
        /// <returns>Sequence of single-element object arrays containing the scenario number.</returns>
        public static IEnumerable<object[]> Scenarios()
        {
            if (!SimpleTestsPaths.IsAvailable) yield break;

            for (int n = 1; n <= 48; n++)
                if (File.Exists(SimpleTestsPaths.FortranFdy(n)) && File.Exists(SimpleTestsPaths.RodisRes(n)))
                    yield return new object[] { n };
        }

        /// <summary>Formats a scenario number into a readable test name (e.g. "Scenario 27").</summary>
        /// <param name="methodInfo">The test method.</param>
        /// <param name="data">The data row (scenario number).</param>
        /// <returns>Display name for the test case.</returns>
        public static string ScenarioName(System.Reflection.MethodInfo methodInfo, object[] data) => $"Scenario {(int)data[0]:D2}";

        /// <summary>PRIMARY test: compares each scenario's RODIS output against the Fortran STEDI 1.2 reference. Fails on an unexplained daily difference, on a metric flagged across
        /// more than ExplainedGuards.MaxExplainedDayFraction of the record, or on a carried storage offset beyond ExplainedGuards.MaxCarriedStorageOffsetML.</summary>
        /// <param name="scenario">Scenario number (1-48) supplied by the data source.</param>
        [DataTestMethod]
        [TestCategory(TestCategories.RequiresSimpleTestsData)]
        [DynamicData(nameof(Scenarios), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(ScenarioName))]
        public void Rodis_Matches_LegacyFortran(int scenario)
        {
            var fortran = LegacyStediFdyReader.Read(SimpleTestsPaths.FortranFdy(scenario));
            var rodis = RodisResCsvReader.Read(SimpleTestsPaths.RodisRes(scenario));
            ScenarioResult result = LegacyStediComparer.Compare(scenario, fortran, rodis);

            lock (RollUp) RollUp.Add(result);
            TestContext.WriteLine(FormatScenarioTable(result));

            if (KnownFortranDefects.TryGet(scenario, out KnownFortranDefect? defect) && defect != null)
            {
                // The scenario is expected to fail because the Fortran executable contradicts its own manual. Accept the failure only while it stays within the documented envelope,
                // so a new or worsening problem in the same scenario still breaks the build.
                IReadOnlyList<string> breaches = KnownFortranDefects.BreachesEnvelope(defect, result);
                TestContext.WriteLine($"Scenario {scenario:D2} has a documented Fortran defect. {defect.Description} Reference: {defect.ManualReference}");

                Assert.AreEqual(0, breaches.Count,
                    $"Scenario {scenario:D2} no longer fails only in the documented way: {string.Join("; ", breaches)}. "
                    + $"If this change is intended, update the envelope in KnownFortranDefects and record why.");
                return;
            }

            Assert.IsFalse(result.HasFailure, $"Scenario {scenario:D2} FAILED against Fortran STEDI 1.2: {result.FailureSummary}");
        }

        /// <summary>Writes the roll-up to the console and a timestamped CSV beside the SimpleTests root. Each metric contributes two columns: its outcome and its max_abs, so a flagged or
        /// informational metric can be judged on magnitude as well as on the number of days affected.</summary>
        [ClassCleanup]
        public static void WriteRollUp()
        {
            if (RollUp.Count == 0) return;

            IReadOnlyList<MetricSpec> metrics = LegacyStediMetrics.All;
            StringBuilder sb = new StringBuilder();
            sb.Append("scenario,overlap_days,result");
            foreach (MetricSpec m in metrics) sb.Append(CultureInfo.InvariantCulture, $",{m.Name}");
            foreach (MetricSpec m in metrics) sb.Append(CultureInfo.InvariantCulture, $",{m.Name}_max_abs");
            sb.AppendLine(",carried_storage_offset,winterfill_fortran_ML,winterfill_rodis_ML,winterfill_diff_pct,failure_reason");

            foreach (ScenarioResult r in RollUp.OrderBy(x => x.Scenario))
            {
                bool isKnownDefect = KnownFortranDefects.TryGet(r.Scenario, out KnownFortranDefect? knownDefect) && knownDefect != null
                                  && KnownFortranDefects.BreachesEnvelope(knownDefect, r).Count == 0;
                string overall = isKnownDefect ? "KNOWN(fortran)" : r.HasFailure ? "FAIL" : r.HasSpillDiffs ? "PASS(spill)" : "PASS";
                sb.Append(CultureInfo.InvariantCulture, $"{r.Scenario:D2},{r.OverlapDays},{overall}");

                foreach (MetricResult m in r.Metrics)
                {
                    string cell = m.Tier switch
                    {
                        MetricTier.Pass => "PASS",
                        MetricTier.PassWithSpillDiffs => $"spill:{m.SpillExceedances}",
                        MetricTier.FailExcessiveExplained => $"EXCESS:{m.SpillExceedances}({m.ExplainedDayFraction:P0})",
                        MetricTier.Informational => $"info:{m.TotalExceedances}",
                        _ => $"FAIL:{m.NonSpillExceedances}",
                    };
                    sb.Append(CultureInfo.InvariantCulture, $",{cell}");
                }

                foreach (MetricResult m in r.Metrics) sb.Append(CultureInfo.InvariantCulture, $",{m.MaxAbs:0.###e+00}");

                sb.AppendLine(CultureInfo.InvariantCulture, $",{r.MaxCarriedStorageOffset:0.####},{r.FortranWinterfillVolume:0.###},"
                            + $"{r.RodisWinterfillVolume:0.###},{r.WinterfillVolumeDifference:P2},\"{r.FailureSummary}\"");
            }

            string csv = sb.ToString();

            try
            {
                if (!string.IsNullOrWhiteSpace(SimpleTestsPaths.Root))
                {
                    File.WriteAllText(
                        Path.Combine(
                            SimpleTestsPaths.Root,
                            $"LegacyStediRollUp_{DateTime.Now:yyyyMMdd_HHmmss}.csv"),
                        csv);
                }
            }
            catch (IOException)
            {
                // The CSV is best-effort; the console copy below always succeeds.
            }
            catch (UnauthorizedAccessException)
            {
                // Do not fail the run because the output location is read-only.
            }

            Console.WriteLine(Environment.NewLine + "===== Legacy STEDI roll-up (RODIS vs Fortran STEDI 1.2) =====" + Environment.NewLine + csv);
        }

        /// <summary>Builds a readable per-metric table for one scenario for the test output, reporting max_abs and the applied tolerance for every metric regardless of outcome.</summary>
        /// <param name="r">The scenario result to format.</param>
        /// <returns>Multi-line table string.</returns>
        private static string FormatScenarioTable(ScenarioResult r)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== Scenario {r.Scenario:D2}  (RODIS vs Fortran STEDI 1.2, {r.OverlapDays} overlapping days) ===");
            if (r.FortranWinterfillVolume > 0.0)
                sb.AppendLine($"    winterfill total: Fortran {r.FortranWinterfillVolume:0.###} ML, RODIS {r.RodisWinterfillVolume:0.###} ML, "
                            + $"differing by {r.WinterfillVolumeDifference:P2} (limit {ExplainedGuards.MaxWinterfillVolumeDifference:P0}).");
            sb.AppendLine($"{"Metric",-16}{"max_abs",12}{"tol",11}{"rmse",12}{"flagged",9}{"flag%",8}{"fails",7}{"carried",11}  {"worst",-12} tier");
            foreach (MetricResult m in r.Metrics)
            {
                string worst = m.WorstDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
                string carried = m.CumulativeOffsetMax > 0 ? m.CumulativeOffsetMax.ToString("0.###e+00", CultureInfo.InvariantCulture) : "-";
                sb.AppendLine($"{m.Name,-16}{m.MaxAbs,12:0.###e+00}{m.AppliedTolerance,11:0.###e+00}{m.Rmse,12:0.###e+00}{m.SpillExceedances,9}{m.ExplainedDayFraction,8:P0}{m.NonSpillExceedances,7}{carried,11}  {worst,-12} {m.Tier}");
            }
            if (r.HasFailure) sb.AppendLine($"FAILURE: {r.FailureSummary}");
            return sb.ToString();
        }
    }
}
