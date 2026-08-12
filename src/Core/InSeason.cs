// <copyright file="InSeason.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.Static
{
    /// <summary>Static helper for testing whether a date falls within a seasonal date range that may wrap across the year boundary.</summary>
    public static class InSeason
    {
        /// <summary>Tests whether a date falls within a season defined by start and end dates (year components ignored).</summary>
        /// <param name="dateTimeToTest">Date to test.</param>
        /// <param name="startSeasonIgnoreYear">First date of the season (year component ignored, inclusive).</param>
        /// <param name="endSeasonIgnoreYear">Last date of the season (year component ignored, inclusive).</param>
        /// <returns>True if <paramref name="dateTimeToTest"/> falls within the season; false otherwise.</returns>
        public static bool IsInSeason(DateTime dateTimeToTest, DateOnly startSeasonIgnoreYear, DateOnly endSeasonIgnoreYear)
        {
            bool result = false;

            DateTime startSeasonTestYear = new DateTime(dateTimeToTest.Year, startSeasonIgnoreYear.Month, startSeasonIgnoreYear.Day);
            DateTime endSeasonTestYear = new DateTime(dateTimeToTest.Year, endSeasonIgnoreYear.Month, endSeasonIgnoreYear.Day).AddDays(1);

            if (endSeasonTestYear > startSeasonTestYear)
            {
                if (dateTimeToTest >= startSeasonTestYear && dateTimeToTest < endSeasonTestYear)
                {
                    result = true;
                }
            } 
            else
            {
                if (dateTimeToTest >= startSeasonTestYear || dateTimeToTest < endSeasonTestYear)
                {
                    result = true;
                }
            }

            return result;
        }
    }
}
