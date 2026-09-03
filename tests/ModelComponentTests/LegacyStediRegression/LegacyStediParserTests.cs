// ============================================================================
// LegacyStediParserTests.cs
//   Self-contained unit tests for the Fortran .fdy and RODIS .res.csv parsers,
//   the comparer and the ExplainedGuards bounds. These write tiny inline fixtures
//   to temporary files, so they need NO access to the O: drive and are safe to run
//   anywhere, including CI. All are marked
//   [TestCategory(TestCategories.SelfContained)].
//
//   They guard against the parsers silently breaking if the Source .res.csv layout
//   evolves (field count, EOH placement, header "N>..." format, missing values,
//   "-0" values) or if the .fdy column order changes, and they lock in the three
//   accepted differences (spill/bypass labelling, winterfill timing, day-1 demand
//   timing) together with the guard rails that stop a systematic divergence hiding
//   behind "explained" days.
//
//   NOTE: TinyFdy and TinyResCsv deliberately encode IDENTICAL values in the two
//   formats, so any difference the comparer reports is a genuine logic error.
// ============================================================================

using System.Globalization;
using System.Text;

namespace RODISUnitTests.LegacyStediRegression
{
    /// <summary>Unit tests for LegacyStediFdyReader, RodisResCsvReader, LegacyStediComparer and the ExplainedGuards bounds, using small inline fixtures written to temporary files.</summary>
    [TestClass]
    public class LegacyStediParserTests
    {
        private readonly List<string> tempFiles = new List<string>();

        /// <summary>Deletes every temporary fixture file created during a test.</summary>
        [TestCleanup]
        public void Cleanup()
        {
            foreach (string path in tempFiles)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { /* best-effort cleanup */ }
            }
            tempFiles.Clear();
        }

        /// <summary>Writes the given text to a new temporary file with the requested extension and registers it for cleanup.</summary>
        /// <param name="contents">File contents to write.</param>
        /// <param name="extension">File extension including the leading dot (e.g. ".fdy").</param>
        /// <returns>Full path to the temporary file.</returns>
        private string WriteTemp(string contents, string extension)
        {
            string path = Path.Combine(Path.GetTempPath(), $"rodis_selftest_{Guid.NewGuid():N}{extension}");
            File.WriteAllText(path, contents);
            tempFiles.Add(path);
            return path;
        }

        /// <summary>A three-day Fortran .fdy fixture: banner and '!' comment lines, the two-line header, then three daily rows (day 3 has a spill and net evaporation).</summary>
        private const string TinyFdy =
            "! \n" +
            "!   SSS  TTTTT EEEEE DDD   I\n" +
            "! Spatial Tool for the Estimation of Dam Impacts\n" +
            "! Version 1.20, September 2011\n" +
            "! ----------------------------------------\n" +
            "!                       |---------- WATER IN ---------|\n" +
            "!Day            Q-impact       Q-NoDams        Q-wfill      Q-climate       Q-demand        Q-spill    Delta-Store      Store end       Q-bypass    Q-unimpound     Q-WithDams\n" +
            "!YYYYMMDD         (ML/d)         (ML/d)         (ML/d)         (ML/d)         (ML/d)         (ML/d)         (ML/d)         (ML/d)         (ML/d)         (ML/d)         (ML/d)\n" +
            "19500101           0.100          1.000          0.000          0.000          0.069          0.000          0.031          0.031          0.000          0.900          0.900\n" +
            "19500102           0.100          1.000          0.000          0.000          0.069          0.000          0.031          0.063          0.000          0.900          0.900\n" +
            "19500103           0.250          1.000          0.000          0.010          0.069          0.500          0.031          0.094          0.000          0.900          0.750\n";

