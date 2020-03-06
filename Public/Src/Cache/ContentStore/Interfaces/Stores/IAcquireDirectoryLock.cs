// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
