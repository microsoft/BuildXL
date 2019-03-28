// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Abstraction representation of a read-only range of characters.
    /// </summary>
    public interface ICharSpan<T> : IEquatable<T>
    {
        /// <summary>
        /// Compares this segment to a 8-bit character array starting at some index
        /// </summary>
        bool Equals8Bit(byte[] buffer, int index);

        /// <summary>
        /// Compares this segment to a 16-bit character array starting at some index
        /// </summary>
        bool Equals16Bit(byte[] buffer, int index);

        /// <summary>
        /// Copy the content of this segment to a byte buffer, assuming the segment only contains ASCII characters.
        /// </summary>
        void CopyAs8Bit(byte[] buffer, int index);

        /// <summary>
        /// Copy the content of this segment to a byte buffer, assuming the segment contains Unicode characters.
        /// </summary>
        void CopyAs16Bit(byte[] buffer, int index);

        /// <summary>
        /// Checks if the segment only contains valid characters for a path atom.
        /// </summary>
        bool CheckIfOnlyContainsValidPathAtomChars(out int characterWithError);

        /// <summary>
        /// Checks if the segment only contains valid characters for an identifier atom.
        /// </summary>
        bool CheckIfOnlyContainsValidIdentifierAtomChars(out int characterWithError);

        /// <summary>
        /// Returns a sub segment of an existing segment.
        /// </summary>
        T Subsegment(int index, int length);

        /// <summary>
        /// Returns a character from the segment.
        /// </summary>
        char this[int index] { get; }

        /// <summary>
        /// The length of the segment
        /// </summary>
        int Length { get; }

        /// <summary>
        /// Indicates whether this segment only contains ASCII characters.
        /// </summary>
        /// <remarks>
        /// Note that this considers 0 as non-ASCII for the sake of the StringTable which treats character 0
        /// as a special marker.
        /// </remarks>
        bool OnlyContains8BitChars { get; }
    }

    /// <summary>
    /// Utility methods for <see cref="ICharSpan{T}"/>
    /// </summary>
    /// <remarks>
    /// Generic constraints do not participate in overload resolution. We do not write extension methods for 'this T'
    /// since those methods could then apply to any type.
    /// </remarks>
    public static class CharSpan
    {
        /// <summary>
        /// Skips the first <paramref name="skipCount" /> characters,
        /// possibly leaving an empty span.
        /// </summary>
        public static T Skip<T>(T span, int skipCount)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(skipCount >= 0 && skipCount <= span.Length);
            return span.Subsegment(skipCount, span.Length - skipCount);
        }
    }
}
