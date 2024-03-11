// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage;
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
using BuildXL.Cache.ContentStore.Sessions.Internal;
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
public sealed class AzureBlobStorageContentSession : ContentSessionBase, IContentNotFoundRegistration, ITrustedContentSession
{
    public record Configuration(
        string Name,
        ImplicitPin ImplicitPin,
        AzureBlobStorageContentStoreConfiguration StoreConfiguration)
    {
    }

    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new(nameof(AzureBlobStorageContentSession));

    private readonly AzureBlobStorageContentStoreConfiguration _configuration;
    private readonly Configuration _sessionConfiguration;

    private readonly AzureBlobStorageContentStore _store;
    private readonly BlobStorageClientAdapter _clientAdapter;

    private readonly IAbsFileSystem _fileSystem = PassThroughFileSystem.Default;

    private readonly List<Func<Context, ContentHash, Task>> _contentNotFoundListeners = new();

    /// <nodoc />
    public AzureBlobStorageContentSession(Configuration sessionConfiguration, AzureBlobStorageContentStore store)
        : base(sessionConfiguration.Name)
    {
        _sessionConfiguration = sessionConfiguration;
        _configuration = _sessionConfiguration.StoreConfiguration;

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
        AbsoluteBlobPath? blobPath = null;
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                (var client, blobPath) = await GetBlobClientAsync(context, contentHash);
                // Let's make sure that we bump the last access time
                var touchResult = await _clientAdapter.TouchAsync(context, client);

                if (!touchResult.Succeeded)
                {
                    // In case the touch operation comes back with a blob not found or condition not met (the latter can be the case when
                    // the touch operation is implemeted with http ranges and targets a non-existent blob), this is treated as a content not found
                    if (touchResult.Exception is RequestFailedException requestFailed &&
                        (requestFailed.ErrorCode == BlobErrorCode.BlobNotFound || requestFailed.ErrorCode == BlobErrorCode.ConditionNotMet))
                    {
                        await TryNotify(context, new DeleteEvent(blobPath!.Value, contentHash));

                        return PinResult.ContentNotFound;
                    }

                    return new PinResult(touchResult);
                }

                await TryNotify(context, new TouchEvent(blobPath!.Value, contentHash, touchResult.Value.contentLength ?? -1));

                return new PinResult(
                    code: PinResult.ResultCode.Success,
                    lastAccessTime: touchResult.Value.dateTimeOffset?.UtcDateTime,
                    contentSize: touchResult.Value.contentLength ?? -1);
            },
            traceOperationStarted: false,
            extraEndMessage: _ => $"ContentHash=[{contentHash.ToShortString()}] BlobPath=[{blobPath.ToString() ?? "UNKNOWN"}]",
            timeout: _configuration.StorageInteractionTimeout);
    }

    private async Task TryNotify(OperationContext context, RemoteContentEvent @event)
    {
        if (_configuration.Announcer is not null)
        {
            await _configuration.Announcer.Notify(context, @event);
        }
    }

    /// <inheritdoc />
    protected override async Task<OpenStreamResult> OpenStreamCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        // OpenStream is kind of problematic, because we have no way to guarantee the information being read matches
        // the hash while we're reading it. Therefore, we are forced to download into a temporary folder before
        // downloading, and then open a stream to the resulting file. This is not ideal, but it's the best we can do.
        //
        // A couple of things make matters worse:
        //
        // 1. We see people opening streams for files upwards of 13GB, so we don't want to dump into a MemoryStream.
        // 2. We can't return a stream from the Storage SDK directly, because most of those implementations aren't very
        //    careful about whether they keep connections open or not, and we can run out of file handles.
        // 3. People often want to seek in the stream, so we can't just return a network-backed stream to a file
        //    being downloaded at the current time.
        var temporaryPath = _fileSystem.GetTempPath() / Guid.NewGuid().ToString();
        var result = await PlaceRemoteFileAsync(context, contentHash, temporaryPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists).ThrowIfFailureAsync();
        switch (result.ResultCode)
        {
            case PlaceFileResult.ResultCode.PlacedWithHardLink:
            case PlaceFileResult.ResultCode.PlacedWithCopy:
            case PlaceFileResult.ResultCode.PlacedWithMove:
                // We don't close this stream on purpose, the caller is expected to do so.
                var fileStream = new FileStream(path: temporaryPath.Path,
                                                FileMode.Open,
                                                FileAccess.Read,
                                                FileShare.Read | FileShare.Delete,
                                                bufferSize: FileSystemConstants.FileIOBufferSize,
                                                options: FileOptions.DeleteOnClose | FileOptions.Asynchronous);
                return new OpenStreamResult(fileStream.WithLength(length: result.FileSize ?? fileStream.Length));
            case PlaceFileResult.ResultCode.NotPlacedContentNotFound:
                return new OpenStreamResult(null);
            default:
                return new OpenStreamResult(OpenStreamResult.ResultCode.Error, errorMessage: "Attempted to download to a random file name but the download failed in a way that's never supposed to happen");
        }
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

    private FileStream OpenFileStream(AbsolutePath path)
    {
        Stream stream;
        try
        {
            stream = _fileSystem.OpenForWrite(
                path,
                expectingLength: null,
                FileMode.Create,
                FileShare.None,
                FileOptions.Asynchronous | FileOptions.SequentialScan).Stream;
        }
        catch (DirectoryNotFoundException)
        {
            _fileSystem.CreateDirectory(path.Parent!);

            stream = _fileSystem.OpenForWrite(
                path,
                expectingLength: null,
                FileMode.Create,
                FileShare.None,
                FileOptions.Asynchronous | FileOptions.SequentialScan).Stream;
        }

        return (FileStream)stream;
    }

    private async Task<Result<RemoteDownloadResult>> PlaceRemoteFileAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileAccessMode accessMode,
        FileReplacementMode replacementMode)
    {
        AbsoluteBlobPath? blobPath = null;
        var result = await context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var statistics = new DownloadStatistics();
                long fileSize;
                ContentHash observedContentHash;

                var stopwatch = StopwatchSlim.Start();
                BlobClient client;
                try
                {
                    (client, blobPath) = await GetBlobClientAsync(context, contentHash);
                    stopwatch.ElapsedAndReset();

                    using var fileStream = OpenFileStream(path);
                    statistics.OpenFileStreamDuration = stopwatch.ElapsedAndReset();

                    await using (var hashingStream = HashInfoLookup
                                     .GetContentHasher(contentHash.HashType)
                                     .CreateWriteHashingStream(
                                         fileStream,
                                         parallelHashingFileSizeBoundary: _configuration.ParallelHashingFileSizeBoundary))
                    {
                        var blobDownloadToOptions = new BlobDownloadToOptions()
                        {
                            TransferOptions = new StorageTransferOptions()
                            {
                                InitialTransferSize = _configuration.InitialTransferSize,
                                MaximumTransferSize = _configuration.MaximumTransferSize,
                                MaximumConcurrency = _configuration.MaximumConcurrency,
                            },
                        };

                        // The following download is done in parallel onto the hashing stream. The hashing stream will
                        // itself run the hashing of the input in parallel as well.
                        await client.DownloadToAsync(
                            hashingStream,
                            blobDownloadToOptions,
                            cancellationToken: context.Token);
                        statistics.DownloadDuration = stopwatch.ElapsedAndReset();

                        observedContentHash = await hashingStream.GetContentHashAsync();
                        statistics.HashingDuration = hashingStream.TimeSpentHashing;
                    }

                    statistics.WriteDuration = fileStream.GetWriteDurationIfAvailable() ?? TimeSpan.MinValue;
                    fileSize = fileStream.Length;
                }
                // This exception will be thrown whenever storage tries to do the first operation on the blob, which
                // should be in the DownloadToAsync.
                catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound)
                {
                    statistics.DownloadDuration ??= stopwatch.ElapsedAndReset();

                    await TryNotify(context, new DeleteEvent(blobPath!.Value, contentHash));
                    return Result.Success(new RemoteDownloadResult
                    {
                        ResultCode = PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                        FileSize = null,
                        DownloadResult = statistics
                    });
                }

                if (observedContentHash != contentHash)
                {
                    // TODO: there should be some way to either notify or delete the file in storage
                    Tracer.Error(context, $"Expected to download file with hash {contentHash} into file {path}, but found {observedContentHash} instead");

                    // The file we downloaded on to disk has the wrong file. Delete it so it can't be used incorrectly.
                    try
                    {
                        _fileSystem.DeleteFile(path);
                    }
                    catch (Exception exception)
                    {
                        return new Result<RemoteDownloadResult>(exception, $"Failed to delete {path} containing partial download results for content {contentHash}");
                    }

                    return Result.Success(
                        new RemoteDownloadResult()
                        {
                            ResultCode = PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                            FileSize = fileSize,
                            DownloadResult = statistics,
                        });
                }

                await TryNotify(context, new AddEvent(blobPath!.Value, contentHash, fileSize));
                return Result.Success(
                    new RemoteDownloadResult
                    {
                        ResultCode = PlaceFileResult.ResultCode.PlacedWithCopy,
                        FileSize = fileSize,
                        DownloadResult = statistics,
                    });
            },
            traceOperationStarted: false,
            timeout: _configuration.StorageInteractionTimeout,
            extraEndMessage: r =>
                             {
                                 var baseline =
                                     $"ContentHash=[{contentHash.ToShortString()}] Path=[{path}] AccessMode=[{accessMode}] ReplacementMode=[{replacementMode}] BlobPath=[{blobPath.ToString() ?? "UNKNOWN"}]";
                                 if (!r.Succeeded)
                                 {
                                     return baseline;
                                 }

                                 var d = r.Value;
                                 return $"{baseline} {r.Value}";
                             });

        if (result.Succeeded)
        {
            return result;
        }

        // If the above failed, then it's likely there's a leftover partial download at the target path. Deleting it preemptively.
        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (Exception e)
        {
            return new Result<RemoteDownloadResult>(e, $"Failed to delete {path} containing partial download results for content {contentHash}");
        }

        return result;
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

    // TODO: In the ContentHash-based variants of PutFile and PutStream, we can know the name of the file before
    // hashing it. Therefore, we could have a custom Stream implementation that lets us hash the file as we upload it
    // and cancel the upload if the hash doesn't match at the end of the file. In those cases, we could also do a
    // existence check before uploading to avoid that extra API request.

    #endregion

    #region ITrustedContentSession implementation

    public async Task<PutResult> PutTrustedFileAsync(
        Context context,
        ContentHashWithSize contentHashWithSize,
        AbsolutePath path,
        FileRealizationMode realizationMode,
        CancellationToken cts,
        UrgencyHint urgencyHint)
    {
        var operationContext = new OperationContext(context, cts);
        using var streamWithLength = _fileSystem.Open(
            path,
            FileAccess.Read,
            FileMode.Open,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (contentHashWithSize.Size != streamWithLength.Length)
        {
            Tracer.Warning(context, $"Expected content size to be {contentHashWithSize.Size} as advertised, but found {streamWithLength.Length} instead");
        }

        return await UploadFromStreamAsync(operationContext, contentHashWithSize.Hash, streamWithLength.Stream, streamWithLength.Length);
    }

    public AbsolutePath? TryGetWorkingDirectory(AbsolutePath? pathHint)
    {
        return null;
    }

    #endregion

    internal Task<PutResult> UploadFromStreamAsync(
        OperationContext context,
        ContentHash contentHash,
        Stream stream,
        long contentSize)
    {
        AbsoluteBlobPath? blobPath = null;
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                (var client, blobPath) = await GetBlobClientAsync(context, contentHash);
                // TODO: bandwidth-check and retry
                return await PerformUploadFromStreamAsync(context, contentHash, client, blobPath!.Value, stream, contentSize);
            },
            traceOperationStarted: false,
            extraEndMessage: _ => $"ContentHash=[{contentHash.ToShortString()}] Size=[{contentSize}] BlobPath=[{blobPath.ToString() ?? "UNKNOWN"}]",
            timeout: ComputeMaximumUploadTime(context, contentSize));
    }

    private TimeSpan ComputeMaximumUploadTime(OperationContext context, long? contentSize)
    {
        // This implies the size is unknown.
        if (contentSize is null || contentSize <= 0)
        {
            return _configuration.StorageInteractionTimeout;
        }

        // We do a linear search on purpose as most operations will be for small file sizes (i.e., hit the first entry)
        // and the list is expected to be very small.
        foreach (var entry in _configuration.UploadSafeguardTimeouts)
        {
            if (contentSize <= entry.MaximumSizeBytes)
            {
                return entry.Timeout;
            }
        }

        return _configuration.StorageInteractionTimeout;
    }

    private async Task<PutResult> PerformUploadFromStreamAsync(OperationContext context, ContentHash contentHash, BlobClient client, AbsoluteBlobPath blobPath, Stream stream, long contentSize)
    {
        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/blob-service-error-codes
        const int PreconditionFailed = 412;
        const int BlobAlreadyExists = 409;

        var contentAlreadyExistsInCache = false;
        var position = stream.Position;

        // The Storage service can only reply existence after the entire blob has been uploaded. Unfortunately
        // for us, the larger the file is the more likely that it is also unique, so it doesn't make sense for
        // us to pre-check existence _before_ doing the upload.
        //
        // The ONLY exception to this are deterministic large files (i.e., there's a large file that's always
        // the same and always uploaded). For that specific case, it would make sense to perform a parallel
        // existence check while we upload the file, and cancel the upload if the file already exists.
        // TODO: implement this.
        try
        {
            var blobUploadOptions = new BlobUploadOptions()
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") },
            };

            // Setting transfer options will more likely than not mean that the SDK will enter a code path
            // where uploads can be chunked. The issue is that chunking uploads are much slower for very small
            // files. Therefore, we only set the transfer options if the file is larger than the initial window.
            if (contentSize >= 0 && contentSize > _configuration.InitialTransferSize)
            {
                blobUploadOptions.TransferOptions = new StorageTransferOptions
                {
                    InitialTransferSize = _configuration.InitialTransferSize,
                    MaximumTransferSize = _configuration.MaximumTransferSize,
                    MaximumConcurrency = _configuration.MaximumConcurrency,
                };
            }

            await client.UploadAsync(
                stream,
                blobUploadOptions,
                cancellationToken: context.Token);
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
                // There is a possible race condition here:
                // 1. We try to upload the blob, but it already exists, so the upload fails.
                // 2. In between that time and the touch above, the blob is deleted by GC because it's not referenced
                //    by a CHL and the upload time was too long ago.
                // 3. We try to touch the blob, but it doesn't exist, so the touch fails.
                // In this case, because we actually have the content available to us at this time, we can retry the
                // upload. We do this manually and exactly once on purpose to prevent any weirdnesses around this.
                if (touchResult.Exception is not null && touchResult.Exception is RequestFailedException exception && exception.Message.Contains("BlobNotFound"))
                {
                    stream.Seek(position, SeekOrigin.Begin);
                    return await PerformUploadFromStreamAsync(context, contentHash, client, blobPath, stream, contentSize);
                }

                return new PutResult(touchResult);
            }

            await TryNotify(context, new TouchEvent(blobPath, contentHash, touchResult.Value.contentLength ?? -1));
        }
        else
        {
            await TryNotify(context, new AddEvent(blobPath, contentHash, contentSize));
        }

        return new PutResult(contentHash, contentSize, contentAlreadyExistsInCache);
    }

    private Task<(BlobClient Client, AbsoluteBlobPath Path)> GetBlobClientAsync(OperationContext context, ContentHash contentHash)
    {
        return _store.GetBlobClientAsync(context, contentHash);
    }
}

public readonly record struct RemoteDownloadResult
{
    public required PlaceFileResult.ResultCode ResultCode { get; init; }

    public long? FileSize { get; init; }

    public required DownloadStatistics DownloadResult { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{nameof(ResultCode)}=[{ResultCode}] " +
               $"{nameof(FileSize)}=[{FileSize ?? -1}] " +
               $"{DownloadResult.ToString() ?? ""}";
    }
}

public record DownloadStatistics
{
    public TimeSpan? OpenFileStreamDuration { get; set; } = null;

    public TimeSpan? DownloadDuration { get; set; } = null;

    public TimeSpan? WriteDuration { get; set; } = null;

    public TimeSpan? HashingDuration { get; set; } = null;

    /// <inheritdoc />
    public override string ToString()
    {
        return
           $"OpenFileStreamDurationMs=[{OpenFileStreamDuration?.TotalMilliseconds ?? -1}] " +
           $"DownloadDurationMs=[{DownloadDuration?.TotalMilliseconds ?? -1}] " +
           $"WriteDuration=[{WriteDuration?.TotalMilliseconds ?? -1}] " +
           $"HashingDuration=[{HashingDuration?.TotalMilliseconds ?? -1}]"
           ;
    }
}
