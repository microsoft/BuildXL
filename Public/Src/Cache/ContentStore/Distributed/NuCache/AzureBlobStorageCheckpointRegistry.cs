// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class AzureBlobStorageCheckpointRegistryConfiguration : IBlobFolderStorageConfiguration
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

        public TimeSpan GarbageCollectionTimeout { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan RegisterCheckpointTimeout { get; set; } = TimeSpan.FromMinutes(1);

        public TimeSpan CheckpointStateTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public TimeSpan StorageInteractionTimeout { get; } = TimeSpan.FromMinutes(1);

        public TimeSpan PushCheckpointCandidateExpiry { get; } = TimeSpan.FromMinutes(10);

        public int CheckpointContentFanOut { get; set; } = 5;

        public bool WriteLegacyFormat { get; set; } = true;

        public TimeSpan SlotWaitTime { get; set; } = TimeSpan.FromMilliseconds(1);

        public int MaxNumSlots { get; set; } = int.MaxValue;
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
    public class AzureBlobStorageCheckpointRegistry :
        StartupShutdownComponentBase,
        ICheckpointRegistry,
        DistributedCentralStorage.ILocationStore,
        DistributedCentralStorage.ICheckpointStore,
        ICheckpointObserver
    {
        protected override Tracer Tracer => WorkaroundTracer;

        public override bool AllowMultipleStartupAndShutdowns => true;

        public Tracer WorkaroundTracer { get; set; } = new Tracer(nameof(AzureBlobStorageCheckpointRegistry));

        private readonly AzureBlobStorageCheckpointRegistryConfiguration _configuration;

        private readonly BlobFolderStorage _storage;

        private readonly IClock _clock;

        private readonly Regex _blobNameRegex;

        private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions()
        {
            WriteIndented = true,
        };

        private CheckpointInfoSnapshot _activeCheckpointInfo = new CheckpointInfoSnapshot(new CheckpointManifest(), Array.Empty<MachineLocation>());

        private readonly MachineLocation _primaryMachineLocation;

        private readonly SemaphoreSlim _gcGate = TaskUtilities.CreateMutex();

        private readonly VolatileSet<MachineLocation> _pushLocations;

        public AzureBlobStorageCheckpointRegistry(
            AzureBlobStorageCheckpointRegistryConfiguration configuration,
            MachineLocation primaryMachineLocation,
            IClock? clock = null)
        {
            _configuration = configuration;
            _clock = clock ?? SystemClock.Instance;
            _primaryMachineLocation = primaryMachineLocation;

            _pushLocations = new VolatileSet<MachineLocation>(clock);

            _storage = new BlobFolderStorage(Tracer, configuration);
            _blobNameRegex = new Regex(@$"{Regex.Escape(_configuration.KeySpacePrefix)}_(?<timestampUtc>[0-9]+)\.json", RegexOptions.Compiled);

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

                            foreach (var consumer in checkpointState.Consumers)
                            {
                                _pushLocations.Add(consumer, _configuration.PushCheckpointCandidateExpiry);
                            }

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
                timeout: _configuration.CheckpointStateTimeout);
        }

        public Task<BoolResult> RegisterCheckpointAsync(OperationContext context, CheckpointState checkpointState)
        {
            TriggerGarbageCollection(context);

            var msg = checkpointState.ToString();
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                context =>
                {
                    var blobName = GenerateBlobName();
                    checkpointState.Consumers.TryAdd(_primaryMachineLocation);
                    if (_configuration.WriteLegacyFormat)
                    {
                        return _storage.WriteAsync(context, blobName, JsonSerializer.Serialize(checkpointState, _jsonSerializerOptions));
                    }
                    else
                    {
                        return _storage.WriteAsync(context, blobName, checkpointState);
                    }
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
                    var blobs = ListBlobsRecentFirstAsync(context)
                        .Skip(retentionLimit);

                    await foreach (var blob in blobs)
                    {
                        try
                        {
                            var deleteSucceeded = await _storage.DeleteIfExistsAsync(context, blob);
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

        private IAsyncEnumerable<BlobName> ListBlobsRecentFirstAsync(OperationContext context)
        {
            var blobs = _storage.ListBlobsAsync(context, _blobNameRegex)
                .Select(name =>
                {
                    // This should never fail, because ListBlobsAsync returns blobs that we know already match.
                    ParseBlobName(name.Name, out var timestampUtc);
                    return (timestampUtc, name);
                })
                .OrderByDescending(kvp => kvp.timestampUtc)
                .Select(kvp => kvp.name);
            return blobs;
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

        public record CheckpointInfoSnapshot(CheckpointManifest Manifest, IReadOnlyList<MachineLocation> Locations);

        public async Task<BoolResult> OnChangeCheckpointAsync(OperationContext context, CheckpointState initialState, CheckpointManifest manifest)
        {
            _activeCheckpointInfo = _activeCheckpointInfo with
            {
                Manifest = manifest
            };

            if (initialState.FileName == null)
            {
                return BoolResult.Success;
            }

            return await context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var (state, index) = await _storage.ReadModifyWriteAsync<CheckpointState, int>(context, initialState.FileName.Value, state =>
                    {
                        var index = state.Consumers.Count;
                        var updated = state.Consumers.TryAdd(_primaryMachineLocation);
                        if (!updated)
                        {
                            index = state.Consumers.IndexOf(_primaryMachineLocation);
                        }
                        return (state, index, updated);
                    },
                    defaultValue: () => initialState).ThrowIfFailureAsync();

                    var locations = GetCandidateLocations(state, index);
                    _activeCheckpointInfo = new CheckpointInfoSnapshot(manifest, locations);
                    return Result.Success(index);
                },
                extraEndMessage: r => $"CheckpointFile=[{initialState.FileName?.Name}] Index=[{r.GetValueOrDefault(-1)}] Locations=({_activeCheckpointInfo.Locations.Count})[{string.Join(", ", _activeCheckpointInfo.Locations)}] Manifest=[{manifest}]");
        }

        private IReadOnlyList<MachineLocation> GetCandidateLocations(CheckpointState state, int index)
        {
            var parentIndexBase = index / _configuration.CheckpointContentFanOut;
            var locations = new List<MachineLocation>();

            for (int i = parentIndexBase; i >= 0 && locations.Count < _configuration.CheckpointContentFanOut; i--)
            {
                var machine = state.Consumers[i];

                // Only allow copying from machines with a lower machine location or producer to prevent cycles
                var compareResult = StringComparer.OrdinalIgnoreCase.Compare(machine.Path, _primaryMachineLocation.Path);
                if (compareResult < 0 || (compareResult != 0 && i == 0))
                {
                    locations.Add(machine);
                }
            }

            return locations;
        }

        public bool IsActiveCheckpointFile(ShortHash hash)
        {
            return _activeCheckpointInfo.Manifest.TryGetEntry(hash, out _);
        }

        public Result<MachineLocation> GetRandomMachineLocation()
        {
            var location = _pushLocations.Enumerate().FirstOrDefault();
            if (location.IsValid)
            {
                _pushLocations.CleanStaleItems(1);
                return location;
            }

            return Result.FromErrorMessage<MachineLocation>("No locations");
        }

        public Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, ContentHash hash)
        {
            var size = _activeCheckpointInfo.Manifest.TryGetEntry(hash, out var entry) && entry.Size > 0 ? entry.Size : -1;
            return Task.FromResult(new GetBulkLocationsResult(new[] { new ContentHashWithSizeAndLocations(hash, size, _activeCheckpointInfo.Locations) }));
        }

        public ValueTask<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentInfo)
        {
            // Do nothing. Start of restore handles registering this machine for the content
            return new ValueTask<BoolResult>(BoolResult.Success);
        }
    }
}
