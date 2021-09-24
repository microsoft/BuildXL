// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using StructGenerators;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class AzureBlobStorageMasterElectionMechanismConfiguration
    {
        public AzureBlobStorageCredentials? Credentials { get; set; }

        public string ContainerName { get; set; } = "checkpoints";

        public string FolderName { get; set; } = "masterElection";

        public string FileName { get; set; } = "master.json";

        public bool IsMasterEligible { get; set; } = true;

        /// <summary>
        /// WARNING: must be longer than the heartbeat interval
        /// </summary>
        public TimeSpan LeaseExpiryTime { get; set; } = TimeSpan.FromMinutes(5);

        public TimeSpan? StorageInteractionTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }

    public class AzureBlobStorageMasterElectionMechanism : StartupShutdownSlimBase, IMasterElectionMechanism
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageMasterElectionMechanism));

        private readonly AzureBlobStorageMasterElectionMechanismConfiguration _configuration;
        private readonly MachineLocation _primaryMachineLocation;
        private readonly IClock _clock;

        private readonly CloudBlobClient _client;
        private readonly CloudBlobContainer _container;
        private readonly CloudBlobDirectory _directory;

        private static readonly BlobRequestOptions DefaultBlobStorageRequestOptions = new BlobRequestOptions()
        {
            RetryPolicy = new ExponentialRetry(),
        };

        public AzureBlobStorageMasterElectionMechanism(
            AzureBlobStorageMasterElectionMechanismConfiguration configuration,
            MachineLocation primaryMachineLocation,
            IClock? clock = null)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            _configuration = configuration;
            _primaryMachineLocation = primaryMachineLocation;
            _clock = clock ?? SystemClock.Instance;

            _client = _configuration.Credentials!.CreateCloudBlobClient();
            _container = _client.GetContainerReference(_configuration.ContainerName);
            _directory = _container.GetDirectoryReference(_configuration.FolderName);
        }

        private delegate bool TryUpdateLease(ref MasterLeaseMetadata metadata);

        private class MasterLease
        {
            public MachineLocation Master { get; init; }

            public DateTime CreationTimeUtc { get; init; }

            public DateTime LastUpdateTimeUtc { get; init; }

            public DateTime LeaseExpiryTimeUtc { get; init; }

            public bool IsExpired(DateTime now)
            {
                return LeaseExpiryTimeUtc < now;
            }

            public Role GetRole(MachineLocation location)
            {
                return location.Equals(Master) ? Role.Master : Role.Worker;
            }

            /// <inheritdoc />
            public override string ToString()
            {
                return
                    $"Master={Master}, CreationTimeUtc={CreationTimeUtc}, LastUpdateTimeUtc={LastUpdateTimeUtc}, LeaseExpiryTimeUtc={LeaseExpiryTimeUtc}";
            }
        }

        private record MasterLeaseMetadata(string? ETag = null, MasterLease? Lease = null);

        private bool TryReleaseLeaseIfHeld(ref MasterLeaseMetadata metadata)
        {
            var now = _clock.UtcNow;
            var lease = metadata.Lease;
            bool isMaster = IsCurrentMaster(lease);
            if (!isMaster)
            {
                // Does not hold the master lease, so cannot release.
                return false;
            }

            metadata = metadata with
            {
                Lease = new MasterLease()
                {
                    CreationTimeUtc = lease!.CreationTimeUtc,
                    LastUpdateTimeUtc = now,
                    LeaseExpiryTimeUtc = DateTime.MinValue,
                    Master = _primaryMachineLocation
                }
            };

            return true;
        }

        private bool TryCreateOrExtendLease(ref MasterLeaseMetadata metadata)
        {
            if (!_configuration.IsMasterEligible)
            {
                // Not eligible to take master lease.
                return false;
            }

            var now = _clock.UtcNow;
            var lease = metadata.Lease;
            bool isMaster = IsCurrentMaster(lease);
            if (!isMaster && !IsLeaseExpired(lease))
            {
                // Other machine still has valid lease
                return false;
            }

            metadata = metadata with
            {
                Lease = new MasterLease()
                {
                    CreationTimeUtc = isMaster ? lease!.CreationTimeUtc : now,
                    LastUpdateTimeUtc = now,
                    LeaseExpiryTimeUtc = now + _configuration.LeaseExpiryTime,
                    Master = _primaryMachineLocation
                }
            };

            return true;
        }

        private bool IsLeaseExpired([NotNullWhen(false)] MasterLease? lease)
        {
            return lease == null || lease.LeaseExpiryTimeUtc < _clock.UtcNow;
        }

        private bool IsCurrentMaster([NotNullWhen(true)] MasterLease? lease)
        {
            return lease != null && lease.Master.Equals(_primaryMachineLocation);
        }

        private MasterElectionState GetElectionState(MasterLeaseMetadata metadata)
        {
            var master = metadata.Lease?.Master ?? default(MachineLocation);
            if (IsLeaseExpired(metadata.Lease))
            {
                // Lease is expired. Lease creator is no longer considerd master machine.
                master = default(MachineLocation);
            }

            return new MasterElectionState(master, master.Equals(_primaryMachineLocation) ? Role.Master : Role.Worker);
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _container.CreateIfNotExistsAsync();
            return await base.StartupCoreAsync(context);
        }

        public Task<Result<MasterElectionState>> GetRoleAsync(OperationContext context)
        {
            return UpdateRoleAsync(context, tryUpdateLease: TryCreateOrExtendLease);
        }

        public Task<Result<Role>> ReleaseRoleIfNecessaryAsync(OperationContext context, bool shuttingDown = false)
        {
            if (!_configuration.IsMasterEligible)
            {
                return Task.FromResult(Result.Success<Role>(Role.Worker));
            }

            return UpdateRoleAsync(context, tryUpdateLease: TryReleaseLeaseIfHeld).SelectAsync(s => s.Role);
        }

        private Task<Result<MasterElectionState>> UpdateRoleAsync(OperationContext context, TryUpdateLease tryUpdateLease)
        {
            return context.PerformOperationWithTimeoutAsync<Result<MasterElectionState>>(
                Tracer,
                async context =>
                {
                    while (true)
                    {
                        context.Token.ThrowIfCancellationRequested();

                        var now = _clock.UtcNow;

                        var metadata = await FetchCurrentLeaseAsync(context);
                        if (!tryUpdateLease(ref metadata))
                        {
                            return GetElectionState(metadata);
                        }

                        bool updated = await CompareUpdateLeaseAsync(context, metadata.Lease!, metadata.ETag);
                        if (updated)
                        {
                            return GetElectionState(metadata);
                        }
                    }
                },
                timeout: _configuration.StorageInteractionTimeout,
                extraEndMessage: r => $"{r!.GetValueOrDefault()}");
        }

        private Task<MasterLeaseMetadata> FetchCurrentLeaseAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var blob = _directory.GetBlockBlobReference(_configuration.FileName);

                    if (!await blob.ExistsAsync(DefaultBlobStorageRequestOptions, operationContext: null, cancellationToken: context.Token))
                    {
                        return Result.Success(new MasterLeaseMetadata());
                    }

                    var downloadContext = new Microsoft.WindowsAzure.Storage.OperationContext();
                    var leaseJson = await blob.DownloadTextAsync(
                        operationContext: downloadContext,
                        cancellationToken: context.Token,
                        encoding: Encoding.UTF8,
                        accessCondition: null,
                        options: DefaultBlobStorageRequestOptions);
                    var lease = JsonUtilities.JsonDeserialize<MasterLease>(leaseJson);
                    return new MasterLeaseMetadata(downloadContext.LastResult.Etag, lease);
                },
                extraEndMessage: r =>
                {
                    if (!r.Succeeded)
                    {
                        return string.Empty;
                    }

                    var state = r.Value;
                    return $"ETag=[{state?.ETag ?? "null"}] Lease=[{state?.Lease}]";
                },
                traceOperationStarted: false).ThrowIfFailureAsync();
        }

        private Task<bool> CompareUpdateLeaseAsync(
            OperationContext context,
            MasterLease lease,
            string? etag)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var serializedLease = JsonUtilities.JsonSerialize(lease);

                    var reference = _directory.GetBlockBlobReference(_configuration.FileName);
                    var accessCondition =
                        etag is null ?
                            AccessCondition.GenerateIfNotExistsCondition() :
                            AccessCondition.GenerateIfMatchCondition(etag);
                    try
                    {
                        await reference.UploadTextAsync(
                            serializedLease,
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
                            || exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotModified)
                        {
                            Tracer.Debug(
                                context,
                                exception,
                                $"Lease does not exist or does not match ETag `{etag ?? "null"}`. Reported ETag is `{exception.RequestInformation.Etag ?? "null"}`");
                            return Result.Success(false);
                        }

                        throw;
                    }

                    // Uploaded successfully, so we overrode the previous lease
                    return Result.Success(true);
                },
                traceOperationStarted: false,
                extraEndMessage: r =>
                {
                    var msg = $"ETag=[{etag ?? "null"}] Lease=[{lease}]";
                    if (!r.Succeeded)
                    {
                        return msg;
                    }

                    return $"{msg} Exchanged=[{r.Value}]";
                }).ThrowIfFailureAsync();
        }

        /// <summary>
        /// WARNING: used for tests only.
        /// </summary>
        internal Task<BoolResult> CleanupStateAsync(OperationContext context)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async context =>
            {
                var blob = _directory.GetBlobReference(_configuration.FileName);
                await blob.DeleteIfExistsAsync(
                    deleteSnapshotsOption: DeleteSnapshotsOption.None,
                    accessCondition: null,
                    options: null,
                    operationContext: null,
                    cancellationToken: context.Token);

                return BoolResult.Success;
            },
            timeout: TimeSpan.FromSeconds(10));
        }
    }
}
