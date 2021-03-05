// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

// ReSharper disable All
namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Handles requests that machine to copy a file to itself.
    /// </summary>
    public interface ICopyRequestHandler
    {
        /// <summary>
        /// Requests the machine to copy a file to itself.
        /// </summary>
        /// <param name="context">The context of the operation</param>
        /// <param name="hash">The hash of the file to be copied.</param>
        /// <param name="token">A cancellation token</param>
        Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash, CancellationToken token);
    }

    /// <summary>
    /// Handles requests to push content to this machine.
    /// </summary>
    public interface IPushFileHandler
    {
        /// <nodoc />
        Task<PutResult> HandlePushFileAsync(Context context, ContentHash hash, FileSource source, CancellationToken token);

        /// <nodoc />
        bool CanAcceptContent(Context context, ContentHash hash, out RejectionReason rejectionReason);
    }

    /// <summary>
    /// Represents the source of a file, rather from a file on disk (via path) or a stream
    /// </summary>
    public class FileSource
    {
        /// <summary>
        /// The source path (null if <see cref="FileSource"/> is stream-based)
        /// </summary>
        public AbsolutePath? Path { get; }

        /// <summary>
        /// The file realization mode for a path-based <see cref="FileSource"/>
        /// </summary>
        public FileRealizationMode FileRealizationMode { get; }

        /// <summary>
        /// The backing stream (null if <see cref="FileSource"/> is path-based)
        /// </summary>
        public Stream? Stream { get; }

        /// <summary>
        /// Creates a stream-based file source
        /// </summary>
        public FileSource(Stream stream)
        {
            Stream = stream;
            Path = default;
            FileRealizationMode = default;
        }

        /// <summary>
        /// Creates a path-based file source with optional realization mode
        /// </summary>
        public FileSource(AbsolutePath path, FileRealizationMode mode = FileRealizationMode.Copy)
        {
            Stream = null;
            Path = path;
            FileRealizationMode = mode;
        }
    }

    /// <summary>
    /// Reason why the server rejected a copy.
    /// NOTE: Make sure that, when adding, we can also translate enum to a PushFileResultStatus (See PushFileResult.Rejected)
    /// </summary>
    public enum RejectionReason
    {
        /// <nodoc />   
        Accepted,

        /// <nodoc />
        ContentAvailableLocally,

        /// <nodoc />
        OlderThanLastEvictedContent,

        /// <nodoc />
        NotSupported,

        /// <nodoc />
        CopyLimitReached,

        /// <nodoc />
        OngoingCopy,
    }

    /// <summary>
    /// Handles delete requests to this machine
    /// </summary>
    public interface IDeleteFileHandler
    {
        /// <nodoc />
        Task<DeleteResult> HandleDeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions);
    }
}
