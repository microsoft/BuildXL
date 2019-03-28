// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// A struct representing a location to log.
    /// </summary>
    [DebuggerDisplay("{" + nameof(ToDebuggerDisplay) + "(),nq}")]
    public struct Location : IEquatable<Location>
    {
        /// <summary>
        /// The Path of the location
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// The current line
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// The current position
        /// </summary>
        public int Position { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1811")]
        private string ToDebuggerDisplay()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}({1}, {2})", File, Line, Position);
        }

        /// <summary>
        /// Returns a location with a path to the file and default line and position
        /// </summary>
        public static Location FromFile(string pathToFile)
        {
            return new Location { File = pathToFile, Line = 0, Position = 0 };
        }

        /// <inheritdoc />
        public bool Equals(Location other) => string.Equals(File, other.File) && Line == other.Line && Position == other.Position;

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is Location && Equals((Location) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                // To avoid the dependency to BuildXL.Utilities, combine hashes manually.
                var hashCode = (File?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ Line;
                hashCode = (hashCode * 397) ^ Position;
                return hashCode;
            }
        }

        /// <nodoc />
        public static bool operator ==(Location left, Location right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(Location left, Location right) => !left.Equals(right);
    }
}
