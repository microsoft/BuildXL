// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

public static class BlobClientExtensions
{
    public static string ToDisplayName(this BlobClient blob)
    {
        return $"{blob.AccountName}/{blob.BlobContainerName}/{blob.Name}";
    }
}

/// <summary>
/// Helper methods for manipulating a blob
/// </summary>
public class BlobStorageClientAdapter
{
    protected Tracer Tracer { get; }

    private readonly BlobFolderStorageConfiguration _configuration;

    private readonly IStandardRetryPolicy _retryPolicy;

    private const string ETagAll = "*";

    public BlobStorageClientAdapter(
        Tracer tracer,
        BlobFolderStorageConfiguration configuration)
    {
        Tracer = tracer;
        _configuration = configuration;
        _retryPolicy = _configuration.RetryPolicy.Create();
    }

    /// <summary>
    /// WARNING: ETag here is a string instead of the ETag type to allow users not in this assembly not to directly
    /// reference the Storage SDK.
    /// </summary>
    public record State<TState>(string? ETag = null, TState? Value = default, IReadOnlyDictionary<string, string>? Metadata = null);

    /// <summary>
    /// Implements read modify write semantics against the blob. Modification is indicated by returning a new instance from <paramref path="transform"/>
    /// </summary>
    public Task<Result<(TState NextState, TResult Result)>> ReadModifyWriteAsync<TState, TResult>(
        OperationContext context,
        BlobClient blob,
        Func<TState, (TState NextState, TResult Result)> transform)
        where TState : new()
    {
        return ReadModifyWriteAsync<TState, TResult>(
            context,
            blob,
            current =>
            {
                var result = transform(current);
                return (result.NextState, result.Result, Updated: !ReferenceEquals(current, result.NextState));
            },
            () => new TState());
    }

    public Task<Result<bool>> EnsureContainerExists(OperationContext context, BlobContainerClient client)
    {
        return StorageClientExtensions.EnsureContainerExistsAsync(Tracer, context, client, _configuration.StorageInteractionTimeout);
    }

