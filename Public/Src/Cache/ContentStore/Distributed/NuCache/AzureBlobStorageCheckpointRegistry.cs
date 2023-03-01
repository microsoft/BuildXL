// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Core.Tasks;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public record AzureBlobStorageCheckpointRegistryConfiguration : BlobFolderStorageConfiguration
    {
        public AzureBlobStorageCheckpointRegistryConfiguration()
        {
            StorageInteractionTimeout = TimeSpan.FromMinutes(1);
        }

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

        public TimeSpan GarbageCollectionTimeout { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan RegisterCheckpointTimeout { get; set; } = TimeSpan.FromMinutes(1);

        public TimeSpan CheckpointStateTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public TimeSpan? LatestFileMaxAge { get; set; } = null;
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
    public class AzureBlobStorageCheckpointRegistry : StartupShutdownComponentBase, ICheckpointRegistry
    {
        protected override Tracer Tracer => WorkaroundTracer;

        public override bool AllowMultipleStartupAndShutdowns => true;

        public Tracer WorkaroundTracer { get; set; } = new Tracer(nameof(AzureBlobStorageCheckpointRegistry));

        private readonly AzureBlobStorageCheckpointRegistryConfiguration _configuration;

        private readonly BlobFolderStorage _storage;

        private readonly IClock _clock;

        private readonly Regex _blobNameRegex;

        private readonly BlobPath _latestBlobPath;

        private readonly MachineLocation _primaryMachineLocation;

        private readonly SemaphoreSlim _gcGate = TaskUtilities.CreateMutex();

        public AzureBlobStorageCheckpointRegistry(
            AzureBlobStorageCheckpointRegistryConfiguration configuration,
            MachineLocation primaryMachineLocation,
            IClock? clock = null)
        {
            _configuration = configuration;
            _clock = clock ?? SystemClock.Instance;
            _primaryMachineLocation = primaryMachineLocation;

            _storage = new BlobFolderStorage(Tracer, configuration);
            _blobNameRegex = new Regex(@$"{Regex.Escape(_configuration.KeySpacePrefix)}_(?<timestampUtc>[0-9]+)\.json", RegexOptions.Compiled);
            _latestBlobPath = new BlobPath($"{_configuration.KeySpacePrefix}.latest.json", relative: true);

            LinkLifetime(_storage);
        }

        public Task<Result<CheckpointState>> GetCheckpointStateAsync(OperationContext context)
        {
            // NOTE: this function is naturally retried by the heartbeat mechanisms in LLS
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var blobs = ListBlobsRecentFirstAsync(context);

                    await foreach (var blob in blobs)
                    {
                        try
                        {
                            var checkpointState = await _storage.ReadAsync<CheckpointState>(context, blob).ThrowIfFailureAsync();
                            checkpointState.FileName = blob;

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
                            Tracer.Error(context, e, $"Failed to obtain {nameof(CheckpointState)} from blob `{blob.Path}`. Skipping.");
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
                timeout: _configuration.CheckpointStateTimeout);
        }

        public Task<BoolResult> RegisterCheckpointAsync(OperationContext context, CheckpointState checkpointState)
        {
            TriggerGarbageCollection(context);

            var blobPath = GenerateNewCheckpointBlobPath();
            var msg = $"Path=[{blobPath}] LatestBlobPath=[{_latestBlobPath}] CheckpointState=[{checkpointState}]";
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var writeTimestampResult = await _storage.WriteAsync(context, blobPath, checkpointState);

                    var writeLatestResult = await _storage.WriteAsync(context, _latestBlobPath, checkpointState);
                    return writeLatestResult & writeTimestampResult;
                },
                traceOperationStarted: false,
                extraStartMessage: msg,
                extraEndMessage: _ => msg,
                timeout: _configuration.RegisterCheckpointTimeout);
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
                    // The enumeration includes the "latest" blob as the first blob, so we need to increase the
                    // retention limit by 1.
                    if (retentionLimit > 0)
                    {
                        retentionLimit++;
                    }

                    var blobs = ListBlobsRecentFirstAsync(context)
                        .Skip(retentionLimit);

                    await foreach (var blob in blobs)
                    {
                        try
                        {
                            var deleteSucceeded = (await _storage.DeleteIfExistsAsync(context, blob)).GetValueOr(false);
                            Tracer.Info(context, $"Delete attempt Name=[{blob.Path}] Succeeded=[{deleteSucceeded}]");
                        }
                        catch (Exception e)
                        {
                            Tracer.Error(context, e, $"Delete attempt Name=[{blob.Path}]");
                        }
                    }

                    return BoolResult.Success;
                },
                timeout: _configuration.GarbageCollectionTimeout);
        }

        private async IAsyncEnumerable<BlobPath> ListBlobsRecentFirstAsync(OperationContext context)
        {
            var latestBlob = _storage.GetBlob(_latestBlobPath);
            BlobProperties? properties = null;
            try
            {
                properties = await latestBlob.GetPropertiesAsync(cancellationToken: context.Token);
            }
            catch (Exception exception)
            {
                Tracer.Error(context, exception, $"Failed to obtain properties from latest blob at path {_latestBlobPath}.");
            }

            if (properties is not null)
            {
                if (_configuration.LatestFileMaxAge is null)
                {
                    yield return _latestBlobPath;
                }
                else
                {
                    var now = _clock.UtcNow;
                    var threshold = now - _configuration.LatestFileMaxAge;
                    if (properties.CreatedOn >= threshold || properties.LastModified >= threshold)
                    {
                        yield return _latestBlobPath;
                    }
                    else
                    {
                        Tracer.Warning(context, $"Obtained latest blob from path {_latestBlobPath} successfully, but deemed too old to use. Falling back to listing. CreatedOn=[{properties?.CreatedOn.ToString() ?? "null"}] LastModified=[{properties?.LastModified.ToString() ?? "null"}]");
                    }
                }
            }

            var blobs = _storage.ListBlobNamesAsync(context, _blobNameRegex)
                .Select(name =>
                {
                    // This should never fail, because ListBlobsAsync returns blobs that we know already match.
                    ParseBlobName(name.Path, out var timestampUtc);
                    return (timestampUtc, name);
                })
                .OrderByDescending(kvp => kvp.timestampUtc)
                .Select(kvp => kvp.name);

            await foreach (var blob in blobs)
            {
                yield return blob;
            }
        }

        private BlobPath GenerateNewCheckpointBlobPath()
        {
            var now = _clock.UtcNow;
            var timestamp = now.ToFileTimeUtc();
            return new BlobPath(@$"{_configuration.KeySpacePrefix}_{timestamp}.json", relative: true);
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
    }
}
