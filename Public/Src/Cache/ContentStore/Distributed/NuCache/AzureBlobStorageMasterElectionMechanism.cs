// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
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
        /// WARNING: must be longer than the heartbeat interval
        /// </summary>
        public TimeSpan LeaseExpiryTime { get; set; } = TimeSpan.FromMinutes(5);

        public TimeSpan StorageInteractionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public TimeSpan SlotWaitTime { get; set; } = TimeSpan.FromMilliseconds(1);

        public int MaxNumSlots { get; set; } = int.MaxValue;
    }

    public class AzureBlobStorageMasterElectionMechanism : StartupShutdownComponentBase, IMasterElectionMechanism
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageMasterElectionMechanism));

        private readonly AzureBlobStorageMasterElectionMechanismConfiguration _configuration;
        private readonly MachineLocation _primaryMachineLocation;
        private readonly IClock _clock;

        private readonly BlobFolderStorage _storage;

        private MasterElectionState _lastElection = MasterElectionState.DefaultWorker;

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

        public async Task<Result<MasterElectionState>> GetRoleAsync(OperationContext context)
        {
            var r = await UpdateRoleAsync(context, tryUpdateLease: TryCreateOrExtendLease);
            if (r.Succeeded)
            {
                _lastElection = r.Value;
            }

            return r;
        }

        public async Task<Result<Role>> ReleaseRoleIfNecessaryAsync(OperationContext context, bool shuttingDown = false)
        {
            if (!_configuration.IsMasterEligible)
            {
                return Result.Success<Role>(Role.Worker);
            }

            var r = await UpdateRoleAsync(context, tryUpdateLease: TryReleaseLeaseIfHeld).AsAsync(s => s.Role);
            if (r.Succeeded)
            {
                // We don't know who the master is any more
                _lastElection = new MasterElectionState(Master: default, Role: r.Value);
            }

            return r;
        }

        private delegate bool TryUpdateLease(ref MasterLease metadata);

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

        private bool TryReleaseLeaseIfHeld(ref MasterLease lease)
        {
            var now = _clock.UtcNow;
            bool isMaster = IsCurrentMaster(lease);
            if (!isMaster)
            {
                // Does not hold the master lease, so cannot release.
                return false;
            }

            lease = new MasterLease()
            {
                CreationTimeUtc = isMaster ? lease!.CreationTimeUtc : now,
                LastUpdateTimeUtc = now,
                LeaseExpiryTimeUtc = now + _configuration.LeaseExpiryTime,
                Master = _primaryMachineLocation
            };

            return true;
        }

        private bool TryCreateOrExtendLease(ref MasterLease lease)
        {
            if (!_configuration.IsMasterEligible)
            {
                // Not eligible to take master lease.
                return false;
            }

            var now = _clock.UtcNow;
            bool isMaster = IsCurrentMaster(lease);
            if (!isMaster && !IsLeaseExpired(lease))
            {
                // Other machine still has valid lease
                return false;
            }

            lease = new MasterLease()
            {
                CreationTimeUtc = isMaster ? lease!.CreationTimeUtc : now,
                LastUpdateTimeUtc = now,
                LeaseExpiryTimeUtc = now + _configuration.LeaseExpiryTime,
                Master = _primaryMachineLocation
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
            return context.PerformOperationWithTimeoutAsync<Result<MasterElectionState>>(
                Tracer,
                async context =>
                {
                    var lease = await _storage.ReadModifyWriteAsync<MasterLease>(context, _configuration.FileName,
                        state =>
                        {
                            tryUpdateLease(ref state);
                            return state;
                        }).ThrowIfFailureAsync();

                    return Result.Success(GetElectionState(lease));
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
