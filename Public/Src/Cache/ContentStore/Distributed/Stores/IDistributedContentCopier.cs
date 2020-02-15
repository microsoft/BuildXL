// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// Handles copies from remote locations to a local store
    /// </summary>
    public interface IDistributedContentCopier
    {
        /// <nodoc />
        IAbsFileSystem FileSystem { get; }

        /// <summary>
        /// Attempts to copy a content locally from a given set of remote locations and put into a local CAS
        /// </summary>
        Task<PutResult> TryCopyAndPutAsync(
            OperationContext operationContext,
            IDistributedContentCopierHost host,
            ContentHashWithSizeAndLocations hashInfo,
            Func<(CopyFileResult copyResult, AbsolutePath tempLocation, int attemptCount), Task<PutResult>> handleCopyAsync,
            Action<IReadOnlyList<MachineLocation>> handleBadLocations = null);
    }
}
