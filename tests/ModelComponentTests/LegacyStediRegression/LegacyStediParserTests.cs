// ============================================================================
// LegacyStediParserTests.cs
//   Self-contained unit tests for the Fortran .fdy and RODIS .res.csv parsers
//   and the spill-aware comparer. These write tiny inline fixtures to temporary
//   files, so they need NO access to the O: drive and are safe to run in CI.
//
//   They guard against the parsers silently breaking if the Source .res.csv
//   layout evolves (field count, EOH placement, header "N>..." format, missing
//   values, "-0" values) or if the .fdy column order changes.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RODIS.Tests.Legacy
{
    /// <summary>Unit tests for LegacyStediFdyReader, RodisResCsvReader and LegacyStediComparer using small inline fixtures written to temporary files.</summary>
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

        /// <summary>A three-day RODIS .res.csv fixture with the same nine mapped fields and values as TinyFdy, exercising the EOM/EOC/EOH markers, "N&gt;..." header, a "-0" value and ISO datetimes.</summary>
        private const string TinyResCsv =
            "File version,3\n" +
            "Missing data value,-9999\n" +
            "EOM\n" +
            "Project name,\n" +
            "Program,RODIS.Core 2.2.2026.813\n" +
            "Latest result run time,2026-08-13 16:26:55\n" +
            "Simulation time,1950-01-01 - 1950-01-03\n" +
            "Field,Units,RunName,ScenarioName,ScenarioInputSetName,Name,Site,ElementName,WaterFeatureType,ElementType,Structure,Custom\n" +
            "EOC\n" +
            "13\n" +
            "1,ML,,tiny,,Confluence: X: Impact,X,Impact,Confluence,Node,Impact,abc\n" +
            "4,ML,,tiny,,Confluence: X: Unimpacted Flow,X,Unimpacted Flow,Confluence,Node,Unimpacted Flow,abc\n" +
            "6,ML,,tiny,,Confluence: X: Net Rainfall Volume,X,Net Rainfall Volume,Confluence,Node,Net Rainfall Volume,abc\n" +
            "7,ML,,tiny,,Confluence: X: Demand Volume Extracted,X,Demand Volume Extracted,Confluence,Node,Demand Volume Extracted,abc\n" +
            "8,ML,,tiny,,Confluence: X: Spill Downstream Flow,X,Spill Downstream Flow,Confluence,Node,Spill Downstream Flow,abc\n" +
            "9,ML,,tiny,,Confluence: X: Change in Storage Volume,X,Change in Storage Volume,Confluence,Node,Change in Storage Volume,abc\n" +
            "10,ML,,tiny,,Confluence: X: Storage Volume End of Timestep,X,Storage Volume End of Timestep,Confluence,Node,Storage Volume End of Timestep,abc\n" +
            "12,ML,,tiny,,Confluence: X: Local Catchment Inflow,X,Local Catchment Inflow,Confluence,Node,Local Catchment Inflow,abc\n" +
            "13,ML,,tiny,,Confluence: X: Downstream Flow,X,Downstream Flow,Confluence,Node,Downstream Flow,abc\n" +
            "Date,1>Confluence> X> Impact,4>Confluence> X> Unimpacted Flow,6>Confluence> X> Net Rainfall Volume,7>Confluence> X> Demand Volume Extracted,8>Confluence> X> Spill Downstream Flow,9>Confluence> X> Change in Storage Volume,10>Confluence> X> Storage Volume End of Timestep,12>Confluence> X> Local Catchment Inflow,13>Confluence> X> Downstream Flow\n" +
            "EOH\n" +
            "1950-01-01,0.1,1,-0,0.069,0,0.031,0.031,0.9,0.9\n" +
            "1950-01-02,0.1,1,-0,0.069,0,0.031,0.063,0.9,0.9\n" +
            "1950-01-03,0.25,1,0.01,0.069,0.5,0.031,0.094,0.9,0.75\n";

        // ---------------------------------------------------------------- .fdy parser

        /// <summary>Verifies the .fdy reader skips banner/comment/header lines and returns exactly the three data days.</summary>
        [TestMethod]
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
        public void FdyReader_MapsAllElevenColumnsInOrder()
        {
            var series = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            double[] day3 = series[new DateOnly(1950, 1, 3)];

            Assert.AreEqual(LegacyStediMetrics.FdyColumnCount, day3.Length);
            Assert.AreEqual(0.250, day3[0], 1e-9, "Q-impact");
            Assert.AreEqual(1.000, day3[1], 1e-9, "Q-NoDams");
            Assert.AreEqual(0.010, day3[3], 1e-9, "Q-climate");
            Assert.AreEqual(0.069, day3[4], 1e-9, "Q-demand");
            Assert.AreEqual(0.500, day3[5], 1e-9, "Q-spill");
            Assert.AreEqual(0.031, day3[6], 1e-9, "Delta-Store");
            Assert.AreEqual(0.094, day3[7], 1e-9, "Store end");
            Assert.AreEqual(0.900, day3[9], 1e-9, "Q-unimpound");
            Assert.AreEqual(0.750, day3[10], 1e-9, "Q-WithDams");
        }

        // ---------------------------------------------------------------- .res.csv parser

        /// <summary>Verifies the .res.csv reader locates the Date header and EOH marker and returns exactly the three data days.</summary>
        [TestMethod]
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
        public void ResCsvReader_MapsFieldNumbersFromHeader()
        {
            var series = RodisResCsvReader.Read(WriteTemp(TinyResCsv, ".csv"));
            Dictionary<int, double> day3 = series[new DateOnly(1950, 1, 3)];

            Assert.AreEqual(0.25, day3[1], 1e-9, "field 1 Impact");
            Assert.AreEqual(1.0, day3[4], 1e-9, "field 4 Unimpacted");
            Assert.AreEqual(0.01, day3[6], 1e-9, "field 6 Net Rainfall");
            Assert.AreEqual(0.069, day3[7], 1e-9, "field 7 Demand");
            Assert.AreEqual(0.5, day3[8], 1e-9, "field 8 Spill");
            Assert.AreEqual(0.031, day3[9], 1e-9, "field 9 Change in Storage");
            Assert.AreEqual(0.094, day3[10], 1e-9, "field 10 Storage");
            Assert.AreEqual(0.75, day3[13], 1e-9, "field 13 Downstream");

            Dictionary<int, double> day1 = series[new DateOnly(1950, 1, 1)];
            Assert.AreEqual(0.0, day1[6], 1e-12, "\"-0\" must parse to zero.");
        }

        /// <summary>Verifies a missing 'Date' header or 'EOH' marker raises a clear InvalidDataException rather than silently returning nothing.</summary>
        [TestMethod]
        public void ResCsvReader_ThrowsWhenMarkersMissing()
        {
            const string noEoh =
                "File version,3\nEOM\nEOC\n1\n1,ML,,x,,X,X,Impact,Confluence,Node,Impact,abc\n" +
                "Date,1>Confluence> X> Impact\n1950-01-01,0.1\n"; // no EOH line
            Assert.ThrowsException<InvalidDataException>(() => RodisResCsvReader.Read(WriteTemp(noEoh, ".csv")));
        }

        // ---------------------------------------------------------------- end-to-end comparer

        /// <summary>Verifies that identical .fdy and .res.csv series produce all-Pass metrics and no failures (the two fixtures encode the same nine values).</summary>
        [TestMethod]
        public void Comparer_IdenticalSeries_AllPass()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            var rodis = RodisResCsvReader.Read(WriteTemp(TinyResCsv, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            Assert.IsFalse(result.HasFailure, "Identical series must not fail.");
            Assert.AreEqual(3, result.OverlapDays);
            Assert.IsTrue(result.Metrics.All(m => m.Tier == MetricTier.Pass),
                "Every metric should Pass: " + string.Join(", ", result.Metrics.Select(m => $"{m.Name}={m.Tier}")));
        }

        /// <summary>Verifies a divergence on a NON-spill day is classified as a genuine Fail, confirming the harness stays sensitive to real regressions.</summary>
        [TestMethod]
        public void Comparer_NonSpillDivergence_Fails()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            // Perturb Impact on day 1 (a non-spill day) well beyond tolerance in the RODIS series.
            string perturbed = TinyResCsv.Replace("1950-01-01,0.1,1,-0,0.069,0,0.031,0.031,0.9,0.9",
                                      "1950-01-01,0.5,1,-0,0.069,0,0.031,0.031,0.9,0.9");
            var rodis = RodisResCsvReader.Read(WriteTemp(perturbed, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult impact = result.Metrics.Single(m => m.Name == "Impact");
            Assert.AreEqual(MetricTier.Fail, impact.Tier, "A non-spill Impact divergence must Fail.");
            Assert.AreEqual(1, impact.NonSpillExceedances);
            Assert.IsTrue(result.HasFailure);
        }

        /// <summary>Verifies a divergence that occurs only on a spill-active day is flagged (PassWithSpillDiffs) rather than failed.</summary>
        [TestMethod]
        public void Comparer_SpillDayDivergence_IsFlaggedNotFailed()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            // Perturb Downstream Flow on day 3 only (day 3 has spill = 0.5 in both series).
            string perturbed = TinyResCsv.Replace("1950-01-03,0.25,1,0.01,0.069,0.5,0.031,0.094,0.9,0.75",
                                                  "1950-01-03,0.25,1,0.01,0.069,0.5,0.031,0.094,0.9,0.85");
            var rodis = RodisResCsvReader.Read(WriteTemp(perturbed, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult downstream = result.Metrics.Single(m => m.Name == "DownstreamFlow");
            Assert.AreEqual(MetricTier.PassWithSpillDiffs, downstream.Tier, "A spill-day divergence must be flagged, not failed.");
            Assert.AreEqual(1, downstream.SpillExceedances);
            Assert.AreEqual(0, downstream.NonSpillExceedances);
            Assert.IsFalse(result.HasFailure);
        }

        /// <summary>Verifies the cumulative-change comparison cancels a constant carried storage offset: a fixed +0.05 ML level offset with matching daily changes must Pass and report the carried offset as a diagnostic.</summary>
        [TestMethod]
        public void Comparer_ConstantStorageOffset_PassesAndReportsCarriedOffset()
        {
            var fortran = LegacyStediFdyReader.Read(WriteTemp(TinyFdy, ".fdy"));
            // Add a constant +0.05 to every Storage LEVEL (field 10) while leaving the daily Change (field 9) unchanged.
            string offsetCsv = TinyResCsv
                .Replace("1950-01-01,0.1,1,-0,0.069,0,0.031,0.031,0.9,0.9", "1950-01-01,0.1,1,-0,0.069,0,0.031,0.081,0.9,0.9")
                .Replace("1950-01-02,0.1,1,-0,0.069,0,0.031,0.063,0.9,0.9", "1950-01-02,0.1,1,-0,0.069,0,0.031,0.113,0.9,0.9")
                .Replace("1950-01-03,0.25,1,0.01,0.069,0.5,0.031,0.094,0.9,0.75", "1950-01-03,0.25,1,0.01,0.069,0.5,0.031,0.144,0.9,0.75");
            var rodis = RodisResCsvReader.Read(WriteTemp(offsetCsv, ".csv"));

            ScenarioResult result = LegacyStediComparer.Compare(1, fortran, rodis);

            MetricResult storage = result.Metrics.Single(m => m.Name == "Storage");
            Assert.AreNotEqual(MetricTier.Fail, storage.Tier, "A constant storage offset with matching daily changes must not Fail.");
            Assert.IsTrue(storage.CumulativeOffsetMax >= 0.049 && storage.CumulativeOffsetMax <= 0.051,
                $"Carried storage offset should be reported as ~0.05 ML but was {storage.CumulativeOffsetMax}.");
        }
    }
}
