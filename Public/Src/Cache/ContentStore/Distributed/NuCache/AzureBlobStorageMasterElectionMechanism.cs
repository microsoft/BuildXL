// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Tasks;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class AzureBlobStorageMasterElectionMechanismConfiguration : IBlobFolderStorageConfiguration
    {
        public AzureBlobStorageCredentials? Credentials { get; set; }

        public string ContainerName { get; set; } = "checkpoints";

        public string FolderName { get; set; } = "masterElection";

        public string FileName { get; set; } = "master.json";

        public bool IsMasterEligible { get; set; } = true;

        /// <summary>
        /// Maximum lease duration when not refreshing.
        /// </summary>
        /// <remarks>
        /// WARNING: must be longer than the heartbeat interval (<see cref="DistributedContentSettings.HeartbeatIntervalMinutes"/>)
        ///
        /// The value is set to 10m because that's rough worst-case estimate of how long it takes CASaaS to reboot in
        /// highly-loaded production stamps, including offline time (i.e., the maximum tolerated offline time).
        /// </remarks>
        public TimeSpan LeaseExpiryTime { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan StorageInteractionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public bool ReleaseLeaseOnShutdown { get; set; } = false;

        public RetryPolicyConfiguration RetryPolicy { get; set; } = BlobFolderStorage.DefaultRetryPolicy;
    }

    public class AzureBlobStorageMasterElectionMechanism : StartupShutdownComponentBase, IMasterElectionMechanism
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageMasterElectionMechanism));

        private readonly AzureBlobStorageMasterElectionMechanismConfiguration _configuration;
        private readonly MachineLocation _primaryMachineLocation;
        private readonly IClock _clock;

        private readonly BlobFolderStorage _storage;

        private MasterElectionState _lastElection = MasterElectionState.DefaultWorker;

        private readonly SemaphoreSlim _roleMutex = TaskUtilities.CreateMutex();

        public AzureBlobStorageMasterElectionMechanism(
            AzureBlobStorageMasterElectionMechanismConfiguration configuration,
            MachineLocation primaryMachineLocation,
            IClock? clock = null)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            _configuration = configuration;
            _primaryMachineLocation = primaryMachineLocation;
            _clock = clock ?? SystemClock.Instance;

            _storage = new BlobFolderStorage(Tracer, configuration);

            LinkLifetime(_storage);
        }

        public MachineLocation Master => _lastElection.Master;

        public Role Role => _lastElection.Role;

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_configuration.ReleaseLeaseOnShutdown)
            {
                await ReleaseRoleIfNecessaryAsync(context).IgnoreFailure();
            }

            return await base.ShutdownCoreAsync(context);
        }

        public async Task<Result<MasterElectionState>> GetRoleAsync(OperationContext context)
        {
            using var releaser = await _roleMutex.AcquireAsync(context.Token);

            var r = await UpdateRoleAsync(context, tryUpdateLease: TryCreateOrExtendLease);

            if (r.Succeeded)
            {
                _lastElection = r.Value;
            }

            return r;
        }

        public async Task<Result<Role>> ReleaseRoleIfNecessaryAsync(OperationContext context)
        {
            if (!_configuration.IsMasterEligible)
            {
                return Result.Success(Role.Worker);
            }

            using var releaser = await _roleMutex.AcquireAsync(context.Token);

            var r = await UpdateRoleAsync(context, tryUpdateLease: TryReleaseLeaseIfHeld);
            if (r.Succeeded)
            {
                // We don't know who the master is any more
                _lastElection = new MasterElectionState(Master: default, Role: Role.Worker);
            }

            return Result.Success(Role.Worker);
        }

        private delegate bool TryUpdateLease(MasterLease current, out MasterLease next);

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

        private bool TryReleaseLeaseIfHeld(MasterLease current, out MasterLease next)
        {
            next = current;

            var now = _clock.UtcNow;

            bool isMaster = IsCurrentMaster(current);
            if (!isMaster)
            {
                // We can't release a lease we do not hold
                return false;
            }

            if (IsLeaseExpired(current, now))
            {
                // We don't need to release a expired lease
                return false;
            }

            next = new MasterLease()
            {
                CreationTimeUtc = isMaster ? current!.CreationTimeUtc : now,
                LastUpdateTimeUtc = now,
                // The whole point of this method is to basically set the lease expiry time as now
                LeaseExpiryTimeUtc = now,
                Master = _primaryMachineLocation,
            };

            return true;
        }

        private bool TryCreateOrExtendLease(MasterLease current, out MasterLease next)
        {
            next = current;

            if (!_configuration.IsMasterEligible || ShutdownStarted)
            {
                // Not eligible to take master lease.
                return false;
            }

            var now = _clock.UtcNow;
            var isMaster = IsCurrentMaster(current);
            if (!IsLeaseExpired(current, now) && !isMaster)
            {
                // We only want to update the lease if it's either expired, or we are the master machine
                return false;
            }

            next = new MasterLease()
            {
                CreationTimeUtc = isMaster ? current!.CreationTimeUtc : now,
                LastUpdateTimeUtc = now,
                LeaseExpiryTimeUtc = now + _configuration.LeaseExpiryTime,
                Master = _primaryMachineLocation
            };

            return true;
        }

        private bool IsLeaseExpired([NotNullWhen(false)] MasterLease? lease, DateTime? now = null)
        {
            return lease == null || lease.LeaseExpiryTimeUtc <= (now ?? _clock.UtcNow);
        }

        private bool IsCurrentMaster([NotNullWhen(true)] MasterLease? lease)
        {
            return lease != null && lease.Master.Equals(_primaryMachineLocation);
        }

        private MasterElectionState GetElectionState(MasterLease lease)
        {
            var master = lease?.Master ?? default(MachineLocation);
            if (IsLeaseExpired(lease))
            {
                // Lease is expired. Lease creator is no longer considerd master machine.
                master = default(MachineLocation);
            }

            return new MasterElectionState(master, master.Equals(_primaryMachineLocation) ? Role.Master : Role.Worker);
        }

        private Task<Result<MasterElectionState>> UpdateRoleAsync(OperationContext context, TryUpdateLease tryUpdateLease)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var result = await _storage.ReadModifyWriteAsync<MasterLease, MasterLease>(context, _configuration.FileName,
                        current =>
                        {
                            var updated = tryUpdateLease(current, out var next);
                            return (NextState: next, Result: next, Updated: updated);
                        }).ThrowIfFailureAsync();

                    return Result.Success(GetElectionState(result.Result));
                },
                timeout: _configuration.StorageInteractionTimeout,
                extraEndMessage: r => $"{r!.GetValueOrDefault()} IsMasterEligible=[{_configuration.IsMasterEligible}]");
        }

        /// <summary>
        /// WARNING: used for tests only.
        /// </summary>
        internal Task<BoolResult> CleanupStateAsync(OperationContext context)
        {
            return _storage.CleanupStateAsync(context, _configuration.FileName);
        }
    }
}
