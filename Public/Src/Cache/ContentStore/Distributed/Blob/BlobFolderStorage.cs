// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    internal interface IBlobFolderStorageConfiguration
    {
        public AzureBlobStorageCredentials? Credentials { get; set; }

        string ContainerName { get; }

        string FolderName { get; }

        TimeSpan StorageInteractionTimeout { get; }

        TimeSpan SlotWaitTime { get; }

        int MaxNumSlots { get; }
    }

    /// <summary>
    /// Represents a full or relative blob path
    /// </summary>
    internal record struct BlobName(string Name, bool IsRelative)
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
    internal class BlobFolderStorage : StartupShutdownSlimBase
    {
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

        public BlobFolderStorage(
            Tracer tracer,
            IBlobFolderStorageConfiguration configuration)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            Tracer = tracer;
            _configuration = configuration;

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
                        attempt++;

                        context.Token.ThrowIfCancellationRequested();

                        if (shouldWait)
                        {
                            var slots = Math.Min((1 << attempt) - 1, _configuration.MaxNumSlots);
                            var delay = _configuration.SlotWaitTime.Multiply(ThreadSafeRandom.ContinuousUniform(0, slots));
                            Tracer.Debug(context, $"Waiting for {delay}");
                            await Task.Delay(delay, context.Token);
                        }
                        shouldWait = true;

                        var readResult = await ReadStateAsync<TState>(context, fileName);
                        if (IsStorageThrottle(readResult))
                        {
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
                        if (IsStorageThrottle(modifyResult))
                        {
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
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async (context) =>
                {
                    var blob = GetBlockBlobReference(fileName);

                    var downloadContext = new Microsoft.WindowsAzure.Storage.OperationContext();
                    string jsonText;
                    try
                    {
                        jsonText = await blob.DownloadTextAsync(
                            operationContext: downloadContext,
                            cancellationToken: context.Token,
                            encoding: Encoding.UTF8,
                            accessCondition: null,
                            options: DefaultBlobStorageRequestOptions);
                    }
                    catch (StorageException e) when (e.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound)
                    {
                        return Result.Success(new State<TState>());
                    }

                    var value = JsonUtilities.JsonDeserialize<TState>(jsonText);
                    return Result.Success(new State<TState>(downloadContext.LastResult.Etag, value));
                },
                extraEndMessage: (Func<Result<State<TState>>, string>?)(r =>
                {
                    if (!r.Succeeded)
                    {
                        return string.Empty;
                    }

                    // We do not log the cluster state here because the file is too large and would spam the logs
                    var value = r.Value;
                    return $"FileName=[{GetDisplayPath(fileName)}] ETag=[{value?.ETag ?? "null"}]";
                }),
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
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async (context) =>
                {
                    var jsonText = (value is string text) ? text : JsonUtilities.JsonSerialize(value, indent: true);

                    var reference = GetBlockBlobReference(fileName);
                    var accessCondition = etag == AlwaysEtag ? AccessCondition.GenerateEmptyCondition() :
                        etag is null ?
                            AccessCondition.GenerateIfNotExistsCondition() :
                            AccessCondition.GenerateIfMatchCondition(etag);

                    try
                    {
                        return await upload(context, reference, jsonText, etag, accessCondition);
                    }
                    catch (StorageException exception) when (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound
                                                             && exception.RequestInformation.ErrorCode == "ContainerNotFound")
                    {
                        await EnsureContainerExists(context).ThrowIfFailureAsync();
                        return await upload(context, reference, jsonText, etag, accessCondition);
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

                    return $"{msg} Exchanged=[{r.Value}]";
                },
                timeout: _configuration.StorageInteractionTimeout);

            async Task<Result<bool>> upload(OperationContext context, CloudBlockBlob reference, string jsonText, string? etag, AccessCondition accessCondition)
            {
                try
                {
                    await reference.UploadTextAsync(
                        jsonText,
                        Encoding.UTF8,
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

        /// <summary>
        /// Lists blobs in folder
        /// </summary>
        public async IAsyncEnumerable<BlobName> ListBlobsAsync(
            OperationContext context,
            Regex? regex = null)
        {
            BlobContinuationToken? continuation = null;
            while (!context.Token.IsCancellationRequested)
            {
                var blobs = await Directory.ListBlobsSegmentedAsync(
                    useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.None,
                    maxResults: null,
                    currentToken: continuation,
                    options: null,
                    operationContext: null,
                    cancellationToken: context.Token);
                continuation = blobs.ContinuationToken;

                foreach (CloudBlockBlob blob in blobs.Results.OfType<CloudBlockBlob>())
                {
                    if (regex is null || regex.IsMatch(blob.Name))
                    {
                        yield return BlobName.CreateAbsolute(blob.Name);
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
    }
}
