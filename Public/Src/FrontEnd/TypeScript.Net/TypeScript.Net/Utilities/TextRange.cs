// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace TypeScript.Net
{
    /// <summary>
    /// Represents a text range for a text span.
    /// </summary>
    /// <remarks>
    /// Whereas ITextRange is used to mark classes that have text ranges, this class is a text range itself.
    /// </remarks>
    public readonly struct TextRange : IEquatable<TextRange>
    {
        private TextRange(int start, int end)
        {
            // TODO:enable!!!!! This will breaks some Typescript tests.
            // Contract.Requires(start <= end);
            Start = start;
            End = end;
        }

        /// <summary>
        /// Creates an instance of a <see cref="TextRange"/> from the given position.
        /// </summary>
        public static TextRange From(int position) => From(position, position);

        /// <summary>
        /// Creates an instance of a <see cref="TextRange"/> from the given range.
        /// </summary>
        public static TextRange From(int start, int end)
        {
            return new TextRange(start, end);
        }

        /// <summary>
        /// Creates an instance of a <see cref="TextRange"/> from the given position and the length.
        /// </summary>
        public static TextRange FromLength(int start, int length)
        {
            return new TextRange(start, start + length);
        }

        /// <summary>
        /// Start index.
        /// </summary>
        public int Start { get; }

        /// <summary>
        /// End index.
        /// </summary>
        public int End { get; }

        /// <summary>
        /// Length of a text range.
        /// </summary>
        public int Length => End - Start;

        /// <summary>
        /// Returns string representation of the range based on the <paramref name="source"/>.
        /// </summary>
        [Pure]
        public string GetText(string source)
        {
            return source.Substring(Start, Length);
        }

        /// <summary>
        /// Returns string representation of the range based on the <paramref name="source"/>.
        /// </summary>
        [Pure]
        public string GetText(TextSource source)
        {
            return source.Substring(Start, Length);
        }

        /// <summary>
        /// Returns true if a given <paramref name="position"/> is within a current range.
        /// </summary>
        [Pure]
        public bool Contains(int position)
        {
            return Start <= position && position < End;
        }

        /// <summary>
        /// Returns true if the given <paramref name="other"/> range intersects with a current instance.
        /// </summary>
        [Pure]
        public bool Intersects(TextRange other)
        {
            if (other.Length == 0)
            {
                return Start <= other.Start && other.Start < End;
            }

            if (Length == 0)
            {
                return other.Start <= Start && Start < other.End;
            }

            return Math.Max(Start, other.Start) < Math.Min(End, other.End);
        }

        /// <inheritdoc />
        public bool Equals(TextRange other)
        {
            return this == other;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[{Start}, {End})";
        }

        /// <summary>
        /// Creates new text range based on the current start index and a give length.
        /// </summary>
        [Pure]
        public TextRange WithLength(int length) => FromLength(Start, length);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is TextRange textRange && textRange.Start == Start && textRange.End == End;
        }

        /// <nodoc />
        public static bool operator ==(TextRange tr1, TextRange tr2)
        {
            return tr1.Equals(tr2);
        }

        /// <nodoc />
        public static bool operator !=(TextRange tr1, TextRange tr2)
        {
            return !tr1.Equals(tr2);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Start >> 32) ^ End;
        }
    }
}
