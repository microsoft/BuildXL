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
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
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
            return new OpenStreamResult(new MemoryStream(Array.Empty<byte>()).WithLength(0));
        }

        var stream = await OpenRemoteStreamAsync(context, contentHash).ThrowIfFailureAsync();
        return new OpenStreamResult(stream);
    }

    private async Task<StreamWithLength?> TryOpenReadAsync(OperationContext context, ContentHash contentHash)
    {
        var client = await GetBlobClientAsync(context, contentHash);
        try
        {
            var readStream = await client.OpenReadAsync(allowBlobModifications: false, cancellationToken: context.Token);
            return readStream.WithLength(readStream.Length);
        }
        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/blob-service-error-codes
        catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    private Task<Result<StreamWithLength?>> OpenRemoteStreamAsync(OperationContext context, ContentHash contentHash)
    {
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context => Result.Success(await TryOpenReadAsync(context, contentHash), isNullAllowed: true),
            traceOperationStarted: false,
            timeout: _configuration.StorageInteractionTimeout,
            extraEndMessage: r =>
                             {
                                 long size = -1;
                                 if (r.Succeeded && r.Value is not null)
                                 {
                                     size = r.Value.Value.Length;
                                 }

                                 return $"ContentHash=[{contentHash}] Size=[{size}]";
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
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var stopwatch = StopwatchSlim.Start();

                try
                {
                    var stream = await TryOpenReadAsync(context, contentHash);
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
                                     $"ContentHash=[{contentHash.ToShortString()}] Path=[{path}] AccessMode=[{accessMode}] ReplacementMode=[{replacementMode}]";
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
                   ResultCode = resultCode, DownloadResult = new DownloadResult() { DownloadDuration = downloadDuration, },
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
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var client = await GetBlobClientAsync(context, contentHash);
                try
                {
                    var properties = await client.GetPropertiesAsync(cancellationToken: context.Token);
                    return new PinResult(
                        code: PinResult.ResultCode.Success,
                        lastAccessTime: properties.Value.LastModified.UtcDateTime,
                        contentSize: properties.Value.ContentLength);
                }
                catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound)
                {
                    return PinResult.ContentNotFound;
                }
            },
            traceOperationStarted: false,
            extraEndMessage: _ => $"ContentHash=[{contentHash}]",
            timeout: _configuration.StorageInteractionTimeout);
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
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var client = await GetBlobClientAsync(context,contentHash);

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
            timeout: _configuration.StorageInteractionTimeout);
    }

    private Task<BlobClient> GetBlobClientAsync(OperationContext context, ContentHash contentHash)
    {
        return _store.GetBlobClientAsync(context, contentHash);
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
