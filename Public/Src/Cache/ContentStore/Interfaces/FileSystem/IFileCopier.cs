// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

// ReSharper disable All
namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Represents an interface that allows copying files from a remote source to a local path.
    /// </summary>
    /// <typeparam name="T">The type of the path that represents a remote file.</typeparam>
    public interface IFileCopier<in T>
        where T : PathBase
    {
        /// <summary>
        /// Copies a file represented by the path into the destination absolute path
        /// specified. Overwrites the destination if required.
        /// </summary>
        /// <exception cref="CopyFileResult">
        /// throws the exception if the copy fails because the source path is not available.
        /// </exception>
        /// <param name="path">the source opaque path to copy from</param>
        /// <param name="destinationPath">the destination absolute path to copy into</param>
        /// <param name="contentSize">size of content</param>
        /// <param name="overwrite">whether or not to overwrite the destination path if it exists.</param>
        /// <param name="cancellationToken">the cancellation token</param>
        Task<CopyFileResult> CopyFileAsync(T path, AbsolutePath destinationPath, long contentSize, bool overwrite, CancellationToken cancellationToken);

        /// <summary>
        /// Copies a file represented by the path into the stream specified.
        /// </summary>
        /// <param name="sourcePath">The source opaque path to copy from.</param>
        /// <param name="destinationStream">The destination stream to copy into.</param>
        /// <param name="expectedContentSize">Size of content.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task<CopyFileResult> CopyToAsync(T sourcePath, Stream destinationStream, long expectedContentSize, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Represents an interface that allows copying files from a remote source to a local path.
    /// </summary>
    public interface IFileCopier : IFileCopier<PathBase>, IFileExistenceChecker<PathBase> { }

    /// <summary>
    /// Represents an interface that allows copying files form a remote source to a local path using absolute paths.
    /// </summary>
    public interface IAbsolutePathFileCopier : IFileCopier<AbsolutePath>, IFileExistenceChecker<AbsolutePath> { }

    /// <summary>
    /// Requests another machine to copy from the current machine.
    /// </summary>
    public interface ICopyRequester
    {
        /// <summary>
        /// Requests another machine to copy a file.
        /// </summary>
        /// <param name="context">The context of the operation</param>
        /// <param name="hash">The hash of the file to be copied.</param>
        /// <param name="targetMachineName">The machine that should copy the file</param>
        Task<BoolResult> RequestCopyFileAsync(Context context, ContentHash hash, string targetMachineName);
    }
}
