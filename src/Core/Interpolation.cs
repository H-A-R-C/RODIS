// <copyright file="Interpolation.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace STEDI.Static
{
    using System;

    /// <summary>
    /// Provides linear and log-space interpolation helpers with explicit error reporting.
    /// </summary>
    public static class Interpolation
    {
        /// <summary>
        /// Performs linear interpolation of y with respect to x using linearly spaced x and y values.
        /// </summary>
        /// <param name="x">X value at which to interpolate.</param>
        /// <param name="xValues">Monotonic array of x coordinates (strictly ascending or strictly descending).</param>
        /// <param name="yValues">Array of y values corresponding to xValues.</param>
        /// <param name="isError">Set to true if interpolation cannot be performed.</param>
        /// <returns>Interpolated y value, or 0 if isError is true.</returns>
        /// <remarks>
        /// Interpolation fails if arrays are mismatched, xValues is not strictly monotonic, contains fewer than two points,
        /// or if x lies outside the range of xValues. Extrapolation is not performed. Exact endpoints return the endpoint y.
        /// </remarks>
        public static double InterpLinLin(double x, double[] xValues, double[] yValues, ref bool isError)
        {
            double y = 0;

            if (xValues == null || yValues == null || xValues.Length < 2 || xValues.Length != yValues.Length)
            {
                isError = true;
                return 0;
            }

            // Determine monotonic direction and validate strict monotonicity
            bool isAscending;
            if (xValues[1] > xValues[0]) isAscending = true;
            else if (xValues[1] < xValues[0]) isAscending = false;
            else
            {
                isError = true;
                return 0;
            }

            for (int i = 2; i < xValues.Length; i++)
            {
                if (isAscending)
                {
                    if (xValues[i] <= xValues[i - 1]) { isError = true; return 0; }
                }
                else
                {
                    if (xValues[i] >= xValues[i - 1]) { isError = true; return 0; }
                }
            }

            // Reject out-of-range values (no extrapolation)
            int last = xValues.Length - 1;
            if (isAscending)
            {
                if (x < xValues[0] || x > xValues[last]) { isError = true; return 0; }
            }
            else
            {
                if (x > xValues[0] || x < xValues[last]) { isError = true; return 0; }
            }

            // Handle exact endpoints explicitly (prevents out-of-range indexing at boundaries)
            if (x == xValues[0]) { isError = false; return yValues[0]; }
            if (x == xValues[last]) { isError = false; return yValues[last]; }

            // Locate the bracketing segment using binary search (O(log n))
            int lowerIndex;
            int upperIndex;

            if (isAscending)
            {
                int idx = Array.BinarySearch(xValues, x);
                if (idx >= 0)
                {
                    // Exact match at an interior point: use segment [idx, idx+1] (gives y exactly yValues[idx])
                    lowerIndex = idx;
                    upperIndex = idx + 1;
                }
                else
                {
                    int insertionPoint = ~idx; // first index with xValues[insertionPoint] > x
                    lowerIndex = insertionPoint - 1;
                    upperIndex = insertionPoint;
                }
            }
            else
            {
                // Binary search for descending array: find insertion point for x to maintain descending order
                int lo = 0;
                int hi = last;
                int foundIndex = -1;

                while (lo <= hi)
                {
                    int mid = lo + ((hi - lo) / 2);
                    if (xValues[mid] == x)
                    {
                        foundIndex = mid;
                        break;
                    }

                    // Descending: larger values are at smaller indices
                    if (xValues[mid] > x)
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }

                if (foundIndex >= 0)
                {
                    lowerIndex = foundIndex;
                    upperIndex = foundIndex + 1;
                }
                else
                {
                    // lo is the insertion point (first index where xValues[index] < x)
                    lowerIndex = lo - 1;
                    upperIndex = lo;
                }
            }

            // Safety clamp: should not be required due to range checks, but keeps behaviour stable if inputs are unusual
            if (lowerIndex < 0 || upperIndex > last) { isError = true; return 0; }

            double x0 = xValues[lowerIndex];
            double x1 = xValues[upperIndex];
            double y0 = yValues[lowerIndex];
            double y1 = yValues[upperIndex];

            // Compute linear interpolation (works for both ascending and descending x)
            y = y0 + (y1 - y0) * (x - x0) / (x1 - x0);

            isError = false;
            return y;
        }

        /// <summary>
        /// Performs linear interpolation in x and logarithmic interpolation in y.
        /// </summary>
        /// <param name="x">X value at which to interpolate.</param>
        /// <param name="xValues">Monotonic array of x coordinates.</param>
        /// <param name="yValues">Array of y values corresponding to xValues (should be positive for log interpolation).</param>
        /// <param name="isError">Set to true if interpolation cannot be performed.</param>
        /// <returns>Interpolated y value, or 0 if isError is true.</returns>
        /// <remarks>
        /// Non-positive y values cannot be logged and are treated as errors; a placeholder is used to allow the computation to proceed,
        /// but callers should treat any isError==true result as invalid.
        /// </remarks>
        public static double InterpLinLog(double x, double[] xValues, double[] yValues, ref bool isError)
        {
            double y = 0;
            isError = false;

            double[] logyValues = new double[yValues.Length];
            for (int i = 0; i < yValues.Length; ++i)
            {
                if (yValues[i] > 0)
                {
                    logyValues[i] = Math.Log(yValues[i]);
                }
                else
                {
                    logyValues[i] = double.MinValue;
                    isError = true;
                }
            }

            bool isErrorFromInterpLinLin = false;
            double logy = InterpLinLin(x, xValues, logyValues, ref isErrorFromInterpLinLin);
            y = Math.Exp(logy);
            isError = isError || isErrorFromInterpLinLin;

            return y;
        }

        /// <summary>
        /// Performs logarithmic interpolation in x and linear interpolation in y.
        /// </summary>
        /// <param name="x">X value at which to interpolate (must be positive for log interpolation).</param>
        /// <param name="xValues">Array of x coordinates (should be positive for log interpolation).</param>
        /// <param name="yValues">Array of y values corresponding to xValues.</param>
        /// <param name="isError">Set to true if interpolation cannot be performed.</param>
        /// <returns>Interpolated y value, or 0 if isError is true.</returns>
        public static double InterpLogLin(double x, double[] xValues, double[] yValues, ref bool isError)
        {
            double y = 0;
            isError = false;

            double logx;
            if (x > 0)
            {
                logx = Math.Log(x);
            }
            else
            {
                logx = double.MinValue;
                isError = true;
            }

            double[] logxValues = new double[xValues.Length];
            for (int i = 0; i < xValues.Length; ++i)
            {
                if (xValues[i] > 0)
                {
                    logxValues[i] = Math.Log(xValues[i]);
                }
                else
                {
                    logxValues[i] = double.MinValue;
                    isError = true;
                }
            }

            bool isErrorFromInterpLinLin = false;
            y = InterpLinLin(logx, logxValues, yValues, ref isErrorFromInterpLinLin);
            isError = isError || isErrorFromInterpLinLin;

            return y;
        }

        /// <summary>
        /// Performs logarithmic interpolation in both x and y.
        /// </summary>
        /// <param name="x">X value at which to interpolate (must be positive for log interpolation).</param>
        /// <param name="xValues">Array of x coordinates (should be positive and monotonic for log interpolation).</param>
        /// <param name="yValues">Array of y values (should be positive for log interpolation).</param>
        /// <param name="isError">Set to true if interpolation cannot be performed.</param>
        /// <returns>Interpolated y value, or 0 if isError is true.</returns>
        public static double InterpLogLog(double x, double[] xValues, double[] yValues, ref bool isError)
        {
            double y = 0;
            isError = false;

            double logx;
            if (x > 0)
            {
                logx = Math.Log(x);
            }
            else
            {
                logx = double.MinValue;
                isError = true;
            }

            double[] logxValues = new double[xValues.Length];
            for (int i = 0; i < xValues.Length; ++i)
            {
                if (xValues[i] > 0)
                {
                    logxValues[i] = Math.Log(xValues[i]);
                }
                else
                {
                    logxValues[i] = double.MinValue;
                    isError = true;
                }
            }

            bool isErrorFromInterpLinLog = false;
            y = InterpLinLog(logx, logxValues, yValues, ref isErrorFromInterpLinLog);
            isError = isError || isErrorFromInterpLinLog;

            return y;
        }

        /// <summary>
        /// Attempts linear interpolation of y with respect to x and returns success as a boolean.
        /// </summary>
        /// <param name="x">X value at which to interpolate.</param>
        /// <param name="xValues">Monotonic array of x coordinates.</param>
        /// <param name="yValues">Array of y values corresponding to xValues.</param>
        /// <param name="y">Interpolated y value.</param>
        /// <returns>true if interpolation succeeds; otherwise false.</returns>
        /// <remarks>
        /// This is a non-breaking convenience wrapper over InterpLinLin that avoids ref AllParameters at call sites.
        /// </remarks>
        public static bool TryInterpLinLin(double x, double[] xValues, double[] yValues, out double y)
        {
            bool isError = false;
            y = InterpLinLin(x, xValues, yValues, ref isError);
            if (isError)
            {
                y = 0;
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Attempts linear-x, log-y interpolation and returns success as a boolean.
        /// </summary>
        /// <param name="x">X value at which to interpolate.</param>
        /// <param name="xValues">Monotonic array of x coordinates.</param>
        /// <param name="yValues">Array of y values corresponding to xValues.</param>
        /// <param name="y">Interpolated y value.</param>
        /// <returns>true if interpolation succeeds; otherwise false.</returns>
        public static bool TryInterpLinLog(double x, double[] xValues, double[] yValues, out double y)
        {
            bool isError = false;
            y = InterpLinLog(x, xValues, yValues, ref isError);
            if (isError)
            {
                y = 0;
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Attempts log-x, linear-y interpolation and returns success as a boolean.
        /// </summary>
        /// <param name="x">X value at which to interpolate.</param>
        /// <param name="xValues">Array of x coordinates.</param>
        /// <param name="yValues">Array of y values corresponding to xValues.</param>
        /// <param name="y">Interpolated y value.</param>
        /// <returns>true if interpolation succeeds; otherwise false.</returns>
        public static bool TryInterpLogLin(double x, double[] xValues, double[] yValues, out double y)
        {
            bool isError = false;
            y = InterpLogLin(x, xValues, yValues, ref isError);
            if (isError)
            {
                y = 0;
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Attempts log-x, log-y interpolation and returns success as a boolean.
        /// </summary>
        /// <param name="x">X value at which to interpolate.</param>
        /// <param name="xValues">Array of x coordinates.</param>
        /// <param name="yValues">Array of y values corresponding to xValues.</param>
        /// <param name="y">Interpolated y value.</param>
        /// <returns>true if interpolation succeeds; otherwise false.</returns>
        public static bool TryInterpLogLog(double x, double[] xValues, double[] yValues, out double y)
        {
            bool isError = false;
            y = InterpLogLog(x, xValues, yValues, ref isError);
            if (isError)
            {
                y = 0;
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