        /// <summary>The RODIS .res.csv metadata block and Date header shared by the inline fixtures, up to but excluding the EOH marker. Field order in the data rows is
        /// 1 Impact, 4 Unimpacted, 5 Pumped Inflow, 6 Net Rainfall, 7 Demand, 8 Spill, 9 Change in Storage, 10 Storage, 11 Bypass, 12 Local Inflow, 13 Downstream.</summary>
        private const string ResCsvPreamble =
            "File version,3\n" +
            "Missing data value,-9999\n" +
            "EOM\n" +
            "Project name,\n" +
            "Program,RODIS.Core 2.2.2026.903\n" +
            "Latest result run time,2026-09-03 09:22:09\n" +
            "Simulation time,1950-01-01 - 1950-04-10\n" +
            "Field,Units,RunName,ScenarioName,ScenarioInputSetName,Name,Site,ElementName,WaterFeatureType,ElementType,Structure,Custom\n" +
            "EOC\n" +
            "15\n" +
            "1,ML,,tiny,,Confluence: X: Impact,X,Impact,Confluence,Node,Impact,abc\n" +
            "4,ML,,tiny,,Confluence: X: Unimpacted Flow,X,Unimpacted Flow,Confluence,Node,Unimpacted Flow,abc\n" +
            "5,ML,,tiny,,Confluence: X: Pumped Inflow,X,Pumped Inflow,Confluence,Node,Pumped Inflow,abc\n" +
            "6,ML,,tiny,,Confluence: X: Net Rainfall Volume,X,Net Rainfall Volume,Confluence,Node,Net Rainfall Volume,abc\n" +
            "7,ML,,tiny,,Confluence: X: Demand Volume Extracted,X,Demand Volume Extracted,Confluence,Node,Demand Volume Extracted,abc\n" +
            "8,ML,,tiny,,Confluence: X: Spill Downstream Flow,X,Spill Downstream Flow,Confluence,Node,Spill Downstream Flow,abc\n" +
            "9,ML,,tiny,,Confluence: X: Change in Storage Volume,X,Change in Storage Volume,Confluence,Node,Change in Storage Volume,abc\n" +
            "10,ML,,tiny,,Confluence: X: Storage Volume End of Timestep,X,Storage Volume End of Timestep,Confluence,Node,Storage Volume End of Timestep,abc\n" +
            "11,ML,,tiny,,Confluence: X: Bypass Downstream Flow,X,Bypass Downstream Flow,Confluence,Node,Bypass Downstream Flow,abc\n" +
            "12,ML,,tiny,,Confluence: X: Local Catchment Inflow,X,Local Catchment Inflow,Confluence,Node,Local Catchment Inflow,abc\n" +
            "13,ML,,tiny,,Confluence: X: Downstream Flow,X,Downstream Flow,Confluence,Node,Downstream Flow,abc\n" +
            "Date,1>Confluence> X> Impact,4>Confluence> X> Unimpacted Flow,5>Confluence> X> Pumped Inflow,6>Confluence> X> Net Rainfall Volume,7>Confluence> X> Demand Volume Extracted,8>Confluence> X> Spill Downstream Flow,9>Confluence> X> Change in Storage Volume,10>Confluence> X> Storage Volume End of Timestep,11>Confluence> X> Bypass Downstream Flow,12>Confluence> X> Local Catchment Inflow,13>Confluence> X> Downstream Flow\n";

        /// <summary>A three-day RODIS .res.csv fixture encoding the SAME values as TinyFdy, exercising the EOM/EOC/EOH markers, the "N&gt;..." header, a "-0" value and ISO dates.</summary>
        private const string TinyResCsv = ResCsvPreamble +
            "EOH\n" +
            "1950-01-01,0.1,1,0,-0,0.069,0,0.031,0.031,0,0.9,0.9\n" +
            "1950-01-02,0.1,1,0,-0,0.069,0,0.031,0.063,0,0.9,0.9\n" +
            "1950-01-03,0.25,1,0,0.01,0.069,0.5,0.031,0.094,0,0.9,0.75\n";

