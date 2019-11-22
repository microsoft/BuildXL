// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
#nullable enable

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
