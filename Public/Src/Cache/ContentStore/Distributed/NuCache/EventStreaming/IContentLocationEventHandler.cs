// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Interfaces for handling events that changes an application state.
    /// </summary>
    public interface IContentLocationEventHandler
    {
        /// <summary>
        /// The specified <paramref name="hashes"/> were added to the <paramref name="sender"/>.
        /// </summary>
        void LocationAdded(OperationContext context, MachineId sender, IReadOnlyList<ShortHashWithSize> hashes, bool reconciling);

        /// <summary>
        /// The specified <paramref name="hashes"/> were removed from the <paramref name="sender"/>.
        /// </summary>
        void LocationRemoved(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, bool reconciling);

        /// <summary>
        /// The content specified by the <paramref name="hashes"/> was touched at <paramref name="accessTime"/>.
        /// </summary>
        void ContentTouched(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, UnixTime accessTime);
    }
}