        /// <summary>Builds a matching pair of multi-day .fdy and .res.csv fixtures, long enough for the flagged-day fraction guard to apply.
        /// Every day spills, so any perturbation counts as an explained difference; this isolates the fraction guard from the explained/unexplained logic.</summary>
        /// <param name="dayCount">Number of daily rows to generate.</param>
        /// <param name="perturbedDays">Number of leading days on which the RODIS Downstream Flow is shifted well beyond tolerance.</param>
        /// <returns>Tuple of (.fdy contents, .res.csv contents).</returns>
        private static (string Fdy, string ResCsv) BuildSpillySeries(int dayCount, int perturbedDays)
        {
            StringBuilder fdy = new StringBuilder("!Day  header line skipped by the reader\n");
            StringBuilder res = new StringBuilder(ResCsvPreamble + "EOH\n");
            DateOnly start = new DateOnly(1950, 1, 1);

            for (int i = 0; i < dayCount; i++)
            {
                DateOnly day = start.AddDays(i);
                // Columns: impact, nodams, wfill, climate, demand, spill, delta, store-end, bypass, unimpound, withdams.
                fdy.Append(day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                fdy.Append("           0.250          1.000          0.000          0.000          0.069          0.500          0.031          0.031          0.000          0.900          0.750\n");

                double downstream = i < perturbedDays ? 0.95 : 0.75;      // perturb only the leading days
                res.Append(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                res.Append(CultureInfo.InvariantCulture, $",0.25,1,0,-0,0.069,0.5,0.031,0.031,0,0.9,{downstream}\n");
            }
            return (fdy.ToString(), res.ToString());
        }

        /// <summary>Builds a matching pair of fixtures in which both engines pump winterfill every day at the same rate, except that RODIS pumps nothing on a given number of leading days.
        /// This reproduces the accepted timing difference, where RODIS is limited to the room available at the start of the step and so pumps up to one day's volume less.</summary>
        /// <param name="dayCount">Number of daily rows to generate.</param>
        /// <param name="rate">Daily winterfill rate applied by the Fortran reference, in ML/day.</param>
        /// <param name="missedDays">Number of leading days on which RODIS pumps nothing.</param>
        /// <returns>Tuple of (.fdy contents, .res.csv contents).</returns>
        private static (string Fdy, string ResCsv) BuildWinterfillSeries(int dayCount, double rate, int missedDays)
        {
            StringBuilder fdy = new StringBuilder("!Day  header line skipped by the reader\n");
            StringBuilder res = new StringBuilder(ResCsvPreamble + "EOH\n");
            DateOnly start = new DateOnly(1950, 1, 1);

            for (int i = 0; i < dayCount; i++)
            {
                DateOnly day = start.AddDays(i);
                // Columns: impact, nodams, wfill, climate, demand, spill, delta, store-end, bypass, unimpound, withdams.
                fdy.Append(day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                fdy.Append(CultureInfo.InvariantCulture,
                    $"           0.100          1.000          {rate,6:0.000}          0.000          0.069          0.000          0.031          0.031          0.000          0.900          0.900\n");

                double pumped = i < missedDays ? 0.0 : rate;
                res.Append(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                res.Append(CultureInfo.InvariantCulture, $",0.1,1,{pumped},-0,0.069,0,0.031,0.031,0,0.9,0.9\n");
            }
            return (fdy.ToString(), res.ToString());
        }

        // ---------------------------------------------------------------- .fdy parser

        /// <summary>Verifies the .fdy reader skips banner/comment/header lines and returns exactly the three data days.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void FdyReader_SkipsCommentsAndReadsAllDataRows()
        {
            var series = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));

            Assert.AreEqual(3, series.Count, "Expected three daily rows.");
            CollectionAssert.AreEqual(
                new[] { new DateOnly(1950, 1, 1), new DateOnly(1950, 1, 2), new DateOnly(1950, 1, 3) },
                series.Keys.OrderBy(d => d).ToArray());
        }

        /// <summary>Verifies the .fdy reader maps all eleven columns in order for a representative day (day 3, which has a spill and net evaporation).</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void FdyReader_MapsAllElevenColumnsInOrder()
        {
            var series = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            double[] day3 = series[new DateOnly(1950, 1, 3)];

            Assert.AreEqual(LegacyStediMetrics.FdyColumnCount, day3.Length);
            Assert.AreEqual(0.250, day3[0], 1e-9, "Q-impact");
            Assert.AreEqual(1.000, day3[1], 1e-9, "Q-NoDams");
            Assert.AreEqual(0.000, day3[2], 1e-9, "Q-wfill");
            Assert.AreEqual(0.010, day3[3], 1e-9, "Q-climate");
            Assert.AreEqual(0.069, day3[4], 1e-9, "Q-demand");
            Assert.AreEqual(0.500, day3[5], 1e-9, "Q-spill");
            Assert.AreEqual(0.031, day3[6], 1e-9, "Delta-Store");
            Assert.AreEqual(0.094, day3[7], 1e-9, "Store end");
            Assert.AreEqual(0.000, day3[8], 1e-9, "Q-bypass");
            Assert.AreEqual(0.900, day3[9], 1e-9, "Q-unimpound");
            Assert.AreEqual(0.750, day3[10], 1e-9, "Q-WithDams");
        }

        // ---------------------------------------------------------------- .res.csv parser

        /// <summary>Verifies the .res.csv reader locates the Date header and EOH marker and returns exactly the three data days.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void ResCsvReader_FindsEohAndReadsAllDataRows()
        {
            var series = RodisResCsvReader.Read(WriteTemp(TinyResCsv, ".csv"));

            Assert.AreEqual(3, series.Count, "Expected three daily rows.");
            CollectionAssert.AreEqual(
                new[] { new DateOnly(1950, 1, 1), new DateOnly(1950, 1, 2), new DateOnly(1950, 1, 3) },
                series.Keys.OrderBy(d => d).ToArray());
        }

        /// <summary>Verifies the .res.csv reader maps the "N&gt;..." header to field numbers and reads the mapped values (including a "-0" that must parse to zero).</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void ResCsvReader_MapsFieldNumbersFromHeader()
        {
            var series = RodisResCsvReader.Read(WriteTemp(TinyResCsv, ".csv"));
            Dictionary<int, double> day3 = series[new DateOnly(1950, 1, 3)];

            Assert.AreEqual(0.25, day3[1], 1e-9, "field 1 Impact");
            Assert.AreEqual(1.0, day3[4], 1e-9, "field 4 Unimpacted");
            Assert.AreEqual(0.0, day3[5], 1e-9, "field 5 Pumped Inflow");
            Assert.AreEqual(0.01, day3[6], 1e-9, "field 6 Net Rainfall");
            Assert.AreEqual(0.069, day3[7], 1e-9, "field 7 Demand");
            Assert.AreEqual(0.5, day3[8], 1e-9, "field 8 Spill");
            Assert.AreEqual(0.031, day3[9], 1e-9, "field 9 Change in Storage");
            Assert.AreEqual(0.094, day3[10], 1e-9, "field 10 Storage");
            Assert.AreEqual(0.0, day3[11], 1e-9, "field 11 Bypass");
            Assert.AreEqual(0.75, day3[13], 1e-9, "field 13 Downstream");

            Dictionary<int, double> day1 = series[new DateOnly(1950, 1, 1)];
            Assert.AreEqual(0.0, day1[6], 1e-12, "\"-0\" must parse to zero.");
        }

        /// <summary>Verifies a missing 'Date' header or 'EOH' marker raises a clear InvalidDataException rather than silently returning nothing.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void ResCsvReader_ThrowsWhenMarkersMissing()
        {
            const string noEoh =
                "File version,3\nEOM\nEOC\n1\n1,ML,,x,,X,X,Impact,Confluence,Node,Impact,abc\n" +
                "Date,1>Confluence> X> Impact\n1950-01-01,0.1\n"; // no EOH line
            Assert.ThrowsException<InvalidDataException>(() => RodisResCsvReader.Read(WriteTemp(noEoh, ".csv")));
        }

        // ---------------------------------------------------------------- end-to-end comparer

        /// <summary>Verifies that identical .fdy and .res.csv series produce no failures, that every asserting metric passes, and that the informational metrics report zero difference.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_IdenticalSeries_AllPass()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            var rodis = RodisResCsvReader.Read(WriteTemp(TinyResCsv, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            Assert.IsFalse(result.HasFailure, "Identical series must not fail: " + result.FailureSummary);
            Assert.AreEqual(3, result.OverlapDays);
            Assert.IsTrue(result.Metrics.Where(m => m.Tier != MetricTier.Informational).All(m => m.Tier == MetricTier.Pass),
                "Every asserting metric should Pass: " + string.Join(", ", result.Metrics.Select(m => $"{m.Name}={m.Tier}")));
            Assert.IsTrue(result.Metrics.All(m => m.MaxAbs <= 2e-3), "Identical series must show no material difference on any metric.");
        }

        /// <summary>Verifies a divergence on an unexplained (non-spill) day is classified as a genuine Fail, confirming the harness stays sensitive to real regressions such as the historical x10 scaling bug.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_NonSpillDivergence_Fails()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            // Perturb Impact on day 1 (a non-spill day) well beyond tolerance in the RODIS series.
            string perturbed = TinyResCsv.Replace("1950-01-01,0.1,1,0,-0,0.069,0,0.031,0.031,0,0.9,0.9",
                                                  "1950-01-01,0.5,1,0,-0,0.069,0,0.031,0.031,0,0.9,0.9");
            var rodis = RodisResCsvReader.Read(WriteTemp(perturbed, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult impact = result.Metrics.Single(m => m.Name == "Impact");
            Assert.AreEqual(MetricTier.Fail, impact.Tier, "A non-spill Impact divergence must Fail.");
            Assert.AreEqual(1, impact.NonSpillExceedances);
            Assert.IsTrue(result.HasFailure);
        }

        /// <summary>Verifies a rare divergence on a spill-active day is flagged (PassWithSpillDiffs) rather than failed. The three-day series is below
        /// ExplainedGuards.MinDaysForFractionGuard, so the flagged-day fraction is deliberately not enforced here.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_SpillDayDivergence_IsFlaggedNotFailed()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            // Perturb Downstream Flow on day 3 only (day 3 has spill = 0.5 in both series).
            string perturbed = TinyResCsv.Replace("1950-01-03,0.25,1,0,0.01,0.069,0.5,0.031,0.094,0,0.9,0.75",
                                                  "1950-01-03,0.25,1,0,0.01,0.069,0.5,0.031,0.094,0,0.9,0.85");
            var rodis = RodisResCsvReader.Read(WriteTemp(perturbed, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult downstream = result.Metrics.Single(m => m.Name == "DownstreamFlow");
            Assert.AreEqual(MetricTier.PassWithSpillDiffs, downstream.Tier, "A spill-day divergence must be flagged, not failed.");
            Assert.AreEqual(1, downstream.SpillExceedances);
            Assert.AreEqual(0, downstream.NonSpillExceedances);
            Assert.IsFalse(result.HasFailure);
        }

        /// <summary>Verifies the cumulative-change comparison cancels a constant carried storage offset: a fixed +0.05 ML level offset with matching daily changes must not Fail, and the carried offset must be reported.
        /// This mirrors the real day-1 demand-timing offset, whose effect on storage is cumulative and permanent, and stays within ExplainedGuards.MaxCarriedStorageOffsetML.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_ConstantStorageOffset_PassesAndReportsCarriedOffset()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            // Add a constant +0.05 to every Storage LEVEL (field 10) while leaving the daily Change (field 9) unchanged.
            string offsetCsv = TinyResCsv
                .Replace("1950-01-01,0.1,1,0,-0,0.069,0,0.031,0.031,0,0.9,0.9", "1950-01-01,0.1,1,0,-0,0.069,0,0.031,0.081,0,0.9,0.9")
                .Replace("1950-01-02,0.1,1,0,-0,0.069,0,0.031,0.063,0,0.9,0.9", "1950-01-02,0.1,1,0,-0,0.069,0,0.031,0.113,0,0.9,0.9")
                .Replace("1950-01-03,0.25,1,0,0.01,0.069,0.5,0.031,0.094,0,0.9,0.75", "1950-01-03,0.25,1,0,0.01,0.069,0.5,0.031,0.144,0,0.9,0.75");
            var rodis = RodisResCsvReader.Read(WriteTemp(offsetCsv, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult storage = result.Metrics.Single(m => m.Name == "Storage");
            Assert.AreNotEqual(MetricTier.Fail, storage.Tier, "A constant storage offset with matching daily changes must not Fail.");
            Assert.IsTrue(storage.CumulativeOffsetMax >= 0.049 && storage.CumulativeOffsetMax <= 0.051,
                $"Carried storage offset should be reported as ~0.05 ML but was {storage.CumulativeOffsetMax}.");
            Assert.IsFalse(result.HasFailure, "A 0.05 ML carried offset is within the permitted bound.");
        }

        // ---------------------------------------------------------------- spill / bypass reporting convention

        /// <summary>Verifies that moving water between the Spill and Bypass columns, without changing the flow arriving at the outlet, is reported on the individual metrics but does not fail.
        /// RODIS reports an upstream dam's bypass release as bypass for its whole journey, whereas legacy STEDI re-labels it as spill at the downstream dam; the split is a reporting
        /// convention, so only the convention-invariant DamReleasedFlow carries the assertion.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_SpillBypassRelabelling_IsInformationalAndReleasedFlowPasses()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            // Day 3: Fortran reports spill 0.500 / bypass 0.000. Re-label 0.2 of that as bypass in RODIS, leaving downstream flow and local inflow unchanged.
            string relabelled = TinyResCsv.Replace("1950-01-03,0.25,1,0,0.01,0.069,0.5,0.031,0.094,0,0.9,0.75",
                                                   "1950-01-03,0.25,1,0,0.01,0.069,0.3,0.031,0.094,0.2,0.9,0.75");
            var rodis = RodisResCsvReader.Read(WriteTemp(relabelled, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult spills = result.Metrics.Single(m => m.Name == "Spills");
            MetricResult bypass = result.Metrics.Single(m => m.Name == "Bypass");
            MetricResult released = result.Metrics.Single(m => m.Name == "DamReleasedFlow");

            Assert.AreEqual(MetricTier.Informational, spills.Tier, "Spills is convention-dependent and must be informational.");
            Assert.AreEqual(MetricTier.Informational, bypass.Tier, "Bypass is convention-dependent and must be informational.");
            Assert.AreEqual(0.2, spills.MaxAbs, 1e-9, "The re-labelled volume must still be reported as a Spills difference.");
            Assert.AreEqual(0.2, bypass.MaxAbs, 1e-9, "The re-labelled volume must still be reported as a Bypass difference.");
            Assert.AreEqual(MetricTier.Pass, released.Tier, "The convention-invariant released flow must Pass when water is merely re-labelled.");
            Assert.IsFalse(result.HasFailure, "A pure re-labelling must not fail the scenario: " + result.FailureSummary);
        }

        /// <summary>Verifies that a genuine change in the flow arriving at the outlet, as opposed to a re-labelling, still fails through DamReleasedFlow. This confirms no sensitivity is lost
        /// by making the individual Spill and Bypass metrics informational. The case mirrors Scenario 48, where RODIS releases bypass water the Fortran reference never releases.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_GenuineReleasedVolumeDifference_FailsThroughReleasedFlow()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            // Day 1 has neither spill nor bypass in the reference. Give RODIS 0.3 ML of bypass AND raise downstream flow to match, so the outlet genuinely receives more water
            // rather than the same water being re-labelled, on a day that is not spill-active in either series.
            string changed = TinyResCsv.Replace("1950-01-01,0.1,1,0,-0,0.069,0,0.031,0.031,0,0.9,0.9",
                                                "1950-01-01,0.1,1,0,-0,0.069,0,0.031,0.031,0.3,0.9,1.2");
            var rodis = RodisResCsvReader.Read(WriteTemp(changed, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult released = result.Metrics.Single(m => m.Name == "DamReleasedFlow");
            Assert.AreEqual(MetricTier.Fail, released.Tier, "A real change in released volume must fail the released-flow metric.");
            Assert.AreEqual(0.3, released.MaxAbs, 1e-9);
            Assert.IsTrue(result.HasFailure);
        }

        // ---------------------------------------------------------------- winterfill timing

        /// <summary>Verifies the accepted winterfill timing difference: RODIS misses a day's pumping when a full dam begins to draw down, which is reported on the informational Winterfill
        /// metric and in the scenario's pumped-volume totals, but does not fail while the total stays within ExplainedGuards.MaxWinterfillVolumeDifference.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_WinterfillTimingDifference_IsInformationalAndVolumeWithinBound()
        {
            // 100 days at 0.015 ML/day, with RODIS missing one day: 1.500 ML against 1.485 ML, a 1% difference, inside the 2% bound.
            (string fdy, string res) = BuildWinterfillSeries(dayCount: 100, rate: 0.015, missedDays: 1);
            var fortran = LegacyStediFdyReader.Read(WriteTemp(fdy, ".fdy"));
            var rodis = RodisResCsvReader.Read(WriteTemp(res, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult winterfill = result.Metrics.Single(m => m.Name == "Winterfill");
            Assert.AreEqual(MetricTier.Informational, winterfill.Tier, "Winterfill is bounded by one day of pumping by construction and must be informational.");
            Assert.AreEqual(0.015, winterfill.MaxAbs, 1e-9, "The missed day must still be reported as a Winterfill difference.");
            Assert.AreEqual(1.500, result.FortranWinterfillVolume, 1e-6);
            Assert.AreEqual(1.485, result.RodisWinterfillVolume, 1e-6);
            Assert.IsFalse(result.HasExcessiveWinterfillVolumeDifference, "A 1% volume difference is within the accepted timing allowance.");
            Assert.IsFalse(result.HasFailure, result.FailureSummary);
        }

        /// <summary>Verifies that a winterfill difference too large to be explained by timing, such as pumping in the wrong season, fails on the total pumped volume. This is the sensitive
        /// check on winterfill, because the daily difference can never exceed one day of pumping and so cannot be asserted directly.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_ExcessiveWinterfillVolumeDifference_Fails()
        {
            // 100 days at 0.015 ML/day, with RODIS missing 20 days: 1.500 ML against 1.200 ML, a 20% difference, well beyond the 2% bound.
            (string fdy, string res) = BuildWinterfillSeries(dayCount: 100, rate: 0.015, missedDays: 20);
            var fortran = LegacyStediFdyReader.Read(WriteTemp(fdy, ".fdy"));
            var rodis = RodisResCsvReader.Read(WriteTemp(res, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            Assert.IsTrue(result.HasExcessiveWinterfillVolumeDifference, "A 20% volume difference is far beyond the accepted timing allowance.");
            Assert.IsTrue(result.HasFailure, "An excessive winterfill volume difference must fail the scenario.");
            StringAssert.Contains(result.FailureSummary, "total winterfill volume");
        }

        // ---------------------------------------------------------------- ExplainedGuards bounds

        /// <summary>Verifies a carried storage offset beyond ExplainedGuards.MaxCarriedStorageOffsetML escalates the scenario to a failure, even though every daily change still matches.
        /// This stops a large systematic storage divergence from being silently absorbed by the change-based comparison.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_ExcessiveCarriedStorageOffset_EscalatesToFailure()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            // Add a constant +5.0 ML to every Storage LEVEL, an order of magnitude beyond the permitted bound, leaving the daily Change unchanged.
            string offsetCsv = TinyResCsv
                .Replace("1950-01-01,0.1,1,0,-0,0.069,0,0.031,0.031,0,0.9,0.9", "1950-01-01,0.1,1,0,-0,0.069,0,0.031,5.031,0,0.9,0.9")
                .Replace("1950-01-02,0.1,1,0,-0,0.069,0,0.031,0.063,0,0.9,0.9", "1950-01-02,0.1,1,0,-0,0.069,0,0.031,5.063,0,0.9,0.9")
                .Replace("1950-01-03,0.25,1,0,0.01,0.069,0.5,0.031,0.094,0,0.9,0.75", "1950-01-03,0.25,1,0,0.01,0.069,0.5,0.031,5.094,0,0.9,0.75");
            var rodis = RodisResCsvReader.Read(WriteTemp(offsetCsv, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            Assert.IsTrue(result.HasExcessiveCarriedOffset, "A 5 ML carried offset must be reported as excessive.");
            Assert.IsTrue(result.HasFailure, "An excessive carried storage offset must fail the scenario.");
            StringAssert.Contains(result.FailureSummary, "carried storage offset");
        }

        /// <summary>Verifies that flagging a metric on too large a fraction of the record escalates it to FailExcessiveExplained, even though every difference falls on a spill-active day.
        /// This stops a systematic divergence from hiding behind a long run of nominally explained days.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_ExcessiveExplainedDayFraction_EscalatesToFailure()
        {
            // 40 days (above the fraction-guard minimum), all spilling, with 20 perturbed days = 50% flagged, well beyond the 10% bound.
            (string fdy, string res) = BuildSpillySeries(dayCount: 40, perturbedDays: 20);
            var fortran = LegacyStediFdyReader.Read(WriteTemp(fdy, ".fdy"));
            var rodis = RodisResCsvReader.Read(WriteTemp(res, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult downstream = result.Metrics.Single(m => m.Name == "DownstreamFlow");
            Assert.AreEqual(0, downstream.NonSpillExceedances, "Every difference should fall on a spill-active day.");
            Assert.AreEqual(MetricTier.FailExcessiveExplained, downstream.Tier, "Flagging half the record must escalate to a failure.");
            Assert.IsTrue(result.HasFailure);
            StringAssert.Contains(result.FailureSummary, "flagged on");
        }

        /// <summary>Verifies that a small number of flagged days in a long record stays within the bound and is still reported as flagged rather than failed,
        /// matching the validated single-dam and two-dam scenarios which flag on only a few percent of days.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void Comparer_FewExplainedDaysInLongRecord_RemainsFlagged()
        {
            // 40 days, all spilling, with 2 perturbed days = 5% flagged, inside the 10% bound.
            (string fdy, string res) = BuildSpillySeries(dayCount: 40, perturbedDays: 2);
            var fortran = LegacyStediFdyReader.Read(WriteTemp(fdy, ".fdy"));
            var rodis = RodisResCsvReader.Read(WriteTemp(res, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult downstream = result.Metrics.Single(m => m.Name == "DownstreamFlow");
            Assert.AreEqual(MetricTier.PassWithSpillDiffs, downstream.Tier, "A few flagged days in a long record must remain flagged, not failed.");
            Assert.AreEqual(2, downstream.SpillExceedances);
            Assert.IsFalse(result.HasFailure, "Flagging 5% of days is within the permitted bound: " + result.FailureSummary);
        }

        /// <summary>Verifies both documented Fortran defects are registered with a manual reference, at least one expected failing metric and positive ceilings, so the envelope can
        /// never be satisfied vacuously.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void KnownFortranDefects_AreRegisteredWithJustification()
        {
            foreach (int scenario in new[] { 3, 48 })
            {
                Assert.IsTrue(KnownFortranDefects.TryGet(scenario, out KnownFortranDefect? defect) && defect != null, $"Scenario {scenario:D2} should have a documented defect.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(defect!.Description), $"Scenario {scenario:D2} defect needs a description.");
                StringAssert.Contains(defect.ManualReference, "section", $"Scenario {scenario:D2} defect needs a manual reference.");
                Assert.IsTrue(defect.ExpectedFailingMetrics.Count > 0, $"Scenario {scenario:D2} defect needs at least one expected failing metric.");
                Assert.IsTrue(defect.MaxMetricDifferenceML > 0.0, $"Scenario {scenario:D2} defect needs a positive metric ceiling.");
            }

            Assert.IsFalse(KnownFortranDefects.TryGet(1, out _), "Scenario 01 passes and must not be registered as a defect.");
        }

        /// <summary>Verifies a failure on a metric outside the documented set breaches the envelope, so a new problem in a known-defect scenario is not silently absorbed.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void KnownFortranDefects_UnexpectedFailingMetric_BreachesEnvelope()
        {
            KnownFortranDefects.TryGet(48, out KnownFortranDefect? defect);
            ScenarioResult result = new ScenarioResult
            {
                Scenario = 48,
                OverlapDays = 3653,
                Metrics = new List<MetricResult>
                {
                    // Impact is expected to fail for this defect and is within the ceiling.
                    new MetricResult { Name = "Impact", Tier = MetricTier.Fail, MaxAbs = 0.63, DayCount = 3653 },
                    // LocalInflow is NOT part of the documented defect, so its failure must be reported.
                    new MetricResult { Name = "LocalInflow", Tier = MetricTier.Fail, MaxAbs = 0.10, DayCount = 3653 },
                },
            };

            IReadOnlyList<string> breaches = KnownFortranDefects.BreachesEnvelope(defect!, result);

            Assert.AreEqual(1, breaches.Count, "Only the unexpected metric should breach the envelope.");
            StringAssert.Contains(breaches[0], "LocalInflow");
        }

        /// <summary>Verifies a documented metric failing by more than its ceiling breaches the envelope, so a worsening defect is not silently absorbed.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void KnownFortranDefects_WorseningDifference_BreachesEnvelope()
        {
            KnownFortranDefects.TryGet(48, out KnownFortranDefect? defect);
            ScenarioResult result = new ScenarioResult
            {
                Scenario = 48,
                OverlapDays = 3653,
                Metrics = new List<MetricResult>
                {
                    // Impact is an expected metric, but 2.0 ML is well beyond the 0.80 ML documented for this defect.
                    new MetricResult { Name = "Impact", Tier = MetricTier.Fail, MaxAbs = 2.0, DayCount = 3653 },
                },
            };

            IReadOnlyList<string> breaches = KnownFortranDefects.BreachesEnvelope(defect!, result);

            Assert.AreEqual(1, breaches.Count);
            StringAssert.Contains(breaches[0], "beyond the");
        }

        /// <summary>Verifies a failure that matches the documented defect exactly does not breach the envelope, so the scenario is tolerated while it behaves as recorded.</summary>
        [TestMethod]
        [TestCategory(TestCategories.SelfContained)]
        public void KnownFortranDefects_FailureWithinEnvelope_IsTolerated()
        {
            KnownFortranDefects.TryGet(48, out KnownFortranDefect? defect);
            ScenarioResult result = new ScenarioResult
            {
                Scenario = 48,
                OverlapDays = 3653,
                Metrics = new List<MetricResult>
                {
                    new MetricResult { Name = "Impact", Tier = MetricTier.Fail, MaxAbs = 0.6308, DayCount = 3653 },
                    new MetricResult { Name = "DamReleasedFlow", Tier = MetricTier.Fail, MaxAbs = 0.6313, DayCount = 3653 },
                    new MetricResult { Name = "Storage", Tier = MetricTier.Fail, MaxAbs = 0.6304, DayCount = 3653, CumulativeOffsetMax = 32.32 },
                    new MetricResult { Name = "DownstreamFlow", Tier = MetricTier.Fail, MaxAbs = 0.6308, DayCount = 3653 },
                    new MetricResult { Name = "LocalInflow", Tier = MetricTier.Pass, MaxAbs = 4.96e-04, DayCount = 3653 },
                },
            };

            Assert.AreEqual(0, KnownFortranDefects.BreachesEnvelope(defect!, result).Count,
                "A failure matching the documented defect must be tolerated.");
        }
    }
}
