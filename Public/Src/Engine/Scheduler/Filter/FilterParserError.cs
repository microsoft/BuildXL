// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Error when parsing a filter expression
    /// </summary>
    public sealed class FilterParserError
    {
        /// <summary>
        /// Position the error relates to. Zero indexed
        /// </summary>
        public readonly int Position;

        /// <summary>
        /// Message for why parsing failed
        /// </summary>
        public readonly string Message;

        /// <summary>
        /// Creates a FilterParserError
        /// </summary>
        public FilterParserError(int position, string message, params object[] args)
        {
            Contract.Requires(!string.IsNullOrEmpty(message));
            Contract.Requires(args != null);
            Contract.Requires(position >= 0);

            Position = position;
            Message = string.Format(CultureInfo.InvariantCulture, message, args);
        }

        /// <summary>
        /// Gets a representation of the filter with text arrows pointing to location of the error for the sake of
        /// providing a nicer log message
        /// </summary>
        public string FormatFilterPointingToPosition(string rawFilter)
        {
            if (rawFilter != null && rawFilter.Length > Position)
            {
                var raw = Position < rawFilter.Length ? rawFilter.Substring(Position + 1) : string.Empty;
                return I($" {rawFilter.Substring(0, Position)}>>{rawFilter[Position]}<<{raw}");
            }

            return string.Empty;
        }
    }
}
