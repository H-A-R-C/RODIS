// <copyright file="Copiers.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.Static
{
    /// <summary>Utility methods for deep-copying common collection types.</summary>
    public class Copiers
    {
        /// <summary>Creates an independent copy of a string-to-string dictionary.</summary>
        /// <param name="input">Dictionary to copy.</param>
        /// <returns>New dictionary with the same key-value pairs.</returns>
        public static Dictionary<string, string> DictionaryCopier (Dictionary<string,string> input)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();

            foreach (KeyValuePair<string, string> kvp in input)
            {
                result.Add(kvp.Key, kvp.Value);
            }

            return result;
        }
    }
}
