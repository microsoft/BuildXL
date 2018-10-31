// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Threading;
using BuildXL.Utilities;

namespace BuildXL.Native.Streams
{  
    internal sealed class OverlappedPoolNode : IDisposable
    {
        private const int PoolNodeSize = 64;

        /// <summary>
        /// OVERLAPPED structures being pooled. This array is pinned so long as this pool node has not been disposed.
        /// </summary>
        private readonly TaggedOverlapped[] m_entries;

        /// <summary>
        /// Targets for m_entries (corresponding by index).
        /// We can't make <see cref="IIOCompletionTarget" /> a member of <see cref="TaggedOverlapped" />
        /// since it is a non-blittable type and so makes any container un-pinnable.
        /// </summary>
        private readonly IIOCompletionTarget[] m_targets;

        private GCHandle m_entriesPinningHandle;
        private long m_availableEntryMask;

        public OverlappedPoolNode(int id)
        {
            Contract.Requires(id >= 0);
            m_entries = new TaggedOverlapped[PoolNodeSize];
            for (int i = 0; i < PoolNodeSize; i++)
            {
                m_entries[i] = new TaggedOverlapped(poolNodeId: id);
            }

            m_targets = new IIOCompletionTarget[PoolNodeSize];
            m_entriesPinningHandle = GCHandle.Alloc(m_entries, GCHandleType.Pinned);

            // All entries are initially available.
            m_availableEntryMask = ~0L;
        }

        public unsafe TaggedOverlapped* TryReserveOverlappedWithTarget(IIOCompletionTarget target)
        {
            // Try to reserve an entry by clearing its available bit. We give up when no bits are set.
            int reservedIndex;
            while (true)
            {
                long availableSigned = Volatile.Read(ref m_availableEntryMask);
                if (availableSigned == 0)
                {
                    // No entry available; caller should allocate a new node or try again later.
                    return null;
                }

                var available = unchecked((ulong)availableSigned);
                reservedIndex = Bits.FindLowestBitSet(available);
                ulong newAvailable = available & ~(1UL << reservedIndex);

                if (Interlocked.CompareExchange(
                    ref m_availableEntryMask,
                    unchecked((long)newAvailable),
                    comparand: availableSigned) == availableSigned)
                {
                    break;
                }
            }

            Contract.Assume(m_targets[reservedIndex] == null);
            m_targets[reservedIndex] = target;

            fixed (TaggedOverlapped* entries = m_entries)
            {
                TaggedOverlapped* reservedEntry = &entries[reservedIndex];
                reservedEntry->Reserve(reservedIndex);
                return reservedEntry;
            }
        }

        public unsafe IIOCompletionTarget ReleaseOverlappedAndGetTarget(TaggedOverlapped* overlapped)
        {
            int entryId = Volatile.Read(ref overlapped->EntryId);
            Contract.Assume(entryId != TaggedOverlapped.AvailableMarker, "Attempting to release an available TaggedOverlapped");
            Contract.Assume(entryId >= 0 && entryId < PoolNodeSize);
            overlapped->Release();

            IIOCompletionTarget target = m_targets[entryId];
            m_targets[entryId] = null;
            Contract.Assume(target != null);

            // Set the bit corresponding to this entry ID. This publishes the entry so that TryReserveOverlapped can use it again.
            while (true)
            {
                long availableSigned = Volatile.Read(ref m_availableEntryMask);
                ulong newAvailable = unchecked((ulong)availableSigned) | (1UL << entryId);

                if (Interlocked.CompareExchange(
                    ref m_availableEntryMask,
                    unchecked((long)newAvailable),
                    comparand: availableSigned) == availableSigned)
                {
                    break;
                }
            }

            return target;
        }

        public void Dispose()
        {
            // Swap the available mask from all available to none available.
            // If this fails, there was a double-dispose or an overlapped is in use (so un-pinning would be scary).
            bool swapSucceeded = Interlocked.CompareExchange(ref m_availableEntryMask, 0L, comparand: ~0L) == ~0L;
            if (!swapSucceeded)
            {
                ExceptionUtilities.FailFast(
                    "OverlappedPoolNode was double-disposed or is still in use (outstanding I/O)",
                    new InvalidOperationException());
            }

            if (m_entriesPinningHandle.IsAllocated)
            {
                m_entriesPinningHandle.Free();
            }
        }
    }
}
