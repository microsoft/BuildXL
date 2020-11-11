// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Interfaces for handling events that changes an application state.
    /// </summary>
    /// <remarks>
    /// All the methods in this interface return the number of mutations occurred when the event is handled.
    /// </remarks>
    public interface IContentLocationEventHandler
    {
        /// <summary>
        /// The specified <paramref name="hashes"/> were added to the <paramref name="sender"/>.
        /// </summary>
        long LocationAdded(OperationContext context, MachineId sender, IReadOnlyList<ShortHashWithSize> hashes, bool reconciling, bool updateLastAccessTime);

        /// <summary>
        /// The specified <paramref name="hashes"/> were removed from the <paramref name="sender"/>.
        /// </summary>
        long LocationRemoved(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, bool reconciling);

        /// <summary>
        /// The content specified by the <paramref name="hashes"/> was touched at <paramref name="accessTime"/>.
        /// </summary>
        long ContentTouched(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, UnixTime accessTime);

        /// <summary>
        /// The specified metadata entry with the given strong fingerprint key was updated
        /// </summary>
        long MetadataUpdated(OperationContext context, StrongFingerprint strongFingerprint, MetadataEntry entry);
    }
}
