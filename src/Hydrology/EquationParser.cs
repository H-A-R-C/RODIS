// <copyright file="EquationParser.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.ModelRun
{
    using NCalc;
    using RODIS.Static;

    // TODO: Minor - clean up Style cop warnings, improve comments.

    /// <summary>
    /// Parser for equations.
    /// </summary>
    public class EquationParser
    {
        /// <summary>Dictionary mapping variable names to their descriptions.</summary>
        public Dictionary<string, string> VariablesWithDescriptions = new Dictionary<string, string>();

        /// <summary>Dictionary mapping variable names to their current numeric values.</summary>
        public Dictionary<string, double> VariableValues = new Dictionary<string, double>();

        /// <summary>Gets or sets the equation string to evaluate (e.g. "0.0001449275*SA^1.314").</summary>
        public string Equation { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the last call to Evaluate produced a valid finite result.</summary>
        public bool IsValidResult { get; set; } = false;

        /// <summary>Gets or sets numeric result of the last call to Evaluate. NaN if not yet evaluated or invalid.</summary>
        public double EquationResult { get; set; } = double.NaN;

        /// <summary>Gets the error message from the last failed Evaluate call, or empty if successful.</summary>
        public string ErrorMessage { get; private set; } = string.Empty;

        /// <summary>Evaluates the equation with current variable values, setting IsValidResult and EquationResult.</summary>
        public void Evaluate()
        {
            this.IsValidResult = false;
            this.EquationResult = double.NaN;
            this.ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(this.Equation))
            {
                this.ErrorMessage = "Equation string is null or empty.";
                return;
            }

            try
            {
                string equationRewritePowers = NCalcVariableValidator.RewritePowers(this.Equation);
                Expression expression = new Expression(equationRewritePowers);

                foreach (var variable in this.VariablesWithDescriptions)
                {
                    if (!this.VariableValues.ContainsKey(variable.Key))
                    {
                        this.ErrorMessage = $"Variable '{variable.Key}' is defined but has no value assigned in VariableValues.";
                        return;
                    }

                    double paramValue = this.VariableValues[variable.Key];
                    string sanitisedName = variable.Key;
                    expression.Parameters[sanitisedName] = paramValue;
                }

                object resultAsObject = expression.Evaluate();
                if (resultAsObject != null)
                {
                    if (resultAsObject is int intValue)
                    {
                        this.IsValidResult = true;
                        this.EquationResult = (double)intValue;
                    }
                    else if (resultAsObject is double doubleValue)
                    {
                        if (double.IsFinite(doubleValue))
                        {
                            this.IsValidResult = true;
                            this.EquationResult = doubleValue;
                        }
                        else
                        {
                            this.ErrorMessage = $"Equation produced a non-finite result ({doubleValue}) for '{this.Equation}'.";
                        }
                    }
                    else
                    {
                        this.ErrorMessage = $"Equation returned unexpected type '{resultAsObject.GetType().Name}' for '{this.Equation}'.";
                    }
                }
                else
                {
                    this.ErrorMessage = $"Equation returned null for '{this.Equation}'.";
                }
            }
            catch (Exception ex)
            {
                this.ErrorMessage = $"Failed to evaluate equation '{this.Equation}': {ex.Message}";
            }
        }

        /// <summary>Returns a human-readable string describing which characters and names are invalid for NCalc variable names.</summary>
        /// <returns>Feedback string listing invalid characters and reserved names.</returns>
        public string GetValidVariableNamesFeedback ()
        {
            string result = "Valid variable names must not contain the character sequences ";
            foreach (char token in NCalcVariableValidator.InvalidCharacters)
            {
                result += token + ", ";
            }

            result += "and must not have complete names of ";
            foreach (string name in NCalcVariableValidator.ReservedNames)
            {
                result += name.ToLower() + ", ";
                result += name.ToUpper();
                if (name != NCalcVariableValidator.ReservedNames.Last())
                {
                    result += ", ";
                }
            }

            return result;
        }

        /// <summary>Returns true if the equation string is syntactically valid for NCalc evaluation.</summary>
        /// <returns>True if the equation can be parsed without errors.</returns>
        public bool IsValidEquation()

        {
            if (string.IsNullOrWhiteSpace(this.Equation))
                return false;

            try
            {
                string equationRewritePowers = NCalcVariableValidator.RewritePowers(this.Equation);
                Expression expression = new Expression(equationRewritePowers);

                // Provide dummy values for all known variables so NCalc can parse the expression
                foreach (var variable in this.VariablesWithDescriptions)
                {
                    string sanitisedName = variable.Key;
                    expression.Parameters[sanitisedName] = 1.0;
                }

                // HasErrors() checks syntax without fully evaluating
                return !expression.HasErrors();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Checks each variable name against NCalc naming rules.</summary>
        /// <returns>Array of booleans, one per variable in VariablesWithDescriptions, true if the name is valid.</returns>
        public bool[] AreValidVariables()
        {
            List<bool> result = new List<bool>();
            foreach (string name in this.VariablesWithDescriptions.Keys)
            {
                bool isThisValid = NCalcVariableValidator.IsValidVariableName(name);
                result.Add(isThisValid);
            }
            return result.ToArray();
        }

        /// <summary>Performs a deep copy of this EquationParser, including all variable definitions and values.</summary>
        /// <returns>Independent deep copy of this equation parser.</returns>
        public EquationParser DeepCopy()
        {
            EquationParser result = new EquationParser()
            {
                Equation = this.Equation,
                EquationResult = this.EquationResult,
            };

            result.VariablesWithDescriptions.Clear();
            result.VariableValues.Clear();

            if (this.VariablesWithDescriptions != null)
            {
                foreach (KeyValuePair<string, string> kvp in this.VariablesWithDescriptions)
                {
                    result.VariablesWithDescriptions.Add(kvp.Key, kvp.Value);
                }
            }

            if (this.VariableValues != null)
            {
                foreach (KeyValuePair<string, double> kvp in this.VariableValues)
                {
                    result.VariableValues.Add(kvp.Key, kvp.Value);
                }
            }

            return result;
        }
    }
}
