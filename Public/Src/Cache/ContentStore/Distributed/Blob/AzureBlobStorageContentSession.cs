// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Core.Tracing;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// A content session implementation backed by Azure Blobs.
/// </summary>
public sealed class AzureBlobStorageContentSession : ContentSessionBase
{
    public record Configuration(
        string Name,
        ImplicitPin ImplicitPin,
        TimeSpan StorageInteractionTimeout,
        int FileDownloadBufferSize = 81920);

    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new(nameof(AzureBlobStorageContentSession));

    private readonly Configuration _configuration;
    private readonly AzureBlobStorageContentStore _store;

    private readonly IAbsFileSystem _fileSystem = PassThroughFileSystem.Default;

    /// <nodoc />
    public AzureBlobStorageContentSession(Configuration configuration, AzureBlobStorageContentStore store)
        : base(configuration.Name)
    {
        _configuration = configuration;
        _store = store;
    }

    #region IContentSession Implementation

    /// <inheritdoc />
    protected override Task<PinResult> PinCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        return contentHash.IsEmptyHash() ? Task.FromResult(new PinResult(PinResult.ResultCode.Success)) : PinRemoteAsync(context, contentHash);
    }

    /// <inheritdoc />
    protected override async Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
        OperationContext context,
        IReadOnlyList<ContentHash> contentHashes,
        UrgencyHint urgencyHint,
        Counter retryCounter,
        Counter fileCounter)
    {
        var tasks = contentHashes.Select(
                (contentHash, index) => PinCoreAsync(
                    context,
                    contentHash,
                    urgencyHint,
                    retryCounter).WithIndexAsync(index))
            .ToList(); // It is important to materialize a LINQ query in order to avoid calling 'PinCoreAsync' on every iteration.

        await TaskUtilities.SafeWhenAll(tasks);
        return tasks;
    }

    /// <inheritdoc />
    protected override async Task<OpenStreamResult> OpenStreamCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        if (contentHash.IsEmptyHash())
        {
            return new OpenStreamResult(new MemoryStream(Array.Empty<byte>(), index: 0, count: 0, writable: false, publiclyVisible: false).WithLength(0));
        }

        var stream = await TryOpenRemoteStreamAsync(context, contentHash).ThrowIfFailureAsync();
        return new OpenStreamResult(stream);
    }

    /// <summary>
    /// Open a Stream that does a streaming read of <paramref name="contentHash"/> from the appropriate Azure Storage
    /// account.
    /// </summary>
    /// <param name="context">Operation Context</param>
    /// <param name="contentHash">Content hash to open stream for</param>
    /// <param name="provideLengthWrap">
    /// When the stream needs to be passed to methods that assume the Length exists, we'll wrap the stream with a
    /// <see cref="WrappingStream"/> if this argument is true.
    /// </param>
    /// <remarks>
    /// This method treats a non-existing blob as a different case because doing so makes it a bit cleaner to write the
    /// error propagation logic.
    /// </remarks>
    /// <returns>
    /// The path at which the blob is supposed to be, and a stream to the blob if necessary.
    /// </returns>
    private async Task<(StreamWithLength?, string)> TryOpenReadAsync(OperationContext context, ContentHash contentHash, bool provideLengthWrap = false)
    {
        var (client, blobPath) = await GetBlobClientAsync(context, contentHash);
        try
        {
            var response = await client.DownloadStreamingAsync(cancellationToken: context.Token);

            var value = response.Value;
            var stream = value.Content;
            if (stream is null)
            {
                return (null, blobPath);
            }

            var length = value.Details.ContentLength;
            if (provideLengthWrap)
            {
                var wrapped = new WrappingStream(stream, length);
                return (wrapped.WithLength(length), blobPath);
            }
            return (stream.WithLength(length), blobPath);
        }
        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/blob-service-error-codes
        catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return (null, blobPath);
        }
    }

    /// <summary>
    /// Creates a stream against the given <see cref="ContentHash"/>
    /// </summary>
    /// <returns>Returns Success(null) if the blob for a given content hash is not found.</returns>
    private Task<Result<StreamWithLength?>> TryOpenRemoteStreamAsync(OperationContext context, ContentHash contentHash)
    {
        string blobPath = string.Empty;
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                (StreamWithLength? stream, blobPath) = await TryOpenReadAsync(context, contentHash, provideLengthWrap: true);
                return Result.Success(stream, isNullAllowed: true);
            },
            traceOperationStarted: false,
            timeout: _configuration.StorageInteractionTimeout,
            extraEndMessage: r =>
                             {
                                 long size = -1;
                                 if (r.Succeeded && r.Value is not null)
                                 {
                                     size = r.Value.Value.Length;
                                 }

                                 return $"ContentHash=[{contentHash.ToShortString()}] Size=[{size}] BlobPath=[{blobPath}]";
                             });
    }

    /// <inheritdoc />
    protected override async Task<PlaceFileResult> PlaceFileCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileAccessMode accessMode,
        FileReplacementMode replacementMode,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        if (replacementMode is FileReplacementMode.SkipIfExists or FileReplacementMode.FailIfExists && _fileSystem.FileExists(path))
        {
            return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
        }

        if (contentHash.IsEmptyHash())
        {
            _fileSystem.CreateEmptyFile(path);
            return new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy, fileSize: 0);
        }

        var remoteDownloadResult = await PlaceRemoteFileAsync(context, contentHash, path, accessMode, replacementMode).ThrowIfFailureAsync();
        return new PlaceFileResult(
            code: remoteDownloadResult.ResultCode,
            fileSize: remoteDownloadResult.FileSize ?? 0,
            source: PlaceFileResult.Source.BackingStore);
    }

    private FileStream OpenFileStream(AbsolutePath path, long length, bool randomAccess)
    {
        Contract.Requires(length >= 0);

        var flags = FileOptions.Asynchronous;
        if (randomAccess)
        {
            flags |= FileOptions.RandomAccess;
        }
        else
        {
            flags |= FileOptions.SequentialScan;
        }

        Stream stream;
        try
        {
            stream = _fileSystem.OpenForWrite(
                path,
                length,
                FileMode.Create,
                FileShare.ReadWrite,
                flags).Stream;
        }
        catch (DirectoryNotFoundException)
        {
            _fileSystem.CreateDirectory(path.Parent!);

            stream = _fileSystem.OpenForWrite(
                path,
                length,
                FileMode.Create,
                FileShare.ReadWrite,
                flags).Stream;
        }

        return (FileStream)stream;
    }

    private Task<Result<RemoteDownloadResult>> PlaceRemoteFileAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileAccessMode accessMode,
        FileReplacementMode replacementMode)
    {
        string blobPath = string.Empty;
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var stopwatch = StopwatchSlim.Start();

                try
                {
                    (StreamWithLength? stream, blobPath) = await TryOpenReadAsync(context, contentHash);
                    if (stream is null)
                    {
                        return CreateDownloadResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, stopwatch.Elapsed);
                    }

                    var timeToFirstByteDuration = stopwatch.ElapsedAndReset();

                    using var remoteStream = stream.Value.Stream;

                    using var fileStream = OpenFileStream(path, stream.Value.Length, randomAccess: false);

                    var openFileStreamDuration = stopwatch.ElapsedAndReset();

                    await remoteStream.CopyToAsync(fileStream, _configuration.FileDownloadBufferSize, context.Token);

                    var downloadDuration = stopwatch.ElapsedAndReset();

                    return Result.Success(
                        new RemoteDownloadResult
                        {
                            ResultCode = PlaceFileResult.ResultCode.PlacedWithCopy,
                            FileSize = stream.Value.Length,
                            TimeToFirstByteDuration = timeToFirstByteDuration,
                            DownloadResult = new DownloadResult()
                            {
                                OpenFileStreamDuration = openFileStreamDuration,
                                DownloadDuration = downloadDuration,
                                WriteDuration = fileStream.GetWriteDurationIfAvailable(),
                            },
                        });
                }
                catch
                {
                    // Probably the file should be missing, but deleting it if the failure occurred in the process.
                    try
                    {
                        _fileSystem.DeleteFile(path);
                    }
                    catch (Exception e)
                    {
                        Tracer.Warning(context, e, $"Failure deleting a file '{path}'.");
                    }

                    throw;
                }
            },
            traceOperationStarted: false,
            timeout: _configuration.StorageInteractionTimeout,
            extraEndMessage: r =>
                             {
                                 var baseline =
                                     $"ContentHash=[{contentHash.ToShortString()}] Path=[{path}] AccessMode=[{accessMode}] ReplacementMode=[{replacementMode}] BlobPath=[{blobPath}]";
                                 if (!r.Succeeded)
                                 {
                                     return baseline;
                                 }

                                 var d = r.Value;
                                 return $"{baseline} {r.Value}";
                             });
    }

    private static RemoteDownloadResult CreateDownloadResult(PlaceFileResult.ResultCode resultCode, TimeSpan downloadDuration)
    {
        return new RemoteDownloadResult()
        {
            ResultCode = resultCode,
            DownloadResult = new DownloadResult() { DownloadDuration = downloadDuration, },
        };
    }

    /// <inheritdoc />
    protected override async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
        OperationContext context,
        IReadOnlyList<ContentHashWithPath> hashesWithPaths,
        FileAccessMode accessMode,
        FileReplacementMode replacementMode,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        var tasks = hashesWithPaths.Select(
            (contentHashWithPath, index) =>
            {
                return PlaceFileCoreAsync(
                    context,
                    contentHashWithPath.Hash,
                    contentHashWithPath.Path,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    urgencyHint,
                    retryCounter).WithIndexAsync(index);
            }).ToList(); // It is important to materialize a LINQ query in order to avoid calling 'PlaceFileCoreAsync' on every iteration.

        await TaskUtilities.SafeWhenAll(tasks);
        return tasks;
    }

    /// <inheritdoc />
    protected override async Task<PutResult> PutFileCoreAsync(
        OperationContext context,
        HashType hashType,
        AbsolutePath path,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        using var streamWithLength = _fileSystem.Open(
            path,
            FileAccess.Read,
            FileMode.Open,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await PutStreamCoreAsync(context, hashType, streamWithLength.Stream, urgencyHint, retryCounter);
    }

    /// <inheritdoc />
    protected override async Task<PutResult> PutFileCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        using var streamWithLength = _fileSystem.Open(
            path,
            FileAccess.Read,
            FileMode.Open,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await UploadFromStreamAsync(
            context,
            contentHash,
            streamWithLength.Stream,
            streamWithLength.Length);
    }

    protected override Task<PutResult> PutStreamCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        Stream stream,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        // This is on purpose because we don't know what the stream implementation is here.
        long contentSize = -1;
        try
        {
            contentSize = stream.Length;
        }
        catch
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
        {
            // Obtaining the length is an optimization which may not be allowed by the underlying stream. We'll proceed
            // anyways without it.
        }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

        return UploadFromStreamAsync(context, contentHash, stream, contentSize);
    }

    /// <inheritdoc />
    protected override async Task<PutResult> PutStreamCoreAsync(
        OperationContext context,
        HashType hashType,
        Stream stream,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        // We can't hash while uploading because the name of the blob must be determined before uploading, so we
        // need to hash before.
        var position = stream.Position;
        var streamWithLength = stream.WithLength(stream.Length - position);
        var contentHash = await HashInfoLookup.GetContentHasher(hashType).GetContentHashAsync(streamWithLength);
        stream.Position = position;

        return await UploadFromStreamAsync(
            context,
            contentHash,
            streamWithLength.Stream,
            streamWithLength.Length);
    }

    #endregion

    private Task<PinResult> PinRemoteAsync(OperationContext context, ContentHash contentHash)
    {
        string blobPath = string.Empty;
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                BlobClient client;
                (client, blobPath) = await GetBlobClientAsync(context, contentHash);
                try
                {
                    // We have to do a 1-byte download here because other operations don't update the last access time
                    // of the blob.
                    var response = await client.DownloadAsync(
                        range: new HttpRange(0, 1),
                        conditions: new BlobRequestConditions() { IfMatch = ETag.All, },
                        rangeGetContentHash: false,
                        cancellationToken: context.Token);
                    
                    long contentSize = -1;
                    var range = response.Value.Details.ContentRange;
                    if (string.IsNullOrEmpty(range))
                    {
                        // There is no range header. This only happens if the file is too small.
                        contentSize = response.Value.ContentLength;
                    }
                    else
                    {
                        try
                        {
                            contentSize = TryExtractContentSizeFromRange(range) ?? contentSize;
                        }
                        catch (Exception ex)
                        {
                            Tracer.Warning(context, ex, $"Failed to extract the content range. ContentRange=[{range ?? "null"}]");
                        }
                    }

                    var lastAccessTime = response.Value.Details.LastAccessed.UtcDateTime;
                    var lastModificationTime = response.Value.Details.LastModified.UtcDateTime;
                    return new PinResult(
                        code: PinResult.ResultCode.Success,
                        lastAccessTime: lastAccessTime.Max(lastModificationTime),
                        contentSize: contentSize);
                }
                catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound || e.ErrorCode == BlobErrorCode.ConditionNotMet)
                {
                    return PinResult.ContentNotFound;
                }
            },
            traceOperationStarted: false,
            extraEndMessage: _ => $"ContentHash=[{contentHash.ToShortString()}] BlobPath=[{blobPath}]",
            timeout: _configuration.StorageInteractionTimeout);
    }

    private static readonly Regex s_contentRangeRegex = new(@"^bytes (\*|(?<start>[0-9]+)-(?<end>[0-9]+))/(?<length>[0-9]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>
    /// Parses the content size out of a Content-Range header.
    /// See: https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Content-Range
    /// </summary>
    internal static long? TryExtractContentSizeFromRange(string range)
    {
        var match = s_contentRangeRegex.Match(range);
        if (match.Success)
        {
            return int.Parse(match.Groups["length"].Value);
        }

        return null;
    }

    private Task<PutResult> UploadFromStreamAsync(
        OperationContext context,
        ContentHash contentHash,
        Stream stream,
        long contentSize)
    {
        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/blob-service-error-codes
        const int PreconditionFailed = 412;
        const int BlobAlreadyExists = 409;
        string blobPath = string.Empty;
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                BlobClient client;
                (client, blobPath) = await GetBlobClientAsync(context, contentHash);

                // WARNING: remember implicit pin

                // TODO: bandwidth checking?
                // TODO: timeouts
                var contentAlreadyExistsInCache = false;
                try
                {
                    // TODO: setup parallel upload by using storage options (ParallelOperationThreadCount)
                    // TODO: setup cancellation time via storage options (MaximumExecutionTime / ServerTimeout)
                    // TODO: ideally here we'd also hash in the bg and cancel the op if it turns out the hash
                    // doesn't match as a protective measure against trusted puts with the wrong hash.

                    await client.UploadAsync(
                        stream,
                        overwrite: false,
                        cancellationToken: context.Token
                    );
                }
                catch (RequestFailedException e) when (e.Status is PreconditionFailed or BlobAlreadyExists)
                {
                    contentAlreadyExistsInCache = true;
                }

                return new PutResult(contentHash, contentSize, contentAlreadyExistsInCache);
            },
            traceOperationStarted: false,
            extraEndMessage: _ => $"ContentHash=[{contentHash.ToShortString()}] Size=[{contentSize}] BlobPath=[{blobPath}]",
            timeout: _configuration.StorageInteractionTimeout);
    }

    private async Task<(BlobClient, string)> GetBlobClientAsync(OperationContext context, ContentHash contentHash)
    {
        var client = await _store.GetBlobClientAsync(context, contentHash);
        var path = $"{client.AccountName}@{client.BlobContainerName}:/{client.Name}";
        return (client, path);
    }
}

