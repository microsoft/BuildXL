// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// The token class tracks the location information during parsing.
    /// </summary>
    [DebuggerDisplay("{ToDebuggerDisplay,nq}")]
    public readonly struct LocationData : IEquatable<LocationData>
    {
        /// <summary>
        /// An invalid token data.
        /// </summary>
        public static readonly LocationData Invalid = default(LocationData);

        /// <summary>
        /// Determines whether token data is valid or not.
        /// </summary>
        [Pure]
        public bool IsValid => Path.IsValid;

        /// <summary>
        /// Line number of the token
        /// </summary>
        public readonly int Line;

        /// <summary>
        /// Path of the token.
        /// </summary>
        public readonly AbsolutePath Path;

        /// <summary>
        /// Column number of the token
        /// </summary>
        public readonly int Position;

        /// <summary>
        /// Constructs a LocationData.
        /// </summary>
        /// <param name="path">Path of the locationData</param>
        /// <param name="line">Line number of the locationData</param>
        /// <param name="column">Column number of the locationData</param>
        public LocationData(AbsolutePath path, int line, int column)
        {
            Contract.Requires(path.IsValid);

            Path = path;
            Line = line;
            Position = column;
        }

        /// <summary>
        /// Helper to create instances of location data.
        /// </summary>
        public static LocationData Create(AbsolutePath path, int line = 0, int column = 0)
        {
            Contract.Requires(path.IsValid);

            return new LocationData(path, line, column);
        }

        /// <summary>
        /// Implicit conversion of TokenData to LocationData.
        /// </summary>
        public static implicit operator LocationData(TokenData token)
        {
            return !token.IsValid ? Invalid : new LocationData(token.Path, token.Line, token.Position);
        }

        /// <summary>
        /// Constructs a LocationData.
        /// </summary>
        public LocationData(Token token)
        {
            Contract.Requires(token.Path.IsValid);

            Path = token.Path;
            Line = token.Line;
            Position = token.Position;
        }

        /// <summary>
        /// Converts a LocationData object to a Log Location
        /// </summary>
        public Location ToLogLocation(PathTable pathTable)
        {
            return new Location
                   {
                       File = Path.ToString(pathTable),
                       Line = Line,
                       Position = Position,
                   };
        }

        internal void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.WriteCompact(Line);
            writer.Write(Path);
            writer.WriteCompact(Position);
        }

        internal static LocationData Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);
            var line = reader.ReadInt32Compact();
            var path = reader.ReadAbsolutePath();
            var position = reader.ReadInt32Compact();
            return path.IsValid ? new LocationData(path, line, position) : LocationData.Invalid;
        }

        /// <summary>
        /// Returns a string representation of the token.
        /// </summary>
        /// <param name="pathTable">The path table used when creating the AbsolutePath in the Path field.</param>
        public string ToString(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);
            Contract.Ensures(!string.IsNullOrEmpty(Contract.Result<string>()));

            return I($"{Path.ToString(pathTable)}({Line}, {Position})");
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Path.GetHashCode(), Line, Position);
        }

        /// <inheritdoc />
        public bool Equals(LocationData other)
        {
            return other.Position == Position && other.Line == Line && Path.Equals(other.Path);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(LocationData left, LocationData right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(LocationData left, LocationData right)
        {
            return !left.Equals(right);
        }

        [SuppressMessage("Microsoft.Performance", "CA1811")]
        private string ToDebuggerDisplay()
        {
            return !IsValid ? "Invalid" : I($"{Path.ToDebuggerDisplay()}({Line}, {Position})");
        }
    }
}
