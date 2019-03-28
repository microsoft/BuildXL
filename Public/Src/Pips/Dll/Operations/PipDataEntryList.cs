// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
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
