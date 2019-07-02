// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Set of integers in some range [0, length). Each integer corresponds to a single bit position in a bit vector.
    /// </summary>
    /// <remarks>
    /// This is somewhat like <see cref="BitArray"/> but with a reasonable enumerator. <see cref="BitArray"/> has a non-generic enumerator
    /// and returns all bits therein, whereas this one yields only those range members actually present (set vs. array semantics).
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
    public sealed class BitSet : IEnumerable<int>
    {
        private long[] m_entries;
        private int m_lengthInEntries;

        /// <summary>
        /// Creates a bit set with the given initial capacity (in bits) and a length of zero.
        /// </summary>
        public BitSet(int initialCapacity = 0)
        {
            Contract.Requires(initialCapacity >= 0 && IsValidBitCount(initialCapacity));
            Contract.Ensures(Capacity == initialCapacity);
            Contract.Ensures(Length == 0);

            int initialCapacityInEntries = GetEntryCount(initialCapacity);
            m_entries = new long[initialCapacityInEntries];
            m_lengthInEntries = 0;
        }

        /// <summary>
        /// Clones the bit set.
        /// </summary>
        public BitSet Clone()
        {
            long[] shallowCopy = new long[m_lengthInEntries];

            // Buffer.BlockCopy is faster than Array.Copy or Array.Clone methods.
            Buffer.BlockCopy(m_entries, 0, shallowCopy, 0, m_lengthInEntries * sizeof(long));
            return new BitSet(shallowCopy);
        }

        /// <summary>
        /// Deserialization constructor
        /// </summary>
        private BitSet(long[] entries)
        {
            Contract.Requires(entries != null);
            m_lengthInEntries = entries.Length;
            m_entries = entries;
        }

        /// <summary>
        /// Number of bits allocated in this set (always greater than or equal to length).
        /// </summary>
        public int Capacity
        {
            get
            {
                Contract.Ensures(Contract.Result<int>() >= Length && Contract.Result<int>() >= 0);
                return GetBitCount(m_entries.Length);
            }
        }

        /// <summary>
        /// Size of the integer range (i.e., number of bits) represented.
        /// The set may be expanded or contracted with <see cref="SetLength"/>.
        /// </summary>
        public int Length => GetBitCount(m_lengthInEntries);

        private ulong GetEntry(int index)
        {
            return unchecked((ulong)m_entries[index / 64]);
        }

        private void SetEntry(int index, ulong value)
        {
            m_entries[index / 64] = unchecked((long)value);
        }

        private bool TryCompareExchangeEntry(int index, ulong value, ulong comparand)
        {
            return Interlocked.CompareExchange(
                ref m_entries[index / 64],
                unchecked((long)value),
                comparand: unchecked((long)comparand)) == unchecked((long)comparand);
        }

        /// <summary>
        /// Adds an integer to this set (if it is not already present).
        /// </summary>
        public void Add(int index)
        {
            if (index < 0 || index >= Length)
            {
                Contract.Assert(false, $"index={index} must be within [0, {Length}) range.");
            }            

            ulong newEntry = GetEntry(index) | (1UL << (index % 64));
            SetEntry(index, newEntry);
        }

        /// <summary>
        /// Adds an integer to this set (if it is not already present).
        /// This operation is atomic.
        /// </summary>
        public void AddAtomic(int index)
        {
            if (index < 0 || index >= Length)
            {
                Contract.Assert(false, $"index={index} must be within [0, {Length}) range.");
            }

            int entryIndex = index / 64;
            ulong targetEntry;
            ulong currentEntry;
            do
            {
                currentEntry = unchecked((ulong)Volatile.Read(ref m_entries[entryIndex]));
                targetEntry = currentEntry | (1UL << (index % 64));
            }
            while (currentEntry != targetEntry &&
                   !TryCompareExchangeEntry(index, targetEntry, comparand: currentEntry));
        }

        /// <summary>
        /// Removes an integer from this set (if present).
        /// </summary>
        public void Remove(int index)
        {
            if (index < 0 || index >= Length)
            {
                Contract.Assert(false, $"index={index} must be within [0, {Length}) range.");
            }

            ulong newEntry = GetEntry(index) & ~(1UL << (index % 64));
            SetEntry(index, newEntry);
        }

        /// <summary>
        /// Indicates if an integer is present in this set.
        /// </summary>
        public bool Contains(int index)
        {
            if (index < 0 || index >= Length)
            {
                Contract.Assert(false, $"index={index} must be within [0, {Length}) range.");
            }

            return (GetEntry(index) & 1UL << (index % 64)) != 0;
        }

        /// <summary>
        /// Removes all integers from this set (without changing length or capacity).
        /// </summary>
        public void Clear()
        {
            Array.Clear(m_entries, 0, m_lengthInEntries);
        }

        /// <summary>
        /// Adds the integers [0, count) into this set (without changing length or capacity). Other integers are cleared.
        /// </summary>
        public void Fill(int count)
        {
            Contract.Requires(count <= Length);

            int entryBitsRoundedUp = RoundToValidBitCount(count);
            int numberOfEntriesToFill = GetEntryCount(entryBitsRoundedUp);

            if (numberOfEntriesToFill == 0)
            {
                return;
            }

            for (int i = 0; i < numberOfEntriesToFill - 1; i++)
            {
                m_entries[i] = unchecked((long)~0UL);
            }

            if (entryBitsRoundedUp == count)
            {
                // Needed since N << 64 == N.
                m_entries[numberOfEntriesToFill - 1] = unchecked((long)~0UL);
            }
            else
            {
                int numberOfBitsSetInLastEntry = 64 - (entryBitsRoundedUp - count);
                ulong lastEntry = (1UL << numberOfBitsSetInLastEntry) - 1;
                m_entries[numberOfEntriesToFill - 1] = unchecked((long)lastEntry);
            }

            for (int i = numberOfEntriesToFill; i < m_lengthInEntries; i++)
            {
                m_entries[i] = 0L;
            }
        }

        /// <summary>
        /// Sets the size of the integer range (i.e., number of bits). Existing entries outside of the
        /// new range are dropped from the set. If the range is expanded, the number of entries remains the same.
        /// </summary>
        public void SetLength(int length)
        {
            Contract.Requires(length >= 0 && IsValidBitCount(length));

            int newLengthInEntries = GetEntryCount(length);
            if (m_entries.Length < newLengthInEntries)
            {
                var newEntries = new long[newLengthInEntries];
                Array.Copy(m_entries, newEntries, m_lengthInEntries);
                m_entries = newEntries;
            }
            else if (newLengthInEntries < m_lengthInEntries)
            {
                // Ensure that the now-removed region is zeroed so that we can expand to include it later
                // (symmetric with allocation)
                Array.Clear(m_entries, newLengthInEntries, m_lengthInEntries - newLengthInEntries);
            }

            Contract.Assert(m_lengthInEntries <= m_entries.Length);

            m_lengthInEntries = newLengthInEntries;
        }

        /// <summary>
        /// Indicates if the given integer range size (as a bit count) is representable.
        /// The bit count must be some multiple of the underlying 64-bit entry size.
        /// </summary>
        [Pure]
        public static bool IsValidBitCount(int bitCount)
        {
            return bitCount % 64 == 0;
        }

        [Pure]
        private static int GetEntryCount(int bitCount)
        {
            Contract.RequiresDebug(IsValidBitCount(bitCount));
            return bitCount / 64;
        }

        [Pure]
        private static int GetBitCount(int entryCount)
        {
            var result = checked(entryCount * 64);
            Contract.AssertDebug(IsValidBitCount(Contract.Result<int>()));
            return result;
        }

        /// <summary>
        /// Rounds up the given bit count as needed to an allowable size for a <see cref="BitSet"/>
        /// (i.e., the returned value satisfies <see cref="IsValidBitCount"/>)
        /// </summary>
        public static int RoundToValidBitCount(int unroundedBitCount)
        {
            var result = checked(unroundedBitCount + 63) & ~63;
            Contract.AssertDebug(IsValidBitCount(Contract.Result<int>()));
            return result;
        }

        /// <summary>
        /// Returns an enumerator for the integers in this set.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(m_entries, m_lengthInEntries);
        }

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #region Serialization

        /// <nodoc />
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(m_lengthInEntries);
            for (int i = 0; i < m_lengthInEntries; i++)
            {
                writer.Write(m_entries[i]);
            }
        }

        /// <nodoc />
        public static BitSet Deserialize(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            Contract.Assume(length >= 0);

            var entries = new long[length];
            for (int i = 0; i < length; i++)
            {
                entries[i] = reader.ReadInt64();
            }

            return new BitSet(entries);
        }

        #endregion

        /// <summary>
        /// Enumerator for the integer entries in a <see cref="BitSet"/>.
        /// </summary>
        public struct Enumerator : IEnumerator<int>
        {
            private long[] m_entries;
            private readonly int m_lengthInEntries;

            private int m_entryIndexPlusOne; // PlusOne so that zero (default) is invalid.
            private ulong m_currentEntry; // Current entry over which we are enumerating set bits inside.
            private int m_currentEntryBitIndex;

            internal Enumerator(long[] entries, int lengthInEntries)
            {
                Contract.Requires(entries != null);
                Contract.Requires(lengthInEntries >= 0);

                m_entries = entries;
                m_lengthInEntries = lengthInEntries;

                m_entryIndexPlusOne = 0;
                m_currentEntry = 0;
                m_currentEntryBitIndex = -1;
            }

            /// <inheritdoc />
            public int Current
            {
                get
                {
                    Contract.Assume(m_entryIndexPlusOne != 0, "Must call MoveNext at least once first");
                    Contract.Assume(m_entryIndexPlusOne <= m_lengthInEntries, "Iteration exhausted (MoveNext() == false)");
                    Contract.Assume(m_currentEntryBitIndex >= 0, "Bit position unset");

                    return checked(((m_entryIndexPlusOne - 1) * 64) + m_currentEntryBitIndex);
                }
            }

            /// <inheritdoc />
            public void Dispose()
            {
                m_entries = null;
            }

            object IEnumerator.Current => Current;

            /// <inheritdoc />
            public bool MoveNext()
            {
                // Might be the initial MoveNext.
                if (m_entryIndexPlusOne == 0)
                {
                    if (!TryAdvanceToNextNonemptyEntry())
                    {
                        // Note this can fail with a positive length (in the case of no entries).
                        return false;
                    }
                }

                // We are now definitely positioned onto some entry, but possibly with the initial (invalid) bit position.

                // Now try to advance the bit position (possibly from the invalid -1) to the first position set in the current entry.
                // If that doesn't work, there are perhaps more entries to look at.
                while (!TryAdvanceToNextSetBit())
                {
                    if (!TryAdvanceToNextNonemptyEntry())
                    {
                        return false;
                    }
                }

                // We definitely called TryAdvanceToNextSetBit and m_entryIndexPlusOne is valid.
                return true;
            }

            private bool TryAdvanceToNextSetBit()
            {
                Contract.Assume(m_currentEntryBitIndex >= -1 && m_entryIndexPlusOne > 0);

                ulong entry = m_currentEntry;
                int shift = ++m_currentEntryBitIndex;
                Contract.Assert(shift >= 0);

                // Only the low bits of the shift operand are used, so N >> 64 == N. Ew.
                if (shift == 64)
                {
                    return false;
                }

                Contract.Assume(shift < 64);

                ulong entryRemaining = entry >> shift;
                int firstSetBitIndex = Bits.FindLowestBitSet(entryRemaining);
                if (firstSetBitIndex < 0)
                {
                    Contract.Assume(entryRemaining == 0);
                    return false;
                }

                shift += firstSetBitIndex; // Adds 0 if the current low bit is set.
                Contract.Assume((entry & (1UL << shift)) != 0);

                m_currentEntryBitIndex = shift;
                return true;
            }

            private bool TryAdvanceToNextNonemptyEntry()
            {
                Contract.Assume(m_entryIndexPlusOne >= 0);

                if (m_entryIndexPlusOne <= m_lengthInEntries)
                {
                    m_entryIndexPlusOne++;
                }
                else
                {
                    return false;
                }

                for (; m_entryIndexPlusOne <= m_lengthInEntries; m_entryIndexPlusOne++)
                {
                    ulong entry = unchecked((ulong)m_entries[m_entryIndexPlusOne - 1]);

                    // Here we reap the benefits of 64-bit entries; we can skip empty entries very quickly (good for sparse sets).
                    if (entry != 0)
                    {
                        m_currentEntry = entry;
                        m_currentEntryBitIndex = -1;
                        return true;
                    }
                }

                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                this = new Enumerator(m_entries, m_lengthInEntries);
            }
        }
    }
}
