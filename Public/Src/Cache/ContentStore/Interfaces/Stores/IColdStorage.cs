// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    ///     Interface to interact with ColdStorage.
    ///     Breaks the circular dependency that is generated when the ColdStorage is called from lower layers
    /// </summary>
    public interface IColdStorage : IStartupShutdownSlim
    {
        /// <summary>
        ///     Put file into ColdStorage during the eviction process
        /// </summary>
        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            DisposableFile disposableFile,
            CancellationToken cts);

        /// <summary>
        ///     Open stream from ColdStorage to copy via GRPC
        /// </summary>
        public Task<OpenStreamResult> OpenStreamAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal);

    }
}
