// <copyright file="StandardModellingTimeSpan.cs" company="HARC">
// Copyright (c) HARC. All rights reserved.
// </copyright>

namespace RODIS.TimeSeries
{
    using RODIS.InputOutput;

    /// <summary>
    /// Base modelling time span units used to describe temporal resolution.
    /// </summary>
    public enum BaseModellingTimeSpan
    {
        /// <summary>Hourly resolution.</summary>
        Hourly,

        /// <summary>Daily resolution.</summary>
        Daily,

        /// <summary>Weekly resolution (REALM water-year semantics may apply).</summary>
        Weekly,

        /// <summary>Monthly resolution (calendar-month dependent).</summary>
        Monthly,

        /// <summary>Yearly resolution (calendar-year dependent).</summary>
        Yearly
    }

    /// <summary>
    /// Represents a modelling time span defined by a base unit and a numeric multiplier.
    /// </summary>
    /// <remarks>
    /// This class provides a calendar-aware abstraction over <see cref="TimeSpan"/>.
    /// Hourly, Daily, and Weekly spans represent exact durations.
    /// Monthly and Yearly spans depend on the chosen start date.
    /// </remarks>
    public sealed class StandardModellingTimeSpan
    {
        private const double DefaultTolerance = 1e-9;

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardModellingTimeSpan"/> class.
        /// </summary>
        /// <param name="baseTimeSpan">Base time unit.</param>
        /// <param name="numberOfBaseTimeSpans">Multiplier of the base unit.</param>
        public StandardModellingTimeSpan(BaseModellingTimeSpan baseTimeSpan, double numberOfBaseTimeSpans)
        {
            this.BaseTimeSpan = baseTimeSpan;
            this.NumberOfBaseTimeSpans = numberOfBaseTimeSpans;
        }

        /// <summary>
        /// Gets or sets the base modelling time span unit.
        /// </summary>
        public BaseModellingTimeSpan BaseTimeSpan { get; set; } = BaseModellingTimeSpan.Daily;

        /// <summary>
        /// Gets or sets the numeric multiplier of the base time span.
        /// </summary>
        public double NumberOfBaseTimeSpans { get; set; } = 1.0;

        /// <summary>
        /// Sets this instance from a <see cref="TimeSpan"/> using the original heuristic logic.
        /// </summary>
        /// <param name="timeSpan">Input time span.</param>
        /// <returns>This instance, updated to represent the supplied duration.</returns>
        /// <remarks>
        /// This method intentionally preserves the original heuristic behaviour.
        /// It does not distinguish hourly or yearly spans and should be treated as an approximate inference mechanism.
        /// </remarks>
        public StandardModellingTimeSpan SetFromTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan < new TimeSpan(1, 0, 0, 0))
            {
                this.BaseTimeSpan = BaseModellingTimeSpan.Daily;
                this.NumberOfBaseTimeSpans = timeSpan.TotalDays;
            }
            else if (timeSpan < new TimeSpan(7, 0, 0, 0))
            {
                this.BaseTimeSpan = BaseModellingTimeSpan.Daily;
                this.NumberOfBaseTimeSpans = timeSpan.TotalDays;
            }
            else if (timeSpan < new TimeSpan(28, 0, 0, 0))
            {
                this.BaseTimeSpan = BaseModellingTimeSpan.Weekly;
                this.NumberOfBaseTimeSpans = timeSpan.TotalDays / 7.0;
            }
            else if (timeSpan <= new TimeSpan(31, 0, 0, 0))
            {
                this.BaseTimeSpan = BaseModellingTimeSpan.Monthly;
                this.NumberOfBaseTimeSpans = 1.0;
            }

