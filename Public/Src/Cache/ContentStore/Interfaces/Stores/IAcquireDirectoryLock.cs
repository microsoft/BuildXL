// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    ///     Interface for acquiring directory lock.
    /// </summary>
    public interface IAcquireDirectoryLock
    {
        /// <summary>
        ///     Obtains unique access to the store by acquiring directory lock.
        /// </summary>
        Task<BoolResult> AcquireDirectoryLockAsync(Context context);
    }
}
