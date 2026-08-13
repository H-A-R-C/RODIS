// <copyright file="NormalDistribution.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.Statistics
{
    /// <summary>
    /// Normal distribution.
    /// </summary>
    public class NormalDistribution
    {
        private static double root2Pi = Math.Sqrt(2 * Math.PI);

        /// <summary>
        /// Generates a random value using the standard Gaussian or normal distribution, mean = 0, standard deviation = 1.
        /// </summary>
        /// <param name="random">Random number generator.</param>
        /// <returns>Standard gaussian distributed random value.</returns>
        public static double NextStandardGaussian(XORShift random)
        {
            // Draw two uniform random values
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();

            // Convert to normally distributed value.
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

            return randStdNormal;
        }

        /// <summary>
        /// Generates a random value using the Gaussian or normal distribution, with specified mean and standard deviation.
        /// </summary>
        /// <param name="random">Random number generator.</param>
        /// <param name="mean">Mean.</param>
        /// <param name="standardDeviation">Standard deviation.</param>
        /// <returns>Gaussian distributed random value.</returns>
        public static double NextGaussian(XORShift random, double mean, double standardDeviation)
        {
            double randStdNormal = NextStandardGaussian(random);

            return mean + (standardDeviation * randStdNormal);
        }

        /// <summary>
        /// Evaluate normal distribution PDF.
        /// </summary>
        /// <param name="x">Value to evaluate PDF at.</param>
        /// <param name="mean">PDF mean.</param>
        /// <param name="standardDeviation">PDF standard deviation.</param>
        /// <returns>PDF value.</returns>
        public static double NormalPDF(double x, double mean, double standardDeviation)
        {
            return 1.0 / Math.Sqrt(2.0 * Math.PI * Math.Pow(standardDeviation, 2.0)) * Math.Exp((-Math.Pow(x - mean, 2.0)) / (2.0 * Math.Pow(standardDeviation, 2.0)));
        }

        /// <summary>
        /// Evaluate normal distribution PDF.
        /// </summary>
        /// <param name="x">Value to evaluate PDF at.</param>
        /// <param name="mean">PDF mean.</param>
        /// <param name="standardDeviation">PDF standard deviation.</param>
        /// <returns>PDF value.</returns>
        public static double StandardNormalPDF(double x)
        {
            double exponent = -0.5 * Math.Pow(x, 2);
            if (exponent < -1e100)
            {
                return 0;
            } 
            else
            {
                return Math.Exp(exponent) / root2Pi;
            }
        }

        /// <summary>
        /// Evaluates the normal distribution CDF at the given z standard value. From Handbook of Mathematical Functions by Abramowitz and Stegun.
        /// </summary>
        /// <param name="z">Z standard.</param>
        /// <returns>Exceedance probability.</returns>
        public static double StandardNormalCDF(double z)
        {
            // Constants
            double a1 = 0.254829592;
            double a2 = -0.284496736;
            double a3 = 1.421413741;
            double a4 = -1.453152027;
            double a5 = 1.061405429;
            double p = 0.3275911;

            // Save the sign of x
            int sign = 1;
            if (z < 0)
            {
                sign = -1;
            }

            z = Math.Abs(z) / Math.Sqrt(2.0);

            // A&S formula 7.1.26
            double t = 1.0 / (1.0 + (p * z));
            double y = 1.0 - (((((((((a5 * t) + a4) * t) + a3) * t) + a2) * t) + a1) * t * Math.Exp(-z * z));

            // As concerned with exceedance probability flip tail
            return 1 - (0.5 * (1.0 + (sign * y)));
        }

        /// <summary>
        /// Inverts the normal distribution CDF. Obtained from p191 "Applied Statistics Algorithms" by Griffiths & Hill(Eds).
        /// </summary>
        /// <param name="p">Exceedance probability.</param>
        /// <returns>Z standard.</returns>
        public static double InvertStandardNormalCDF(double p)
        {
            const double A0 = 2.50662823884E0;
            const double A1 = -18.61500062529E0;
            const double A2 = 41.39119773534E0;
            const double A3 = -25.44106049637E0;
            const double B1 = -8.47351093090E0;
            const double B2 = 23.08336743743E0;
            const double B3 = -21.06224101826E0;
            const double B4 = 3.13082909833E0;
            const double C0 = -2.78718931138E0;
            const double C1 = -2.29796479134E0;
            const double C2 = 4.85014127135E0;
            const double C3 = 2.32121276858E0;
            const double D1 = 3.54388924762E0;
            const double D2 = 1.63706781897E0;

            // Check input probabilty is in bounds
            if (p > 1)
            {
                throw new ArgumentException(string.Format("Attempted to invert probability > 1: {0:0.0000}", p));
            }

            if (p < 0)
            {
                throw new ArgumentException(string.Format("Attempted to invert probability < 0: {0:0.0000}", p));
            }

            // As concerned with exceedance probabilities flip the tail of calculation
            p = 1 - p;

            double q = p - 0.5;
            if (Math.Abs(q) > 0.42E0)
            {
                double r1 = p;
                if (q > 0)
                {
                    r1 = 1 - p;
                }

                if (r1 < 0)
                {
                    return 0;
                }
                else
                {
                    r1 = Math.Sqrt(-Math.Log(r1));
                    double ppnd = ((((((C3 * r1) + C2) * r1) + C1) * r1) + C0) / ((((D2 * r1) + D1) * r1) + 1);
                    if (q < 0)
                    {
                        ppnd = -ppnd;
                    }

                    return ppnd;
                }
            }
            else
            {
                double r = q * q;
                double z = q * ((((((A3 * r) + A2) * r) + A1) * r) + A0) / ((((((((B4 * r) + B3) * r) + B2) * r) + B1) * r) + 1);
                return z;
            }
        }
    }
}
