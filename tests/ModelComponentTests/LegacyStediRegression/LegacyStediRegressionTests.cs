// ============================================================================
// LegacyStediRegressionTests.cs
//   MSTest fixture that validates RODIS legacy-STEDI runs against the Fortran
//   STEDI 1.2 (SKM, 2012) reference outputs for every SimpleTests scenario.
//
//   PRIMARY assertion    : RODIS .res.csv vs Fortran .fdy. Fails on an
//                          UNEXPLAINED exceedance, on a metric flagged across
//                          too large a fraction of the record, or on a carried
//                          storage offset beyond the ExplainedGuards bound.
//   SECONDARY diagnostic : RODIS .res.csv vs March STEDI2025 .res.csv, strict
//                          (~1e-6). March is an interim debugging aid, so a
//                          missing March file yields Inconclusive, not Fail.
//
//   EVERY test here reads the 2_SimpleTests tree from the O: drive, so all are
//   marked [TestCategory(TestCategories.RequiresSimpleTestsData)]. Exclude them
//   when the drive is not mapped, e.g.:
//       dotnet test --filter TestCategory!=RequiresSimpleTestsData
//
//   A roll-up (one row per scenario) is written on class cleanup.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RODISUnitTests.LegacyStediRegression
{
    /// <summary>Resolves the on-disk paths of the Fortran reference, RODIS output and optional March baseline for each SimpleTests scenario.</summary>
    public static class SimpleTestsPaths
    {
        /// <summary>Gets the SimpleTests root directory, overridable via the RODIS_SIMPLETESTS_ROOT environment variable for CI or local runs.</summary>
        public static string Root =>
            Environment.GetEnvironmentVariable("RODIS_SIMPLETESTS_ROOT") ?? @"O:\2. Technical\1. Software\RODIS\2_SimpleTests";

        /// <summary>Gets a value indicating whether the SimpleTests root directory is currently reachable (used to skip drive-dependent tests gracefully).</summary>
        public static bool IsAvailable => Directory.Exists(Root);

        /// <summary>Builds the scenario folder name for a scenario number (e.g. 7 -&gt; "Scenario07", 27 -&gt; "Scenario27").</summary>
        /// <param name="scenario">Scenario number (1-50).</param>
        /// <returns>Zero-padded scenario folder name.</returns>
        public static string Folder(int scenario) => $"Scenario{scenario:D2}";

        /// <summary>Returns the full path to the Fortran STEDI 1.2 whole-catchment .fdy reference for the scenario.</summary>
        /// <param name="scenario">Scenario number (1-50).</param>
        /// <returns>Full path to the Fortran .fdy file.</returns>
        public static string FortranFdy(int scenario) =>
            Path.Combine(Root, Folder(scenario), "1_OldSTEDI_outputs", $"LegacySTEDI_{Folder(scenario)}.fdy");

        /// <summary>Returns the full path to the RODIS whole-catchment .res.csv output for the scenario.</summary>
        /// <param name="scenario">Scenario number (1-50).</param>
        /// <returns>Full path to the RODIS .res.csv file.</returns>
        public static string RodisRes(int scenario) =>
            Path.Combine(Root, Folder(scenario), "2_NewSTEDI_outputs", $"RODIS_RunSTEDILegacyVersion_{Folder(scenario)}.res.csv");

        /// <summary>Returns the full path to the optional March STEDI2025 whole-catchment .res.csv baseline for the scenario.</summary>
        /// <param name="scenario">Scenario number (1-50).</param>
        /// <returns>Full path to the March .res.csv file (may not exist).</returns>
        public static string MarchRes(int scenario) =>
            Path.Combine(Root, Folder(scenario), "2_NewSTEDI_outputs", $"STEDI2025_{Folder(scenario)}.res.csv");
    }

    /// <summary>Regression and validation tests comparing RODIS legacy-STEDI outputs against the Fortran STEDI 1.2 reference (primary) and March STEDI2025 (optional).
    /// All tests in this class require the 2_SimpleTests tree on the O: drive (or RODIS_SIMPLETESTS_ROOT).</summary>
    [TestClass]
    public class LegacyStediRegressionTests
    {
        private const double AbsToleranceMarch = 1e-6;     // strict: RODIS should reproduce the interim March build almost exactly (per-metric Fortran tolerances live in LegacyStediMetrics)

        private static readonly List<ScenarioResult> RollUp = new List<ScenarioResult>();

        /// <summary>Gets or sets the MSTest-injected test context (used to write per-scenario tables and the roll-up).</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Enumerates the scenarios that have BOTH a Fortran reference and a RODIS output present on disk, yielding one test case per scenario.
        /// Returns nothing when the SimpleTests root is unavailable, so the class simply reports no cases rather than throwing.</summary>
        /// <returns>Sequence of single-element object arrays containing the scenario number.</returns>
        public static IEnumerable<object[]> Scenarios()
        {
            if (!SimpleTestsPaths.IsAvailable) yield break;

            for (int n = 1; n <= 50; n++)
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
        /// <param name="scenario">Scenario number (1-50) supplied by the data source.</param>
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

            Assert.IsFalse(result.HasFailure, $"Scenario {scenario:D2} FAILED against Fortran STEDI 1.2: {result.FailureSummary}");
        }

        /// <summary>SECONDARY diagnostic: compares each scenario's RODIS output against the March STEDI2025 baseline at strict tolerance.
        /// March is an interim debugging aid, not the long-term reference, so a missing March file yields Inconclusive rather than a failure.</summary>
        /// <param name="scenario">Scenario number (1-50) supplied by the data source.</param>
        [DataTestMethod]
        [TestCategory(TestCategories.RequiresSimpleTestsData)]
        [DynamicData(nameof(Scenarios), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(ScenarioName))]
        public void Rodis_Reproduces_March2025_Interim(int scenario)
        {
            string marchPath = SimpleTestsPaths.MarchRes(scenario);
            if (!File.Exists(marchPath)) Assert.Inconclusive($"No March STEDI2025 baseline for scenario {scenario:D2}; skipping interim regression check.");

            var rodis = RodisResCsvReader.Read(SimpleTestsPaths.RodisRes(scenario));
            var march = RodisResCsvReader.Read(marchPath);

            List<DateOnly> commonDays = rodis.Keys.Where(march.ContainsKey).OrderBy(d => d).ToList();
            double worst = 0.0; DateOnly? worstDate = null; string worstMetric = string.Empty;

            foreach (MetricSpec metric in LegacyStediMetrics.All)
            {
                foreach (DateOnly day in commonDays)
                {
                    if (rodis[day].TryGetValue(metric.ResFieldNumber, out double r) && march[day].TryGetValue(metric.ResFieldNumber, out double m))
                    {
                        double diff = Math.Abs(r - m);
                        if (diff > worst) { worst = diff; worstDate = day; worstMetric = metric.Name; }
                    }
                }
            }

            TestContext.WriteLine($"Scenario {scenario:D2}: max |RODIS - March| = {worst:0.###e+00} ({worstMetric} on {worstDate:yyyy-MM-dd}) over {commonDays.Count} days.");
            Assert.IsTrue(worst <= AbsToleranceMarch,
                $"Scenario {scenario:D2} diverged from the March interim build: {worstMetric} differs by {worst:0.###e+00} on {worstDate:yyyy-MM-dd} (tol {AbsToleranceMarch:0.###e+00}).");
        }

        /// <summary>Writes the roll-up (one row per scenario and metric outcome) to the console and a timestamped CSV beside the SimpleTests root.</summary>
        [ClassCleanup]
        public static void WriteRollUp()
        {
            if (RollUp.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("scenario,overlap_days,result," + string.Join(",", LegacyStediMetrics.All.Select(m => m.Name)) + ",carried_storage_offset,failure_reason");
            foreach (ScenarioResult r in RollUp.OrderBy(x => x.Scenario))
            {
                string overall = r.HasFailure ? "FAIL" : r.HasSpillDiffs ? "PASS(spill)" : "PASS";
                IEnumerable<string> cells = r.Metrics.Select(m => m.Tier switch
                {
                    MetricTier.Pass => "PASS",
                    MetricTier.PassWithSpillDiffs => $"spill:{m.SpillExceedances}",
                    MetricTier.FailExcessiveExplained => $"EXCESS:{m.SpillExceedances}({m.ExplainedDayFraction:P0})",
                    _ => $"FAIL:{m.NonSpillExceedances}",
                });
                sb.AppendLine($"{r.Scenario:D2},{r.OverlapDays},{overall}," + string.Join(",", cells) +
                              $",{r.MaxCarriedStorageOffset:0.####},\"{r.FailureSummary}\"");
            }

            string csv = sb.ToString();
            try { File.WriteAllText(Path.Combine(SimpleTestsPaths.Root, $"LegacyStediRollUp_{DateTime.Now:yyyyMMdd_HHmmss}.csv"), csv); }
            catch (IOException) { /* roll-up CSV is best-effort; the console copy below always succeeds */ }
            catch (UnauthorizedAccessException) { /* ditto: do not fail the run because the share is read-only */ }

            Console.WriteLine(Environment.NewLine + "===== Legacy STEDI roll-up (RODIS vs Fortran STEDI 1.2) =====" + Environment.NewLine + csv);
        }

        /// <summary>Builds a readable per-metric table for one scenario for the test output.</summary>
        /// <param name="r">The scenario result to format.</param>
        /// <returns>Multi-line table string.</returns>
        private static string FormatScenarioTable(ScenarioResult r)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== Scenario {r.Scenario:D2}  (RODIS vs Fortran STEDI 1.2, {r.OverlapDays} overlapping days) ===");
            sb.AppendLine($"{"Metric",-15}{"max_abs",12}{"rmse",12}{"flagged",9}{"flag%",8}{"fails",7}{"carried",11}  {"worst",-12} tier");
            foreach (MetricResult m in r.Metrics)
            {
                string worst = m.WorstDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
                string carried = m.CumulativeOffsetMax > 0 ? m.CumulativeOffsetMax.ToString("0.###e+00", CultureInfo.InvariantCulture) : "-";
                sb.AppendLine($"{m.Name,-15}{m.MaxAbs,12:0.###e+00}{m.Rmse,12:0.###e+00}{m.SpillExceedances,9}{m.ExplainedDayFraction,8:P0}{m.NonSpillExceedances,7}{carried,11}  {worst,-12} {m.Tier}");
            }
            if (r.HasFailure) sb.AppendLine($"FAILURE: {r.FailureSummary}");
            return sb.ToString();
        }
    }
}
