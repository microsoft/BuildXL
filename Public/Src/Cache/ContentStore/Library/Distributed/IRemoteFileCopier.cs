// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Represents an interface that allows copying files from a remote source to a local path.
    /// </summary>
    public interface IRemoteFileCopier<in T>
        where T : PathBase
    {
        /// <summary>
        /// Copies a file represented by the path into the stream specified.
        /// </summary>
        Task<CopyFileResult> CopyToAsync(OperationContext context, T sourcePath, Stream destinationStream, long expectedContentSize, CopyOptions options);
    }

    /// <nodoc />
    public static class RemoteFileCopierExtensions
    {
        /// <inheritdoc cref="IRemoteFileCopier{T}.CopyToAsync"/>
        public static Task<CopyFileResult> CopyToAsync<T>(this IRemoteFileCopier<T> remoteFileCopier, OperationContext context, T sourcePath, Stream destinationStream, long expectedContentSize, CopyOptions options)
            where T : PathBase
        {
            return remoteFileCopier.CopyToAsync(context, sourcePath, destinationStream, expectedContentSize, options);
        }
    }
}
