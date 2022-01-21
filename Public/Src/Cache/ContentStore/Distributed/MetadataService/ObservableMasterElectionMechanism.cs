// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Wraps an <see cref="IMasterElectionMechanism"/> so that an <see cref="IRoleObserver"/> can subscribe
    /// to role updates.
    /// </summary>
    public class ObservableMasterElectionMechanism : StartupShutdownComponentBase, IMasterElectionMechanism
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(ObservableMasterElectionMechanism));

        private readonly IRoleObserver _observer;
        private readonly IMasterElectionMechanism _inner;

        public ObservableMasterElectionMechanism(IMasterElectionMechanism inner, IRoleObserver observer)
        {
            _inner = inner;
            _observer = observer;
            LinkLifetime(inner);
            LinkLifetime(observer);
        }

        public MachineLocation Master => _inner.Master;

        public Role Role => _inner.Role;

        public async Task<Result<MasterElectionState>> GetRoleAsync(OperationContext context)
        {
            var result = await _inner.GetRoleAsync(context);
            if (result.Succeeded)
            {
                OnRoleUpdated(context, result.Value.Role);
            }

            return result;
        }

        public async Task<Result<Role>> ReleaseRoleIfNecessaryAsync(OperationContext context, bool shuttingDown)
        {
            var result = await _inner.ReleaseRoleIfNecessaryAsync(context, shuttingDown);
            if (!shuttingDown && result.Succeeded)
            {
                OnRoleUpdated(context, result.Value);
            }

            return result;
        }

        private void OnRoleUpdated(OperationContext context, Role role)
        {
            _observer.OnRoleUpdatedAsync(context, role).FireAndForget(context);
        }
    }
}
