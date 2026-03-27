// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The kind of trigger for capturing a GC dump of the bxl.exe process.
    /// </summary>
    public enum EngineDumpTriggerKind
    {
        /// <summary>
        /// No GC dump trigger configured.
        /// </summary>
        None,

        /// <summary>
        /// Trigger when bxl.exe process memory exceeds the specified value in megabytes.
        /// </summary>
        MemoryMb,

        /// <summary>
        /// Trigger after the specified number of seconds of build execution.
        /// </summary>
        TimeSec,

        /// <summary>
        /// Trigger when the build reaches the specified completion percentage (1-100).
        /// </summary>
        BuildPercentage,
    }

    /// <summary>
    /// Specifies when a GC heap dump of the bxl.exe process should be captured.
    /// </summary>
    /// <remarks>
    /// The dump is written to the logs directory as a .gcdump file for offline analysis
    /// with Visual Studio, PerfView, or dotnet-gcdump. The trigger fires at most once per build.
    ///
    /// CLI usage: /EngineDumpTrigger:&lt;value&gt;&lt;unit&gt;
    ///   /EngineDumpTrigger:8000mb  — capture when process memory exceeds 8000 MB
    ///   /EngineDumpTrigger:600s    — capture after 600 seconds of build execution
    ///   /EngineDumpTrigger:50pct   — capture when 50% of pips are complete
    /// </remarks>
    public readonly struct EngineDumpTrigger : IEquatable<EngineDumpTrigger>
    {
        private const string SuffixPct = "pct";
        private const string SuffixPercent = "percent";
        private const string SuffixMb = "mb";
        private const string SuffixSeconds = "s";

        /// <summary>
        /// A disabled trigger (no dump will be captured).
        /// </summary>
        public static readonly EngineDumpTrigger Disabled = new EngineDumpTrigger(EngineDumpTriggerKind.None, 0);

        /// <summary>
        /// The kind of trigger.
        /// </summary>
        public EngineDumpTriggerKind Kind { get; }

        /// <summary>
        /// The threshold value for the trigger, interpreted according to <see cref="Kind"/>.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Whether this trigger is enabled (i.e., not <see cref="EngineDumpTriggerKind.None"/>).
        /// </summary>
        public bool IsEnabled => Kind != EngineDumpTriggerKind.None;

        /// <summary>
        /// Creates a new EngineDumpTrigger.
        /// </summary>
        public EngineDumpTrigger(EngineDumpTriggerKind kind, int value)
        {
            Kind = kind;
            Value = value;
        }

        /// <summary>
        /// Attempts to parse a trigger specification string with a unit suffix.
        /// </summary>
        /// <param name="input">The input string (e.g., "8000mb", "600s", "50pct").</param>
        /// <param name="trigger">The parsed trigger, or <see cref="Disabled"/> on failure.</param>
        /// <returns>True if parsing succeeded; false otherwise.</returns>
        public static bool TryParse(string input, out EngineDumpTrigger trigger)
        {
            trigger = Disabled;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            input = input.Trim();

            if (input.EndsWith(SuffixPercent, StringComparison.OrdinalIgnoreCase))
            {
                return TryParsePercentage(input.Substring(0, input.Length - SuffixPercent.Length), out trigger);
            }

            if (input.EndsWith(SuffixPct, StringComparison.OrdinalIgnoreCase))
            {
                return TryParsePercentage(input.Substring(0, input.Length - SuffixPct.Length), out trigger);
            }

            if (input.EndsWith(SuffixMb, StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(input.Substring(0, input.Length - SuffixMb.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                    && value >= 1)
                {
                    trigger = new EngineDumpTrigger(EngineDumpTriggerKind.MemoryMb, value);
                    return true;
                }

                return false;
            }

            if (input.EndsWith(SuffixSeconds, StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(input.Substring(0, input.Length - SuffixSeconds.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                    && value >= 1)
                {
                    trigger = new EngineDumpTrigger(EngineDumpTriggerKind.TimeSec, value);
                    return true;
                }

                return false;
            }

            return false;
        }

        private static bool TryParsePercentage(string numericPart, out EngineDumpTrigger trigger)
        {
            trigger = Disabled;
            if (int.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                && value >= 1
                && value <= 100)
            {
                trigger = new EngineDumpTrigger(EngineDumpTriggerKind.BuildPercentage, value);
                return true;
            }

            return false;
        }

        /// <summary>
        /// A human-readable description of why this trigger fires, for log messages.
        /// </summary>
        public string TriggerReason => Kind switch
        {
            EngineDumpTriggerKind.MemoryMb => $"process memory exceeded {Value} MB",
            EngineDumpTriggerKind.TimeSec => $"{Value} seconds elapsed since execution start",
            EngineDumpTriggerKind.BuildPercentage => $"build reached {Value}% completion",
            _ => "unknown",
        };

        /// <inheritdoc />
        public override string ToString()
        {
            return Kind switch
            {
                EngineDumpTriggerKind.MemoryMb => $"{Value}{SuffixMb}",
                EngineDumpTriggerKind.TimeSec => $"{Value}{SuffixSeconds}",
                EngineDumpTriggerKind.BuildPercentage => $"{Value}{SuffixPct}",
                _ => "disabled",
            };
        }

        /// <inheritdoc />
        public bool Equals(EngineDumpTrigger other) => Kind == other.Kind && Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is EngineDumpTrigger other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (Kind, Value).GetHashCode();

        /// <nodoc />
        public static bool operator ==(EngineDumpTrigger left, EngineDumpTrigger right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(EngineDumpTrigger left, EngineDumpTrigger right) => !left.Equals(right);
    }
}
