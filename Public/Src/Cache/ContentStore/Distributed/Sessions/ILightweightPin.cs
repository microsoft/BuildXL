// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Pin in a (hopefully) faster way, while being less reliable.
    /// </summary>
    public interface ILightweightPin
    {
        /// <summary>
        /// Check for existence in L2, return the missing content, then fire-and-forget L2 pin to locally copy the found content.
        /// </summary>
        Task<IEnumerable<Task<Indexed<PinResult>>>> LightweightPinAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint);
    }
}
