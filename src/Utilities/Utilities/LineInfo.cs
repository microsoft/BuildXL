// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// The token class tracks the location information during parsing.
    /// </summary>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct LineInfo : IEquatable<LineInfo>
    {
        /// <summary>
        /// Static singleton invalid value.
        /// </summary>
        public static readonly LineInfo Invalid = default(LineInfo);

        /// <summary>
        /// Line number of the token
        /// </summary>
        public readonly int Line;

        /// <summary>
        /// Column number of the token
        /// </summary>
        public readonly int Position;

        /// <summary>
        /// Whether this value is different from <see cref="LineInfo.Invalid"/>.
        /// </summary>
        public bool IsValid => !Equals(Invalid);

        /// <summary>
        /// Constructs a token.
        /// </summary>
        /// <param name="line">Line number of the token</param>
        /// <param name="column">Column number of the token</param>
        public LineInfo(int line, int column)
        {
            Line = line;
            Position = column;
        }

        internal void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.WriteCompact(Line);
            writer.WriteCompact(Position);
        }

        internal static LineInfo Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);
            var line = reader.ReadInt32Compact();
            var position = reader.ReadInt32Compact();
            return new LineInfo(line, position);
        }

        /// <summary>
        /// Implicit conversion of Token to LineInfo.
        /// </summary>
        public static implicit operator LineInfo(Token token)
        {
            return token == null ? default(LineInfo) : new LineInfo(token.Line, token.Position);
        }

        /// <summary>
        /// Implicit conversion of Token to LineInfo.
        /// </summary>
        public static implicit operator LineInfo(TokenTextData token)
        {
            return token == null ? default(LineInfo) : new LineInfo(token.Line, token.Position);
        }

        /// <summary>
        /// Implicit conversion of TokenData to LineInfo.
        /// </summary>
        public static implicit operator LineInfo(TokenData token)
        {
            return !token.IsValid ? default(LineInfo) : new LineInfo(token.Line, token.Position);
        }

        /// <summary>
        /// Converts this LineInfo to a LocationData with the given path.
        /// </summary>
        public LocationData ToLocationData(AbsolutePath path)
        {
            return !path.IsValid ? LocationData.Invalid : new LocationData(path, Line, Position);
        }

        /// <summary>
        /// Implicit conversion of LocationData to LineInfo.
        /// </summary>
        public static implicit operator LineInfo(LocationData location)
        {
            return new LineInfo(location.Line, location.Position);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Line, Position);
        }

        /// <inheritdoc />
        public bool Equals(LineInfo other)
        {
            return other.Position == Position && other.Line == Line;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(LineInfo left, LineInfo right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(LineInfo left, LineInfo right)
        {
            return !left.Equals(right);
        }

        [SuppressMessage("Microsoft.Performance", "CA1811")]
        private string ToDebuggerDisplay()
        {
            return I($"({Line}, {Position})");
        }
    }
}
