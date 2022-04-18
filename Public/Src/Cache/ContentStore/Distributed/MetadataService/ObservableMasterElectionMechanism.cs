// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public class ObservableMasterElectionMechanismConfiguration
    {
        public bool IsBackgroundEnabled => GetRoleInterval != Timeout.InfiniteTimeSpan;

        public TimeSpan GetRoleInterval { get; set; } = Timeout.InfiniteTimeSpan;

        public bool GetRoleOnStartup { get; set; } = false;
    }

    /// <summary>
    /// Wraps an <see cref="IMasterElectionMechanism"/> so that an <see cref="IRoleObserver"/> can subscribe
    /// to role updates.
    /// </summary>
    public class ObservableMasterElectionMechanism : StartupShutdownComponentBase, IMasterElectionMechanism
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(ObservableMasterElectionMechanism));

        private readonly IRoleObserver? _observer;
        private readonly IMasterElectionMechanism _inner;
        private readonly ObservableMasterElectionMechanismConfiguration _configuration;
        private readonly IClock _clock;

        private DateTime _lastGetRoleTime = DateTime.MinValue;

        private readonly CancellationTokenSource _getRoleLoopCancellation = new CancellationTokenSource();

        public ObservableMasterElectionMechanism(
            ObservableMasterElectionMechanismConfiguration configuration,
            IMasterElectionMechanism inner,
            IClock clock,
            IRoleObserver? observer)
        {
            _configuration = configuration;
            _inner = inner;
            _observer = observer;
            _clock = clock;
            LinkLifetime(inner);
            LinkLifetime(observer);

            RunInBackground(nameof(PeriodicGetRoleAsync), PeriodicGetRoleAsync, fireAndForget: true);
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await base.StartupCoreAsync(context).ThrowIfFailureAsync();

            if (_configuration.GetRoleOnStartup)
            {
                await GetRoleAsync(context).FireAndForgetErrorsAsync(context);
            }

            return BoolResult.Success;
        }

        private async Task<BoolResult> PeriodicGetRoleAsync(OperationContext context)
        {
            using var cancelContext = context.WithCancellationToken(_getRoleLoopCancellation.Token);
            context = cancelContext.Context;

            while (!context.Token.IsCancellationRequested)
            {
                await _clock.Delay(_configuration.GetRoleInterval, context.Token);

                // Skip periodic get role if role was recently queried
                if (_lastGetRoleTime.IsStale(_clock.UtcNow, _configuration.GetRoleInterval))
                {
                    await GetRoleAsync(context).FireAndForgetErrorsAsync(context);
                }
            }

            return BoolResult.Success;
        }

        public MachineLocation Master => _inner.Master;

        public Role Role => _inner.Role;

        public async Task<Result<MasterElectionState>> GetRoleAsync(OperationContext context)
        {
            var result = await _inner.GetRoleAsync(context);
            if (result.Succeeded)
            {
                _lastGetRoleTime = _clock.UtcNow;
                OnRoleUpdated(context, result.Value.Role);
            }

            return result;
        }

        public async Task<Result<Role>> ReleaseRoleIfNecessaryAsync(OperationContext context)
        {
            var result = await _inner.ReleaseRoleIfNecessaryAsync(context);
            if (result.Succeeded)
            {
                OnRoleUpdated(context, result.Value);
            }

            return result;
        }

        private void OnRoleUpdated(OperationContext context, Role role)
        {
            if (_observer != null)
            {
                _observer.OnRoleUpdatedAsync(context, role).FireAndForget(context);
            }
        }
    }
}
