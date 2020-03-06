// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// An interface that allows checking file existence.
    /// </summary>
    public interface IFileExistenceChecker<in T> where T : PathBase
    {
        /// <summary>
        /// Checks that the file represented by <paramref name="path"/> exists.
        /// </summary>
        Task<FileExistenceResult> CheckFileExistsAsync(T path, TimeSpan timeout, CancellationToken cancellationToken);
    }
}
