// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Globalization;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Diagnostic information from the logger.
    /// </summary>
    public readonly struct Diagnostic : IEquatable<Diagnostic>
    {
        /// <summary>
        /// Id of the logged event.
        /// </summary>
        public int ErrorCode { get; }

        /// <summary>
        /// Level of the logged event.
        /// </summary>
        public EventLevel Level { get; }

        /// <summary>
        /// Message of the logged event.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Optional location for the diagnostic.
        /// </summary>
        public Location? Location { get; }

        /// <summary>
        /// Returns full representation of the message.
        /// </summary>
        public string FullMessage => ToString();

        /// <nodoc />
        public bool IsError => Level == EventLevel.Error || Level == EventLevel.Critical;

        /// <nodoc />
        public Diagnostic(int errorCode, EventLevel level, string message, Location? location)
        {
            ErrorCode = errorCode;
            Level = level;
            Message = message;
            Location = location;
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider")]
        public override string ToString()
        {
            string location =
                Location == null
                    ? string.Empty
                    : string.Format(CultureInfo.InvariantCulture, "{0}({1},{2}):", Location.Value.File, Location.Value.Line, Location.Value.Position);
            return string.Format(CultureInfo.InvariantCulture, "{0}{1} {2}: {3}", location, Level, ErrorCode, Message);
        }

        /// <inheritdoc />
        public bool Equals([AllowNull]Diagnostic other)
        {
            return ErrorCode == other.ErrorCode && Level == other.Level && string.Equals(Message, other.Message);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            if (obj is null)
            {
                return false;
            }

            return obj is Diagnostic && Equals((Diagnostic)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (((ErrorCode * 31) + (int)Level) * 31) + Message?.GetHashCode() ?? 0;
            }
        }

        /// <nodoc />
        public static bool operator ==(Diagnostic left, Diagnostic right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(Diagnostic left, Diagnostic right)
        {
            return !left.Equals(right);
        }
    }
}
