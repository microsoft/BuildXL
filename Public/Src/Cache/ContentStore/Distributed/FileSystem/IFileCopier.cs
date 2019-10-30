// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

// ReSharper disable All
namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Represents an interface that allows copying files from a remote source to a local path.
    /// </summary>
    /// <typeparam name="T">The type of the path that represents a remote file.</typeparam>
    public interface IFileCopier<in T>
        where T : PathBase
    {
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
    /// Represents an interface that allows copying files from a remote source to a local path with an ability to specify tracing context for the operation.
    /// </summary>
    public interface ITraceableFileCopier<in T>
        where T : PathBase
    {
        /// <summary>
        /// Copies a file represented by the path into the stream specified.
        /// </summary>
        Task<CopyFileResult> CopyToAsync(OperationContext context, T sourcePath, Stream destinationStream, long expectedContentSize);
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
    /// Represents an interface that allows copying files from a remote source to a local path using absolute path with an ability to specify tracing context for the operation.
    /// </summary>
    public interface ITraceableAbsolutePathFileCopier : IAbsolutePathFileCopier, ITraceableFileCopier<AbsolutePath>
    {
    }

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
        /// <param name="targetMachine">The machine that should copy the file</param>
        Task<BoolResult> RequestCopyFileAsync(OperationContext context, ContentHash hash, MachineLocation targetMachine);

        /// <summary>
        /// Pushes content to a target machine.
        /// </summary>
        Task<BoolResult> PushFileAsync(OperationContext context, ContentHash hash, Stream source, MachineLocation targetMachine);
    }

    /// <summary>
    /// Extension methods for <see cref="IFileCopier{T}"/> interface.
    /// </summary>
    public static class AbsolutePathFileCopierExtensions
    {
        /// <nodoc />
        public static Task<CopyFileResult> CopyToWithOperationContextAsync<T>(
            this IFileCopier<T> copier,
            OperationContext context,
            T sourcePath,
            Stream destinationStream,
            long expectedContentSize) where T : PathBase
        {
            if (copier is ITraceableFileCopier<T> traceable)
            {
                return traceable.CopyToAsync(context, sourcePath, destinationStream, expectedContentSize);
            }

            return copier.CopyToAsync(sourcePath, destinationStream, expectedContentSize, context.Token);
        }
    }
}
