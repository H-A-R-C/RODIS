// <copyright file="UnitStringJsonConverter.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>

namespace RODIS.JSON
{
    using System;
    using System.Globalization;
    using Newtonsoft.Json;

    /// <summary>
    /// JSON converter that reads a value as either a bare number or a string with units.
    /// When a bare number is encountered, the specified default unit suffix is appended.
    /// When a string is encountered, it is returned as-is (assumed to already contain units).
    /// </summary>
    /// <remarks>
    /// Apply via attribute: [JsonConverter(typeof(UnitStringJsonConverter), "ML")]
    /// Supports Float, Integer, String, and Null JSON tokens.
    /// </remarks>
    public class UnitStringJsonConverter : JsonConverter<string>
    {
        private readonly string defaultUnit;

        /// <summary>Initialises the converter with a default unit suffix for bare numeric values.</summary>
        /// <param name="defaultUnit">Unit suffix to append when a bare number is read, e.g. "ML" or "km2".</param>
        public UnitStringJsonConverter(string defaultUnit)
        {
            this.defaultUnit = defaultUnit ?? throw new ArgumentNullException(nameof(defaultUnit));
        }

        /// <summary>Reads a JSON token as a unit string, appending default units to bare numbers.</summary>
        public override string ReadJson(JsonReader reader, Type objectType, string existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Float:
                case JsonToken.Integer:
                    double numericValue = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                    return numericValue.ToString(CultureInfo.InvariantCulture) + " " + this.defaultUnit;

                case JsonToken.String:
                    return (string)reader.Value ?? string.Empty;

                case JsonToken.Null:
                    return string.Empty;

                default:
                    throw new JsonSerializationException($"Unexpected token {reader.TokenType} when reading unit string. Expected a number or string at path '{reader.Path}'.");
            }
        }

        /// <summary>Writes the unit string value to JSON as a plain string.</summary>
        public override void WriteJson(JsonWriter writer, string value, JsonSerializer serializer)
        {
            writer.WriteValue(value ?? string.Empty);
        }
    }
}