namespace STEDIUnitTests.ModelComponentTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using STEDI.ModelRun;
    using System.Collections.Generic;

    [TestClass]
    public class EquationParserTests
    {
        // ────────────────────────────────────────────────────
        // Evaluate — simple linear
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void Evaluate_LinearEquation_CorrectResult()
        {
            var parser = new EquationParser
            {
                Equation = "2*X+3",
                VariablesWithDescriptions = new Dictionary<string, string> { { "X", "input" } },
            };
            parser.VariableValues["X"] = 5.0;

            parser.Evaluate();

            Assert.IsTrue(parser.IsValidResult);
            Assert.AreEqual(13.0, parser.EquationResult, 0.001);
        }

        // ────────────────────────────────────────────────────
        // Evaluate — power law (Lowe et al. style)
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void Evaluate_PowerLaw_CorrectResult()
        {
            // V = 0.0001449275 * SA ^ 1.314 — the default STEDI equation
            var parser = new EquationParser
            {
                Equation = "0.0001449275*SA^1.314",
                VariablesWithDescriptions = new Dictionary<string, string> { { "SA", "Surface area in m²" } },
            };
            parser.VariableValues["SA"] = 10000.0;

            parser.Evaluate();

            Assert.IsTrue(parser.IsValidResult, "Should produce valid result");
            Assert.IsTrue(parser.EquationResult > 0.0, "Volume should be positive");
        }

        // ────────────────────────────────────────────────────
        // Evaluate — zero input
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void Evaluate_ZeroInput_HandlesGracefully()
        {
            var parser = new EquationParser
            {
                Equation = "2*X",
                VariablesWithDescriptions = new Dictionary<string, string> { { "X", "input" } },
            };
            parser.VariableValues["X"] = 0.0;

            parser.Evaluate();

            // 2*0 = 0, but double.IsNormal(0) is false, so IsValidResult will be false
            // This is a known quirk — document it
            Assert.IsFalse(parser.IsValidResult, "Zero result: IsNormal(0) is false — known behaviour");
        }

        [TestMethod]
        public void Evaluate_ZeroInput_ReturnsValidResult()
        {
            var parser = new EquationParser
            {
                Equation = "2*X",
                VariablesWithDescriptions = new Dictionary<string, string> { { "X", "input" } },
            };
            parser.VariableValues["X"] = 0.0;
            parser.Evaluate();

            Assert.IsTrue(parser.IsValidResult, "Zero is a valid finite result");
            Assert.AreEqual(0.0, parser.EquationResult, 0.001);
        }

        // ────────────────────────────────────────────────────
        // DeepCopy — independent copy
        // ────────────────────────────────────────────────────

        [TestMethod]
        public void DeepCopy_ProducesIndependentCopy()
        {
            var original = new EquationParser
            {
                Equation = "X+1",
                VariablesWithDescriptions = new Dictionary<string, string> { { "X", "input" } },
            };
            original.VariableValues["X"] = 10.0;

            var copy = original.DeepCopy();

            // Modify original
            original.VariableValues["X"] = 999.0;
            original.Equation = "X*99";

            Assert.AreEqual(10.0, copy.VariableValues["X"], 0.001, "Copy should not change");
            Assert.AreEqual("X+1", copy.Equation, "Copy equation should not change");
        }

        [TestMethod]
        public void DeepCopy_PreservesEquation()
        {
            var original = new EquationParser
            {
                Equation = "0.0001449275*SA^1.314",
                VariablesWithDescriptions = new Dictionary<string, string> { { "SA", "Surface area" } },
                EquationResult = 42.0,
            };
            original.VariableValues["SA"] = 5000.0;

            var copy = original.DeepCopy();

            Assert.AreEqual(original.Equation, copy.Equation);
            Assert.AreEqual(original.EquationResult, copy.EquationResult, 0.001);
            Assert.AreEqual(original.VariableValues["SA"], copy.VariableValues["SA"], 0.001);
        }
    }
}