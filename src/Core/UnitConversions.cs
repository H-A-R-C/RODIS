// <copyright file="UnitConversions.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace STEDI.Static
{
    using UnitsNet;
    using UnitsNet.Units;

    public static class UnitConversions
    {
        /// <summary>
        /// Value converted to specified area units from string, which may or may not have units specified.
        /// </summary>
        /// <param name="stringWithOrWithoutUnits">String containing value and (optionally) units.</param>
        /// <param name="areaUnit">Unit to use for returning value. Also used if string does not contain a unit.</param>
        /// <returns>Value converted to specified area units.</returns>
        public static double ValueFromAreaString(string stringWithOrWithoutUnits, AreaUnit areaUnit = AreaUnit.SquareKilometer)
        {
            double result = double.NaN;

            if (!string.IsNullOrEmpty(stringWithOrWithoutUnits))
            {
                Area area = new Area();
                if (Area.TryParse(stringWithOrWithoutUnits, out area))
                {
                    result = area.As(areaUnit);
                }
                else
                {
                    // If no units provided, assume result is in default units provided above.
                    double.TryParse(stringWithOrWithoutUnits, out result);
                }
            }

            return result;
        }

        /// <summary>
        /// Value converted to specified volume units from string, which may or may not have units specified.
        /// </summary>
        /// <param name="stringWithOrWithoutUnits">String containing value and (optionally) units.</param>
        /// <param name="volumeUnit">Unit to use for returning value. Also used if string does not contain a unit.</param>
        /// <returns>Value converted to specified volume units.</returns>
        public static double ValueFromVolumeString(string stringWithOrWithoutUnits, VolumeUnit volumeUnit = VolumeUnit.Megaliter)
        {
            double result = double.NaN;

            if (!string.IsNullOrEmpty(stringWithOrWithoutUnits))
            {
                Volume volume = new Volume();
                if (Volume.TryParse(stringWithOrWithoutUnits, out volume))
                {
                    result = volume.As(volumeUnit);
                }
                else
                {
                    // If no units provided, assume result is in default units provided above.
                    double.TryParse(stringWithOrWithoutUnits, out result);
                }
            }

            return result;
        }
    }
}
