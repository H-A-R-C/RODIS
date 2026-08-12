// <copyright file="NCalcVariableValidator.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace STEDI.Static
{
    using System.Text.RegularExpressions;

    public static class NCalcVariableValidator
    {
        /// <summary>Reserved NCalc keywords and built-in function names that cannot be used as variable names.</summary>
        public static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Built-in functions
            "abs", "acos", "asin", "atan", "ceiling", "cos", "exp", "floor", "log", "log10",
            "round", "sin", "sqrt", "tan", "max", "min", "pow",

            // Logical and control keywords
            "if", "in", "not", "and", "or", "true", "false", "null"
        };

        /// <summary>Characters that are invalid in NCalc variable names (operators, brackets, quotes, whitespace).</summary>
        public static readonly char[] InvalidCharacters = 
        {
            '+', '-', '*', '/', '%', '^', '=', '<', '>', '!', '&', '|',
            '(', ')', '[', ']', '{', '}', ',', '.', '\'', '"', ' '
        };

        /// <summary>
        /// Checks if a variable name is valid for use in NCalc.
        /// </summary>
        public static bool IsValidVariableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Must start with a letter or underscore, and contain only letters, digits, or underscores
            if (!Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                return false;

            // Must not be a reserved keyword
            if (ReservedNames.Contains(name))
                return false;

            // Must not contain invalid characters
            if (name.IndexOfAny(InvalidCharacters) >= 0)
                return false;

            return true;
        }

        /// <summary>Sanitises a variable name by prefixing and replacing non-word characters if it is unsafe for NCalc.</summary>
        /// <param name="name">Raw variable name.</param>
        /// <returns>Safe variable name for NCalc.</returns>
        public static string SanitizeVariableName(string name)
        {
            return IsValidVariableName(name) ? name : $"var_{Regex.Replace(name, @"\W", "_")}";
        }

        /// <summary>Rewrites power expressions (a^b) to NCalc Pow(a,b) function calls.</summary>
        /// <param name="expression">Expression string potentially containing ^ operators.</param>
        /// <returns>Expression with ^ replaced by Pow() calls.</returns>
        public static string RewritePowers(string expression)
        {
            // Regex to match expressions like a ^ b, where a and b are numbers or variables
            string pattern = @"(?<left>\b[\w\.]+)\s*\^\s*(?<right>[\w\.]+)";
            string rewritten = Regex.Replace(expression, pattern, "Pow(${left},${right})");

            return rewritten;
        }
    }
}
