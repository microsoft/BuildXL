// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Transforms content hashes to an opaque path that a remote interface can operate with.
    /// </summary>
    /// <typeparam name="T">The representation of path that a remote interface can process.</typeparam>
    public interface IPathTransformer<T>
        where T : PathBase
    {
        /// <summary>
        /// Return opaque content location data that will be set for locally produced content.
        /// </summary>
        /// <param name="cacheRoot">The cache root path to the local cache.</param>
        /// <remarks>
        /// Returned location data is saved to the ContentLocationStore
        /// and used by peers for downloading content from local machine
        /// e.g. An implementation could be \\machine\cacheRoot
        /// </remarks>
        byte[] GetLocalMachineLocation(AbsolutePath cacheRoot);

        /// <summary>
        /// Generates a path a content hash on a remote machine given opaque data about the remote machine and a content hash.
        /// </summary>
        /// <param name="contentHash">The content hash that is reachable via this path.</param>
        /// <param name="contentLocationIdContent">Data about the machine reachable in the path.</param>
        /// <returns>A path that allows a file copier to retrieve the content hash locally.</returns>
        T GeneratePath(ContentHash contentHash, byte[] contentLocationIdContent);

        /// <summary>
        /// Given a specific path, retrieves machine specific data about the path.
        /// </summary>
        /// <param name="path">An opaque path</param>
        /// <returns>Machine data from the path</returns>
        /// <remarks>
        /// Used by <see cref="IFileCopier{T}"/> for downloading content from the peer
        /// represented by given opaque path
        /// e.g. An implementation could be \\machine\cacheRoot
        /// </remarks>
        byte[] GetPathLocation(T path);
    }

    /// <summary>
    /// Transforms content hashes to an opaque path that a remote interface can operate with.
    /// </summary>
    public interface IPathTransformer : IPathTransformer<PathBase> { }

    /// <summary>
    /// Transforms content hashes to an opaque path that a remote interface can operate with.
    /// </summary>
    public interface IAbsolutePathTransformer : IPathTransformer<AbsolutePath> { }
}