public readonly record struct RemoteDownloadResult
{
    public required PlaceFileResult.ResultCode ResultCode { get; init; }

    public long? FileSize { get; init; }

    public TimeSpan? TimeToFirstByteDuration { get; init; }

    public required DownloadResult DownloadResult { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{nameof(ResultCode)}=[{ResultCode}] " +
               $"{nameof(FileSize)} =[{FileSize ?? -1}] " +
               $"{nameof(TimeToFirstByteDuration)}=[{TimeToFirstByteDuration ?? TimeSpan.Zero}] " +
               $"{DownloadResult.ToString() ?? ""}";
    }
}

public readonly record struct DownloadResult
{
    public required TimeSpan DownloadDuration { get; init; }

    public TimeSpan? OpenFileStreamDuration { get; init; }

    public TimeSpan? MemoryMapDuration { get; init; }

    public TimeSpan? WriteDuration { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"OpenFileStreamDurationMs=[{OpenFileStreamDuration?.TotalMilliseconds ?? -1}] " +
               $"MemoryMapDurationMs=[{MemoryMapDuration?.TotalMilliseconds ?? -1}] " +
               $"DownloadDurationMs=[{DownloadDuration.TotalMilliseconds}]" +
               $"WriteDuration=[{WriteDuration?.TotalMilliseconds ?? -1}]";
    }
}

