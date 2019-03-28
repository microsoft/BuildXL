// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Text;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents an token text string
    /// </summary>
    public readonly struct TokenText : IEquatable<TokenText>
    {
        /// <summary>
        /// An invalid path.
        /// </summary>
        public static readonly TokenText Invalid = new TokenText(StringId.Invalid);

        /// <summary>
        /// Identifier of this token as understood by the owning token text table.
        /// </summary>
        public readonly StringId Value;

        /// <summary>
        /// Creates an absolute path for some underlying HierchicalNameId value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a path table, this constructor should primarily be called by PathTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        public TokenText(StringId value)
        {
            Value = value;
        }

        /// <summary>
        /// Creates a token text from a string segment.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public static TokenText Create(TokenTextTable table, StringSegment text)
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<TokenText>().IsValid);

            return new TokenText(table.AddString(text));
        }

        /// <summary>
        /// Creates a token text from a string segment.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public static TokenText Create(TokenTextTable table, CharArraySegment text)
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<TokenText>().IsValid);

            return new TokenText(table.AddString(text));
        }

        /// <summary>
        /// Creates a token text from a string segment.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public static TokenText Create(TokenTextTable table, StringBuilderSegment text)
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<TokenText>().IsValid);

            return new TokenText(table.AddString(text));
        }

        /// <summary>
        /// Gets the length in character of a token text string.
        /// </summary>
        [Pure]
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public int GetLength(TokenTextTable table)
        {
            Contract.Requires(table != null);

            return table.GetLength(Value);
        }

        /// <summary>
        /// Determines whether an instance is valid or not.
        /// </summary>
        [Pure]
        public bool IsValid => this != Invalid;

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        /// <filterpriority>2</filterpriority>
        public bool Equals(TokenText other)
        {
            return Value == other.Value;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <returns>
        /// true if the specified object  is equal to the current object; otherwise, false.
        /// </returns>
        /// <param name="obj">The object to compare with the current object. </param>
        /// <filterpriority>2</filterpriority>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <remarks>
        /// It is illegal for a file to have both a rewrite count of 0 AND 1 in the graph.
        /// Therefore we will give both the same hash value as there shouldn't be many collisions, only to report errors.
        /// Furthermore we expect the rewrites > 1 to be limited and eliminated over time. We will use the higher-order bits,
        /// One strategy would be to reverse the bits on the rewrite count and bitwise or it with the absolute path so collisions
        /// would only occur when there are tons of files or high rewrite counts.
        /// </remarks>
        /// <returns>
        /// A hash code for the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override int GetHashCode()
        {
            // see remarks on why it is implemented this way.
            return Value.GetHashCode();
        }

        /// <summary>
        /// Indicates whether two object instances are equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare. </param>
        /// <param name="right">The second object to compare. </param>
        /// <filterpriority>3</filterpriority>
        public static bool operator ==(TokenText left, TokenText right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two objects instances are not equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <filterpriority>3</filterpriority>
        public static bool operator !=(TokenText left, TokenText right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Returns a string representation of the token text.
        /// </summary>
        /// <param name="table">The path table used when creating the TokenText.</param>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [Pure]
        public string ToString(TokenTextTable table)
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<string>() != null);

            return !IsValid ? "{Invalid}" : table.GetString(Value);
        }

        /// <summary>
        /// Copies the text to the string builder
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public void CopyTo(TokenTextTable table, StringBuilder builder)
        {
            Contract.Requires(table != null);
            Contract.Requires(builder != null);

            if (!IsValid)
            {
                return;
            }

            table.CopyString(Value, builder);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this == Invalid ? "{Invalid}" : I($"{{TokenText (id: 0x{Value.Value:x})}}");
        }
    }
}