            return this;
        }

        /// <summary>
        /// Sets this instance from a <see cref="TimeSpan"/> using explicit, unambiguous rules.
        /// </summary>
        /// <param name="timeSpan">Input time span.</param>
        /// <returns>This instance, updated to represent the supplied duration.</returns>
        /// <remarks>
        /// Unlike <see cref="SetFromTimeSpan(TimeSpan)"/>, this method explicitly distinguishes
        /// Hourly and Yearly spans and always assigns a base unit and multiplier.
        ///
        /// Classification rules:
        /// <1 day ? Hourly (multiplier = TotalHours)
        /// <7 days ? Daily (multiplier = TotalDays)
        /// <32 days ? Weekly (multiplier = TotalDays / 7)
        /// <370 days ? Monthly (multiplier = 1)
        /// = 370 days ? Yearly (multiplier = TotalDays / 365.25)
        /// This is still an inference mechanism (months/years vary by calendar), but it is explicit and deterministic.
        /// </remarks>
        public StandardModellingTimeSpan SetFromTimeSpanStrict(TimeSpan timeSpan)
        {
            if (timeSpan < TimeSpan.FromDays(1))
            {
                this.BaseTimeSpan = BaseModellingTimeSpan.Hourly;
                this.NumberOfBaseTimeSpans = timeSpan.TotalHours;
            }
            else if (timeSpan < TimeSpan.FromDays(7))
            {
                this.BaseTimeSpan = BaseModellingTimeSpan.Daily;
                this.NumberOfBaseTimeSpans = timeSpan.TotalDays;
            }
            else if (timeSpan < TimeSpan.FromDays(32))
            {
                this.BaseTimeSpan = BaseModellingTimeSpan.Weekly;
                this.NumberOfBaseTimeSpans = timeSpan.TotalDays / 7.0;
            }
            else if (timeSpan < TimeSpan.FromDays(370))
            {
                this.BaseTimeSpan = BaseModellingTimeSpan.Monthly;
                this.NumberOfBaseTimeSpans = 1.0;
            }
            else
            {
                this.BaseTimeSpan = BaseModellingTimeSpan.Yearly;
                this.NumberOfBaseTimeSpans = timeSpan.TotalDays / 365.25;
            }

            return this;
        }

        /// <summary>
        /// Converts this modelling time span to a concrete <see cref="TimeSpan"/>,
        /// starting at a specified date.
        /// </summary>
        /// <param name="fromDateTime">Start date for the conversion.</param>
        /// <returns>Concrete <see cref="TimeSpan"/> representing this modelling span.</returns>
        /// <remarks>
        /// Monthly and Yearly spans are calendar-dependent.
        /// Fractional months or years are truncated to whole calendar units.
        /// </remarks>
        public TimeSpan GetAsTimeSpan(DateTime fromDateTime)
        {
            return this.BaseTimeSpan switch
            {
                BaseModellingTimeSpan.Hourly =>
                    TimeSpan.FromHours(this.NumberOfBaseTimeSpans),

                BaseModellingTimeSpan.Daily =>
                    fromDateTime.AddDays(this.NumberOfBaseTimeSpans) - fromDateTime,

                BaseModellingTimeSpan.Weekly =>
                    ReadWriteGetDatFiles.REALMWeeksToTimeSpan(fromDateTime, this.NumberOfBaseTimeSpans),

                BaseModellingTimeSpan.Monthly =>
                    fromDateTime.AddMonths((int)this.NumberOfBaseTimeSpans) - fromDateTime,

                BaseModellingTimeSpan.Yearly =>
                    fromDateTime.AddYears((int)this.NumberOfBaseTimeSpans) - fromDateTime,

                _ => throw new InvalidOperationException("Unsupported BaseModellingTimeSpan.")
            };
        }

        /// <summary>
        /// Converts this modelling time span to a concrete <see cref="TimeSpan"/>,
        /// starting at 1 July 2020.
        /// </summary>
        public TimeSpan GetAsTimeSpan()
        {
            return this.GetAsTimeSpan(new DateTime(2020, 7, 1));
        }

        /// <summary>
        /// Determines value-based equality using a default tolerance for numeric comparison.
        /// </summary>
        public bool Equals(StandardModellingTimeSpan? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return this.BaseTimeSpan == other.BaseTimeSpan
                && NearlyEqual(this.NumberOfBaseTimeSpans, other.NumberOfBaseTimeSpans, DefaultTolerance);
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current instance.
        /// </summary>
        /// <param name="obj">The object to compare with the current instance.</param>
        /// <returns>
        /// True if <paramref name="obj"/> is a <see cref="StandardModellingTimeSpan"/> and
        /// is value-equal to this instance; otherwise false.
        /// </returns>
        /// <remarks>
        /// This override delegates to the strongly-typed <see cref="Equals(StandardModellingTimeSpan?)"/> method to ensure consistent value-based equality semantics.
        /// </remarks>
        public override bool Equals(object? obj)
        {
            return this.Equals(obj as StandardModellingTimeSpan);
        }

        /// <summary>
        /// Returns a hash code for the current instance.
        /// </summary>
        /// <returns>
        /// A hash code consistent with the value-based equality implementation.
        /// </returns>
        /// <remarks>
        /// The numeric multiplier is rounded to match the tolerance used in equality comparisons, ensuring that objects considered equal also produce the same hash code.
        /// </remarks>
        public override int GetHashCode()
        {
            double rounded = Math.Round(this.NumberOfBaseTimeSpans, 9);
            return HashCode.Combine(this.BaseTimeSpan, rounded);
        }

        /// <summary>
        /// Determines whether two <see cref="StandardModellingTimeSpan"/> instances are equal.
        /// </summary>
        /// <param name="left">The left-hand operand.</param>
        /// <param name="right">The right-hand operand.</param>
        /// <returns>
        /// True if both instances are value-equal; otherwise false.
        /// </returns>
        /// <remarks>
        /// This operator delegates to <see cref="Equals(object?)"/> to preserve null handling and value-based equality semantics.
        /// </remarks>
        public static bool operator ==(StandardModellingTimeSpan? left, StandardModellingTimeSpan? right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Determines whether two <see cref="StandardModellingTimeSpan"/> instances are not equal.
        /// </summary>
        /// <param name="left">The left-hand operand.</param>
        /// <param name="right">The right-hand operand.</param>
        /// <returns>
        /// True if the instances are not value-equal; otherwise false.
        /// </returns>
        public static bool operator !=(StandardModellingTimeSpan? left, StandardModellingTimeSpan? right)
        {
            return !Equals(left, right);
        }

        /// <summary>
        /// Compares two doubles with a specified tolerance.
        /// </summary>
        public static bool NearlyEqual(double a, double b, double tolerance)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        /// <summary>
        /// Determines equality using a caller-supplied tolerance.
        /// </summary>
        public bool EqualsWithTolerance(StandardModellingTimeSpan? other, double tolerance)
        {
            if (other is null)
            {
                return false;
            }

            return this.BaseTimeSpan == other.BaseTimeSpan
                && NearlyEqual(this.NumberOfBaseTimeSpans, other.NumberOfBaseTimeSpans, tolerance);
        }
    }
}
