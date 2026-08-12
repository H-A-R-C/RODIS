// <copyright file="JsonUtils.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace STEDI.JSON
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Json;

    public static class JsonUtils
    {
        /// <summary>
        /// Loads a JSON file whose root is an object and each property is a scenario object, producing Dictionary&lt;string, Dictionary&lt;string, string&gt;&gt;.
        /// Arrays and objects are coerced to JSON text strings via GetRawText() by default.
        /// Set compactArraysAndObjects=true to write them without whitespace.
        /// </summary>
        /// <param name="path">Path to strings.</param>
        /// <param name="compactArraysAndObjects"></param>
        public static Dictionary<string, Dictionary<string, string>> LoadScenarioDictionaryAsStrings(
            string path,
            bool compactArraysAndObjects = false)
        {
            var json = File.ReadAllText(path);
            return ParseScenarioDictionaryAsStrings(json, compactArraysAndObjects);
        }

        /// <summary>
        /// Async variant for large files or server-side code.
        /// </summary>
        public static async System.Threading.Tasks.Task<Dictionary<string, Dictionary<string, string>>> LoadScenarioDictionaryAsStringsAsync(
            string path,
            bool compactArraysAndObjects = false)
        {
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            return ParseScenarioDictionaryAsStrings(json, compactArraysAndObjects);
        }

        /// <summary>
        /// Core parser: arrays/objects → raw JSON text (or compact).
        /// Numbers/booleans/null/strings → string values.
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> ParseScenarioDictionaryAsStrings(
            string json,
            bool compactArraysAndObjects = false)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(json, options)
                      ?? new Dictionary<string, Dictionary<string, JsonElement>>();

            var result = new Dictionary<string, Dictionary<string, string>>(raw.Count);

            foreach (var scenarioKvp in raw)
            {
                var scenarioName = scenarioKvp.Key;
                var scenarioDict = scenarioKvp.Value;
                var inner = new Dictionary<string, string>(scenarioDict.Count);

                foreach (var entry in scenarioDict)
                {
                    var key = entry.Key;
                    var elem = entry.Value;
                    inner[key] = CoerceToString(elem, compactArraysAndObjects);
                }

                result[scenarioName] = inner;
            }

            return result;
        }

        private static string CoerceToString(JsonElement elem, bool compactArraysAndObjects)
        {
            switch (elem.ValueKind)
            {
                case JsonValueKind.String:
                    return elem.GetString() ?? string.Empty;

                case JsonValueKind.Number:
                    // Preserve integers as integers when possible, otherwise use invariant double
                    if (elem.TryGetInt64(out var i64))
                        return i64.ToString(CultureInfo.InvariantCulture);
                    if (elem.TryGetDouble(out var d))
                        return d.ToString(CultureInfo.InvariantCulture);
                    // fallback: raw text (covers decimals, etc.)
                    return elem.GetRawText();

                case JsonValueKind.True:
                    return "true";

                case JsonValueKind.False:
                    return "false";

                case JsonValueKind.Null:
                    return "null";

                case JsonValueKind.Array:
                case JsonValueKind.Object:
                    return compactArraysAndObjects ? CompactRaw(elem) : elem.GetRawText();

                default:
                    return elem.GetRawText();
            }
        }

        // Writes arrays/objects without whitespace (e.g., "[1900,1950,1955,1960,2100]")
        private static string CompactRaw(JsonElement e)
        {
            using var buffer = new MemoryStream();
            using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false });
            e.WriteTo(writer);
            writer.Flush();
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
