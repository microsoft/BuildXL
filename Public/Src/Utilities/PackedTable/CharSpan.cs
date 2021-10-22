// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Refers to some portion of an underlying text, which may be a string or may be an entry in a StringTable,
    /// and which can be used with a CharSpan.EqualityComparer to be stored in a dictionary.
    /// </summary>
    /// <remarks>
    /// The NameTable construction code has an interesting problem: build a dictionary of substrings (atoms),
    /// allowing lookup based on slices of an ordinary string, without allocating actual string objects at all.
    /// 
    /// This type is the somewhat hacky answer: it may contain a string or may contain a StringId, and its
    /// EqualityComparator allows comparing the two appropriately. The EqualityComparator carries the state
    /// about which StringTable to use to look up the StringId (if any).
    /// 
    /// This type wastes a lot of space, since for StringId values, there will be three unused fields!
    /// StringId CharSpans always refer to the whole underlying string, so m_string, m_start, and m_length
    /// are all unused. This is immensely inefficient but the limitations of C# polymorphism don't allow
    /// doing much better. (If you have ideas, please share!)
    /// </remarks>
    public struct CharSpan
    {
        /// <summary>
        /// The backing string, if any.
        /// </summary>
        private readonly string m_string;

        /// <summary>
        /// The backing StringId, if any.
        /// </summary>
        private readonly StringId m_stringId;

        /// <summary>
        /// The starting character position.
        /// </summary>
        private readonly int m_start;

        /// <summary>
        /// The length, in characters.
        /// </summary>
        private readonly int m_length;

        /// <summary>
        /// Create a CharSpan over a string.
        /// </summary>
        public CharSpan(string s) : this(s, 0, s.Length) { }

        /// <summary>
        /// Create a CharSpan over a substring.
        /// </summary>
        public CharSpan(string s, int start, int length)
        {
            Check(s, start, length);
            m_string = s;
            m_stringId = default;
            m_start = start;
            m_length = length;
        }

        /// <summary>
        /// Create a CharSpan over all of a string in a StringTable.
        /// </summary>
        /// <remarks>
        /// In this case, m_start and m_length are ignored.
        /// </remarks>
        public CharSpan(StringId s)
        {
            if (s == default)
            { 
                throw new ArgumentException("Cannot construct CharSpan from default StringId");
            }
            m_string = default;
            m_stringId = s;
            m_start = default;
            m_length = default;
        }

        private static void Check(string s, int start, int length)
        {
            if (s == null)
            {
                throw new ArgumentException($"String may not be null");
            }
            if (start < 0) 
            {
                throw new ArgumentException($"Both start {start} and length {length} must be >= 0");
            }
            if (s.Length < start + length) {
                throw new ArgumentException($"String length {s.Length} must be <= the sum of start {start} and length {length}");
            }
        }

        /// <summary>
        /// Get a span over the underlying text.
        /// </summary>
        /// <param name="table">The table to look up strings in, if this is a StringId CharSpan.</param>
        public ReadOnlySpan<char> AsSpan(StringTable table)
        {
            if (m_string != null)
            { 
                return m_string.AsSpan().Slice(m_start, m_length);
            }
            else
            {
                return table[m_stringId];
            }
        }

        /// <summary>
        /// Compare CharSpans, where some CharSpans may be over strings from the given StringTable.
        /// </summary>
        public struct EqualityComparer : IEqualityComparer<CharSpan>
        {
            private readonly StringTable m_stringTable;

            /// <summary>
            /// Construct an EqualityComparer which uses the given StringTable to look up StringIds.
            /// </summary>
            public EqualityComparer(StringTable stringTable)
            {
                if (stringTable == null) { throw new ArgumentException("Must pass non-null StringTable"); }
                m_stringTable = stringTable;
            }

            /// <summary>
            /// Equality.
            /// </summary>
            public bool Equals([AllowNull] CharSpan x, [AllowNull] CharSpan y) =>
                x.AsSpan(m_stringTable).CompareTo(y.AsSpan(m_stringTable), StringComparison.InvariantCulture) == 0;

            private static int CharToInt(char c) => c;

            /// <summary>
            /// Hashing.
            /// </summary>
            public int GetHashCode([DisallowNull] CharSpan obj) => SpanUtilities.GetHashCode(obj.AsSpan(m_stringTable), CharToInt);
        }
    }
}
