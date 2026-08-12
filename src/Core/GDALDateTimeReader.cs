// <copyright file="GdalDateTimeReader.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace STEDI.Static
{
    using System.Globalization;
    using OSGeo.OGR;

    /// <summary>Reads date and datetime values from GDAL/OGR vector data sources, with heuristic parsing for multiple field types.</summary>
    public class GdalDateTimeReader
    {
        /// <summary>Date and datetime format strings tried during string-based parsing, in priority order.</summary>
        public readonly string[] ParseFormats = new[]
                {
                    "yyyy-MM-dd",
                    "yyyy-MM-ddTHH:mm:ssZ",
                    "yyyy-MM-ddTHH:mm:ss",
                    "yyyy-MM-dd HH:mm:ss",
                    "yyyy-MM-ddTHH:mm:ss.fffZ",
                    "yyyy-MM-ddTHH:mm:ss.fff",
                    "o",
                    "s",
                };

        /// <summary>Reads a date or datetime value from a single OGR feature field.</summary>
        /// <param name="fieldIndex">Zero-based index of the field in the feature definition.</param>
        /// <param name="fieldType">OGR field type of the field.</param>
        /// <param name="feat">OGR Feature to read from.</param>
        /// <returns>Parsed DateTime, or null if the value cannot be interpreted as a date.</returns>
        public DateTime? ReadFromFeature(int fieldIndex, OSGeo.OGR.FieldType fieldType, Feature feat)
        {
            if (feat == null)
                throw new ArgumentNullException(nameof(feat));

            DateTime? result = null;

            if (fieldType == OSGeo.OGR.FieldType.OFTDate || fieldType == OSGeo.OGR.FieldType.OFTDateTime)
            {
                // 'second' is float in GDAL C# binding
                int year = 0, month = 0, day = 0, hour = 0, minute = 0, tzFlag = 0;
                float second = 0f;

                // GetFieldAsDateTime returns void; it populates the out parameters.
                // Call it and then inspect the out parameters to determine validity.
                try
                {
                    feat.GetFieldAsDateTime(fieldIndex, out year, out month, out day, out hour, out minute, out second, out tzFlag);
                }
                catch
                {
                    // If the binding throws for some reason, fall back to string parsing below.
                    year = month = day = 0;
                }

                // If year/month/day are non-zero, treat as valid date/time
                if (year > 0 && month > 0 && day > 0)
                {
                    if (fieldType == OSGeo.OGR.FieldType.OFTDate)
                    {
                        // Date-only: return midnight, unspecified kind
                        try
                        {
                            result = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
                        }
                        catch
                        {
                            result = null;
                        }

                    }
                    else // OFTDateTime
                    {
                        DateTimeKind kind = tzFlag == 1 ? DateTimeKind.Utc : DateTimeKind.Unspecified;
                        int sec = (int)Math.Floor(second);
                        int ms = (int)Math.Round((second - sec) * 1000.0f);
                        if (ms >= 1000) { sec += 1; ms -= 1000; }

                        try
                        {
                            result = new DateTime(year, month, day, hour, minute, sec, ms, kind);
                        }
                        catch
                        {
                            result = null;
                        }
                    }
                }
                else
                {
                    // Fallback to string parsing if GetFieldAsDateTime didn't produce valid values
                    string raw = feat.GetFieldAsString(fieldIndex);
                    result = ParseStringToDateTime(raw, this.ParseFormats);
                }
            }
            else if (fieldType == OSGeo.OGR.FieldType.OFTString || fieldType == OSGeo.OGR.FieldType.OFTWideString)
            {
                string raw = feat.GetFieldAsString(fieldIndex);
                result = ParseStringToDateTime(raw, this.ParseFormats);

                if (result == null)
                {
                    long intVal = feat.GetFieldAsInteger64(fieldIndex);
                    if (intVal >= 1800 && intVal < 3000)
                    {
                        // year only
                        result = new DateTime((int)intVal, 1, 1);
                    }
                }
            }
            else
            {
                // integer/real heuristics
                if (fieldType == OSGeo.OGR.FieldType.OFTInteger || fieldType == OSGeo.OGR.FieldType.OFTInteger64)
                {
                    long intVal = feat.GetFieldAsInteger64(fieldIndex);
                    if (intVal >= 10000101 && intVal <= 99991231)
                    {
                        string s = intVal.ToString();
                        if (DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                            result = dt;
                    }

                    if (result == null)
                    {
                        if (intVal >= 1800 && intVal < 3000)
                        {
                            // year only
                            result = new DateTime((int) intVal, 1, 1);
                        }

                        if (result == null)
                        {
                            DateTime epoch = DateTime.UnixEpoch;
                            if (intVal >= 0 && intVal <= 4102444800L)
                                result = epoch.AddSeconds(intVal);
                        }
                    }
                }
                else if (fieldType == OSGeo.OGR.FieldType.OFTReal)
                {
                    double dbl = feat.GetFieldAsDouble(fieldIndex);
                    if (dbl > 59 && dbl < 60000)
                    {
                        try
                        {
                            DateTime excelBase = new DateTime(1899, 12, 31);
                            result = excelBase.AddDays(dbl);
                        }
                        catch { }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Read a date/datetime field from a vector datasource and return one DateTime? per feature.
        /// </summary>
        /// <param name="path">Path to shapefile (.shp) or geopackage (.gpkg)</param>
        /// <param name="layerName">Optional layer name. If null or empty, the first layer is used.</param>
        /// <param name="fieldName">Attribute field name that contains date or datetime values</param>
        /// <returns>IEnumerable of nullable DateTime values in the order of features</returns>
        public IEnumerable<DateTime?> ReadDateField(string path, string layerName, string fieldName)
        {
            Ogr.RegisterAll();

            using (DataSource ds = Ogr.Open(path, 0))
            {
                if (ds == null)
                    throw new InvalidOperationException($"Could not open datasource: {path}");

                Layer layer = string.IsNullOrEmpty(layerName) ? ds.GetLayerByIndex(0) : ds.GetLayerByName(layerName);
                if (layer == null)
                    throw new InvalidOperationException($"Layer not found: {layerName ?? "(first layer)"}");

                FeatureDefn defn = layer.GetLayerDefn();
                int fieldIndex = defn.GetFieldIndex(fieldName);
                if (fieldIndex < 0)
                    throw new InvalidOperationException($"Field not found: {fieldName}");

                FieldDefn fieldDef = defn.GetFieldDefn(fieldIndex);
                OSGeo.OGR.FieldType fieldType = fieldDef.GetFieldType();

                layer.ResetReading();
                Feature feat = null;
                try
                {
                    while ((feat = layer.GetNextFeature()) != null)
                    {
                        DateTime? result = ReadFromFeature(fieldIndex, fieldType, feat);

                        yield return result;

                        feat.Dispose();
                        feat = null;
                    }
                }
                finally
                {
                    if (feat != null)
                        feat.Dispose();
                }
            }
        }

        /// <summary>
        /// Try multiple parse strategies for string date/datetime values.
        /// Returns null if parsing fails.
        /// </summary>
        private static DateTime? ParseStringToDateTime(string raw, string[] formats)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();

            // Try exact formats first (invariant culture)
            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dtExact))
                return dtExact;

            // Try ISO 8601 round-trip parse
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dtRound))
                return dtRound;

            // Try parsing as date-only and return midnight
            if (DateTime.TryParseExact(raw, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtDateOnly))
                return DateTime.SpecifyKind(dtDateOnly.Date, DateTimeKind.Unspecified);

            // Last resort: general parse
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtAny))
                return dtAny;

            return null;
        }
    }
}
