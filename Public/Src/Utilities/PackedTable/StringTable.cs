// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Boilerplate ID type to avoid ID confusion in code.
    /// </summary>
#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
    public struct StringId : Id<StringId>
#pragma warning restore CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
#pragma warning restore CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
    {
        /// <summary>Comparer.</summary>
        public struct EqualityComparer : IEqualityComparer<StringId>
        {
            /// <summary>Comparison.</summary>
            public bool Equals(StringId x, StringId y) => x.Value == y.Value;
            /// <summary>Hashing.</summary>
            public int GetHashCode(StringId obj) => obj.Value;
        }

        private readonly int m_value;
        /// <summary>Value as int.</summary>
        public int Value => m_value;
        /// <summary>Constructor.</summary>
        public StringId(int value) { Id<StringId>.CheckValidId(value); m_value = value; }
        /// <summary>Constructor via interface.</summary>
        public StringId CreateFrom(int value) => new(value);
        /// <summary>Debugging.</summary>
        public override string ToString() => $"StringId[{Value}]";
        /// <summary>Comparison.</summary>
        public static bool operator ==(StringId x, StringId y) => x.Equals(y);
        /// <summary>Comparison.</summary>
        public static bool operator !=(StringId x, StringId y) => !x.Equals(y);
        /// <summary>Comparison.</summary>
        public IEqualityComparer<StringId> Comparer => default(EqualityComparer);
        /// <summary>Comparison via IComparable.</summary>
        public int CompareTo([AllowNull] StringId other) => Value.CompareTo(other.Value);
    }

    /// <summary>
    /// Table of unique strings.
    /// </summary>
    /// <remarks>
    /// For efficiency and to reduce code complexity, this is treated as a MultiValueTable where each ID is associated
    /// with a ReadOnlySpan[char].
    /// 
    /// To allow the contents to be readable when directly loaded from disk in a text editor, the table internally pads
    /// every entry with newline characters.
    /// </remarks>
    public class StringTable : MultiValueTable<StringId, char>
    {
        /// <summary>
        /// Not the most efficient plan, but: actually append newlines to every addition, via copying through this buffer.
        /// </summary>
        private SpannableList<char> m_buffer = new SpannableList<char>(DefaultCapacity);

        /// <summary>
        /// Construct a StringTable.
        /// </summary>
        public StringTable(int capacity = DefaultCapacity) : base(capacity)
        {
        }

        private ReadOnlySpan<char> AppendNewline(ReadOnlySpan<char> chars)
        {
            m_buffer.Clear();
            m_buffer.Fill(chars.Length + Environment.NewLine.Length, default);
            chars.CopyTo(m_buffer.AsSpan());
            Environment.NewLine.AsSpan().CopyTo(m_buffer.AsSpan().Slice(chars.Length));
            return m_buffer.AsSpan();
        }

        /// <summary>
        /// Add the given string at the end of this table.
        /// </summary>
        /// <remarks>
        /// No checking or caching is done; for that, use a CachingBuilder.
        /// </remarks>
        public override StringId Add(ReadOnlySpan<char> multiValues) => base.Add(AppendNewline(multiValues));

        /// <summary>
        /// Get the characters for the given ID.
        /// </summary>
        public override ReadOnlySpan<char> this[StringId id] 
        {
            get
            {
                ReadOnlySpan<char> text = base[id];
                // splice the newline back out so clients never see it
                return text.Slice(0, text.Length - Environment.NewLine.Length);
            }
            set => throw new ArgumentException($"Cannot set text of {id}; strings are immutable once added to StringTable");
        }

        /// <summary>
        /// Build a SingleValueTable which caches items by hash value, adding any item only once.
        /// </summary>
        public class CachingBuilder
        {
            /// <summary>
            /// Efficient lookup by hash value.
            /// </summary>
            /// <remarks>
            /// This is really only necessary when building the table, and should probably be split out into a builder type.
            /// </remarks>
            private readonly Dictionary<CharSpan, StringId> m_entries;

            private readonly StringTable m_stringTable;

            /// <summary>
            /// Construct a CachingBuilder.
            /// </summary>
            public CachingBuilder(StringTable stringTable)
            {
                m_stringTable = stringTable;
                m_entries = new Dictionary<CharSpan, StringId>(new CharSpan.EqualityComparer(stringTable));
                // Prepopulate the dictionary that does the caching
                foreach (StringId id in m_stringTable.Ids)
                {
                    m_entries.Add(new CharSpan(id), id);
                }
            }

            /// <summary>
            /// Get or add the given string.
            /// </summary>
            public StringId GetOrAdd(string s) => GetOrAdd(new CharSpan(s));

            /// <summary>
            /// Get or add this value to the StringTable.
            /// </summary>
            /// <remarks>
            /// The CharSpan type lets us refer to a slice of an underlying string (to allow splitting strings without allocating).
            /// </remarks>
            public virtual StringId GetOrAdd(CharSpan value)
            {
                if (m_entries.TryGetValue(value, out StringId id))
                {
                    return id;
                }
                else
                {
                    id = m_stringTable.Add(value.AsSpan(m_stringTable));
                    // and add the entry as a reference to our own backing store, not the one passed in 
                    // (since the value passed in is probably holding onto part of an actual string)
                    m_entries.Add(new CharSpan(id), id);
                    return id;
                }
            }
        }
    }
}
