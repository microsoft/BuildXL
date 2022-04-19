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
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public interface IBlobFolderStorageConfiguration
    {
        public AzureBlobStorageCredentials? Credentials { get; set; }

        string ContainerName { get; }

        string FolderName { get; }

        TimeSpan StorageInteractionTimeout { get; }

        RetryPolicyConfiguration RetryPolicy { get; }
    }

    /// <summary>
    /// Represents a full or relative blob path
    /// </summary>
    public record struct BlobName(string Name, bool IsRelative)
    {
        public static implicit operator BlobName(string fileName)
        {
            return new BlobName(fileName, IsRelative: true);
        }

        public static BlobName CreateAbsolute(string name) => new BlobName(name, false);

        public override string ToString()
        {
            return Name;
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
            MaximumRetryWindow = TimeSpan.FromSeconds(30),
            WindowJitter = 1.0,
        };

        protected override Tracer Tracer { get; }

        private readonly IBlobFolderStorageConfiguration _configuration;

        private readonly CloudBlobClient _client;
        private readonly CloudBlobContainer _container;
        public CloudBlobDirectory Directory { get; }

        private readonly string _directoryPath;
        private readonly string _containerPath;

        private const string AlwaysEtag = "*";

        private static readonly BlobRequestOptions DefaultBlobStorageRequestOptions = new BlobRequestOptions()
        {
            RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(),
        };

        private readonly IStandardRetryPolicy _retryPolicy;

        public BlobFolderStorage(
            Tracer tracer,
            IBlobFolderStorageConfiguration configuration)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            Tracer = tracer;
            _configuration = configuration;
            _retryPolicy = _configuration.RetryPolicy.Create();

            _client = _configuration.Credentials!.CreateCloudBlobClient();
            _container = _client.GetContainerReference(_configuration.ContainerName);
            Directory = _container.GetDirectoryReference(_configuration.FolderName);

            _containerPath = $"{_configuration.ContainerName}:";
            _directoryPath = $"{_containerPath}/{_configuration.FolderName}";
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
                    return Result.Success(await _container.CreateIfNotExistsAsync(
                        accessType: BlobContainerPublicAccessType.Off,
                        options: DefaultBlobStorageRequestOptions,
                        operationContext: null,
                        cancellationToken: context.Token));
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

        public record State<TState>(string? ETag = null, TState? Value = default);

        /// <summary>
        /// Implements read modify write semantics against the blob. Modification is indicated by returning a new instance from <paramref name="transform"/>
        /// </summary>
        public Task<Result<TState>> ReadModifyWriteAsync<TState>(
            OperationContext context,
            BlobName fileName,
            Func<TState, TState> transform)
            where TState : new()
        {
            return ReadModifyWriteAsync<TState, Unit>(context, fileName, current => (transform(current), Unit.Void)).SelectAsync(r => r.Value.NextState);
        }

        /// <summary>
        /// Implements read modify write semantics against the blob. Modification is indicated by returning a new instance from <paramref name="transform"/>
        /// </summary>
        public Task<Result<(TState NextState, TResult Result)>> ReadModifyWriteAsync<TState, TResult>(
            OperationContext context,
            BlobName fileName,
            Func<TState, (TState NextState, TResult Result)> transform,
            Func<TState>? defaultValue = null)
            where TState : new()
        {
            return ReadModifyWriteAsync<TState, TResult>(context, fileName, current =>
            {
                var result = transform(current);
                return (result.NextState, result.Result, Updated: !ReferenceEquals(current, result.NextState));
            });
        }

        /// <summary>
        /// Implements read modify write semantics against the blob. Modification is indicated by returning true for Updated in <paramref name="transform"/> result
        /// </summary>
        public Task<Result<(TState NextState, TResult Result)>> ReadModifyWriteAsync<TState, TResult>(
            OperationContext context,
            BlobName fileName,
            Func<TState, (TState NextState, TResult Result, bool Updated)> transform,
            Func<TState>? defaultValue = null)
            where TState : new()
        {
            var attempt = 0;

            defaultValue ??= () => new TState();

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

                        var readResult = await ReadStateAsync<TState>(context, fileName);
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

                        var modifyResult = await CompareExchangeAsync<TState>(context, fileName, next.NextState, currentState.ETag, attempt);
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

        private string GetDisplayPath(BlobName fileName)
        {
            var rootPath = fileName.IsRelative ? _directoryPath : _containerPath;
            return $"{rootPath}/{fileName.Name}";
        }

        /// <summary>
        /// Reads the given object from the json blob
        /// </summary>
        public Task<Result<TState>> ReadAsync<TState>(OperationContext context, BlobName fileName)
        {
            return ReadStateAsync<TState>(context, fileName).AsAsync(s => s.Value)!;
        }

        /// <summary>
        /// Deletes the blob
        /// </summary>
        public Task<Result<bool>> DeleteIfExistsAsync(OperationContext context, BlobName fileName)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var blob = GetBlockBlobReference(fileName);
                    return Result.Success(await blob.DeleteIfExistsAsync(
                        deleteSnapshotsOption: DeleteSnapshotsOption.None,
                        accessCondition: null,
                        options: null,
                        operationContext: null,
                        cancellationToken: context.Token));
                },
                extraEndMessage: r =>
                {
                    var msg = $"FileName=[{GetDisplayPath(fileName)}]";

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
            BlobName fileName,
            TState value)
        {
            return await CompareExchangeAsync<TState>(context, fileName, value, etag: AlwaysEtag, attempt: 0);
        }

        public Task<Result<State<TState>>> ReadStateAsync<TState>(OperationContext context, BlobName fileName)
        {
            return ReadStateAsync(context, fileName, stream => JsonUtilities.JsonDeserializeAsync<TState>(stream));
        }

        public Task<Result<State<TState>>> ReadStateAsync<TState>(OperationContext context, BlobName fileName, Func<MemoryStream, ValueTask<TState>> readAsync)
        {
            long length = -1;
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async (context) =>
                {
                    var blob = GetBlockBlobReference(fileName);

                    using var stream = new MemoryStream();
                    var downloadContext = new Microsoft.WindowsAzure.Storage.OperationContext();
                    try
                    {
                        await blob.DownloadToStreamAsync(
                            target: stream,
                            operationContext: downloadContext,
                            cancellationToken: context.Token,
                            accessCondition: null,
                            options: DefaultBlobStorageRequestOptions);
                    }
                    catch (StorageException e) when (e.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound)
                    {
                        return Result.Success(new State<TState>());
                    }

                    length = stream.Length;
                    stream.Position = 0;
                    var value = await readAsync(stream);
                    return Result.Success(new State<TState>(downloadContext.LastResult.Etag, value));
                },
                extraEndMessage: r =>
                {
                    if (!r.Succeeded)
                    {
                        return string.Empty;
                    }

                    // We do not log the cluster state here because the file is too large and would spam the logs
                    var value = r.Value;
                    return $"FileName=[{GetDisplayPath(fileName)}] ETag=[{value?.ETag ?? "null"}] Length=[{length}]";
                },
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout);
        }

        private Task<Result<bool>> CompareExchangeAsync<TState>(
            OperationContext context,
            BlobName fileName,
            TState value,
            string? etag,
            int attempt)
        {
            return CompareUpdateContentAsync(
                context,
                fileName,
                () =>
                {
                    var jsonText = (value is string text) ? text : JsonUtilities.JsonSerialize(value, indent: true);
                    var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonText));
                    return stream;
                },
                etag,
                attempt);
        }

        public Task<Result<bool>> CompareUpdateContentAsync(
            OperationContext context,
            BlobName fileName,
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
                    var reference = GetBlockBlobReference(fileName);
                    var accessCondition = etag == AlwaysEtag ? AccessCondition.GenerateEmptyCondition() :
                        string.IsNullOrEmpty(etag) ?
                            AccessCondition.GenerateIfNotExistsCondition() :
                            AccessCondition.GenerateIfMatchCondition(etag);

                    try
                    {
                        return await uploadAsync();
                    }
                    catch (StorageException exception) when (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound
                                                             && exception.RequestInformation.ErrorCode == "ContainerNotFound")
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
                            await reference.UploadFromStreamAsync(
                                value,
                                accessCondition: accessCondition,
                                options: DefaultBlobStorageRequestOptions,
                                operationContext: null,
                                context.Token);
                        }
                        catch (StorageException exception)
                        {
                            // We obtain PreconditionFailed when If-Match fails, and NotModified when If-None-Match fails
                            // (corresponds to IfNotExistsCondition)
                            if (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.PreconditionFailed
                                || exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotModified
                                // Used only in the development storage case
                                || exception.RequestInformation.ErrorCode == "BlobAlreadyExists")
                            {
                                Tracer.Debug(
                                    context,
                                    exception,
                                    $"Value does not exist or does not match ETag `{etag ?? "null"}`. Reported ETag is `{exception.RequestInformation.Etag ?? "null"}`");
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
                    var msg = $"FileName=[{GetDisplayPath(fileName)}] ETag=[{etag ?? "null"}] Attempt=[{attempt}]";
                    if (!r.Succeeded)
                    {
                        return msg;
                    }

                    return $"{msg} Exchanged=[{r.Value}] Length=[{length}]";
                },
                timeout: _configuration.StorageInteractionTimeout,
                caller: caller);
        }

        private CloudBlockBlob GetBlockBlobReference(BlobName fileName)
        {
            if (fileName.IsRelative)
            {
                return Directory.GetBlockBlobReference(fileName.Name);
            }
            else
            {
                return _container.GetBlockBlobReference(fileName.Name);
            }
        }

        public Task<BoolResult> TouchAsync(OperationContext context, BlobName fileName)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async context =>
                {
                    var reference = GetBlockBlobReference(fileName);

                    // This should update last access time in blob storage
                    await reference.FetchAttributesAsync(
                        accessCondition: null,
                        options: DefaultBlobStorageRequestOptions,
                        operationContext: null,
                        cancellationToken: context.Token);

                    return BoolResult.Success;
                },
                traceOperationStarted: false,
                extraEndMessage: _ => $"FileName=[{GetDisplayPath(fileName)}]",
                timeout: _configuration.StorageInteractionTimeout);
        }

        /// <summary>
        /// Lists blobs in folder
        /// </summary>
        public IAsyncEnumerable<BlobName> ListBlobsAsync(
            OperationContext context,
            Regex? regex = null,
            string? subDirectoryPath = null,
            int? maxResults = null)
        {
            return ListBlobsCoreAsync(context, regex, subDirectoryPath, maxResults: maxResults).Select(blob => BlobName.CreateAbsolute(blob.Name));
        }

        /// <summary>
        /// Lists blobs in folder ordered by last access or write time
        /// </summary>
        public async Task<IReadOnlyList<BlobName>> ListLruOrderedBlobsAsync(
            OperationContext context,
            int maxResults,
            Regex? regex = null,
            string? subDirectoryPath = null)
        {
            var blobs = await ListBlobsCoreAsync(
                context,
                regex,
                subDirectoryPath,
                getMetadata: true,
                maxResults: maxResults).ToListAsync();

            blobs.Sort(LruCompareBlobs);

            return blobs.SelectList(blob => BlobName.CreateAbsolute(blob.Name));
        }

        private int LruCompareBlobs(CloudBlob x, CloudBlob y)
        {
            return GetLastAccessTime(x).CompareTo(GetLastAccessTime(y));
        }

        private DateTimeOffset GetLastAccessTime(CloudBlob b)
        {
            var lastModified = b.Properties.LastModified;

            // NOTE: Last access time is modified on a day granularity by blob lifetime management in blob store
            if (b.Metadata.TryGetValue("LastAccessTime", out var lastAccessTimeMetadata)
                && DateTimeOffset.TryParse(lastAccessTimeMetadata, out var lastAccessTime))
            {
                if (lastModified > lastAccessTime)
                {
                    return lastModified.Value;
                }
                else
                {
                    return lastAccessTime;
                }
            }

            return lastModified ?? DateTimeOffset.MinValue;
        }

        /// <summary>
        /// Lists blobs in folder
        /// </summary>
        private async IAsyncEnumerable<CloudBlob> ListBlobsCoreAsync(
            OperationContext context,
            Regex? regex = null,
            string? subDirectoryPath = null,
            bool getMetadata = false,
            int? maxResults = null)
        {
            BlobContinuationToken? continuation = null;

            while (!context.Token.IsCancellationRequested)
            {
                var directory = Directory;
                if (subDirectoryPath != null)
                {
                    directory = directory.GetDirectoryReference(subDirectoryPath);
                }

                var blobs = await context.PerformOperationWithTimeoutAsync(
                    Tracer,
                    async context =>
                    {
                        var result = await directory.ListBlobsSegmentedAsync(
                            useFlatBlobListing: true,
                            blobListingDetails: getMetadata ? BlobListingDetails.Metadata : BlobListingDetails.None,
                            maxResults: maxResults,
                            currentToken: continuation,
                            options: null,
                            operationContext: null,
                            cancellationToken: context.Token);
                        return Result.Success(result);
                    },
                    timeout: _configuration.StorageInteractionTimeout,
                    extraEndMessage: r => $"ItemCount={r.GetValueOrDefault()?.Results.Count()}").ThrowIfFailureAsync();

                continuation = blobs.ContinuationToken;

                foreach (CloudBlob blob in blobs.Results.OfType<CloudBlob>())
                {
                    if (regex is null || regex.IsMatch(blob.Name))
                    {
                        yield return blob;
                    }
                }

                if (continuation == null)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// WARNING: used for tests only.
        /// </summary>
        internal Task<BoolResult> CleanupStateAsync(OperationContext context, string fileName)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async context =>
            {
                var blob = Directory.GetBlobReference(fileName);
                await blob.DeleteIfExistsAsync(
                    deleteSnapshotsOption: DeleteSnapshotsOption.None,
                    accessCondition: null,
                    options: null,
                    operationContext: null,
                    cancellationToken: context.Token);

                return BoolResult.Success;
            },
            timeout: _configuration.StorageInteractionTimeout,
            extraEndMessage: r => $"FileName=[{GetDisplayPath(fileName)}]");
        }

        protected bool IsStorageThrottle(ResultBase result)
        {
            if (result.Succeeded)
            {
                return false;
            }

            if (result.Exception is StorageException storageException)
            {
                var httpStatusCode = storageException.RequestInformation.HttpStatusCode;
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
