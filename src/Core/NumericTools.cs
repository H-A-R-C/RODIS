// <copyright file="NumericTools.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.Static
{
    using System.Collections;

    /// <summary>Static helpers for numeric type checking, array counting, and enum parsing.</summary>
    public static class NumericTools
    {
        /// <summary>Returns the element count of an array or collection object.</summary>
        /// <param name="valuesObj">Array or ICollection instance (null returns 0).</param>
        /// <returns>Element count.</returns>
        public static int GetValueCount(object valuesObj)
        {
            if (valuesObj == null) return 0;

            if (valuesObj is Array arr) return arr.Length;

            if (valuesObj is ICollection coll) return coll.Count;

            throw new InvalidOperationException($"Unsupported trace values container type: {valuesObj.GetType().Name}");
        }


        /// <summary>Returns true if the type is a built-in numeric type (excluding enums).</summary>
        /// <param name="t">Type to test.</param>
        /// <returns>True if the type is numeric.</returns>
        public static bool IsNumericType(Type t)
        {
            if (t.IsEnum) return false;

            t = Nullable.GetUnderlyingType(t) ?? t;
            return t == typeof(byte) || t == typeof(sbyte) ||
                   t == typeof(short) || t == typeof(ushort) ||
                   t == typeof(int) || t == typeof(uint) ||
                   t == typeof(long) || t == typeof(ulong) ||
                   t == typeof(float) || t == typeof(double) ||
                   t == typeof(decimal);
        }

        /// <summary>Parses a string to an enum value (case-insensitive).</summary>
        /// <typeparam name="TEnum">Enum type to parse to.</typeparam>
        /// <param name="value">String representation of the enum value.</param>
        /// <returns>Parsed enum value.</returns>
        public static TEnum ParseEnum<TEnum>(string value) where TEnum : struct
        {
            if (!Enum.TryParse<TEnum>(value, true, out var result))
                throw new InvalidOperationException($"Invalid {typeof(TEnum).Name} value '{value}'.");

            return result;
        }
    }
}
