// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// This represents a block of data used during execution. Examples of this are Arguments and ResponseFiles to the Process.
    /// </summary>
    /// <remarks>
    /// Pip data is a pair (h; e_0, ..., e_N) of header and entries of type PipDataEntry. The entries are logically group into fragments,
    /// and the header contains the escaping rule and separator for the fragments that follow. A fragment consists of one or
    /// more entries. A string literal and path fragment consists of a single entry. A fragment can also be nested pip data.
    /// Nested pip data is a contiguous subsequence (e_I, e_{I+1}, ..., e_{I+M}) of the entries (e_0, ..., e_N), where M > 1.
    /// The entry e_I is the header for the nested pip data, and the entries e_{I+1} and e_{I+M} mark the start and the end
    /// of the nested pip data (or the fragment itself). The entry e_I maintains the number of entries between e_{I+1} and e_{I+M},
    /// while the entry e_{I+M} maintains the number of logical fragments in the nested pip data. These numbers become handy
    /// for navigation around pip data. One may wonder why these numbers are not tracked by the header. The reason being
    /// is the header is of type PipDataEntry, which only carries a single integer as its data.
    /// </remarks>
    public readonly struct PipData : IEnumerable<PipFragment>, IEquatable<PipData>
    {
        // Maximum length command line where paths are assumed to be expanded
        // to a length equal to max path with quotes.This works around the issue where two builds from different roots may
        // vary in their usage of response files because one may go over the max command line length and the other may not.
        private const int MaxPathStringLength = 260;
        private static readonly string MaxPathString = @"c:\max ".PadRight(MaxPathStringLength, 'p');
        private static readonly Func<AbsolutePath, string> MaxPathExpander = _ => MaxPathString;

        // Assumed max length of a rendered IPC moniker.
        private static readonly string LongestIpcMoniker = "0".PadRight(PipFragmentRenderer.MaxIpcMonikerLength);

        /// <nodoc/>
        public static readonly Func<string, string> MaxMonikerRenderer = _ => LongestIpcMoniker;

        /// <summary>
        /// Gets the default invalid pip data
        /// </summary>
        public static readonly PipData Invalid = default(PipData);

        /// <summary>
        /// The associated pointer into StringTable for <see cref="Entries"/>. For use during (de)serailization.
        /// </summary>
        /// <remarks>
        /// This is serialized in place of Entries (if specified) because the underlying serialized bytes for the entries
        /// exist in the string table.
        /// This MAY be Invalid when PipData is created in a context without a StringTable.
        /// </remarks>
        private readonly StringId m_entriesStringId;

        internal readonly PipDataEntry HeaderEntry;
        internal readonly PipDataEntryList Entries;

        private PipData(PipDataEntry entry, PipDataEntryList entries, StringId entriesStringId)
        {
            Contract.Requires(entry.EntryType == PipDataEntryType.NestedDataHeader);
            Contract.Requires(entry.Escaping != PipDataFragmentEscaping.Invalid);
            Contract.Requires(entries.Count != 0);

            HeaderEntry = entry;
            Entries = entries;
            m_entriesStringId = entriesStringId;
        }

        #region Serialization
        internal void Serialize(PipWriter writer)
        {
            Contract.Requires(writer != null);

            writer.WritePipDataId(m_entriesStringId);
            if (m_entriesStringId.IsValid)
            {
                HeaderEntry.Serialize(writer);
            }
            else
            {
                writer.WriteCompact(Entries.Count);
                if (Entries.Count > 0)
                {
                    HeaderEntry.Serialize(writer);
                    foreach (var e in Entries)
                    {
                        e.Serialize(writer);
                    }
                }
            }
        }

        internal static PipData Deserialize(PipReader reader)
        {
            Contract.Requires(reader != null);
            var entriesStringId = reader.ReadPipDataId();
            PipDataEntry headerEntry;
            PipDataEntryList entries;
            if (entriesStringId.IsValid)
            {
                headerEntry = PipDataEntry.Deserialize(reader);

                // Use the string table to get the raw bytes to back the entries
                entries = new PipDataEntryList(reader.StringTable.GetBytes(entriesStringId));
            }
            else
            {
                var count = reader.ReadInt32Compact();
                if (count == 0)
                {
                    return Invalid;
                }

                headerEntry = PipDataEntry.Deserialize(reader);
                var entriesArray = new PipDataEntry[count];
                for (int i = 0; i < count; i++)
                {
                    entriesArray[i] = PipDataEntry.Deserialize(reader);
                }

                entries = PipDataEntryList.FromEntries(entriesArray);
            }

            Contract.Assume(headerEntry.EntryType == PipDataEntryType.NestedDataHeader);
            Contract.Assume(headerEntry.Escaping != PipDataFragmentEscaping.Invalid);
            return new PipData(headerEntry, entries, entriesStringId);
        }
        #endregion

        /// <summary>
        /// When serializing which separator to use.
        /// </summary>
        /// <remarks>For command line arguments this would be a space, for a response file this is a newline.</remarks>
        public StringId FragmentSeparator => HeaderEntry.GetStringValue();

        /// <summary>
        /// Sets how this value should be escaped.
        /// </summary>
        public PipDataFragmentEscaping FragmentEscaping => HeaderEntry.Escaping;

        /// <summary>
        /// Gets the count of fragments contained in this pip data
        /// </summary>
        public int FragmentCount
        {
            get
            {
                var lastEntry = Entries[Entries.Count - 1];
                return lastEntry.GetIntegralValue();
            }
        }

        /// <summary>
        /// Whether this pip data is valid (and not the default value)
        /// </summary>
        public bool IsValid => HeaderEntry.EntryType != PipDataEntryType.Invalid;

        /// <summary>
        /// Creates a PipData instance.
        /// </summary>
        internal static PipData CreateInternal(PipDataEntry entry, PipDataEntryList entries, StringId entriesStringId)
        {
            Contract.Requires(entry.EntryType == PipDataEntryType.NestedDataHeader);
            Contract.Requires(entry.GetStringValue().IsValid);

            return new PipData(entry, entries, entriesStringId);
        }

        /// <summary>
        /// Creates a copy of the PipData instance with the given separator and escaping
        /// </summary>
        public PipData With(StringId fragmentSeparator, PipDataFragmentEscaping fragmentEscaping)
        {
            Contract.Requires(fragmentSeparator.IsValid);
            Contract.Requires(fragmentEscaping != PipDataFragmentEscaping.Invalid);

            return CreateInternal(
                new PipDataEntry(fragmentEscaping, PipDataEntryType.NestedDataHeader, fragmentSeparator.Value),
                Entries,
                m_entriesStringId);
        }

        /// <summary>
        /// Formats PipData to a command line.
        /// </summary>
        public string ToString(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);

            return ToString(new PipFragmentRenderer(pathTable));
        }

        /// <summary>
        /// Formats PipData to a command line using the given function to expand paths.
        /// </summary>
        public string ToString(Func<AbsolutePath, string> pathExpander, StringTable stringTable, Func<string, string> monikerRenderer = null)
        {
            Contract.Requires(pathExpander != null);
            Contract.Requires(stringTable != null);

            monikerRenderer = monikerRenderer ?? new Func<string, string>(m => m);
            return ToString(new PipFragmentRenderer(pathExpander, stringTable, monikerRenderer));
        }

        /// <summary>
        /// Formats PipData to a command line using the given a <see cref="PipFragmentRenderer"/>.
        /// </summary>
        public string ToString(PipFragmentRenderer renderer)
        {
            Contract.Requires(renderer != null);

            using (PooledObjectWrapper<StringBuilder> wrapper = Pools.GetStringBuilder())
            {
                StringBuilder builder = wrapper.Instance;
                AppendPipData(builder, this, renderer);
                return builder.ToString();
            }
        }

        /// <summary>
        /// Gets the length of the pip data formatted to a command line assuming the given maximum path length.
        /// </summary>
        public int GetMaxPossibleLength(StringTable stringTable)
        {
            Contract.Requires(stringTable != null);

            var renderer = new PipFragmentRenderer(MaxPathExpander, stringTable, MaxMonikerRenderer);
            int computedLength = GetMaxLength(this, renderer);

#if DEBUG
            // Expensive check on in DEBUG builds: costs 6% percent of entire runtime
            string actualValue = ToString(MaxPathExpander, stringTable, MaxMonikerRenderer);
            Contract.Assert(computedLength == actualValue.Length);
#endif

            return computedLength;
        }

        private static void AppendPipData(StringBuilder builder, in PipData pipData, PipFragmentRenderer renderer)
        {
            Contract.Requires(renderer != null);
            Contract.Requires(builder != null);
            Contract.Requires(pipData.IsValid);

            bool first = true;
            foreach (PipFragment fragment in pipData)
            {
                // append fragment separator unless first fragment
                if (!first)
                {
                    builder.Append(renderer.Render(pipData.FragmentSeparator));
                }

                first = false;

                // recursively handle compound fragments
                if (fragment.FragmentType == PipFragmentType.NestedFragment)
                {
                    AppendPipData(builder, fragment.GetNestedFragmentValue(), renderer);
                    continue;
                }

                // delegate scalar fragments to 'renderer', escape the result, and append to 'builder'
                string renderedEntry = renderer.Render(fragment);
                switch (pipData.FragmentEscaping)
                {
                    case PipDataFragmentEscaping.CRuntimeArgumentRules:
                        builder.AppendEscapedCommandLineWordAndAssertPostcondition(renderedEntry);
                        break;
                    case PipDataFragmentEscaping.NoEscaping:
                        builder.Append(renderedEntry);
                        break;
                    default:
                        throw Contract.AssertFailure("Unexpected enumeration value");
                }
            }
        }

        private static int GetMaxLength(in PipData pipData, PipFragmentRenderer renderer)
        {
            Contract.Requires(pipData.IsValid);

            int fragmentSeparatorLength = renderer.GetLength(pipData.FragmentSeparator, PipDataFragmentEscaping.NoEscaping);
            int length = fragmentSeparatorLength * (pipData.FragmentCount - 1);
            foreach (PipFragment fragment in pipData)
            {
                Contract.Assume(fragment.FragmentType != PipFragmentType.Invalid);

                // recursively handle compound fragments
                if (fragment.FragmentType == PipFragmentType.NestedFragment)
                {
                    length += GetMaxLength(fragment.GetNestedFragmentValue(), renderer);
                    continue;
                }

                // ask renderer to approximate max length for scalar fragments
                length += renderer.GetMaxLength(fragment, pipData.FragmentEscaping, MaxPathStringLength);
            }

            return length;
        }

        #region IEquatable<PipData> implementation

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(PipData other)
        {
            return HeaderEntry.Equals(other.HeaderEntry) &&
                Entries.Equals(other.Entries);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(HeaderEntry.GetHashCode(), Entries.GetHashCode());
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
        public static bool operator ==(PipData left, PipData right)
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
        public static bool operator !=(PipData left, PipData right)
        {
            return !left.Equals(right);
        }
        #endregion

        #region Enumeration

        /// <summary>
        /// Gets an enumerator over the fragments in the pip data
        /// </summary>
        public FragmentEnumerator GetEnumerator()
        {
            return new FragmentEnumerator(this);
        }

        /// <summary>
        /// Gets an enumerator over the fragments in the pip data
        /// </summary>
        IEnumerator<PipFragment> IEnumerable<PipFragment>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets an enumerator over the fragments in the pip data
        /// </summary>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Enumerator over fragments in a PipData
        /// </summary>
        public struct FragmentEnumerator : IEnumerator<PipFragment>
        {
            private readonly PipData m_pipData;
            private int m_currentIndex;

            internal FragmentEnumerator(PipData pipData)
            {
                m_pipData = pipData;
                m_currentIndex = 0;
            }

            /// <summary>
            /// Gets the <see cref="PipFragment"/> in the collection at the current position of the enumerator.
            /// </summary>
            public PipFragment Current => new PipFragment(m_pipData.Entries, m_currentIndex);

            /// <summary>
            /// Disposes the enumerator.
            /// </summary>
            public void Dispose()
            {
            }

            /// <summary>
            /// Gets the <see cref="PipFragment"/> in the collection at the current position of the enumerator.
            /// </summary>
            object System.Collections.IEnumerator.Current => Current;

            /// <summary>
            /// Advances the enumerator to the next element of the collection.
            /// </summary>
            /// <returns>true if the enumerator was successfully advanced to the next position. Otherwise, false.</returns>
            public bool MoveNext()
            {
                if (m_currentIndex == 0)
                {
                    Contract.Assert(m_pipData.Entries[0].EntryType == PipDataEntryType.NestedDataStart);
                    m_currentIndex++;
                }
                else if (m_currentIndex < m_pipData.Entries.Count)
                {
                    bool canContinue = false;
                    bool encounteredHeader = false;
                    while (!canContinue)
                    {
                        var current = m_pipData.Entries[m_currentIndex];
                        switch (current.EntryType)
                        {
                            case PipDataEntryType.AbsolutePath:
                            case PipDataEntryType.StringLiteral:
                            case PipDataEntryType.NestedDataEnd:
                                m_currentIndex++;
                                canContinue = true;
                                break;
                            case PipDataEntryType.VsoHashEntry1Path:
                                Contract.Assert(m_currentIndex + 1 < m_pipData.Entries.Count);
                                Contract.Assert(m_pipData.Entries[m_currentIndex + 1].EntryType == PipDataEntryType.VsoHashEntry2RewriteCount);
                                m_currentIndex += 2;
                                canContinue = true;
                                break;
                            case PipDataEntryType.VsoHashEntry2RewriteCount:
                                Contract.Assume(false, "should never encounter part 2 of VsoHash fragment");
                                break;
                            case PipDataEntryType.IpcMoniker:
                                m_currentIndex++;
                                canContinue = true;
                                break;
                            case PipDataEntryType.NestedDataHeader:
                                m_currentIndex++;
                                encounteredHeader = true;
                                break;
                            case PipDataEntryType.NestedDataStart:
                                Contract.Assume(encounteredHeader);
                                m_currentIndex += current.GetIntegralValue();
                                canContinue = true;
                                break;
                            default:
                                Contract.Assert(false, "invalid entry type: " + current.EntryType);
                                break;
                        }
                    }
                }

                if (m_currentIndex >= m_pipData.Entries.Count)
                {
                    m_currentIndex = m_pipData.Entries.Count;
                    return false;
                }

                return Current.IsValid;
            }

            /// <summary>
            /// Resets the enumerator to its initial state
            /// </summary>
            public void Reset()
            {
                m_currentIndex = 0;
            }
        }
        #endregion
    }
}
