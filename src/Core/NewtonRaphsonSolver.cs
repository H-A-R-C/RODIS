// <copyright file="NewtonRaphsonSolver.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace STEDI.Static
{
    using System;

    /// <summary>Newton-Raphson root-finding solver with analytic and numeric derivative overloads.</summary>
    public static class NewtonRaphsonSolver
    {
        /// <summary>Finds a root of f(x) = 0 using Newton-Raphson iteration with an analytic derivative.</summary>
        /// <param name="f">Function whose root is sought.</param>
        /// <param name="fPrime">Analytic derivative of f.</param>
        /// <param name="initialGuess">Starting estimate for the root.</param>
        /// <param name="tolerance">Convergence tolerance on successive x estimates.</param>
        /// <param name="maxIterations">Maximum number of iterations before failure.</param>
        /// <returns>Approximate root of f.</returns>
        public static double Solve(
            Func<double, double> f,
            Func<double, double> fPrime,
            double initialGuess,
            double tolerance = 1e-7,
            int maxIterations = 100)
        {
            if (f == null) throw new ArgumentNullException(nameof(f));
            if (fPrime == null) throw new ArgumentNullException(nameof(fPrime));

            double x = initialGuess;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                double fx = f(x);
                double dfx = fPrime(x);

                if (double.IsNaN(fx) || double.IsInfinity(fx))
                    throw new ArithmeticException("Function returned non-finite value.");

                if (double.IsNaN(dfx) || double.IsInfinity(dfx))
                    throw new ArithmeticException("Derivative returned non-finite value.");

                if (Math.Abs(dfx) < double.Epsilon)
                    throw new DivideByZeroException("Derivative too small; possible division by zero.");

                double nextX = x - fx / dfx;

                if (double.IsNaN(nextX) || double.IsInfinity(nextX))
                    throw new ArithmeticException("Iteration produced non-finite estimate.");

                if (Math.Abs(nextX - x) < tolerance)
                    return nextX;

                x = nextX;
            }

            throw new InvalidOperationException("Newton-Raphson did not converge within the maximum number of iterations.");
        }

        /// <summary>Finds a root of f(x) = 0 using Newton-Raphson iteration with a forward-difference numeric derivative.</summary>
        /// <param name="f">Function whose root is sought.</param>
        /// <param name="initialGuess">Starting estimate for the root.</param>
        /// <param name="tolerance">Convergence tolerance on successive x estimates (also used as the finite-difference step size).</param>
        /// <param name="maxIterations">Maximum number of iterations before failure.</param>
        /// <returns>Approximate root of f.</returns>
        public static double Solve(
            Func<double, double> f,
            double initialGuess,
            double tolerance = 1e-7,
            int maxIterations = 100)
        {
            if (f == null) throw new ArgumentNullException(nameof(f));

            double x = initialGuess;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                double fx = f(x);

                double dfx = (f(x + tolerance) - fx) / tolerance;

                if (double.IsNaN(fx) || double.IsInfinity(fx))
                    throw new ArithmeticException("Function returned non-finite value.");

                if (double.IsNaN(dfx) || double.IsInfinity(dfx))
                    throw new ArithmeticException("Derivative returned non-finite value.");

                if (Math.Abs(dfx) < 1e-14)
                    throw new DivideByZeroException("Derivative too small; possible division by zero.");

                double nextX = x - fx / dfx;

                if (double.IsNaN(nextX) || double.IsInfinity(nextX))
                    throw new ArithmeticException("Iteration produced non-finite estimate.");

                if (Math.Abs(nextX - x) < tolerance)
                    return nextX;

                x = nextX;
            }

            throw new InvalidOperationException("Newton-Raphson did not converge within the maximum number of iterations.");
        }
    }
}
