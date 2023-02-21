// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
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
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a full or relative blob path
    /// </summary>
    public readonly record struct BlobPath
    {
        public string Path { get; init; }

        public bool IsRelative { get; init; }

        public BlobPath(string path, bool relative)
        {
            Contract.RequiresNotNullOrWhiteSpace(path);
            Path = path;
            IsRelative = relative;
        }

        public static BlobPath CreateAbsolute(string path) => new(path, false);

        public override string ToString()
        {
            return Path;
        }
    }

    /// <summary>
    /// Helper methods for manipulating a blob
    /// </summary>
    public class BlobFolderStorage : StartupShutdownSlimBase
    {
        public static RetryPolicyConfiguration DefaultRetryPolicy { get; } = new RetryPolicyConfiguration()
        {
            RetryPolicy = StandardRetryPolicy.ExponentialSpread,
            MinimumRetryWindow = TimeSpan.FromMilliseconds(1),
            MaximumRetryWindow = TimeSpan.FromSeconds(15),
            WindowJitter = 1.0,
        };

        protected override Tracer Tracer { get; }

        private readonly BlobFolderStorageConfiguration _configuration;

        private readonly BlobServiceClient _blobClient;
        private readonly BlobContainerClient _blobContainer;

        private readonly IStandardRetryPolicy _retryPolicy;

        private const string ETagAll = "*";

        public BlobFolderStorage(
            Tracer tracer,
            BlobFolderStorageConfiguration configuration)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            Tracer = tracer;
            _configuration = configuration;
            _retryPolicy = _configuration.RetryPolicy.Create();

            _blobClient = _configuration.Credentials!.CreateBlobServiceClient();
            _blobContainer = _blobClient.GetBlobContainerClient(_configuration.ContainerName);
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await EnsureContainerExists(context).ThrowIfFailure();

            return await base.StartupCoreAsync(context);
        }

        internal Task<Result<bool>> EnsureContainerExists(OperationContext context)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var created = true;
                    try
                    {
                        // There is a CreateIfNotExistsAsync API, but it doesn't work in practice against the Azure
                        // Storage emulator.
                        await _blobContainer.CreateAsync(
                            publicAccessType: PublicAccessType.None,
                            cancellationToken: context.Token);
                    }
                    catch (RequestFailedException exception) when (exception.ErrorCode == "ContainerAlreadyExists")
                    {
                        created = false;
                    }

                    return Result.Success(created);
                },
                traceOperationStarted: false,
                extraEndMessage: r =>
                {
                    var msg = $"Container=[{_configuration.ContainerName}]";

                    if (!r.Succeeded)
                    {
                        return msg;
                    }

                    return $"{msg} Created=[{r.Value}]";
                },
                timeout: _configuration.StorageInteractionTimeout);
        }

        /// <summary>
        /// WARNING: ETag here is a string instead of the ETag type to allow users in this assembly not to directly
        /// reference the Storage SDK.
        /// </summary>
        public record State<TState>(string? ETag = null, TState? Value = default);

        /// <summary>
        /// Implements read modify write semantics against the blob. Modification is indicated by returning a new instance from <paramref path="transform"/>
        /// </summary>
        public Task<Result<(TState NextState, TResult Result)>> ReadModifyWriteAsync<TState, TResult>(
            OperationContext context,
            BlobPath path,
            Func<TState, (TState NextState, TResult Result)> transform)
            where TState : new()
        {
            return ReadModifyWriteAsync<TState, TResult>(
                context,
                path,
                current =>
                {
                    var result = transform(current);
                    return (result.NextState, result.Result, Updated: !ReferenceEquals(current, result.NextState));
                },
                () => new TState());
        }

        /// <summary>
        /// Implements read modify write semantics against the blob. Modification is indicated by returning true for Updated in <paramref path="transform"/> result
        /// </summary>
        public Task<Result<(TState NextState, TResult Result)>> ReadModifyWriteAsync<TState, TResult>(
            OperationContext context,
            BlobPath path,
            Func<TState, (TState NextState, TResult Result, bool Updated)> transform,
            Func<TState> defaultValue)
        {
            var attempt = 0;

            return context.PerformOperationAsync(Tracer,
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

                        var readResult = await ReadStateAsync<TState>(context, path);
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

                        var modifyResult = await CompareExchangeAsync<TState>(context, path, next.NextState, currentState.ETag, attempt);
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
        public Task<Result<TState>> ReadAsync<TState>(OperationContext context, BlobPath path)
            where TState : new()
        {
            return ReadStateAsync<TState>(context, path).AsAsync(state => state.Value ?? new TState())!;
        }

        /// <summary>
        /// Deletes the blob
        /// </summary>
        public Task<Result<bool>> DeleteIfExistsAsync(OperationContext context, BlobPath path)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var blob = GetBlob(path);
                    var response = await blob.DeleteIfExistsAsync(
                        snapshotsOption: DeleteSnapshotsOption.None,
                        conditions: null,
                        cancellationToken: context.Token);
                    return Result.Success(response.Value);
                },
                extraEndMessage: r =>
                {
                    var msg = $"Path=[{GetDisplayName(path)}]";

                    if (r.Succeeded)
                    {
                        return $"{msg} Deleted=[{r.Value.ToString() ?? "false"}]";
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
            BlobPath path,
            TState value)
        {
            return await CompareExchangeAsync<TState>(context, path, value, etag: ETag.All.ToString(), attempt: 0);
        }

        public Task<Result<State<TState>>> ReadStateAsync<TState>(OperationContext context, BlobPath path)
        {
            return ReadStateAsync(context, path, stream => JsonUtilities.JsonDeserializeAsync<TState>(stream));
        }

        public Task<Result<State<TState>>> ReadStateAsync<TState>(
            OperationContext context,
            BlobPath path,
            Func<MemoryStream, ValueTask<TState>> readAsync)
        {
            long length = -1;
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async (context) =>
                {
                    var blob = GetBlob(path);

                    using var stream = new MemoryStream();
                    string? etag;
                    try
                    {
                        var response = await blob.DownloadToAsync(
                            destination: stream,
                            options: new BlobDownloadToOptions(),
                            cancellationToken: context.Token);

                        etag = response.Headers.ETag.ToString();
                    }
                    catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                    {
                        return Result.Success(new State<TState>());
                    }

                    length = stream.Length;
                    stream.Position = 0;
                    var value = await readAsync(stream);
                    return Result.Success(new State<TState>(etag, value));
                },
                extraEndMessage: r =>
                {
                    if (!r.Succeeded)
                    {
                        return string.Empty;
                    }

                    // We do not log the cluster state here because the file is too large and would spam the logs
                    var value = r.Value;
                    return $"Path=[{GetDisplayName(path)}] ETag=[{value?.ETag ?? "null"}] Length=[{length}]";
                },
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout);
        }

        private Task<Result<bool>> CompareExchangeAsync<TState>(
            OperationContext context,
            BlobPath path,
            TState value,
            string? etag,
            int attempt)
        {
            return CompareUpdateContentAsync(
                context,
                path,
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
            BlobPath path,
            Func<Stream> getValue,
            string? etag,
            int attempt,
            [CallerMemberName] string? caller = null)
        {
            long length = -1;
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async (context) =>
                {
                    var reference = GetBlob(path);
                    BlobRequestConditions? accessCondition = null;
                    if (etag is null)
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
                        accessCondition = new BlobRequestConditions() { IfMatch = new ETag(etag) };
                    }

                    try
                    {
                        return await uploadAsync();
                    }
                    catch (RequestFailedException exception) when (exception.Status == (int)HttpStatusCode.NotFound
                                                                   && exception.ErrorCode == "ContainerNotFound")
                    {
                        await EnsureContainerExists(context).ThrowIfFailureAsync();
                        return await uploadAsync();
                    }

                    async Task<Result<bool>> uploadAsync()
                    {
                        var value = getValue();
                        length = value.Length;

                        try
                        {
                            await reference.UploadAsync(value, conditions: accessCondition, cancellationToken: context.Token);
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
                    var msg = $"Path=[{GetDisplayName(path)}] ETag=[{etag ?? "null"}] Attempt=[{attempt}]";
                    if (!r.Succeeded)
                    {
                        return msg;
                    }

                    return $"{msg} Exchanged=[{r.Value}] Length=[{length}]";
                },
                timeout: _configuration.StorageInteractionTimeout,
                caller: caller);
        }

        public BlobClient GetBlob(BlobPath path)
        {
            return _blobContainer.GetBlobClient(GetAbsolutePath(path));
        }

        private string GetDisplayName(BlobPath path)
        {
            return $"{_configuration.ContainerName}:{GetAbsolutePath(path)}";
        }

        private string GetAbsolutePath(BlobPath path)
        {
            return path.IsRelative ? $"{_configuration.FolderName}/{path.Path}" : path.Path;
        }

        public Task<Result<DateTimeOffset?>> TouchAsync(OperationContext context, BlobPath path)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async context =>
                {
                    var reference = GetBlob(path);

                    // This updates the last access time in blob storage when last access time tracking is enabled. Please note,
                    // we're not downloading anything here because we're doing a 0-length download.
                    // See: https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview?tabs=azure-portal#move-data-based-on-last-accessed-time
                    var response = await reference.DownloadAsync(
                        range: new HttpRange(0, 1),
                        conditions: new BlobRequestConditions() { IfMatch = ETag.All, },
                        rangeGetContentHash: false,
                        cancellationToken: context.Token);

                    return Result.Success<DateTimeOffset?>(response.Value.Details.LastAccessed, isNullAllowed: true);
                },
                traceOperationStarted: false,
                extraEndMessage: _ => $"Path=[{GetDisplayName(path)}]",
                timeout: _configuration.StorageInteractionTimeout);
        }

        /// <summary>
        /// Lists blobs in folder
        /// </summary>
        public IAsyncEnumerable<BlobPath> ListBlobNamesAsync(
            OperationContext context,
            Regex? regex = null,
            BlobPath? subDirectoryPath = null,
            int? maxResults = null)
        {
            return ListBlobsAsync(context, regex, subDirectoryPath, maxResults: maxResults).Select(blob => BlobPath.CreateAbsolute(blob.Name));
        }

        /// <summary>
        /// Lists blobs in folder ordered by last access or write time
        /// </summary>
        public async Task<IReadOnlyList<BlobPath>> ListLruOrderedBlobsAsync(
            OperationContext context,
            int maxResults,
            Regex? regex = null,
            BlobPath? subDirectoryPath = null)
        {
            var blobs = await ListBlobsAsync(
                context,
                regex,
                subDirectoryPath,
                blobTraits: BlobTraits.Metadata,
                maxResults: maxResults).ToListAsync();

            blobs.Sort(LruCompareBlobs);

            return blobs.SelectList(blob => BlobPath.CreateAbsolute(blob.Name));
        }

        private int LruCompareBlobs(BlobItem x, BlobItem y)
        {
            return GetLastAccessTime(x).CompareTo(GetLastAccessTime(y));
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
            Regex? regex = null,
            BlobPath? prefix = null,
            BlobTraits blobTraits = BlobTraits.None,
            int? maxResults = null)
        {
            var delimiter = "/";

            string? listingPrefix = null;
            if (!string.IsNullOrEmpty(_configuration.FolderName))
            {
                listingPrefix = $"{_configuration.FolderName}{delimiter}";
            }

            if (prefix is not null)
            {
                if (prefix.Value.IsRelative)
                {
                    listingPrefix = $"{listingPrefix}{prefix}";
                }
                else
                {
                    listingPrefix = prefix.Value.Path;
                }

                if (string.IsNullOrEmpty(listingPrefix))
                {
                    listingPrefix = null;
                }
            }

            IAsyncEnumerable<BlobItem> items = _blobContainer.GetBlobsAsync(blobTraits, BlobStates.None, prefix: listingPrefix, cancellationToken: context.Token);
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
                if (httpStatusCode == 429 || httpStatusCode == (int)HttpStatusCode.ServiceUnavailable || httpStatusCode == (int)HttpStatusCode.InternalServerError)
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
}
