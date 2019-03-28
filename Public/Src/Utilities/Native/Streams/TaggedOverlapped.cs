// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities;

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// <see cref="Overlapped" /> followed by a request ID known to this completion manager.
    /// Since a dequeued completion packet comes with an <c>OVERLAPPED*</c> as the only unique identifier
    /// of the originating I/O request, we store a request identifier immediately following it (as an alternative
    /// to an (OVERLAPPED* -> request) dictionary).
    /// </summary>
    internal struct TaggedOverlapped
    {
        public const int AvailableMarker = -1;

        public Overlapped Overlapped;

        /// <summary>
        /// Index of the pool node (in the larger pool) that owns this structure.
        /// </summary>
        public readonly int PoolNodeId;

        /// <summary>
        /// Entry index in the owning pool node.
        /// When the node is available, this is set to <see cref="AvailableMarker" />
        /// </summary>
        public int EntryId;

        public TaggedOverlapped(int poolNodeId)
        {
            Contract.Requires(poolNodeId >= 0);

            PoolNodeId = poolNodeId;
            EntryId = AvailableMarker;
            Overlapped = default(Overlapped);
        }

        /// <summary>
        /// Returns an ID for this operation, unique within a completion manager. The ID is valid only for the duration of the operation.
        /// </summary>
        public ulong GetUniqueId()
        {
            Contract.Requires(EntryId != AvailableMarker);
            return Bits.GetLongFromInts(unchecked((uint)PoolNodeId), (uint)EntryId);
        }

        public void Reserve(int entryId)
        {
            if (Interlocked.CompareExchange(ref EntryId, entryId, comparand: AvailableMarker) != AvailableMarker)
            {
                Contract.Assume(false, "TaggedOverlapped not available; bad behavior of bitfield allocation in OverlappedPoolNode");
            }
        }

        public void Release()
        {
            int entryId;
            do
            {
                entryId = Volatile.Read(ref EntryId);
                Contract.Assume(entryId != AvailableMarker, "TaggedOverlapped not in use");
            }
            while (Interlocked.CompareExchange(ref EntryId, AvailableMarker, comparand: entryId) != entryId);
        }
    }
}
