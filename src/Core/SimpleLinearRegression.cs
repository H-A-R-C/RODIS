// <copyright file="SimpleLinearRegression.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace STEDI.Static
{
    /// <summary>Ordinary least-squares simple linear regression (y = intercept + slope × x) with diagnostic statistics.</summary>
    public sealed class SimpleLinearRegression
    {
        /// <summary>Gets Fitted slope coefficient.</summary>
        public double Slope { get; private set; }

        /// <summary>Gets Fitted intercept coefficient.</summary>
        public double Intercept { get; private set; }

        /// <summary>Gets Coefficient of determination (R²).</summary>
        public double RSquared { get; private set; }

        /// <summary>Gets Residual variance (s²) with n − 2 degrees of freedom.</summary>
        public double ResidualVariance { get; private set; }

        /// <summary>Gets Standard error of the slope estimate.</summary>
        public double SE_Slope { get; private set; }

        /// <summary>Gets Standard error of the intercept estimate.</summary>
        public double SE_Intercept { get; private set; }

        /// <summary>Gets Number of observations used in the fit.</summary>
        public int Count { get; private set; }

        /// <summary>
        /// Fit a simple linear regression y = intercept + slope * x using ordinary least squares.
        /// Throws ArgumentNullException if x or y is null.
        /// Throws ArgumentException if lengths differ or Count &lt; 2 or if x has zero variance.
        /// </summary>
        public static SimpleLinearRegression Fit(double[] x, double[] y)
        {
            if (x is null)
            {
                throw new ArgumentNullException(nameof(x));
            }

            if (y is null)
            {
                throw new ArgumentNullException(nameof(y));
            }

            if (x.Length != y.Length)
            {
                throw new ArgumentException("x and y must have the same length.");
            }

            int n = x.Length;
            if (n < 2)
            {
                throw new ArgumentException("At least two observations are required.");
            }

            double xbar = x.Average();
            double ybar = y.Average();

            // Compute sums for Sxx and Sxy
            double sxx = 0.0;
            double sxy = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - xbar;
                double dy = y[i] - ybar;
                sxx += dx * dx;
                sxy += dx * dy;
            }

            if (sxx == 0.0 || double.IsNaN(sxx) || double.IsInfinity(sxx))
            {
                throw new ArgumentException("x has zero variance or invalid values; slope is undefined.");
            }

            double slope = sxy / sxx;
            double intercept = ybar - slope * xbar;

            // Residuals and residual variance
            double ssRes = 0.0;
            double ssTot = 0.0;
            for (int i = 0; i < n; i++)
            {
                double yi = y[i];
                double yhat = intercept + slope * x[i];
                double res = yi - yhat;
                ssRes += res * res;
                double dyt = yi - ybar;
                ssTot += dyt * dyt;
            }

            double df = n - 2;
            if (df <= 0)
            {
                throw new ArgumentException("Not enough degrees of freedom to estimate variance.");
            }

            double residualVariance = ssRes / df;

            double seSlope = Math.Sqrt(residualVariance / sxx);
            double seIntercept = Math.Sqrt(residualVariance * (1.0 / n + (xbar * xbar) / sxx));

            double rSquared = ssTot == 0.0 ? 1.0 : 1.0 - ssRes / ssTot;

            return new SimpleLinearRegression
            {
                Count = n,
                Slope = slope,
                Intercept = intercept,
                ResidualVariance = residualVariance,
                SE_Slope = seSlope,
                SE_Intercept = seIntercept,
                RSquared = rSquared
            };
        }

        /// <summary>
        /// Predict y for a given x using the fitted model. Throws InvalidOperationException if model not fitted.
        /// </summary>
        public double Predict(double x)
        {
            if (double.IsNaN(Slope) || double.IsNaN(Intercept))
            {
                throw new InvalidOperationException("Model appears not to be fitted.");
            }

            return Intercept + Slope * x;
        }
    }
}