/// <summary>
/// Some streams may not provide a Length that can  be read. However, our OpenStream verb returns streams that are
/// always expected to provide the length. We _always_ know what the length of a file is when downloading from
/// Azure Storage, but <see cref="BlobClient.DownloadStreamingAsync"/> returns a Stream implementation that doesn't
/// provide the Length for some reason. This class is a pass-through that provides an overriden Length.
/// </summary>
internal class WrappingStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override bool CanTimeout => _stream.CanTimeout;

    public override int ReadTimeout { get => _stream.ReadTimeout; set => _stream.ReadTimeout = value; }

    public override int WriteTimeout { get => _stream.WriteTimeout; set => _stream.WriteTimeout = value; }

    public override long Length { get; }

    public override long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    private readonly Stream _stream;

    public WrappingStream(Stream stream, long length)
    {
        _stream = stream;
        Length = length;
    }

    public override void Flush()
    {
        _stream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _stream.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _stream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _stream.Write(buffer, offset, count);
    }

    protected override void Dispose(bool disposing)
    {
        _stream.Dispose();
    }

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        return _stream.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    public override void Close()
    {
        _stream.Close();
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _stream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _stream.FlushAsync(cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _stream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override int ReadByte()
    {
        return _stream.ReadByte();
    }

    public override void WriteByte(byte value)
    {
        _stream.WriteByte(value);
    }
}
