// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Performs remote pinning for <see cref="IReadOnlyContentSession.PinAsync(BuildXL.Cache.ContentStore.Interfaces.Tracing.Context, System.Collections.Generic.IReadOnlyList{BuildXL.Cache.ContentStore.Hashing.ContentHash}, CancellationToken, UrgencyHint)"/>
    /// </summary>
    public delegate Task<IEnumerable<Task<Indexed<PinResult>>>> RemotePinAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal
        );
}
