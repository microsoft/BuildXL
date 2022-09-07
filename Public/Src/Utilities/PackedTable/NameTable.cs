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
    public readonly struct NameId : Id<NameId>, IEquatable<NameId>
    {
        /// <summary>Comparer.</summary>
        public struct EqualityComparer : IEqualityComparer<NameId>
        {
            /// <summary>Comparison.</summary>
            public bool Equals(NameId x, NameId y) => x.Value == y.Value;
            /// <summary>Hashing.</summary>
            public int GetHashCode(NameId obj) => obj.Value;
        }

        /// <summary>A global comparer to avoid boxing allocation on each usage</summary>
        public static IEqualityComparer<NameId> EqualityComparerInstance { get; } = new EqualityComparer();

        /// <summary>Value as int.</summary>
        public int Value { get; }

        /// <summary>Constructor.</summary>
        public NameId(int value) { Id<NameId>.CheckValidId(value); Value = value; }
        
        /// <summary>Constructor via interface.</summary>
        public NameId CreateFrom(int value) => new(value);
        
        /// <summary>Debugging.</summary>
        public override string ToString() => $"NameId[{Value}]";

        /// <inheritdoc />
        public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

        /// <inheritdoc />
        public override int GetHashCode() => Value;

        /// <inheritdoc />
        public bool Equals(NameId other) => Value == other.Value;

        /// <summary>Comparison.</summary>
        public static bool operator ==(NameId x, NameId y) => x.Value == y.Value;
        
        /// <summary>Comparison.</summary>
        public static bool operator !=(NameId x, NameId y) => !(x == y);
        
        /// <summary>Comparison.</summary>
        public IEqualityComparer<NameId> Comparer => EqualityComparerInstance;
        
        /// <summary>Comparison via IComparable.</summary>
        public int CompareTo([AllowNull] NameId other) => Value.CompareTo(other.Value);
    }

    /// <summary>
    /// Suffix tree representation, where all prefixes are maximally shared.
    /// </summary>
    public readonly struct NameEntry : IEquatable<NameEntry>
    {
        /// <summary>
        /// Prefix of this name (e.g. all the portion of the name before this final atom).
        /// </summary>
        public readonly NameId Prefix;
        /// <summary>
        /// Suffix of the name (e.g. the final string at the end of the name).
        /// </summary>
        public readonly StringId Atom;

        /// <summary>
        /// Construct a NameEntry.
        /// </summary>
        public NameEntry(NameId prefix, StringId atom) { Prefix = prefix; Atom = atom; }

        /// <inheritdoc />
        public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

        /// <inheritdoc />
        public override int GetHashCode() => (Prefix.GetHashCode(), Atom.GetHashCode()).GetHashCode();

        /// <inheritdoc />
        public bool Equals(NameEntry other) => Prefix == other.Prefix && Atom == other.Atom;

        /// <summary>
        /// Equality for NameEntries.
        /// </summary>
        public struct EqualityComparer : IEqualityComparer<NameEntry>
        {
            /// <summary>
            /// Equality.
            /// </summary>
            public bool Equals(NameEntry x, NameEntry y) => x.Prefix == y.Prefix && x.Atom == y.Atom;
            /// <summary>
            /// Hashing.
            /// </summary>
            public int GetHashCode([DisallowNull] NameEntry obj) =>
                HashCodeHelper.Combine(obj.Prefix.GetHashCode(), obj.Atom.GetHashCode());
        }
    }

    /// <summary>
    /// Suffix table representation of sequential names, such as pip names or file paths.
    /// </summary>
    /// <remarks>
    /// This representation shares all sub-names as much as possible. For example, for a NameTable with
    /// period delimiters that is initially empty:
    /// 
    /// - Storing the name "a.b.c" will result in three names in the table: "a", "a.b", and "a.b.c".
    /// - Each name is represented by a pointer to its prefix, and the atom at the end.
    /// - If we then store "a.b.d", only one additional name will be added, because the prefix "a.b" will be shared.
    /// 
    /// The intent is to optimize the representation of long, sequential names which have many repeated subparts.
    /// Both file paths and pip names fit this description, and this type is used for both.
    /// </remarks>
    public class NameTable : SingleValueTable<NameId, NameEntry>
    {
        /// <summary>
        /// The separator between parts of names in this table.
        /// </summary>
        /// <remarks>
        /// Typically either '.' or '\\' or '/'
        /// 
        /// Note that this is not part of the persistent state of the table; this is provided at construction, and
        /// the code doing the construction should know what type of separator is needed.
        /// </remarks>
        public readonly char Separator;

        /// <summary>
        /// The backing string table used by names in this table; not owned by this table, may be shared (and probably is).
        /// </summary>
        public readonly StringTable StringTable;

        /// <summary>
        /// Construct a NameTable.
        /// </summary>
        public NameTable(char separator, StringTable stringTable)
        {
            Separator = separator;
            StringTable = stringTable;
        }

        /// <summary>
        /// Length in characters of the given name.
        /// </summary>
        public int Length(NameId id)
        {
            int len = 0;
            bool atEnd = false;

            NameEntry entry;
            while (!atEnd)
            {
                // Walk up the prefix chain to the end.
                entry = this[id];
                if (entry.Atom == default) { throw new Exception($"Invalid atom for id {entry.Atom}"); }

                // Are we at the end yet?
                atEnd = entry.Prefix == default;

                len += StringTable[entry.Atom].Length;

                if (!atEnd)
                {
                    len++;
                    id = entry.Prefix;
                }
            }

            return len;
        }

        /// <summary>
        /// Get the full text of the given name, writing into the given span, returning the prefix of the span
        /// containing the full name.
        /// </summary>
        /// <remarks>
        /// Use this in hot paths when string allocation is undesirable.
        /// </remarks>
        public ReadOnlySpan<char> GetText(NameId nameId, Span<char> span)
        {
            NameEntry entry = this[nameId];
            ReadOnlySpan<char> prefixSpan;
            if (entry.Prefix != default)
            {
                // recurse on the prefix, which will result in it getting written into the first part of span
                prefixSpan = GetText(entry.Prefix, span);
                // add the separators
                span[prefixSpan.Length] = Separator;

                prefixSpan = span.Slice(0, prefixSpan.Length + 1);
            }
            else
            {
                // we're at the start -- base case of the recursion
                prefixSpan = span.Slice(0, 0);
            }
            ReadOnlySpan<char> atom = StringTable[entry.Atom];
            atom.CopyTo(span.Slice(prefixSpan.Length));
            return span.Slice(0, prefixSpan.Length + atom.Length);
        }

        /// <summary>
        /// Get the full text of the given name, allocating a new string for it.
        /// </summary>
        /// <remarks>
        /// This allocates not only a string but a StringBuilder; do not use in hot paths.
        /// </remarks>
        public string GetText(NameId nameId, int capacity = 1000)
        {
            char[] buf = new char[capacity];
            Span<char> span = new Span<char>(buf);
            ReadOnlySpan<char> textSpan = GetText(nameId, span);
            return new string(textSpan);
        }

        /// <summary>
        /// Build a NameTable, caching already-seen names and name prefixes.
        /// </summary>
        public class Builder : CachingBuilder<NameEntry.EqualityComparer>
        {
            /// <summary>
            /// Construct a Builder.
            /// </summary>
            public Builder(NameTable table, StringTable.CachingBuilder stringTableBuilder) : base(table) 
            {
                StringTableBuilder = stringTableBuilder;
            }

            private NameTable NameTable => (NameTable)ValueTable;

            /// <summary>
            /// The builder for the underlying strings.
            /// </summary>
            public readonly StringTable.CachingBuilder StringTableBuilder;

            private static bool CharComparer(char c1, char c2) => c1 == c2;

            /// <summary>
            /// Split this string into its constituent pieces and ensure it exists as a Name.
            /// </summary>
            /// <remarks>
            /// This is not very efficient since it uses string.Split rather than something Span-based,
            /// but all the allocations are temporary, so not optimizing it... yet.
            /// </remarks>
            public NameId GetOrAdd(string s)
            {
                NameId prefixId = default;
                ReadOnlySpan<char> span = s.AsSpan();
                // Index relative to the start of the whole string.
                int start = 0;
                while (span.Length > 0)
                {
                    // Get the next atom (without allocating).
                    // Using an explicit lambda expression and not a method group to avoid delegate allocation on each call.
                    ReadOnlySpan<char> nextAtom = span.SplitPrefix(NameTable.Separator, static (left, right) => CharComparer(left, right));

                    // Look up the next atom and add it to the string table
                    // (allocating only if we're expanding the string table's backing store).
                    StringId atomId = StringTableBuilder.GetOrAdd(new CharSpan(s, start, nextAtom.Length));

                    // if this prefix/atom pair already exists, we will get the ID of the current version,
                    // hence sharing it. Otherwise, we'll make a new entry and get a new ID for it.
                    // Either way, we'll then iterate, using the ID (current or new) as the prefix for
                    // the next piece.
                    prefixId = GetOrAdd(new NameEntry(prefixId, atomId));

                    // Advance span past the separator (if we're not at the end).
                    int nextIndex = nextAtom.Length == span.Length ? nextAtom.Length : nextAtom.Length + 1;
                    span = span.Slice(nextIndex);
                    start += nextIndex;
                }

                // The ID we wind up with is the ID of the entire name.
                return prefixId;
            }
        }
    }
}
