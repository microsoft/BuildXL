// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class AzureBlobStorageCheckpointRegistryConfiguration
    {
        public AzureBlobStorageCredentials? Credentials { get; set; }

        public string ContainerName { get; set; } = string.Empty;

        public string FolderName { get; set; } = string.Empty;

        /// <summary>
        /// WARNING: this has to be an alphanumeric string without any kind of special characters
        /// </summary>
        public string KeySpacePrefix { get; set; } = "20210721";

        /// <summary>
        /// Number of checkpoints to keep in the registry. We keep more than one because:
        /// 
        /// 1. It can be handy for debugging
        /// 2. In case we need to fix production issues by rolling back to a previous version
        /// 3. In case there's corruption in the upload of a newer version, nodes will still be able to function
        /// </summary>
        public int CheckpointLimit { get; set; } = 10;

        /// <summary>
        /// Propagated from <see cref="ContentLocationEventStoreConfiguration.NewEpochEventStartCursorDelay"/>
        /// </summary>
        public TimeSpan NewEpochEventStartCursorDelay { get; internal set; } = TimeSpan.FromMinutes(1);

        public TimeSpan? GarbageCollectionTimeout { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan? RegisterCheckpointTimeout { get; set; } = TimeSpan.FromMinutes(1);

        public TimeSpan? GetCheckpointStateTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public bool Standalone { get; set; } = false;
    }

    /// <summary>
    /// The layout of data in Azure Storage as is as follows:
    ///
    /// - We have one file per registered checkpoint
    /// - The files are named as follows: [UniquenessPrefix]_[Timestamp].reg.txt
    /// - Files contain a <see cref="CheckpointState"/>, which is parsed when reading a checkpoint
    ///
    /// Methods operate as follows:
    /// 
    /// - <see cref="GetCheckpointStateAsync"/> enumerates files whose names start with the prefix, sorts them by
    ///   timestamp, and reads the newest one that can be parsed.
    /// - <see cref="RegisterCheckpointAsync"/> simply adds a new file with the current timestamp, and also prunes
    ///   stale checkpoints.
    /// </summary>
    public class AzureBlobStorageCheckpointRegistry : StartupShutdownSlimBase, ICheckpointRegistry
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageCheckpointRegistry));

        private readonly AzureBlobStorageCheckpointRegistryConfiguration _configuration;

        private readonly CloudBlobClient _client;
        private readonly CloudBlobContainer _container;
        private readonly CloudBlobDirectory _directory;

        private readonly IClock _clock;

        private readonly Regex _blobNameRegex;

        private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions()
        {
            WriteIndented = true,
        };

        private readonly SemaphoreSlim _gcGate = TaskUtilities.CreateMutex();

        private readonly MachineLocation _primaryMachineLocation;

        private readonly IRetryPolicy _retryPolicy = RetryPolicyFactory.GetExponentialPolicy(e => e is StorageException);

        public AzureBlobStorageCheckpointRegistry(
            AzureBlobStorageCheckpointRegistryConfiguration configuration,
            MachineLocation primaryMachineLocation,
            IClock? clock = null)
        {
            _configuration = configuration;
            _primaryMachineLocation = primaryMachineLocation;
            _clock = clock ?? SystemClock.Instance;

            _client = _configuration.Credentials!.CreateCloudBlobClient();
            _container = _client.GetContainerReference(_configuration.ContainerName);
            _directory = _container.GetDirectoryReference(_configuration.FolderName);

            _blobNameRegex = new Regex(@$"{Regex.Escape(_configuration.KeySpacePrefix)}_(?<timestampUtc>[0-9]+)\.json", RegexOptions.Compiled);
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _container.CreateIfNotExistsAsync(
                accessType: BlobContainerPublicAccessType.Off,
                options: null,
                operationContext: null,
                cancellationToken: context.Token);

            return BoolResult.Success;
        }

        public Task<Result<CheckpointState>> GetCheckpointStateAsync(OperationContext context)
        {
            // NOTE: this function is naturally retried by the heartbeat mechanisms in LLS
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var blobs = ListBlobsAsync(context, _directory, _blobNameRegex)
                        .Select(blob =>
                        {
                            // This should never fail, because ListBlobsAsync returns blobs that we know already match.
                            ParseBlobName(blob.Name, out var timestampUtc);
                            return (timestampUtc, blob);
                        })
                        .OrderByDescending(kvp => kvp.timestampUtc)
                        .Select(kvp => kvp.blob);

                    await foreach (var blob in blobs)
                    {
                        try
                        {
                            using var stream = await blob.OpenReadAsync();
                            var checkpointState = await CheckpointState.FromJsonStreamAsync(stream, context.Token).ThrowIfFailureAsync();
                            return Result.Success(checkpointState);
                        }
                        catch (TaskCanceledException) when (context.Token.IsCancellationRequested)
                        {
                            // We hit timeout or a proper cancellation.
                            // Breaking from the loop instead of tracing error for each iteration.
                            break;
                        }
                        catch (Exception e)
                        {
                            Tracer.Error(context, e, $"Failed to obtain {nameof(CheckpointState)} from blob `{blob.Name}`. Skipping.");
                            continue;
                        }
                    }

                    // Add slack for start cursor to account for clock skew
                    return CheckpointState.CreateUnavailable(_clock.UtcNow - _configuration.NewEpochEventStartCursorDelay);
                },
                extraEndMessage: result =>
                {
                    if (!result.Succeeded)
                    {
                        return string.Empty;
                    }

                    var checkpointState = result.Value;
                    return $"CheckpointId=[{checkpointState.CheckpointId}] SequencePoint=[{checkpointState.StartSequencePoint}]";
                },
                timeout: _configuration.GetCheckpointStateTimeout);
        }

        public Task<BoolResult> RegisterCheckpointAsync(OperationContext context, string checkpointId, EventSequencePoint sequencePoint)
        {
            TriggerGarbageCollection(context);

            var msg = $"CheckpointId=[{checkpointId}] SequencePoint=[{sequencePoint}]";
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                context =>
                {
                    return _retryPolicy.ExecuteAsync(() => RegisterCheckpointCoreAsync(context, checkpointId, sequencePoint), context.Token);
                },
                traceOperationStarted: false,
                extraStartMessage: msg,
                extraEndMessage: _ => msg,
                timeout: _configuration.RegisterCheckpointTimeout);
        }

        private async Task<BoolResult> RegisterCheckpointCoreAsync(OperationContext context, string checkpointId, EventSequencePoint sequencePoint)
        {
            var blobName = GenerateBlobName();
            var blob = _directory.GetBlockBlobReference(blobName);

            var checkpointState = new CheckpointState(sequencePoint, checkpointId, _clock.UtcNow, _primaryMachineLocation);

            await blob.UploadTextAsync(
                checkpointState.ToJson(_jsonSerializerOptions).ThrowIfFailure(),
                encoding: Encoding.UTF8,
                accessCondition: null,
                options: null,
                operationContext: null,
                cancellationToken: context.Token);

            return BoolResult.Success;
        }

        public Task<BoolResult> ClearCheckpointsAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () => await GarbageCollectAsync(context, retentionLimit: 0),
                traceOperationStarted: false);
        }

        internal void TriggerGarbageCollection(OperationContext context)
        {
            context.PerformOperationAsync<BoolResult>(Tracer, () =>
            {
                return _gcGate.DeduplicatedOperationAsync(
                    (timeWaiting, currentCount) => GarbageCollectAsync(context, retentionLimit: _configuration.CheckpointLimit),
                    (timeWaiting, currentCount) => BoolResult.SuccessTask,
                    token: context.Token);
            },
            traceOperationStarted: false).FireAndForget(context);
        }

        /// <summary>
        /// Deletes all but the most recent <paramref name="retentionLimit"/> entries from the registry. Used to ensure
        /// our checkpoint retention is kept bounded.
        /// </summary>
        internal Task<BoolResult> GarbageCollectAsync(OperationContext context, int retentionLimit)
        {
            Contract.Requires(retentionLimit >= 0);

            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var blobs = ListBlobsAsync(context, _directory)
                        .Select(blob =>
                        {
                            if (ParseBlobName(blob.Name, out var timestampUtc))
                            {
                                return (timestampUtc, blob);
                            }
                            else
                            {
                                // If we can't parse, it means there is some file in this folder that shouldn't be
                                // there, so we delete it.
                                return (timestampUtc: DateTime.MinValue, blob);
                            }
                        })
                        .OrderByDescending(kvp => kvp.timestampUtc)
                        .Skip(retentionLimit)
                        .Select(kvp => kvp.blob);

                    await foreach (var blob in blobs)
                    {
                        try
                        {
                            var deleteSucceeded = await blob.DeleteIfExistsAsync(
                                deleteSnapshotsOption: DeleteSnapshotsOption.None,
                                accessCondition: null,
                                options: null,
                                operationContext: null,
                                cancellationToken: context.Token);
                            Tracer.Info(context, $"Delete attempt Name=[{blob.Name}] Succeeded=[{deleteSucceeded}]");
                        }
                        catch (Exception e)
                        {
                            Tracer.Error(context, e, $"Delete attempt Name=[{blob.Name}]");
                        }
                    }

                    return BoolResult.Success;
                },
                timeout: _configuration.GarbageCollectionTimeout);
        }

        private string GenerateBlobName()
        {
            var now = _clock.UtcNow;
            var timestamp = now.ToFileTimeUtc();
            return @$"{_configuration.KeySpacePrefix}_{timestamp}.json";
        }

        private bool ParseBlobName(string blobName, out DateTime timestampUtc)
        {
            var match = _blobNameRegex.Match(blobName);
            if (!match.Success || !match.Groups["timestampUtc"].Success)
            {
                timestampUtc = DateTime.MinValue;
                return false;
            }

            var timestampString = match.Groups["timestampUtc"].Value;
            if (!long.TryParse(timestampString, out var timestamp))
            {
                timestampUtc = DateTime.MinValue;
                return false;
            }

            timestampUtc = DateTime.FromFileTimeUtc(timestamp);
            return true;
        }

        private async IAsyncEnumerable<CloudBlockBlob> ListBlobsAsync(
            OperationContext context,
            CloudBlobDirectory container,
            Regex? regex = null)
        {
            BlobContinuationToken? continuation = null;
            while (!context.Token.IsCancellationRequested)
            {
                var blobs = await container.ListBlobsSegmentedAsync(
                    useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.Metadata,
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
                        yield return blob;
                    }
                }

                if (continuation == null)
                {
                    break;
                }
            }
        }
    }
}
