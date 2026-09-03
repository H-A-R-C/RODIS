// <copyright file="FarmDamTimeSeriesDemandModelTests.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODISUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using RODIS.ModelRun;
    using RODIS.Series;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    [TestClass]
    public class FarmDamTimeSeriesDemandModelTests
    {
        private const double AnnualDemandFactor = 0.5;
        private const double DamCapacityML = 50.0;
        private const double ExpectedMeanAnnualDemandML = AnnualDemandFactor * DamCapacityML;
        private const double Tolerance = 1.0E-10;

        /// <summary>Creates a daily pattern over an inclusive date range using the supplied value function.</summary>
        private static TimeSeriesValue[] MakeDailyPattern(DateTime startDate, DateTime endDate, Func<DateTime, double> valueForDate)
        {
            List<TimeSeriesValue> result = new List<TimeSeriesValue>();

            for (DateTime date = startDate; date <= endDate; date = date.AddDays(1))
            {
                result.Add(new TimeSeriesValue
                {
                    Time = date,
                    Value = valueForDate(date),
                    IsValid = true,
                });
            }

            return result.ToArray();
        }

        /// <summary>Creates and initialises a time-series demand model using the supplied input pattern.</summary>
        /// <param name="pattern">Demand-pattern time series.</param>
        /// <returns>Initialised demand model.</returns>
        private static FarmDamTimeSeriesDemandModel MakeModel(TimeSeriesValue[] pattern)
        {
            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);
            model.Initialise();
            return model;
        }

        /// <summary>Creates an uninitialised time-series demand model using the supplied input pattern.</summary>
        /// <param name="pattern">Demand-pattern time series.</param>
        /// <returns>Uninitialised demand model.</returns>
        private static FarmDamTimeSeriesDemandModel MakeUninitialisedModel(TimeSeriesValue[] pattern)
        {
            return new FarmDamTimeSeriesDemandModel
            {
                DemandGroup = "TestDemand",
                AnnualDemandFactor = AnnualDemandFactor,
                DamStorageCapacityVolumeAtSpill = DamCapacityML,
                InputFilePath = "inline test pattern",
                InputPattern = pattern,
            };
        }


        /// <summary>Runs the demand model daily over an inclusive date range and returns the total unrestricted demand.</summary>
        private static double SumDailyDemand(FarmDamTimeSeriesDemandModel model, DateTime startDate, DateTime endDate)
        {
            double result = 0.0;

            for (DateTime date = startDate; date <= endDate; date = date.AddDays(1))
            {
                model.RunTimeStep(date, TimeSpan.FromDays(1));

                Assert.IsTrue(
                    double.IsFinite(model.UnrestrictedDemand),
                    $"Demand on {date:yyyy-MM-dd} should be finite.");

                Assert.IsTrue(
                    model.UnrestrictedDemand >= 0.0,
                    $"Demand on {date:yyyy-MM-dd} should not be negative.");

                result += model.UnrestrictedDemand;
            }

            return result;
        }

        /// <summary>Verifies two identical complete common years produce the configured annual demand in each year.</summary>
        [TestMethod]
        public void Initialise_TwoIdenticalCommonYears_PreservesAnnualDemandInEachYear()
        {
            DateTime startDate = new DateTime(1950, 1, 1);
            DateTime endDate = new DateTime(1951, 12, 31);

            TimeSeriesValue[] pattern = MakeDailyPattern(
                startDate,
                endDate,
                date => 1.0);

            FarmDamTimeSeriesDemandModel model = MakeModel(pattern);

            double demand1950 = SumDailyDemand(
                model,
                new DateTime(1950, 1, 1),
                new DateTime(1950, 12, 31));

            double demand1951 = SumDailyDemand(
                model,
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31));

            Assert.AreEqual(
                ExpectedMeanAnnualDemandML,
                demand1950,
                Tolerance,
                "The first complete common year should deliver the configured annual demand.");

            Assert.AreEqual(
                ExpectedMeanAnnualDemandML,
                demand1951,
                Tolerance,
                "The second complete common year should deliver the configured annual demand.");
        }

        /// <summary>Verifies a common year and leap year preserve the configured mean annual demand across the two-year period.</summary>
        [TestMethod]
        public void Initialise_CommonAndLeapYears_PreserveMeanAnnualDemand()
        {
            DateTime startDate = new DateTime(1951, 1, 1);
            DateTime endDate = new DateTime(1952, 12, 31);

            TimeSeriesValue[] pattern = MakeDailyPattern(
                startDate,
                endDate,
                date => 1.0);

            FarmDamTimeSeriesDemandModel model = MakeModel(pattern);

            double demand1951 = SumDailyDemand(
                model,
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31));

            double demand1952 = SumDailyDemand(
                model,
                new DateTime(1952, 1, 1),
                new DateTime(1952, 12, 31));

            double actualMeanAnnualDemand = (demand1951 + demand1952) / 2.0;

            Assert.AreEqual(
                ExpectedMeanAnnualDemandML,
                actualMeanAnnualDemand,
                Tolerance,
                "A common year and leap year should average to the configured annual demand.");

            Assert.AreEqual(
                ExpectedMeanAnnualDemandML * 365.0 / 365.5,
                demand1951,
                Tolerance,
                "The common year contains 365 of the 731 equally weighted daily pattern values.");

            Assert.AreEqual(
                ExpectedMeanAnnualDemandML * 366.0 / 365.5,
                demand1952,
                Tolerance,
                "The leap year contains 366 of the 731 equally weighted daily pattern values.");
        }

        /// <summary>Verifies years with different raw pattern totals retain their relative weighting while preserving the configured mean annual demand.</summary>
        [TestMethod]
        public void Initialise_YearsWithDifferentPatternTotals_PreserveShapeAndMeanAnnualDemand()
        {
            DateTime startDate = new DateTime(1950, 1, 1);
            DateTime endDate = new DateTime(1951, 12, 31);

            TimeSeriesValue[] pattern = MakeDailyPattern(
                startDate,
                endDate,
                date => date.Year == 1950 ? 1.0 : 3.0);

            FarmDamTimeSeriesDemandModel model = MakeModel(pattern);

            double demand1950 = SumDailyDemand(
                model,
                new DateTime(1950, 1, 1),
                new DateTime(1950, 12, 31));

            double demand1951 = SumDailyDemand(
                model,
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31));

            double actualMeanAnnualDemand = (demand1950 + demand1951) / 2.0;

            Assert.AreEqual(
                ExpectedMeanAnnualDemandML,
                actualMeanAnnualDemand,
                Tolerance,
                "Different yearly pattern totals should still preserve the configured mean annual demand.");

            Assert.AreEqual(
                3.0,
                demand1951 / demand1950,
                Tolerance,
                "The year whose raw pattern is three times larger should retain three times the annual demand.");

            Assert.AreEqual(
                12.5,
                demand1950,
                Tolerance,
                "The lower-pattern year should receive 12.5 ML.");

            Assert.AreEqual(
                37.5,
                demand1951,
                Tolerance,
                "The higher-pattern year should receive 37.5 ML.");
        }

        /// <summary>Verifies multiplying every input-pattern value by a constant does not alter the resulting demand series.</summary>
        [TestMethod]
        public void Initialise_ScalingPatternValues_DoesNotChangeDemand()
        {
            DateTime startDate = new DateTime(1951, 1, 1);
            DateTime endDate = new DateTime(1952, 12, 31);

            TimeSeriesValue[] basePattern = MakeDailyPattern(
                startDate,
                endDate,
                date => date.Month);

            TimeSeriesValue[] scaledPattern = MakeDailyPattern(
                startDate,
                endDate,
                date => date.Month * 10.0);

            FarmDamTimeSeriesDemandModel baseModel = MakeModel(basePattern);
            FarmDamTimeSeriesDemandModel scaledModel = MakeModel(scaledPattern);

            for (DateTime date = startDate; date <= endDate; date = date.AddDays(1))
            {
                baseModel.RunTimeStep(date, TimeSpan.FromDays(1));
                scaledModel.RunTimeStep(date, TimeSpan.FromDays(1));

                Assert.AreEqual(
                    baseModel.UnrestrictedDemand,
                    scaledModel.UnrestrictedDemand,
                    Tolerance,
                    $"Multiplying the raw pattern by ten should not change demand on {date:yyyy-MM-dd}.");
            }

            double baseMeanAnnualDemand = (
                SumDailyDemand(baseModel, new DateTime(1951, 1, 1), new DateTime(1951, 12, 31))
                + SumDailyDemand(baseModel, new DateTime(1952, 1, 1), new DateTime(1952, 12, 31))) / 2.0;

            double scaledMeanAnnualDemand = (
                SumDailyDemand(scaledModel, new DateTime(1951, 1, 1), new DateTime(1951, 12, 31))
                + SumDailyDemand(scaledModel, new DateTime(1952, 1, 1), new DateTime(1952, 12, 31))) / 2.0;

            Assert.AreEqual(ExpectedMeanAnnualDemandML, baseMeanAnnualDemand, Tolerance);
            Assert.AreEqual(ExpectedMeanAnnualDemandML, scaledMeanAnnualDemand, Tolerance);
        }

        /// <summary>Verifies incomplete years at the ends of the pattern do not affect normalisation based on the complete intervening year.</summary>
        [TestMethod]
        public void Initialise_PartialEndYears_DoNotAffectCompleteYearNormalisation()
        {
            DateTime startDate = new DateTime(1950, 7, 1);
            DateTime endDate = new DateTime(1952, 6, 30);

            TimeSeriesValue[] pattern = MakeDailyPattern(
                startDate,
                endDate,
                date =>
                {
                    if (date.Year == 1951)
                    {
                        return 1.0;
                    }

                    return 1000.0;
                });

            FarmDamTimeSeriesDemandModel model = MakeModel(pattern);

            double demand1951 = SumDailyDemand(
                model,
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31));

            Assert.AreEqual(
                ExpectedMeanAnnualDemandML,
                demand1951,
                Tolerance,
                "Large values in partial years should not alter normalisation based on the complete 1951 calendar year.");
        }

        /// <summary>Verifies a repeating seasonal pattern retains its within-year shape while preserving the configured mean annual demand.</summary>
        [TestMethod]
        public void Initialise_DifferentWithinYearPatterns_PreserveShapeAndMeanAnnualDemand()
        {
            DateTime startDate = new DateTime(1950, 1, 1);
            DateTime endDate = new DateTime(1951, 12, 31);

            TimeSeriesValue[] pattern = MakeDailyPattern(
                startDate,
                endDate,
                date =>
                {
                    if (date.Year == 1950)
                    {
                        return date.Month <= 6 ? 1.0 : 2.0;
                    }

                    return date.Month <= 6 ? 4.0 : 1.0;
                });

            FarmDamTimeSeriesDemandModel model = MakeModel(pattern);

            double firstHalf1950 = SumDailyDemand(
                model,
                new DateTime(1950, 1, 1),
                new DateTime(1950, 6, 30));

            double secondHalf1950 = SumDailyDemand(
                model,
                new DateTime(1950, 7, 1),
                new DateTime(1950, 12, 31));

            double firstHalf1951 = SumDailyDemand(
                model,
                new DateTime(1951, 1, 1),
                new DateTime(1951, 6, 30));

            double secondHalf1951 = SumDailyDemand(
                model,
                new DateTime(1951, 7, 1),
                new DateTime(1951, 12, 31));

            double demand1950 = firstHalf1950 + secondHalf1950;
            double demand1951 = firstHalf1951 + secondHalf1951;
            double actualMeanAnnualDemand = (demand1950 + demand1951) / 2.0;

            Assert.IsTrue(
                secondHalf1950 > firstHalf1950,
                "The 1950 pattern should retain its higher second-half demand.");

            Assert.IsTrue(
                firstHalf1951 > secondHalf1951,
                "The 1951 pattern should retain its higher first-half demand.");

            Assert.AreEqual(
                ExpectedMeanAnnualDemandML,
                actualMeanAnnualDemand,
                Tolerance,
                "The two complete calendar years should average to the configured annual demand.");
        }

        /// <summary>Verifies a complete daily leap-year pattern containing 29 February is accepted and preserves the configured annual demand.</summary>
        [TestMethod]
        public void Initialise_CompleteLeapYearPattern_IsAccepted()
        {
            TimeSeriesValue[] pattern = MakeDailyPattern(
                new DateTime(1952, 1, 1),
                new DateTime(1952, 12, 31),
                date => 1.0);

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);

            model.Initialise();

            Assert.AreEqual(
                366,
                pattern.Length,
                "The valid leap-year fixture should contain 366 daily values.");

            Assert.IsTrue(
                pattern.Any(value => value.Time == new DateTime(1952, 2, 29)),
                "The valid leap-year fixture should contain 29 February 1952.");

            double actualAnnualDemand = SumDailyDemand(
                model,
                new DateTime(1952, 1, 1),
                new DateTime(1952, 12, 31));

            Assert.AreEqual(
                ExpectedMeanAnnualDemandML,
                actualAnnualDemand,
                Tolerance,
                "A complete leap-year pattern should deliver the configured annual demand.");
        }

        /// <summary>Verifies initialisation rejects a daily leap-year pattern that omits 29 February.</summary>
        [TestMethod]
        public void Initialise_LeapYearPatternMissing29February_ThrowsInvalidDataException()
        {
            DateTime leapDay = new DateTime(1952, 2, 29);

            TimeSeriesValue[] pattern = MakeDailyPattern(
                new DateTime(1952, 1, 1),
                new DateTime(1952, 12, 31),
                date => 1.0)
                .Where(value => value.Time != leapDay)
                .ToArray();

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(
                model.Initialise,
                "A daily pattern covering 1952 but omitting 29 February should be rejected.");

            Assert.AreEqual(
                365,
                pattern.Length,
                "The invalid fixture should contain exactly one fewer value than a complete leap year.");

            StringAssert.Contains(
                exception.Message,
                "1952-02-29",
                "The exception should identify the missing date.");
        }

        /// <summary>Verifies initialisation rejects a daily leap-year pattern containing two entries for 29 February.</summary>
        [TestMethod]
        public void Initialise_LeapYearPatternWithDuplicate29February_ThrowsInvalidDataException()
        {
            DateTime leapDay = new DateTime(1952, 2, 29);

            List<TimeSeriesValue> pattern = MakeDailyPattern(
                new DateTime(1952, 1, 1),
                new DateTime(1952, 12, 31),
                date => 1.0)
                .ToList();

            int leapDayIndex = pattern.FindIndex(value => value.Time == leapDay);

            pattern.Insert(
                leapDayIndex + 1,
                new TimeSeriesValue
                {
                    Time = leapDay,
                    Value = 1.0,
                    IsValid = true,
                });

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern.ToArray());

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(
                model.Initialise,
                "A daily pattern containing two entries for 29 February should be rejected.");

            Assert.AreEqual(
                367,
                pattern.Count,
                "The invalid fixture should contain one more value than a complete leap year.");

            Assert.AreEqual(
                2,
                pattern.Count(value => value.Time == leapDay),
                "The invalid fixture should contain exactly two leap-day entries.");

            StringAssert.Contains(
                exception.Message,
                "1952-02-29",
                "The exception should identify the duplicated date.");
        }

        /// <summary>Verifies initialisation rejects an input-pattern entry marked invalid rather than silently converting it into valid demand.</summary>
        [TestMethod]
        public void Initialise_InvalidPatternEntry_ThrowsInvalidDataException()
        {
            DateTime invalidDate = new DateTime(1952, 2, 29);

            TimeSeriesValue[] pattern = MakeDailyPattern(
                new DateTime(1952, 1, 1),
                new DateTime(1952, 12, 31),
                date => 1.0);

            TimeSeriesValue invalidValue = pattern.Single(value => value.Time == invalidDate);
            invalidValue.IsValid = false;

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(
                model.Initialise,
                "An invalid demand-pattern entry should be rejected.");

            StringAssert.Contains(exception.Message, "1952-02-29");
            StringAssert.Contains(exception.Message, "not valid");
        }

        // ------------------------------------------------------------------------
        // Monthly scale-factor validation
        // ------------------------------------------------------------------------

        /// <summary>Verifies initialisation rejects a negative monthly demand scale factor.</summary>
        [TestMethod]
        public void Initialise_NegativeMonthlyScaleFactor_ThrowsInvalidDataException()
        {
            TimeSeriesValue[] pattern = MakeDailyPattern(
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31),
                date => 1.0);

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);
            model.MonthlyScaleFactors[1] = -0.5;

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(
                model.Initialise,
                "A negative monthly scale factor should be rejected.");

            StringAssert.Contains(exception.Message, "February");
            StringAssert.Contains(exception.Message, "-0.5");
        }

        /// <summary>Verifies initialisation rejects non-finite monthly demand scale factors.</summary>
        [DataTestMethod]
        [DataRow(double.NaN, "NaN")]
        [DataRow(double.PositiveInfinity, "Infinity")]
        [DataRow(double.NegativeInfinity, "-Infinity")]
        public void Initialise_NonFiniteMonthlyScaleFactor_ThrowsInvalidDataException(double invalidValue, string expectedText)
        {
            TimeSeriesValue[] pattern = MakeDailyPattern(
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31),
                date => 1.0);

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);
            model.MonthlyScaleFactors[1] = invalidValue;

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(
                model.Initialise,
                "A non-finite monthly scale factor should be rejected.");

            StringAssert.Contains(exception.Message, "February");
            StringAssert.Contains(exception.Message, expectedText);
        }

        /// <summary>Verifies initialisation requires exactly 12 monthly demand scale factors.</summary>
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(11)]
        [DataRow(13)]
        public void Initialise_IncorrectMonthlyScaleFactorCount_ThrowsInvalidDataException(int factorCount)
        {
            TimeSeriesValue[] pattern = MakeDailyPattern(
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31),
                date => 1.0);

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);
            model.MonthlyScaleFactors = new double[factorCount];

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(
                model.Initialise,
                "Exactly 12 monthly scale factors should be required.");

            StringAssert.Contains(exception.Message, "exactly 12");
            StringAssert.Contains(exception.Message, factorCount.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Verifies a zero monthly scale factor is valid and suppresses demand for that month.</summary>
        [TestMethod]
        public void Initialise_ZeroMonthlyScaleFactor_IsAcceptedAndProducesZeroDemand()
        {
            TimeSeriesValue[] pattern = MakeDailyPattern(
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31),
                date => 1.0);

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);
            model.MonthlyScaleFactors[1] = 0.0;

            model.Initialise();
            model.RunTimeStep(new DateTime(1951, 2, 15), TimeSpan.FromDays(1));

            Assert.AreEqual(
                0.0,
                model.UnrestrictedDemand,
                Tolerance,
                "A zero February scale factor should produce zero February demand.");
        }

        [DataTestMethod]
        [DataRow(double.NaN, "NaN")]
        [DataRow(double.PositiveInfinity, "Infinity")]
        [DataRow(double.NegativeInfinity, "-Infinity")]
        public void Initialise_NonFiniteAnnualDemandFactor_ThrowsInvalidDataException(
            double invalidValue,
            string expectedText)
        {
            TimeSeriesValue[] pattern = MakeDailyPattern(
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31),
                date => 1.0);

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);
            model.AnnualDemandFactor = invalidValue;

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(
                model.Initialise);

            StringAssert.Contains(exception.Message, "AnnualDemandFactor");
            StringAssert.Contains(exception.Message, expectedText);
        }

        [DataTestMethod]
        [DataRow(double.NaN, "NaN")]
        [DataRow(double.PositiveInfinity, "Infinity")]
        public void Initialise_NonFiniteDamCapacity_ThrowsInvalidDataException(
            double invalidValue,
            string expectedText)
        {
            TimeSeriesValue[] pattern = MakeDailyPattern(
                new DateTime(1951, 1, 1),
                new DateTime(1951, 12, 31),
                date => 1.0);

            FarmDamTimeSeriesDemandModel model = MakeUninitialisedModel(pattern);
            model.DamStorageCapacityVolumeAtSpill = invalidValue;

            InvalidDataException exception = Assert.ThrowsException<InvalidDataException>(
                model.Initialise);

            StringAssert.Contains(exception.Message, "DamStorageCapacityVolumeAtSpill");
            StringAssert.Contains(exception.Message, expectedText);
        }
    }
}