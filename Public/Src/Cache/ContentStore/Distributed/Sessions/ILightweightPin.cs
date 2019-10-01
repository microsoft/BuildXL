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
    public interface ILightweightPin
    {
        Task<IEnumerable<Task<Indexed<PinResult>>>> LightweightPinAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint);
    }
}
