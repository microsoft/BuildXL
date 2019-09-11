// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Represents a list of <see cref="PipDataEntry"/> items
    /// </summary>
    internal readonly struct PipDataEntryList : IReadOnlyList<PipDataEntry>, IEquatable<PipDataEntryList>
    {
        // NOTE: Not readonly to avoid compiler injected defensive copies
        private readonly ArrayView<byte> m_bytes;

        public PipDataEntryList(ArrayView<byte> bytes)
        {
            m_bytes = bytes;
        }

        public PipDataEntry this[int index]
        {
            get
            {
                int byteIndex = index * PipDataEntry.BinarySize;
                return PipDataEntry.Read(m_bytes, ref byteIndex);
            }
        }

        public int Count => m_bytes.Length / PipDataEntry.BinarySize;

        public PipDataEntryList GetSubView(int index)
        {
            return new PipDataEntryList(m_bytes.GetSubView(index * PipDataEntry.BinarySize));
        }

        public PipDataEntryList GetSubView(int index, int length)
        {
            return new PipDataEntryList(m_bytes.GetSubView(index * PipDataEntry.BinarySize, length * PipDataEntry.BinarySize));
        }

        public ReadOnlyListEnumerator<PipDataEntryList, PipDataEntry> GetEnumerator()
        {
            return new ReadOnlyListEnumerator<PipDataEntryList, PipDataEntry>(this);
        }

        IEnumerator<PipDataEntry> IEnumerable<PipDataEntry>.GetEnumerator()
        {
            var count = Count;
            for (int i = 0; i < count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            var count = Count;
            for (int i = 0; i < count; i++)
            {
                yield return this[i];
            }
        }

        public override int GetHashCode()
        {
            return m_bytes.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        public bool Equals(PipDataEntryList other)
        {
            return m_bytes.Equals(other.m_bytes);
        }

        internal static PipDataEntryList FromEntries(IReadOnlyList<PipDataEntry> entries)
        {
            byte[] bytes = new byte[entries.Count * PipDataEntry.BinarySize];
            int byteIndex = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].Write(bytes, ref byteIndex);
            }

            return new PipDataEntryList(bytes);
        }

        public void Serialize(BuildXLWriter writer)
        {
            var count = Count;
            writer.WriteCompact(count);
            for (int i = 0; i < count; i++)
            {
                var entry = this[i];
                entry.Serialize(writer);
                if (entry.EntryType == PipDataEntryType.DirectoryIdHeaderSealId)
                {
                    // Get next entry and write out the full directory artifact rather than serializing an entry path
                    i++;
                    var nextEntry = this[i];
                    Contract.Assert(nextEntry.EntryType == PipDataEntryType.AbsolutePath);
                    writer.Write(new DirectoryArtifact(nextEntry.GetPathValue(), entry.GetUInt32Value()));
                }
            }
        }

        public static (int count, IEnumerable<PipDataEntry> entries) Deserialize(BuildXLReader reader)
        {
            var count = reader.ReadInt32Compact();

            return (count, getEntries());

            IEnumerable<PipDataEntry> getEntries()
            {
                for (int i = 0; i < count; i++)
                {
                    var entry = PipDataEntry.Deserialize(reader);
                    if (entry.EntryType == PipDataEntryType.DirectoryIdHeaderSealId)
                    {
                        var directory = reader.ReadDirectoryArtifact();

                        PipDataEntry.CreateDirectoryIdEntries(directory, out var entry1SealId, out var entry2Path);
                        yield return entry1SealId;
                        yield return entry2Path;
                        i++; // Skip next entry since the directory artifact encapsulates both
                    }
                    else
                    {
                        yield return entry;
                    }
                }
            }
        }

        /// <nodoc />
        public static bool operator ==(PipDataEntryList left, PipDataEntryList right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(PipDataEntryList left, PipDataEntryList right)
        {
            return !left.Equals(right);
        }
    }
}