    /// <summary>
    /// Implements read modify write semantics against the blob. Modification is indicated by returning true for Updated in <paramref path="transform"/> result
    /// </summary>
    public Task<Result<(TState NextState, TResult Result)>> ReadModifyWriteAsync<TState, TResult>(
        OperationContext context,
        BlobClient blob,
        Func<TState, (TState NextState, TResult Result, bool Updated)> transform,
        Func<TState> defaultValue)
    {
        var attempt = 0;

        return context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                var shouldWait = false;
                while (true)
                {
                    context.Token.ThrowIfCancellationRequested();

                    if (shouldWait)
                    {
                        var delay = _retryPolicy.Compute(attempt);
                        Tracer.Debug(context, $"Waiting for {delay} in attempt {attempt}");
                        await Task.Delay(delay, context.Token);
                    }

                    shouldWait = true;

                    attempt++;

                    var readResult = await ReadStateAsync<TState>(context, blob);
                    if (IsStorageThrottle(readResult) || IsCancelledOrTimeout(readResult))
                    {
                        // This continue here means we'll retry these cases. In the cancellation case, we retry
                        // because the inner operation could have been cancelled by the internal cancellation token
                        // to the timeout. If that's not the case (i.e., caller intended for the operation to be
                        // cancelled), we'll cancel anyways at the top of the loop.
                        continue;
                    }

                    var currentState = readResult.ThrowIfFailure();
                    var currentValue = currentState.Value ?? defaultValue();
                    var next = transform(currentValue);
                    if (!next.Updated)
                    {
                        return Result.Success((next.NextState, next.Result));
                    }

                    var modifyResult = await CompareExchangeAsync<TState>(context, blob, next.NextState, currentState.ETag, attempt);
                    if (IsStorageThrottle(modifyResult) || IsCancelledOrTimeout(modifyResult))
                    {
                        // Same rationale as previous if statement
                        continue;
                    }

                    var succeeded = modifyResult.ThrowIfFailure();
                    if (succeeded)
                    {
                        return Result.Success((next.NextState, next.Result));
                    }
                }
            },
            traceOperationStarted: false,
            // Logging specifically as Attempts in order to distinguish the overall number of attempts to satisfy
            // the RMW vs each individual retry.
            extraEndMessage: _ => $"Attempts=[{attempt}]");
    }

    /// <summary>
    /// Reads the given object from the json blob
    /// </summary>
    public Task<Result<TState>> ReadAsync<TState>(OperationContext context, BlobClient blob)
        where TState : new()
    {
        return ReadStateAsync<TState>(context, blob).AsAsync(state => state.Value ?? new TState())!;
    }

    /// <summary>
    /// Deletes the blob
    /// </summary>
    public Task<Result<bool>> DeleteIfExistsAsync(OperationContext context, BlobClient blob)
    {
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var response = await blob.DeleteIfExistsAsync(
                    snapshotsOption: DeleteSnapshotsOption.None,
                    conditions: null,
                    cancellationToken: context.Token);
                return Result.Success(response.Value);
            },
            extraEndMessage: r =>
                             {
                                 var msg = $"Path=[{blob.ToDisplayName()}]";

                                 if (r.Succeeded)
                                 {
                                     return $"{msg} Deleted=[{r.Value}]";
                                 }

                                 return msg;
                             },
            traceOperationStarted: false,
            timeout: _configuration.StorageInteractionTimeout);
    }

    /// <summary>
    /// Writes the object to the blob unconditionally
    /// </summary>
    public async Task<BoolResult> WriteAsync<TState>(
        OperationContext context,
        BlobClient blob,
        TState value)
    {
        return await CompareExchangeAsync<TState>(context, blob, value, etag: ETag.All.ToString(), attempt: 0);
    }

    public Task<Result<State<TState>>> ReadStateAsync<TState>(OperationContext context, BlobClient blob)
    {
        return ReadStateAsync(context, blob, static binaryData =>
            {
                using var stream = binaryData.ToStream();
                return JsonUtilities.JsonDeserializeAsync<TState>(stream);
            });
    }

    public Task<Result<State<TState>>> ReadStateAsync<TState>(
        OperationContext context,
        BlobClient blob,
        Func<BinaryData, ValueTask<TState>> readAsync)
    {
        long length = -1;
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async (context) =>
            {
                string? etag;
                try
                {
                    var content = await blob.DownloadContentAsync(context.Token);
                    Response response = content.GetRawResponse();
                    var metadata = (IReadOnlyDictionary<string, string>)content.Value.Details.Metadata;
                    etag = content.Value.Details.ETag.ToString();
                    var value = await readAsync(content.Value.Content);
                    length = content.Value.Details.ContentLength;

                    return Result.Success(new State<TState>(etag, value, metadata));
                }
                catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                {
                    return Result.Success(new State<TState>());
                }
            },
            extraEndMessage: r =>
                             {
                                 if (!r.Succeeded)
                                 {
                                     return string.Empty;
                                 }

                                 // We do not log the cluster state here because the file is too large and would spam the logs
                                 var value = r.Value;
                                 return $"Path=[{blob.ToDisplayName()}] ETag=[{value?.ETag ?? "null"}] Length=[{length}]";
                             },
            traceOperationStarted: false,
            timeout: _configuration.StorageInteractionTimeout);
    }

    private Task<Result<bool>> CompareExchangeAsync<TState>(
        OperationContext context,
        BlobClient blob,
        TState value,
        string? etag,
        int attempt)
    {
        return CompareUpdateContentAsync(
            context,
            blob,
            () =>
            {
                var jsonText = value as string ?? JsonUtilities.JsonSerialize(value, indent: true);
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonText));
                return stream;
            },
            etag,
            attempt);
    }

    public Task<Result<bool>> CompareUpdateContentAsync(
        OperationContext context,
        BlobClient blob,
        Func<Stream> getValue,
        string? etag,
        int attempt,
        [CallerMemberName] string? caller = null,
        IDictionary<string, string>? metadata = null)
    {
        long length = -1;
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async (context) =>
            {
                BlobRequestConditions? accessCondition = null;
                if (string.IsNullOrEmpty(etag))
                {
                    // Perform the operation only if the the blob doesn't exist
                    accessCondition = new BlobRequestConditions() { IfNoneMatch = ETag.All };
                }
                else if (etag == ETagAll)
                {
                    // Always perform the operation
                    accessCondition = null;
                }
                else
                {
                    // Perform the operation only if the blob exists, and the ETag matches
                    accessCondition = new BlobRequestConditions() { IfMatch = new ETag(etag!) };
                }

                try
                {
                    return await uploadAsync();
                }
                catch (RequestFailedException exception) when (exception.Status == (int)HttpStatusCode.NotFound
                                                               && exception.ErrorCode == "ContainerNotFound")
                {
                    await EnsureContainerExists(context, blob.GetParentBlobContainerClient()).ThrowIfFailureAsync();
                    return await uploadAsync();
                }

                async Task<Result<bool>> uploadAsync()
                {
                    var value = getValue();
                    length = value.Length;

                    try
                    {
                        await blob.UploadAsync(value, metadata: metadata, conditions: accessCondition, cancellationToken: context.Token);
                    }
                    catch (RequestFailedException exception)
                    {
                        // We obtain PreconditionFailed when If-Match fails, and NotModified when If-None-Match fails
                        // (corresponds to IfNotExistsCondition)
                        if (exception.Status == (int)HttpStatusCode.PreconditionFailed
                            || exception.Status == (int)HttpStatusCode.NotModified
                            // Used only in the development storage case
                            || exception.ErrorCode == "BlobAlreadyExists")
                        {
                            Tracer.Debug(
                                context,
                                exception,
                                $"Value does not exist or does not match ETag `{etag ?? "null"}`");
                            return Result.Success(false);
                        }

                        throw;
                    }

                    // Uploaded successfully, so we overwrote the previous value
                    return Result.Success(true);
                }
            },
            traceOperationStarted: false,
            extraEndMessage: r =>
                             {
                                 // We do not log the cluster state here because the file is too large and would spam the logs
                                 var msg = $"Path=[{blob.ToDisplayName()}] ETag=[{etag ?? "null"}] Attempt=[{attempt}]";
                                 if (!r.Succeeded)
                                 {
                                     return msg;
                                 }

                                 return $"{msg} Exchanged=[{r.Value}] Length=[{length}]";
                             },
            timeout: _configuration.StorageInteractionTimeout,
            caller: caller);
    }

    public Task<Result<(DateTimeOffset dateTimeOffset, long? contentLength, ETag ETag)>> TouchAsync(OperationContext context, BlobClient blob, bool hard = false, ETag? etag = null)
    {
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                // This updates the last access time in blob storage when last access time tracking is enabled. 
                // See: https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview?tabs=azure-portal#move-data-based-on-last-accessed-time
                if (hard)
                {
                    // Update the last access time by updating a header. This method is good because it forces an
                    // update to the last modification time (which we use as last access time as well), and the update
                    // is never binned (i.e., it's updated every time).
                    // The method is bad because it requires write access to the blob, which some clients may not have.
                    //
                    // We update the HTTP headers to a random value because there's no direct way to force an update of
                    // the last access time, so we update the last modification time. There's also no way to update the
                    // last modification time in the blob storage API, so we have to update either the metadata or the
                    // properties.
                    //
                    // Updating the Metadata is not a good idea here because the API replaces the previous metadata
                    // with the new one, so you need to have the old metadata in order to perform a single-item update,
                    // and we actually use the metadata to implement compression.
                    //
                    // The same thing happens with the properties, but since we don't actually use the properties it's
                    // not a big deal.
                    BlobRequestConditions? conditions = null;
                    if (etag is not null)
                    {
                        conditions = new BlobRequestConditions()
                        {
                            IfMatch = etag.Value,
                        };
                    }

                    var response = await blob.SetHttpHeadersAsync(
                        httpHeaders: new BlobHttpHeaders()
                        {
                            // Setting this to a random value guarantees the last modification time gets updated.
                            // The reason we set this particular field is we don't expect it to ever be useful for our
                            // use-case. If it ever becomes useful, we can change it to a more meaningful value and
                            // we'll have to pick another one.
                            ContentLanguage = Guid.NewGuid().ToString(),
                        },
                        conditions,
                        cancellationToken: context.Token);

                    // The last modified time in this case is also the last access time. Unfortunately, the blob size
                    // is unavailable with this method.
                    return Result.Success<(DateTimeOffset, long?, ETag)>(
                        (response.Value.LastModified, null, response.Value.ETag),
                        isNullAllowed: true);
                }
                else
                {
                    // Update the last access time using a 1-byte download. This method is good because it can be used
                    // when there's only read access, but bad because the update of the last access time is binned on
                    // 1d increments (i.e., it's only updated every 24h).
                    //
                    // See: https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview?tabs=azure-portal#move-data-based-on-last-accessed-time
                    try
                    {
                        var response = await blob.DownloadContentAsync(
                            new BlobDownloadOptions()
                            {
                                Range = new HttpRange(0, 1),
                                Conditions = new BlobRequestConditions() { IfMatch = etag ?? ETag.All, }
                            },
                            cancellationToken: context.Token);

                        return Result.Success<(DateTimeOffset, long?, ETag)>((response.Value.Details.LastAccessed, response.Value.Details.ContentLength, response.Value.Details.ETag), isNullAllowed: true);
                    }
                    catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.InvalidRange)
                    {
                        // We are dealing with a zero-size piece of content. Use unbounded download API to touch the file.
                        var response = await blob.DownloadContentAsync(
                            conditions: new BlobRequestConditions() { IfMatch = etag ?? ETag.All, },
                            cancellationToken: context.Token);

                        return Result.Success<(DateTimeOffset, long?, ETag)>((response.Value.Details.LastAccessed, response.Value.Details.ContentLength, response.Value.Details.ETag), isNullAllowed: true);
                    }
                }
            },
            traceOperationStarted: false,
            extraEndMessage: _ => $"Path=[{blob.ToDisplayName()}]",
            timeout: _configuration.StorageInteractionTimeout);
    }

    /// <summary>
    /// Updates the metadata of an existing blob with the provided value
    /// </summary>
    public Task<Result<bool>> UpdateMetadataAsync(OperationContext context, BlobClient blob, IDictionary<string, string>? metadata)
    {
        return context.PerformOperationWithTimeoutAsync(
            Tracer,
            async context =>
            {
                var response = await blob.SetMetadataAsync(
                    metadata,
                    conditions: new BlobRequestConditions() { IfMatch = ETag.All, },
                    cancellationToken: context.Token);

                return Result.Success<bool>(true);
            },
            traceOperationStarted: false,
            extraEndMessage: _ => $"Path=[{blob.ToDisplayName()}]",
            timeout: _configuration.StorageInteractionTimeout);
    }

    /// <summary>
    /// Lists blobs in folder
    /// </summary>
    public IAsyncEnumerable<BlobItem> ListBlobNamesAsync(
        OperationContext context,
        BlobContainerClient client,
        string? prefix = null,
        Regex? regex = null,
        int? maxResults = null)
    {
        return ListBlobsAsync(context, client, prefix, regex, maxResults: maxResults);
    }

    /// <summary>
    /// Lists blobs in folder ordered by most recently used (using last access or write time)
    /// </summary>
    public async Task<IReadOnlyList<BlobItem>> ListMruOrderedBlobsAsync(
        OperationContext context,
        BlobContainerClient client,
        string? prefix = null,
        Regex? regex = null,
        int? maxResults = null)
    {
        var blobs = await ListBlobsAsync(
            context,
            client,
            prefix,
            regex,
            blobTraits: BlobTraits.Metadata,
            maxResults: maxResults).ToListAsync(cancellationToken: context.Token);

        blobs.Sort(MruCompareBlobs);

        return blobs;
    }

    private int MruCompareBlobs(BlobItem x, BlobItem y)
    {
        return GetLastAccessTime(y).CompareTo(GetLastAccessTime(x));
    }

    private DateTimeOffset GetLastAccessTime(BlobItem b)
    {
        var lastModified = b.Properties.LastModified ?? b.Properties.CreatedOn;

        // NOTE: Last access time is modified on a day granularity by blob lifetime management in blob store
        var lastAccessTime = b.Properties.LastAccessedOn;
        if (lastAccessTime is not null)
        {
            if (lastModified > lastAccessTime)
            {
                return lastModified.Value;
            }
            else
            {
                return lastAccessTime.Value;
            }
        }

        return lastModified ?? DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Lists blobs in folder
    /// </summary>
    private async IAsyncEnumerable<BlobItem> ListBlobsAsync(
        OperationContext context,
        BlobContainerClient client,
        string? prefix = null,
        Regex? regex = null,
        BlobTraits blobTraits = BlobTraits.None,
        int? maxResults = null)
    {
        IAsyncEnumerable<BlobItem> items = client.GetBlobsAsync(
            blobTraits,
            BlobStates.None,
            prefix,
            cancellationToken: context.Token);
        if (maxResults is not null)
        {
            items = items.Take(maxResults.Value);
        }

        await foreach (var item in items)
        {
            if (regex is null || regex.IsMatch(item.Name))
            {
                yield return item;
            }
        }
    }

    protected bool IsStorageThrottle(ResultBase result)
    {
        if (result.Succeeded)
        {
            return false;
        }

        if (result.Exception is RequestFailedException storageException)
        {
            var httpStatusCode = storageException.Status;
            if (httpStatusCode == 429 || httpStatusCode == (int)HttpStatusCode.ServiceUnavailable ||
                httpStatusCode == (int)HttpStatusCode.InternalServerError)
            {
                return true;
            }
        }

        return false;
    }

    protected bool IsCancelledOrTimeout(ResultBase result)
    {
        if (result.Succeeded)
        {
            return false;
        }

        if (result.IsCancelled || result.Exception is TaskCanceledException || result.Exception is TimeoutException)
        {
            return true;
        }

        return false;
    }
}
