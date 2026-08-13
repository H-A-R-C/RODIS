// <copyright file="DateTimeTools.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.Static
{
    using System.Globalization;
    using RODIS.Series;

    /// <summary>DateTime utility methods not available in the standard library.</summary>
    public static class DateTimeTools
    {
        private static readonly string[] validDateFormats = new[]
        {
            "yyyy-MM-dd",
            "dd/MM/yyyy",
            "d/MM/yyyy",
            "dd/M/yyyy",
            "d/M/yyyy",
            "yyyy/MM/dd",
            "dd-MM-yyyy",
        };

        /// <summary>Returns the earlier of two dates.</summary>
        public static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

        /// <summary>Returns the later of two dates.</summary>
        public static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

        /// <summary>Returns the water year a date falls in, labelled by the calendar year the water year starts in.</summary>
        /// <param name="date">Calendar date.</param>
        /// <param name="waterYearStartMonth">Month (1–12) when the water year begins. Default 7 (July, Australian convention).</param>
        /// <returns>Water year label (e.g. for July start: Jul 2000–Jun 2001 ? 2000).</returns>
        public static int WaterYear(DateTime date, int waterYearStartMonth = 7)
        {
            return date.Month >= waterYearStartMonth ? date.Year : date.Year - 1;
        }

        /// <summary>Returns the water year a date falls in, given a MonthOfYear start.</summary>
        /// <param name="date">Calendar date.</param>
        /// <param name="waterYearStart">Month when the water year begins.</param>
        /// <returns>Water year label.</returns>
        public static int WaterYear(DateTime date, MonthOfYear waterYearStart)
        {
            return WaterYear(date, ToMonthNumber(waterYearStart));  // MonthOfYear is zero-indexed, DateTime.Month is 1-indexed
        }

        /// <summary>Attempts to parse a string as a DateTime, trying common date validDateFormats. Returns true on success.</summary>
        /// <param name="text">String to parse.</param>
        /// <param name="result">Parsed DateTime if successful; otherwise default.</param>
        /// <returns>True if parsing succeeded.</returns>
        public static bool TryParseDate(string text, out DateTime result)
        {
            // Try explicit validDateFormats FIRST (avoids InvariantCulture misinterpreting d/MM as MM/dd)
            if (DateTime.TryParseExact(text, validDateFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                return true;
            }

            // Fallback to generic parsing only if explicit validDateFormats all fail
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        /// <summary>Clamps a requested date window to the Modelled series date range.</summary>
        public static (DateTime Start, DateTime End) ClampDateWindow(DateTime start, DateTime end, TimeSeriesValue[] series)
        {
            DateTime seriesStart = series[0].Time;
            DateTime seriesEnd = series[^1].Time;

            // Treat default(DateTime) as unbounded.
            if (start == default) start = seriesStart;
            if (end == default) end = seriesEnd;

            if (start < seriesStart) start = seriesStart;
            if (end > seriesEnd) end = seriesEnd;

            if (end < start)
                throw new InvalidOperationException($"Invalid date window: End < Start ({end:O} < {start:O}).");

            return (start, end);
        }

        /// <summary>Converts a 1-based month number (DateTime.Month) to MonthOfYear.</summary>
        /// <param name="month">Month number (1–12).</param>
        /// <returns>Corresponding MonthOfYear value.</returns>
        public static MonthOfYear ToMonthOfYear(int month)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1–12.");
            return (MonthOfYear)(month - 1);
        }

        /// <summary>Converts MonthOfYear to a 1-based month number (compatible with DateTime.Month).</summary>
        /// <param name="month">MonthOfYear value.</param>
        /// <returns>Month number (1–12).</returns>
        public static int ToMonthNumber(MonthOfYear month) => (int)month + 1;
    }
}