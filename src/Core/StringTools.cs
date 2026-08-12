// <copyright file="StringTools.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

using System.Globalization;
using System.IO;
using System.Numerics;

namespace STEDI.Static
{
    public static class StringTools
    {
        /// <summary>
        /// Enum to string.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="value">Enum value.</param>
        /// <returns>String.</returns>
        public static string EnumToString<T>(T value)
            where T : Enum
            => Enum.GetName(typeof(T), value);

        /// <summary>
        /// Substring compare.
        /// </summary>
        /// <param name="longString">Longer of the two strings.</param>
        /// <param name="shortString">Shorter of the two strings.</param>
        /// <returns>True if first part of the longer string is identical to entire shorter string.</returns>
        public static bool SubstringCompare(string longString, string shortString)
        {
            // Returns true if the first part of the longer string is identical to the entire of the shorter string
            // Returns false if first part of strings are not identical
            // Also returns false if either string is null or zero length
            bool outcome = false;

            if (longString != null && shortString != null)
            {
                if (longString.Length != 0 && shortString.Length != 0)
                {
                    if (longString.Length > shortString.Length)
                    {
                        if (longString.Substring(0, shortString.Length) == shortString)
                        {
                            outcome = true;
                        }
                    }
                    else
                    {
                        if (shortString.Substring(0, longString.Length) == longString)
                        {
                            outcome = true;
                        }
                    }
                }
            }

            return outcome;
        }

        /// <summary>
        /// Removes specified string parts from the input string, if they are present, otherwise returns the input string.
        /// </summary>
        /// <param name="longString">Input string.</param>
        /// <param name="partsToRemove">Array of parts of strings to remove, if present in the longer string.</param>
        /// <returns>String with specified parts removed, if present.</returns>
        public static string RemoveSpecifiedSubStringIfPresent(string longString, string[] partsToRemove)
        {
            string result = longString;

            foreach (string part in partsToRemove)
            {
                if (result.Contains(part))
                {
                    int start = result.IndexOf(part);
                    int length = part.Length;
                    int end = start + length;

                    result = result.Substring(0, start) + (end < result.Length ? result.Substring(end) : string.Empty);
                }
            }

            return result;
        }

        /// <summary>Format an integer using leading zeros to ensure at least <paramref name="minChars"/> digits.</summary>
        /// <param name="value">Value to format.</param>
        /// <param name="minChars">Minimum number of digits (>= 0).</param>
        /// <returns>Formatted string (e.g. value=7, minChars=3 -> "007").</returns>
        public static string FormatWithLeadingZeros(int value, int minChars)
        {
            if (minChars < 0) throw new ArgumentOutOfRangeException(nameof(minChars), minChars, "minChars must be >= 0.");
            return value.ToString("D" + minChars, CultureInfo.InvariantCulture);
        }

        /// <summary>Format a long using leading zeros to ensure at least <paramref name="minChars"/> digits.</summary>
        public static string FormatWithLeadingZeros(long value, int minChars)
        {
            if (minChars < 0) throw new ArgumentOutOfRangeException(nameof(minChars), minChars, "minChars must be >= 0.");
            return value.ToString("D" + minChars, CultureInfo.InvariantCulture);
        }

        /// <summary>Returns the number of digits required to represent values up to <paramref name="maxValue"/>.</summary>
        /// <param name="maxValue">Maximum value (e.g. number of replicates).</param>
        /// <returns>Minimum number of digits required (>= 1).</returns>
        public static int OutputSpaceNeeded(int maxValue)
        {
            if (maxValue <= 0)
                return 1; // sensible default (avoids log issues and matches typical usage)

            // Fast integer digit count (no string allocation)
            int digits = 0;
            int n = maxValue;
            while (n > 0)
            {
                n /= 10;
                digits++;
            }

            return digits;
        }
    }
}
