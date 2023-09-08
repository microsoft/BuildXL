// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tracing;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// A content session implementation backed by Azure Blobs.
/// </summary>
public sealed class AzureBlobStorageContentSession : ContentSessionBase, IContentNotFoundRegistration
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
    private readonly BlobStorageClientAdapter _clientAdapter;

    private readonly IAbsFileSystem _fileSystem = PassThroughFileSystem.Default;

    private readonly List<Func<Context, ContentHash, Task>> _contentNotFoundListeners = new();

    /// <nodoc />
    public AzureBlobStorageContentSession(Configuration configuration, AzureBlobStorageContentStore store)
        : base(configuration.Name)
    {
        _configuration = configuration;
        _store = store;
        _clientAdapter = new BlobStorageClientAdapter(
            Tracer,
            new BlobFolderStorageConfiguration()
            {
                StorageInteractionTimeout = _configuration.StorageInteractionTimeout,
            });
    }

    #region IContentSession Implementation

    /// <inheritdoc />
    protected override Task<PinResult> PinCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        string blobPath = string.Empty;
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                BlobClient client;
                (client, blobPath) = await GetBlobClientAsync(context, contentHash);
                // Let's make sure that we bump the last access time
                var touchResult = await _clientAdapter.TouchAsync(context, client);

                if (!touchResult.Succeeded)
                {
                    // In case the touch operation comes back with a blob not found or condition not met (the latter can be the case when
                    // the touch operation is implemeted with http ranges and targets a non-existent blob), this is treated as a content not found
                    if (touchResult.Exception is RequestFailedException requestFailed &&
                        (requestFailed.ErrorCode == BlobErrorCode.BlobNotFound || requestFailed.ErrorCode == BlobErrorCode.ConditionNotMet))
                    {
                        return PinResult.ContentNotFound;
                    }
                    else
                    {
                        return new PinResult(touchResult);
                    }
                }

                return new PinResult(
                    code: PinResult.ResultCode.Success,
                    lastAccessTime: touchResult.Value.dateTimeOffset?.UtcDateTime,
                    contentSize: touchResult.Value.contentLength ?? -1);
            },
            traceOperationStarted: false,
            extraEndMessage: _ => $"ContentHash=[{contentHash.ToShortString()}] BlobPath=[{blobPath}]",
            timeout: _configuration.StorageInteractionTimeout);
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
            return new OpenStreamResult(new MemoryStream(Array.Empty<byte>()).WithLength(0));
        }

        var stream = await TryOpenRemoteStreamAsync(context, contentHash).ThrowIfFailureAsync();
        return new OpenStreamResult(stream);
    }

    private async Task<(StreamWithLength?, string)> TryOpenReadAsync(OperationContext context, ContentHash contentHash)
    {
        var (client, blobPath) = await GetBlobClientAsync(context, contentHash);
        try
        {
            var readStream = await client.OpenReadAsync(allowBlobModifications: false, cancellationToken: context.Token);
            return (readStream.WithLength(readStream.Length), blobPath);
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
                StreamWithLength? stream;
                (stream, blobPath) = await TryOpenReadAsync(context, contentHash);
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
        var remoteDownloadResult = await PlaceRemoteFileAsync(context, contentHash, path, accessMode, replacementMode).ThrowIfFailureAsync();
        var result = new PlaceFileResult(
            code: remoteDownloadResult.ResultCode,
            fileSize: remoteDownloadResult.FileSize ?? 0,
            source: PlaceFileResult.Source.BackingStore);

        // if the content was not found, let listeners know
        if (result.Code == PlaceFileResult.ResultCode.NotPlacedContentNotFound && _contentNotFoundListeners.Any())
        {
            foreach (var listener in _contentNotFoundListeners)
            {
                await listener(context, contentHash);
            }
        }

        return result;
    }

    /// <summary>
    /// Registers a listener that will be called when a content is not found on <see cref="PlaceFileCoreAsync"/>.
    /// </summary>
    public void AddContentNotFoundOnPlaceListener(Func<Context, ContentHash, Task> listener)
    {
        _contentNotFoundListeners.Add(listener);
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
                    StreamWithLength? stream;
                    (stream, blobPath) = await TryOpenReadAsync(context, contentHash);
                    if (stream == null)
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
                            FileSize = remoteStream.Length,
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

                if (contentAlreadyExistsInCache)
                {
                    // Garbage collection requires that we bump the last access time of blobs when we attempt
                    // to add a content hash list and one of its contents already exists. The reason for this is that,
                    // if we don't, a race condition exists where GC could attempt to delete the piece of content before
                    // it knows that the new CHL exists. By touching the content, the last access time for this blob will
                    // now be above the deletion threshold (GC will not delete blobs touched more recently than 24 hours ago)
                    // and the race condition is eliminated.
                    var touchResult = await _clientAdapter.TouchAsync(context, client);

                    if (!touchResult.Succeeded)
                    {
                        return new PutResult(touchResult);
                    }
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
