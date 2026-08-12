// <copyright file="BoxCoxTransformation.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace STEDI.Statistics
{
    /// <summary>
    /// Box–Cox transformation utilities.
    /// </summary>
    public static class BoxCoxTransformation
    {
        // Treat very small lambda as zero to improve numerical stability.
        private const double LambdaZeroTolerance = 1e-12;

        /// <summary>
        /// Applies the (optionally shifted) Box–Cox transformation.
        /// </summary>
        /// <param name="value">The value to transform.</param>
        /// <param name="lambda1">The Box–Cox lambda parameter.</param>
        /// <param name="lambda2">Optional shift; the transform is applied to <c>value + lambda2</c>.</param>
        /// <returns>The transformed value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if inputs are non-finite or if <c>value + lambda2 &lt;= 0</c>.
        /// </exception>
        public static double BoxCox(double value, double lambda1, double lambda2 = 0.0)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) ||
                double.IsNaN(lambda1) || double.IsInfinity(lambda1) ||
                double.IsNaN(lambda2) || double.IsInfinity(lambda2))
            {
                throw new ArgumentOutOfRangeException(
                    $"Inputs must be finite. value={value}, lambda1={lambda1}, lambda2={lambda2}.");
            }

            double z = value + lambda2;
            if (z <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Box–Cox requires value + lambda2 > 0. value={value}, lambda2={lambda2}.");
            }

            if (Math.Abs(lambda1) < LambdaZeroTolerance)
            {
                return Math.Log(z);
            }

            return (Math.Pow(z, lambda1) - 1.0) / lambda1;
        }

        /// <summary>
        /// Applies the inverse (optionally shifted) Box–Cox transformation.
        /// </summary>
        /// <param name="transformedValue">The transformed value.</param>
        /// <param name="lambda1">The Box–Cox lambda parameter used in the forward transform.</param>
        /// <param name="lambda2">Optional shift used in the forward transform.</param>
        /// <returns>The un-transformed value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if inputs are non-finite or if the inverse transform is not defined
        /// (i.e., <c>lambda1 * transformedValue + 1 &lt;= 0</c> when <c>lambda1 != 0</c>).
        /// </exception>
        public static double InverseBoxCox(double transformedValue, double lambda1, double lambda2 = 0.0)
        {
            if (double.IsNaN(transformedValue) || double.IsInfinity(transformedValue) ||
                double.IsNaN(lambda1) || double.IsInfinity(lambda1) ||
                double.IsNaN(lambda2) || double.IsInfinity(lambda2))
            {
                throw new ArgumentOutOfRangeException(
                    $"Inputs must be finite. transformedValue={transformedValue}, lambda1={lambda1}, lambda2={lambda2}.");
            }

            if (Math.Abs(lambda1) < LambdaZeroTolerance)
            {
                // exp() is always > 0, shift may make result negative (caller decides if that's acceptable).
                return Math.Exp(transformedValue) - lambda2;
            }

            double inner = (lambda1 * transformedValue) + 1.0;
            if (inner <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(transformedValue),
                    $"Inverse Box–Cox requires (lambda1 * transformedValue + 1) > 0. lambda1={lambda1}, transformedValue={transformedValue}.");
            }

            return Math.Pow(inner, 1.0 / lambda1) - lambda2;
        }
    }
}
