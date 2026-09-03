// <copyright file="FormatHelpers.cs" company="HARC">
// Copyright (c) HARC Services Pty Ltd. All rights reserved.
// </copyright>
using System.Numerics;

public static class FormatHelpers
{
    public static string FormatWithLeadingZeros<T>(T value, int width)
        where T : struct, IFormattable
    {
        return value.ToString($"D{width}", null);
    }

    public static string FormatWithLeadingZerosGeneric<T>(T value, int width)
        where T : INumber<T>
    {
        return ((IFormattable)value).ToString($"D{width}", null);
    }
}

