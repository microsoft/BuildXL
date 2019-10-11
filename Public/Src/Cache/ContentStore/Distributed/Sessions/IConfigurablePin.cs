// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Pin in a configurable way.
    /// </summary>
    public interface IConfigurablePin
    {
        /// <summary>
        /// Pin in a configurable way.
        /// </summary>
        Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration config);
    }
}
